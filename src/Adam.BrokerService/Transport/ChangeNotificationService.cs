using Adam.Shared.Contracts;
using Adam.Shared.Transport;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Adam.BrokerService.Transport;

/// <summary>
/// Broadcasts change notifications to all connected authenticated clients.
/// Fire-and-forget with per-connection error isolation.
/// </summary>
public sealed class ChangeNotificationService
{
    private readonly ConnectionRegistry _registry;
    private readonly ILogger<ChangeNotificationService> _logger;

    public ChangeNotificationService(ConnectionRegistry registry, ILogger<ChangeNotificationService> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    /// <summary>
    /// Broadcast a change notification to all authenticated connections except the sender.
    /// </summary>
    public async Task BroadcastAsync(
        string entityId,
        string action,
        string changedByUserId,
        string? excludeConnectionId = null,
        CancellationToken ct = default)
    {
        var notification = new ChangeNotification
        {
            EntityId = entityId,
            Action = action,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ChangedByUserId = changedByUserId
        };

        var envelope = new Envelope
        {
            CorrelationId = Guid.NewGuid().ToString(), // server-generated; clients ignore correlation for notifications
            MessageType = MessageTypeCode.ChangeNotification,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(notification))
        };

        _logger.LogDebug("Broadcasting {Action} for {EntityId} to all connections except {Exclude}",
            action, entityId, excludeConnectionId ?? "none");

        await _registry.BroadcastAsync(envelope, excludeConnectionId, ct);
    }

    /// <summary>
    /// Convenience method for asset updates.
    /// </summary>
    public Task NotifyUpdatedAsync(string assetId, string changedByUserId, string? excludeConnectionId = null, CancellationToken ct = default)
        => BroadcastAsync(assetId, "updated", changedByUserId, excludeConnectionId, ct);

    /// <summary>
    /// Convenience method for asset creation.
    /// </summary>
    public Task NotifyCreatedAsync(string assetId, string changedByUserId, string? excludeConnectionId = null, CancellationToken ct = default)
        => BroadcastAsync(assetId, "created", changedByUserId, excludeConnectionId, ct);

    /// <summary>
    /// Convenience method for asset deletion.
    /// </summary>
    public Task NotifyDeletedAsync(string assetId, string changedByUserId, string? excludeConnectionId = null, CancellationToken ct = default)
        => BroadcastAsync(assetId, "deleted", changedByUserId, excludeConnectionId, ct);
}
