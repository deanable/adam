using Adam.Shared.Data;
using Adam.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Adam.BrokerService.Handlers;

public sealed class AuditLogger
{
    private readonly ILogger<AuditLogger> _logger;

    public AuditLogger(ILogger<AuditLogger> logger)
    {
        _logger = logger;
    }

    public async Task LogAsync(AppDbContext db, string userId, string action, string entityType, string? entityId, string? details, CancellationToken ct = default)
    {
        var log = new AccessLog
        {
            Id = Guid.NewGuid(),
            UserId = Guid.Parse(userId),
            Action = action,
            EntityType = entityType,
            EntityId = entityId != null ? Guid.Parse(entityId) : null,
            Details = details,
            Timestamp = DateTimeOffset.UtcNow
        };

        db.AccessLogs.Add(log);
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Audit: {Action} on {EntityType} {EntityId} by {UserId}",
            action, entityType, entityId, userId);
    }
}
