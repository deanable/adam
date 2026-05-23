using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Adam.Shared.Contracts;
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
        Port = port;
        LoadTlsCertificate();

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start(50);

        _logger.LogInformation("Broker service listening on port {Port} (TLS: {Tls})", port, TlsEnabled);

        _ = IdleMonitorLoopAsync(_cts.Token);

        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_cts.Token);

                if (_connections.Count >= MaxConnections)
                {
                    Interlocked.Increment(ref _rejectedCount);
                    _logger.LogWarning("Connection rejected: max connections ({Max}) reached", MaxConnections);
                    client.Close();
                    continue;
                }

                var connectionId = Guid.NewGuid().ToString("N");
                var state = new ConnectionState(connectionId, client);
                _connections.TryAdd(connectionId, state);

                _logger.LogInformation("Client connected: {ConnectionId} (active: {Count})", connectionId, _connections.Count);

                var task = HandleConnectionAsync(state, _cts.Token);
                _connectionTasks.TryAdd(connectionId, task);
                _ = task.ContinueWith(_ => _connectionTasks.TryRemove(connectionId, out Task? _), TaskScheduler.Default);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("TCP listener shutting down");
        }
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        _listener?.Stop();

        _logger.LogInformation("Broker service stopping: waiting for {Count} connection(s) to drain", _connectionTasks.Count);

        // Give active connections up to 30 seconds to finish
        var drainTasks = _connectionTasks.Values.ToArray();
        if (drainTasks.Length > 0)
        {
            var drainCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await Task.WhenAll(drainTasks.Select(t => t.WaitAsync(drainCts.Token)));
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Drain timeout expired; {Count} connection(s) did not finish gracefully", _connectionTasks.Count);
            }
        }

        foreach (var kvp in _connections)
        {
            try { kvp.Value.Client.Close(); } catch { /* ignore */ }
        }
        _connections.Clear();
        _connectionTasks.Clear();

        _logger.LogInformation("Broker service stopped");
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
                _serverCertificate = new X509Certificate2(certPath, certPassword);
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
        return new X509Certificate2(cert.Export(X509ContentType.Pfx), (string?)null);
    }

    private async Task HandleConnectionAsync(ConnectionState state, CancellationToken ct)
    {
        Stream stream = state.Client.GetStream();
        try
        {
            if (TlsEnabled && _serverCertificate != null)
            {
                var sslStream = new SslStream(stream, leaveInnerStreamOpen: false);
                await sslStream.AuthenticateAsServerAsync(_serverCertificate, clientCertificateRequired: false, enabledSslProtocols: System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13, checkCertificateRevocation: false);
                stream = sslStream;
                _logger.LogDebug("Connection {ConnectionId}: TLS handshake complete", state.Id);
            }

            // Register for change notification broadcasts
            _connectionRegistry.Register(state.Id, stream);

            while (!ct.IsCancellationRequested)
            {
                if (state.RequestCount >= MaxRequestsPerConnection)
                {
                    _logger.LogInformation("Connection {ConnectionId}: request limit ({Limit}) reached, disconnecting",
                        state.Id, MaxRequestsPerConnection);
                    break;
                }

                var envelope = await TcpFrame.ReceiveAsync(stream, ct);
                if (envelope == null) break;

                state.LastActivity = DateTimeOffset.UtcNow;
                Interlocked.Increment(ref state._requestCount);

                // Populate client IP for audit/rate-limiting
                if (string.IsNullOrEmpty(envelope.ClientIp) && state.Client.Client.RemoteEndPoint is IPEndPoint remoteEp)
                    envelope.ClientIp = remoteEp.Address.ToString();

                // Tag envelope with connection ID so handlers know who sent it
                envelope.ConnectionId = state.Id;

                var response = await _handler.HandleAsync(envelope, ct);
                await TcpFrame.SendAsync(stream, response, ct);
            }
        }
        catch (AuthenticationException ex)
        {
            _logger.LogWarning("Connection {ConnectionId} TLS authentication failed: {Message}", state.Id, ex.Message);
        }
        catch (IOException ex)
        {
            _logger.LogWarning("Connection {ConnectionId} lost: {Message}", state.Id, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling connection {ConnectionId}", state.Id);
        }
        finally
        {
            _connectionRegistry.Unregister(state.Id);
            try { stream.Dispose(); } catch { /* ignore */ }
            try { state.Client.Close(); } catch { /* ignore */ }
            _connections.TryRemove(state.Id, out _);
            _logger.LogInformation("Client disconnected: {ConnectionId} (active: {Count})", state.Id, _connections.Count);
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
            foreach (var kvp in _connections)
            {
                if (now - kvp.Value.LastActivity > IdleTimeout)
                {
                    _logger.LogInformation("Connection {ConnectionId} idle for {Idle}s; disconnecting",
                        kvp.Key, (now - kvp.Value.LastActivity).TotalSeconds);
                    try { kvp.Value.Client.Close(); } catch { /* ignore */ }
                }
            }
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
