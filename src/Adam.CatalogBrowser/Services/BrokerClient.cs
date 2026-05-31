using System.Collections.Concurrent;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Adam.Shared.Contracts;
using Adam.Shared.Transport;
using Google.Protobuf;

namespace Adam.CatalogBrowser.Services;

public enum ConnectionStatus
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting
}

public sealed class BrokerClient : IAsyncDisposable
{
    private readonly string _host;
    private readonly int _port;
    private readonly bool _useTls;
    private readonly bool _allowSelfSigned;
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

    public BrokerClient(string host, int port, bool useTls = false, bool allowSelfSigned = false)
    {
        _host = host;
        _port = port;
        _useTls = useTls;
        _allowSelfSigned = allowSelfSigned;
    }

    private void SetStatus(ConnectionStatus status)
    {
        if (Status == status) return;
        Status = status;
        StatusChanged?.Invoke(this, status);
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BrokerClient));

        await _lock.WaitAsync(ct);
        try
        {
            if (_client?.Connected == true) return;

            SetStatus(ConnectionStatus.Connecting);
            _client?.Dispose();
            _client = new TcpClient();
            await _client.ConnectAsync(_host, _port, ct);
            var networkStream = _client.GetStream();
            _reconnectAttempts = 0;

            if (_useTls)
            {
                var sslStream = new SslStream(networkStream, leaveInnerStreamOpen: false, userCertificateValidationCallback: ValidateServerCertificate);
                await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = _host,
                    EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                }, ct);
                _stream = sslStream;
            }
            else
            {
                _stream = networkStream;
            }

            _receiveCts?.Cancel();
            _receiveCts = new CancellationTokenSource();
            _ = ReceiveLoopAsync(_receiveCts.Token);

            SetStatus(ConnectionStatus.Connected);
        }
        catch
        {
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
        await _lock.WaitAsync();
        try
        {
            _receiveCts?.Cancel();
            _stream?.Dispose();
            _client?.Dispose();
            _stream = null;
            _client = null;
            _reconnectAttempts = 0;

            foreach (var kvp in _pending)
                kvp.Value.TrySetCanceled();
            _pending.Clear();

            SetStatus(ConnectionStatus.Disconnected);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<Envelope> SendAsync(Envelope request, CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BrokerClient));

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
                throw;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Not connected"))
            {
                // Non-retryable: not authenticated / not connected by design
                throw;
            }
            catch (Exception ex) when (IsRetryable(ex) && attempt < maxRetries - 1)
            {
                var delay = TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt));
                await Task.Delay(delay, ct);
                // Ensure connection before retry
                if (!IsConnected && Status != ConnectionStatus.Reconnecting)
                {
                    try { await ConnectAsync(ct); }
                    catch { /* reconnect will be attempted by ReceiveLoop */ }
                }
            }
        }

        // Should not reach here, but fallback
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
                throw new InvalidOperationException("Not connected. Call ConnectAsync first.");
            }

            await TcpFrame.SendAsync(_stream, request, ct);
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
                if (_stream == null) break;

                var envelope = await TcpFrame.ReceiveAsync(_stream, ct);
                if (envelope == null) break;

                if (_pending.TryRemove(envelope.CorrelationId, out var tcs))
                {
                    tcs.TrySetResult(envelope);
                }
                else if (envelope.MessageType == MessageTypeCode.ChangeNotification)
                {
                    // Server-initiated push notification (no pending request)
                    var notification = ProtoHelper.Deserialize<ChangeNotification>(envelope.Payload.ToByteArray());
                    NotificationReceived?.Invoke(this, notification);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException)
            {
                // Connection lost — trigger auto-reconnect
                if (!_disposed && _reconnectAttempts < MaxReconnectAttempts)
                {
                    _ = Task.Run(() => AttemptReconnectAsync(ct));
                }
                else
                {
                    foreach (var kvp in _pending)
                        kvp.Value.TrySetException(ex);
                    _pending.Clear();
                    SetStatus(ConnectionStatus.Disconnected);
                }
                break;
            }
            catch (Exception ex)
            {
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
            try
            {
                await Task.Delay(delay, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await ConnectAsync(ct);
                _reconnectAttempts = 0;
                return;
            }
            catch
            {
                _reconnectAttempts++;
            }
        }

        if (_reconnectAttempts >= MaxReconnectAttempts)
        {
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
        if (_disposed) return;
        _disposed = true;
        _receiveCts?.Cancel();
        _receiveCts?.Dispose();
        _stream?.Dispose();
        _client?.Dispose();
        _lock.Dispose();
        SetStatus(ConnectionStatus.Disconnected);
    }
}
