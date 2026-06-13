using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Adam.Shared.Contracts;
using Adam.Shared.Transport;
using Google.Protobuf;

namespace Adam.Shared.Services;

public enum ConnectionStatus
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting
}

public sealed class BrokerClient : IAsyncDisposable
{
    private string _host;
    private int _port;
    private bool _useTls;
    private bool _allowSelfSigned;
    private TcpClient? _client;
    private Stream? _stream;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<Envelope>> _pending = new();
    private CancellationTokenSource? _receiveCts;
    private bool _disposed;

    // Reconnection state
    private int _reconnectAttempts;
    private const int MaxReconnectAttempts = 10;
    private readonly TimeSpan[] ReconnectDelays =
    {
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(8),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30)
    };

    public bool IsConnected => _client?.Connected == true && _stream != null;
    public ConnectionStatus Status { get; private set; } = ConnectionStatus.Disconnected;

    public event EventHandler<ConnectionStatus>? StatusChanged;
    public event EventHandler<ChangeNotification>? NotificationReceived;
    /// <summary>
    /// Raised when the server sends a SessionInvalidated notification (Phase 7 T7.5).
    /// The client should call ValidateTokenAsync to refresh the user profile.
    /// </summary>
    public event EventHandler? SessionInvalidated;

    public BrokerClient(string host, int port, bool useTls = false, bool allowSelfSigned = false)
    {
        _host = host;
        _port = port;
        _useTls = useTls;
        _allowSelfSigned = allowSelfSigned;
    }

    /// <summary>The host this client will connect to.</summary>
    public string Host => _host;

    /// <summary>The port this client will connect to.</summary>
    public int Port => _port;

    /// <summary>
    /// Retargets the client at a different endpoint. Only allowed while
    /// disconnected, so an in-flight connection is never silently repointed.
    /// Use this before <see cref="ConnectAsync"/> to honor a host/port the user
    /// entered or that was published to the registry.
    /// </summary>
    public void Reconfigure(string host, int port, bool useTls = false, bool allowSelfSigned = false)
    {
        if (IsConnected)
        {
            ConnectionDebugLogger.Warn($"Reconfigure rejected: already connected to {_host}:{_port}");
            throw new InvalidOperationException("Disconnect before changing the broker endpoint.");
        }

        ConnectionDebugLogger.Info($"Reconfigure: {_host}:{_port} (TLS={_useTls}, SelfSigned={_allowSelfSigned}) \u2192 {host}:{port} (TLS={useTls}, SelfSigned={allowSelfSigned})");
        _host = host;
        _port = port;
        _useTls = useTls;
        _allowSelfSigned = allowSelfSigned;
    }

    private void SetStatus(ConnectionStatus status)
    {
        if (Status == status) return;
        var prev = Status;
        Status = status;
        ConnectionDebugLogger.Info($"Status: [{prev}] \u2192 [{status}] (host={_host}:{_port})");
        StatusChanged?.Invoke(this, status);
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (_disposed)
        {
            ConnectionDebugLogger.Error("ConnectAsync: ObjectDisposedException — client already disposed");
            throw new ObjectDisposedException(nameof(BrokerClient));
        }

        var sw = Stopwatch.StartNew();

        await _lock.WaitAsync(ct);
        try
        {
            if (_client?.Connected == true)
                return;


            SetStatus(ConnectionStatus.Connecting);
            _client?.Dispose();
            _client = new TcpClient();

            ConnectionDebugLogger.Info($"ConnectAsync: TCP connect started to {_host}:{_port}");
            var tcpSw = Stopwatch.StartNew();
            await _client.ConnectAsync(_host, _port, ct);
            ConnectionDebugLogger.Info($"ConnectAsync: TCP connect completed in {tcpSw.Elapsed.TotalMilliseconds:F1}ms (local={_client.Client.LocalEndPoint})");

            var networkStream = _client.GetStream();
            _reconnectAttempts = 0;

            if (_useTls)
            {
                ConnectionDebugLogger.Info($"ConnectAsync: TLS handshake started (target={_host}, protocols=TLS 1.2 | TLS 1.3)");
                var tlsSw = Stopwatch.StartNew();
                var sslStream = new SslStream(networkStream, leaveInnerStreamOpen: false, userCertificateValidationCallback: ValidateServerCertificate);
                await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = _host,
                    EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                }, ct);
                _stream = sslStream;
                ConnectionDebugLogger.Info($"ConnectAsync: TLS handshake completed in {tlsSw.Elapsed.TotalMilliseconds:F1}ms");
            }
            else
            {
                _stream = networkStream;
                ConnectionDebugLogger.Info("ConnectAsync: TLS not enabled, using plain TCP stream");
            }

            _receiveCts?.Cancel();
            _receiveCts = new CancellationTokenSource();
            _ = ReceiveLoopAsync(_receiveCts.Token);

            ConnectionDebugLogger.Info($"ConnectAsync: fully connected to {_host}:{_port} in {sw.Elapsed.TotalMilliseconds:F1}ms");
            SetStatus(ConnectionStatus.Connected);
        }
        catch (OperationCanceledException)
        {
            ConnectionDebugLogger.Warn($"ConnectAsync: cancelled after {sw.Elapsed.TotalMilliseconds:F0}ms (timeout or user cancellation)");
            SetStatus(ConnectionStatus.Disconnected);
            throw;
        }
        catch (SocketException ex)
        {
            ConnectionDebugLogger.Error(ex, $"ConnectAsync: SocketException after {sw.Elapsed.TotalMilliseconds:F0}ms — {ex.SocketErrorCode}");
            SetStatus(ConnectionStatus.Disconnected);
            throw;
        }
        catch (Exception ex)
        {
            ConnectionDebugLogger.Error(ex, $"ConnectAsync: failed after {sw.Elapsed.TotalMilliseconds:F0}ms");
            SetStatus(ConnectionStatus.Disconnected);
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    private bool ValidateServerCertificate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors)
    {
        if (sslPolicyErrors == SslPolicyErrors.None)
            return true;
        if (_allowSelfSigned && sslPolicyErrors == SslPolicyErrors.RemoteCertificateChainErrors)
            return true;
        return false;
    }

    public async Task DisconnectAsync()
    {
        ConnectionDebugLogger.Info($"DisconnectAsync: disconnecting from {_host}:{_port} (pending={_pending.Count})");
        var sw = Stopwatch.StartNew();

        await _lock.WaitAsync();
        try
        {
            _receiveCts?.Cancel();
            _stream?.Dispose();
            _client?.Dispose();
            _stream = null;
            _client = null;
            _reconnectAttempts = 0;

            var cancelCount = _pending.Count;
            foreach (var kvp in _pending)
                kvp.Value.TrySetCanceled();
            _pending.Clear();

            ConnectionDebugLogger.Info($"DisconnectAsync: completed in {sw.Elapsed.TotalMilliseconds:F1}ms (cancelled {cancelCount} pending requests)");
            SetStatus(ConnectionStatus.Disconnected);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<Envelope> SendAsync(Envelope request, CancellationToken ct = default)
    {
        if (_disposed)
        {
            ConnectionDebugLogger.Error("SendAsync: ObjectDisposedException");
            throw new ObjectDisposedException(nameof(BrokerClient));
        }

        using var timeoutCts = new CancellationTokenSource();
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        var linkedCt = linkedCts.Token;

        const int maxRetries = 3;
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                return await SendAsyncInternal(request, linkedCt);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                ConnectionDebugLogger.Warn($"SendAsync: cancelled by caller (type={request.MessageType})");
                throw;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Not connected"))
            {
                ConnectionDebugLogger.Warn($"SendAsync: non-retryable — {ex.Message}");
                throw;
            }
            catch (Exception ex) when (IsRetryable(ex) && attempt < maxRetries - 1)
            {
                var delayMs = 500 * Math.Pow(2, attempt);
                ConnectionDebugLogger.Warn($"SendAsync: attempt {attempt + 1} failed with {ex.GetType().Name}, retrying in {delayMs:F0}ms");
                var delay = TimeSpan.FromMilliseconds(delayMs);
                await Task.Delay(delay, ct);

                if (!IsConnected && Status != ConnectionStatus.Reconnecting)
                {
                    try { await ConnectAsync(ct); }
                    catch { }
                }
            }
        }

        return await SendAsyncInternal(request, linkedCt);
    }

    private async Task<Envelope> SendAsyncInternal(Envelope request, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<Envelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[request.CorrelationId] = tcs;

        await _lock.WaitAsync(ct);
        try
        {
            if (_stream == null)
            {
                _pending.TryRemove(request.CorrelationId, out _);
                ConnectionDebugLogger.Error("SendAsyncInternal: stream is null, not connected");
                throw new InvalidOperationException("Not connected. Call ConnectAsync first.");
            }

            await TcpFrame.SendAsync(_stream, request, ct: ct);
        }
        finally
        {
            _lock.Release();
        }

        using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
        return await tcs.Task;
    }

    private static bool IsRetryable(Exception ex)
    {
        return ex is IOException
            or SocketException
            or TimeoutException
            or TaskCanceledException;
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_stream == null)
                {
                    ConnectionDebugLogger.Warn("ReceiveLoopAsync: stream is null, exiting");
                    break;
                }

                var envelope = await TcpFrame.ReceiveAsync(_stream, ct: ct);

                if (envelope == null)
                {
                    ConnectionDebugLogger.Info("ReceiveLoopAsync: null envelope (server closed connection)");
                    break;
                }

                if (_pending.TryRemove(envelope.CorrelationId, out var tcs))
                {
                    tcs.TrySetResult(envelope);
                }
                else if (envelope.MessageType == MessageTypeCode.ChangeNotification)
                {
                    var notification = ProtoHelper.Deserialize<ChangeNotification>(envelope.Payload.ToByteArray());
                    ConnectionDebugLogger.Info($"ReceiveLoopAsync: change notification received (action={notification.Action}, entityId={notification.EntityId})");
                    NotificationReceived?.Invoke(this, notification);
                }
                else if (envelope.MessageType == MessageTypeCode.SessionInvalidated)
                {
                    ConnectionDebugLogger.Info($"ReceiveLoopAsync: session invalidation received");
                    SessionInvalidated?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    ConnectionDebugLogger.Warn($"ReceiveLoopAsync: unmatched envelope corrId={envelope.CorrelationId} (no pending request)");
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException)
            {
                ConnectionDebugLogger.Warn($"ReceiveLoopAsync: connection lost ({ex.GetType().Name}: {ex.Message})");

                if (!_disposed && _reconnectAttempts < MaxReconnectAttempts)
                {
                    ConnectionDebugLogger.Info($"ReceiveLoopAsync: starting auto-reconnect (attempt {_reconnectAttempts + 1}/{MaxReconnectAttempts})");
                    _ = Task.Run(() => AttemptReconnectAsync(ct));
                }
                else
                {
                    ConnectionDebugLogger.Warn($"ReceiveLoopAsync: max reconnects ({MaxReconnectAttempts}) reached or disposed, failing all pending");
                    foreach (var kvp in _pending)
                        kvp.Value.TrySetException(ex);
                    _pending.Clear();
                    SetStatus(ConnectionStatus.Disconnected);
                }
                break;
            }
            catch (Exception ex)
            {
                ConnectionDebugLogger.Error(ex, "ReceiveLoopAsync: unhandled exception");
                foreach (var kvp in _pending)
                    kvp.Value.TrySetException(ex);
                _pending.Clear();
                break;
            }
        }
    }

    private async Task AttemptReconnectAsync(CancellationToken ct)
    {
        SetStatus(ConnectionStatus.Reconnecting);

        while (_reconnectAttempts < MaxReconnectAttempts && !ct.IsCancellationRequested && !_disposed)
        {
            var delay = ReconnectDelays[Math.Min(_reconnectAttempts, ReconnectDelays.Length - 1)];
            ConnectionDebugLogger.Info($"AttemptReconnectAsync: attempt {_reconnectAttempts + 1}/{MaxReconnectAttempts}, waiting {delay.TotalSeconds:F0}s before retry");

            try
            {
                await Task.Delay(delay, ct);
            }
            catch (OperationCanceledException)
            {
                ConnectionDebugLogger.Warn("AttemptReconnectAsync: cancelled during delay");
                break;
            }

            try
            {
                ConnectionDebugLogger.Info($"AttemptReconnectAsync: executing reconnect #{_reconnectAttempts + 1}");
                await ConnectAsync(ct);
                _reconnectAttempts = 0;
                ConnectionDebugLogger.Info($"AttemptReconnectAsync: reconnected successfully");
                return;
            }
            catch (Exception ex)
            {
                _reconnectAttempts++;
                ConnectionDebugLogger.Warn($"AttemptReconnectAsync: reconnect #{_reconnectAttempts} failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        if (_reconnectAttempts >= MaxReconnectAttempts)
        {
            ConnectionDebugLogger.Error($"AttemptReconnectAsync: exhausted {MaxReconnectAttempts} attempts, giving up");
            SetStatus(ConnectionStatus.Disconnected);
            foreach (var kvp in _pending)
                kvp.Value.TrySetException(new InvalidOperationException("Max reconnection attempts exceeded."));
            _pending.Clear();
        }
    }

    /// <summary>
    /// Sends a <see cref="MessageTypeCode.StartServiceRequest"/> to the broker and returns
    /// the deserialized result.
    /// </summary>
    public async Task<ServiceOperationResult> StartServiceAsync(string authToken, CancellationToken ct = default)
    {
        var request = new Envelope
        {
            AuthToken = authToken,
            CorrelationId = Guid.NewGuid().ToString(),
            MessageType = MessageTypeCode.StartServiceRequest,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new StartServiceRequest()))
        };

        var response = await SendAsync(request, ct);
        if (response.StatusCode != 0)
            return new ServiceOperationResult(false, response.ErrorMessage, response.StatusCode);

        var payload = ProtoHelper.Deserialize<StartServiceResponse>(response.Payload.ToByteArray());
        return new ServiceOperationResult(payload.Success, payload.Message, response.StatusCode);
    }

    /// <summary>
    /// Sends a <see cref="MessageTypeCode.StopServiceRequest"/> to the broker and returns
    /// the deserialized result.
    /// </summary>
    public async Task<ServiceOperationResult> StopServiceAsync(string authToken, CancellationToken ct = default)
    {
        var request = new Envelope
        {
            AuthToken = authToken,
            CorrelationId = Guid.NewGuid().ToString(),
            MessageType = MessageTypeCode.StopServiceRequest,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new StopServiceRequest()))
        };

        var response = await SendAsync(request, ct);
        if (response.StatusCode != 0)
            return new ServiceOperationResult(false, response.ErrorMessage, response.StatusCode);

        var payload = ProtoHelper.Deserialize<StopServiceResponse>(response.Payload.ToByteArray());
        return new ServiceOperationResult(payload.Success, payload.Message, response.StatusCode);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        ConnectionDebugLogger.Info("DisposeAsync: disposing BrokerClient");
        _disposed = true;
        _receiveCts?.Cancel();
        _receiveCts?.Dispose();
        _stream?.Dispose();
        _client?.Dispose();
        _lock.Dispose();
        SetStatus(ConnectionStatus.Disconnected);
    }
}
