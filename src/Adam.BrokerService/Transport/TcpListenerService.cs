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
    private const int MaxRequestsPerConnection = 5;

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly ConcurrentDictionary<string, ConnectionState> _connections = new();
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

                _ = HandleConnectionAsync(state, _cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("TCP listener shutting down");
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener?.Stop();

        foreach (var kvp in _connections)
        {
            kvp.Value.Client.Close();
        }
        _connections.Clear();

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

                state.RequestCount++;
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

    private sealed class ConnectionState
    {
        public string Id { get; }
        public TcpClient Client { get; }
        public int RequestCount { get; set; }

        public ConnectionState(string id, TcpClient client)
        {
            Id = id;
            Client = client;
        }
    }
}
