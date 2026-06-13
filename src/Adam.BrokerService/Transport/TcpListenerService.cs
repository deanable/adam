using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Adam.Shared.Contracts;
using Adam.Shared.Services;
using Adam.Shared.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Adam.BrokerService.Transport;

public sealed class TcpListenerService
{
    private const int MaxConnections = 50;
    private const int MaxRequestsPerConnection = 500;
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(5);

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly ConcurrentDictionary<string, ConnectionState> _connections = new();
    private readonly ConcurrentDictionary<string, Task> _connectionTasks = new();
    private readonly ILogger<TcpListenerService> _logger;
    private readonly IConnectionHandler _handler;
    private readonly IConfiguration _configuration;
    private readonly ConnectionRegistry _connectionRegistry;
    private long _rejectedCount;
    private X509Certificate2? _serverCertificate;

    public int Port { get; private set; } = 9100;
    public int ActiveConnectionCount => _connections.Count;
    public long RejectedConnectionCount => Interlocked.Read(ref _rejectedCount);
    public bool TlsEnabled { get; private set; }

    public TcpListenerService(ILogger<TcpListenerService> logger, IConnectionHandler handler, IConfiguration configuration, ConnectionRegistry connectionRegistry)
    {
        _logger = logger;
        _handler = handler;
        _configuration = configuration;
        _connectionRegistry = connectionRegistry;
    }

