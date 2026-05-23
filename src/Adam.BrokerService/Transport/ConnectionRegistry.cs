using System.Collections.Concurrent;
using Adam.Shared.Contracts;
using Adam.Shared.Transport;
using Microsoft.Extensions.Logging;

namespace Adam.BrokerService.Transport;

/// <summary>
/// Tracks active authenticated TCP connections for targeted broadcasts.
/// Thread-safe; all operations are non-blocking.
/// </summary>
public sealed class ConnectionRegistry : IDisposable
{
    private readonly ConcurrentDictionary<string, ConnectionEntry> _connections = new();
    private readonly ILogger<ConnectionRegistry> _logger;

    public ConnectionRegistry(ILogger<ConnectionRegistry> logger)
    {
        _logger = logger;
    }

    public int Count => _connections.Count;

    /// <summary>
    /// Register a new connection (before authentication).
    /// </summary>
    public void Register(string connectionId, Stream stream)
    {
        _connections.TryAdd(connectionId, new ConnectionEntry(connectionId, stream));
        _logger.LogDebug("Connection registered: {ConnectionId} (total: {Count})", connectionId, _connections.Count);
    }

    /// <summary>
    /// Associate a user with an existing connection after successful login.
    /// </summary>
    public void SetUserId(string connectionId, string userId)
    {
        if (_connections.TryGetValue(connectionId, out var entry))
        {
            entry.UserId = userId;
            _logger.LogDebug("Connection {ConnectionId} authenticated as user {UserId}", connectionId, userId);
        }
    }

    /// <summary>
    /// Remove a connection on disconnect.
    /// </summary>
    public void Unregister(string connectionId)
    {
        if (_connections.TryRemove(connectionId, out var entry))
        {
            entry.Dispose();
            _logger.LogDebug("Connection unregistered: {ConnectionId} (total: {Count})", connectionId, _connections.Count);
        }
    }

    /// <summary>
    /// Get all authenticated connection IDs.
    /// </summary>
    public IReadOnlyList<string> GetAuthenticatedConnectionIds()
    {
        return _connections
            .Where(kvp => !string.IsNullOrEmpty(kvp.Value.UserId))
            .Select(kvp => kvp.Key)
            .ToList();
    }

    /// <summary>
    /// Get all authenticated connection IDs except the specified one.
    /// Used to exclude the sender from broadcast.
    /// </summary>
    public IReadOnlyList<string> GetAuthenticatedConnectionIdsExcept(string excludeConnectionId)
    {
        return _connections
            .Where(kvp => kvp.Key != excludeConnectionId && !string.IsNullOrEmpty(kvp.Value.UserId))
            .Select(kvp => kvp.Key)
            .ToList();
    }

    /// <summary>
    /// Send an envelope to a specific connection.
    /// </summary>
    public async Task<bool> SendAsync(string connectionId, Envelope envelope, CancellationToken ct = default)
    {
        if (!_connections.TryGetValue(connectionId, out var entry))
        {
            _logger.LogWarning("Cannot send to unknown connection: {ConnectionId}", connectionId);
            return false;
        }

        try
        {
            await entry.Lock.WaitAsync(ct);
            try
            {
                await TcpFrame.SendAsync(entry.Stream, envelope, ct);
                return true;
            }
            finally
            {
                entry.Lock.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send to connection {ConnectionId}; removing", connectionId);
            Unregister(connectionId);
            return false;
        }
    }

    /// <summary>
    /// Broadcast an envelope to all authenticated connections except the excluded one.
    /// Fire-and-forget with per-connection error isolation.
    /// </summary>
    public async Task BroadcastAsync(Envelope envelope, string? excludeConnectionId = null, CancellationToken ct = default)
    {
        var targets = excludeConnectionId != null
            ? GetAuthenticatedConnectionIdsExcept(excludeConnectionId)
            : GetAuthenticatedConnectionIds();

        if (targets.Count == 0) return;

        var tasks = new List<Task>(targets.Count);
        foreach (var connectionId in targets)
        {
            tasks.Add(SendAsync(connectionId, envelope, ct));
        }

        try
        {
            await Task.WhenAll(tasks);
        }
        catch
        {
            // Individual failures are already logged in SendAsync
        }
    }

    public void Dispose()
    {
        foreach (var entry in _connections.Values)
        {
            entry.Dispose();
        }
        _connections.Clear();
    }

    private sealed class ConnectionEntry : IDisposable
    {
        public string ConnectionId { get; }
        public Stream Stream { get; }
        public SemaphoreSlim Lock { get; }
        public string UserId { get; set; } = string.Empty;

        public ConnectionEntry(string connectionId, Stream stream)
        {
            ConnectionId = connectionId;
            Stream = stream;
            Lock = new SemaphoreSlim(1, 1);
        }

        public void Dispose()
        {
            Lock.Dispose();
        }
    }
}
