using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Adam.Shared.Contracts;
using Adam.Shared.Transport;
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
    private long _rejectedCount;

    public int Port { get; private set; } = 9100;
    public int ActiveConnectionCount => _connections.Count;
    public long RejectedConnectionCount => Interlocked.Read(ref _rejectedCount);

    public TcpListenerService(ILogger<TcpListenerService> logger, IConnectionHandler handler)
    {
        _logger = logger;
        _handler = handler;
    }

    public async Task StartAsync(int port, CancellationToken ct = default)
    {
        Port = port;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start(50);

        _logger.LogInformation("Broker service listening on port {Port}", port);

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

    private async Task HandleConnectionAsync(ConnectionState state, CancellationToken ct)
    {
        try
        {
            using var stream = state.Client.GetStream();
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
                var response = await _handler.HandleAsync(envelope, ct);
                await TcpFrame.SendAsync(stream, response, ct);
            }
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
            state.Client.Close();
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