    public async Task StartAsync(int port, CancellationToken ct = default)
    {
        ConnectionDebugLogger.Info($"[SERVER] TcpListenerService.StartAsync(port={port}) called");
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("[TIMING] TcpListenerService.StartAsync(port={Port}) called", port);

        Port = port;
        LoadTlsCertificate();
        _logger.LogInformation("[TIMING] TLS certificate loaded in {ElapsedMs:F0}ms (TLS enabled: {TlsEnabled})", sw.Elapsed.TotalMilliseconds, TlsEnabled);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start(50);
        _logger.LogInformation("[TIMING] TcpListener.Start() completed in {ElapsedMs:F0}ms — now listening on port {Port}", sw.Elapsed.TotalMilliseconds, port);
        _logger.LogInformation("Broker service listening on port {Port} (TLS: {Tls}, backlog=50)", port, TlsEnabled);

        _ = IdleMonitorLoopAsync(_cts.Token);
        _logger.LogInformation("[TIMING] Idle monitor loop started. Total startup: {ElapsedMs:F0}ms — entering accept loop", sw.Elapsed.TotalMilliseconds);
        ConnectionDebugLogger.Info($"[SERVER] Entering accept loop (total startup: {sw.Elapsed.TotalMilliseconds:F0}ms)");

        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                var acceptSw = Stopwatch.StartNew();
                var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                var acceptMs = acceptSw.Elapsed.TotalMilliseconds;

                _logger.LogInformation("[TIMING] AcceptTcpClientAsync returned in {AcceptMs:F0}ms (active connections: {Count})",
                    acceptMs, _connections.Count);
                if (_connections.Count >= MaxConnections)
                {
                    Interlocked.Increment(ref _rejectedCount);
                    _logger.LogWarning("Connection rejected: max connections ({Max}) reached", MaxConnections);
                    ConnectionDebugLogger.Warn($"[SERVER] Connection rejected: max connections ({MaxConnections}) reached");
                client.Close();
                    continue;
                }

                var connectionId = Guid.NewGuid().ToString("N");
                var remoteEp = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
                var state = new ConnectionState(connectionId, client);
                _connections.TryAdd(connectionId, state);

                _logger.LogInformation("Client connected: {ConnectionId} (active: {Count})", connectionId, _connections.Count);
                _logger.LogDebug("[DIAG] New connection from {RemoteEndpoint}", remoteEp);
                var task = HandleConnectionAsync(state, _cts.Token);
                _connectionTasks.TryAdd(connectionId, task);
                _ = task.ContinueWith(_ => _connectionTasks.TryRemove(connectionId, out Task? _), TaskScheduler.Default);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("[TIMING] TCP listener accept loop cancelled after {ElapsedMs:F0}ms total runtime", sw.Elapsed.TotalMilliseconds);
        }
    }

    public async Task StopAsync()
    {
        ConnectionDebugLogger.Info($"[SERVER] TcpListenerService.StopAsync() called with {_connections.Count} active connections");
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("[TIMING] TcpListenerService.StopAsync() called with {Count} active connections", _connections.Count);

        _cts?.Cancel();
        _listener?.Stop();
        _logger.LogInformation("[TIMING] TcpListener stopped in {ElapsedMs:F0}ms", sw.Elapsed.TotalMilliseconds);
        _logger.LogInformation("Broker service stopping: waiting for {Count} connection task(s) to drain", _connectionTasks.Count);
        ConnectionDebugLogger.Info($"[SERVER] Draining {_connectionTasks.Count} connection(s) with 30s timeout...");

        var drainTasks = _connectionTasks.Values.ToArray();
        if (drainTasks.Length > 0)
        {
            var drainCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            _logger.LogInformation("[TIMING] Draining {Count} connection(s) with 30s timeout...", drainTasks.Length);
            try
            {
                await Task.WhenAll(drainTasks.Select(t => t.WaitAsync(drainCts.Token)));
                _logger.LogInformation("[TIMING] All {Count} connections drained in {ElapsedMs:F0}ms",
                    drainTasks.Length, sw.Elapsed.TotalMilliseconds);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("[TIMING] Drain timeout expired after {ElapsedMs:F0}ms; {Count} connection(s) did not finish gracefully",
                    sw.Elapsed.TotalMilliseconds, _connectionTasks.Count);
                ConnectionDebugLogger.Warn($"[SERVER] Drain timeout expired after {sw.Elapsed.TotalMilliseconds:F0}ms; {_connectionTasks.Count} connections did not finish");
            }
        }

        _logger.LogInformation("[TIMING] Closing {Count} remaining connections...", _connections.Count);
        foreach (var kvp in _connections)
        {
            try { kvp.Value.Client.Close(); } catch { /* ignore */ }
        }
        _connections.Clear();
        _connectionTasks.Clear();

        _logger.LogInformation("[TIMING] TcpListenerService stopped in {ElapsedMs:F0}ms", sw.Elapsed.TotalMilliseconds);
    }

    private void LoadTlsCertificate()
    {
        var tlsSection = _configuration.GetSection("Broker:Tls");
        TlsEnabled = tlsSection.GetValue<bool>("Enabled", false);
        if (!TlsEnabled) return;

        var certPath = tlsSection.GetValue<string>("CertificatePath", "");
        var certPassword = tlsSection.GetValue<string>("CertificatePassword", "");
        var thumbprint = tlsSection.GetValue<string>("CertificateThumbprint", "");

        try
        {
            if (!string.IsNullOrEmpty(certPath) && File.Exists(certPath))
            {
                _serverCertificate = X509CertificateLoader.LoadPkcs12FromFile(certPath, certPassword, X509KeyStorageFlags.DefaultKeySet, loaderLimits: null);
                _logger.LogInformation("TLS certificate loaded from path: {Path}", certPath);
            }
            else if (!string.IsNullOrEmpty(thumbprint))
            {
                using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
                store.Open(OpenFlags.ReadOnly);
                var cert = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false)
                    .OfType<X509Certificate2>().FirstOrDefault();
                store.Close();
                if (cert != null)
                {
                    _serverCertificate = cert;
                    _logger.LogInformation("TLS certificate loaded from store by thumbprint");
                }
                else
                {
                    _logger.LogError("TLS certificate with thumbprint {Thumbprint} not found in LocalMachine\\My", thumbprint);
                    TlsEnabled = false;
                }
            }
            else
            {
                _logger.LogWarning("TLS enabled but no certificate configured. Generating self-signed dev certificate...");
                _serverCertificate = GenerateSelfSignedCertificate();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load TLS certificate. Falling back to plaintext.");
            TlsEnabled = false;
        }
    }

    private static X509Certificate2 GenerateSelfSignedCertificate()
    {
        var subjectName = new X500DistinguishedName("CN=adam-broker-dev");
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subjectName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(san.Build());

        var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(1));
        // Export and re-import to get a certificate with a private key that works across platforms
        return X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pfx), (string?)null, X509KeyStorageFlags.DefaultKeySet, loaderLimits: null);
    }

    private async Task HandleConnectionAsync(ConnectionState state, CancellationToken ct)
    {
        var connectionSw = Stopwatch.StartNew();
        var messageCount = 0;
        Stream stream = state.Client.GetStream();

        var localEp = state.Client.Client.LocalEndPoint?.ToString() ?? "unknown";
        var remoteEp = state.Client.Client.RemoteEndPoint?.ToString() ?? "unknown";

        _logger.LogInformation("[TIMING] Connection {ConnectionId}: starting handler (local={LocalEP}, remote={RemoteEP})",
            state.Id, localEp, remoteEp);
        try
        {
            if (TlsEnabled && _serverCertificate != null)
            {
                var tlsSw = Stopwatch.StartNew();
                var sslStream = new SslStream(stream, leaveInnerStreamOpen: false);
                await sslStream.AuthenticateAsServerAsync(_serverCertificate, clientCertificateRequired: false, enabledSslProtocols: System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13, checkCertificateRevocation: false);
                stream = sslStream;
                _logger.LogInformation("[TIMING] Connection {ConnectionId}: TLS handshake completed in {ElapsedMs:F0}ms",
                    state.Id, tlsSw.Elapsed.TotalMilliseconds);
            }
            else
            {
                _logger.LogDebug("Connection {ConnectionId}: TLS not enabled, using plain TCP stream", state.Id);
            }

            // Register for change notification broadcasts
            _connectionRegistry.Register(state.Id, stream);
            _logger.LogDebug("Connection {ConnectionId}: registered for change notifications", state.Id);

            while (!ct.IsCancellationRequested)
            {
                if (state.RequestCount >= MaxRequestsPerConnection)
                {
                    _logger.LogInformation("Connection {ConnectionId}: request limit ({Limit}) reached after {Count} requests, disconnecting (elapsed={ElapsedMs:F0}ms)",
                        state.Id, MaxRequestsPerConnection, state.RequestCount, connectionSw.Elapsed.TotalMilliseconds);
                    break;
                }

                var envelope = await TcpFrame.ReceiveAsync(stream, ct: ct);
                if (envelope == null)
                {
                    _logger.LogInformation("Connection {ConnectionId}: null envelope received (client disconnected), handled {Count} messages in {ElapsedMs:F0}ms",
                        state.Id, messageCount, connectionSw.Elapsed.TotalMilliseconds);
                    ConnectionDebugLogger.Info($"[SERVER] Connection {state.Id}: client disconnected, handled {messageCount} messages");
                    break;
                }

                messageCount++;
                state.LastActivity = DateTimeOffset.UtcNow;
                Interlocked.Increment(ref state._requestCount);

                // Populate client IP for audit/rate-limiting
                if (string.IsNullOrEmpty(envelope.ClientIp) && state.Client.Client.RemoteEndPoint is IPEndPoint remoteIpEp)
                    envelope.ClientIp = remoteIpEp.Address.ToString();

                // Tag envelope with connection ID so handlers know who sent it
                envelope.ConnectionId = state.Id;

                var requestSw = Stopwatch.StartNew();
                var response = await _handler.HandleAsync(envelope, ct);
                await TcpFrame.SendAsync(stream, response, ct: ct);
                _logger.LogDebug("Connection {ConnectionId}: processed message #{Count} (type={Type}) in {ElapsedMs:F0}ms",
                    state.Id, messageCount, envelope.MessageType, requestSw.Elapsed.TotalMilliseconds);
            }
        }
        catch (AuthenticationException ex)
        {
            _logger.LogWarning("Connection {ConnectionId} TLS authentication failed after {ElapsedMs:F0}ms: {Message}",
                state.Id, connectionSw.Elapsed.TotalMilliseconds, ex.Message);
            ConnectionDebugLogger.Error(ex, $"[SERVER] Connection {state.Id}: TLS authentication failed after {connectionSw.Elapsed.TotalMilliseconds:F0}ms");
        }
        catch (IOException ex)
        {
            _logger.LogWarning("Connection {ConnectionId} lost after {ElapsedMs:F0}ms: {Message}",
                state.Id, connectionSw.Elapsed.TotalMilliseconds, ex.Message);
            ConnectionDebugLogger.Warn($"[SERVER] Connection {state.Id}: connection lost after {connectionSw.Elapsed.TotalMilliseconds:F0}ms: {ex.Message}");
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogWarning("Connection {ConnectionId} disposed after {ElapsedMs:F0}ms: {Message}",
                state.Id, connectionSw.Elapsed.TotalMilliseconds, ex.Message);
            ConnectionDebugLogger.Warn($"[SERVER] Connection {state.Id}: disposed after {connectionSw.Elapsed.TotalMilliseconds:F0}ms: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling connection {ConnectionId} after {ElapsedMs:F0}ms and {Count} messages",
                state.Id, connectionSw.Elapsed.TotalMilliseconds, messageCount);
            ConnectionDebugLogger.Error(ex, $"[SERVER] Connection {state.Id}: unhandled error after {connectionSw.Elapsed.TotalMilliseconds:F0}ms and {messageCount} messages");
        }
        finally
        {
            _connectionRegistry.Unregister(state.Id);
            try { stream.Dispose(); } catch { /* ignore */ }
            try { state.Client.Close(); } catch { /* ignore */ }
            _connections.TryRemove(state.Id, out _);
            _logger.LogInformation("Connection {ConnectionId}: disconnected (elapsed={ElapsedMs:F0}ms, active: {Count})",
                state.Id, connectionSw.Elapsed.TotalMilliseconds, _connections.Count);
            ConnectionDebugLogger.Info($"[SERVER] Connection {state.Id}: disconnected (elapsed={connectionSw.Elapsed.TotalMilliseconds:F1}ms, active={_connections.Count}, messages={messageCount})");
        }
    }

    private async Task IdleMonitorLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var now = DateTimeOffset.UtcNow;
            var idleCount = 0;
            foreach (var kvp in _connections)
            {
                var idle = now - kvp.Value.LastActivity;
                if (idle > IdleTimeout)
                {
                    _logger.LogInformation("Connection {ConnectionId} idle for {Idle}s; disconnecting",
                        kvp.Key, idle.TotalSeconds);
                    ConnectionDebugLogger.Info($"[SERVER] Connection {kvp.Key} idle for {idle.TotalSeconds:F0}s; disconnecting");
                    try { kvp.Value.Client.Close(); } catch { /* ignore */ }
                    idleCount++;
                }
            }
            if (idleCount > 0)
                ConnectionDebugLogger.Info($"[SERVER] IdleMonitor: disconnected {idleCount} idle connection(s) (active: {_connections.Count})");
        }
    }

    private sealed class ConnectionState
    {
        public string Id { get; }
        public TcpClient Client { get; }
        public DateTimeOffset LastActivity { get; set; }
        internal long _requestCount;
        public long RequestCount => Interlocked.Read(ref _requestCount);

        public ConnectionState(string id, TcpClient client)
        {
            Id = id;
            Client = client;
            LastActivity = DateTimeOffset.UtcNow;
        }
    }
}
