using Adam.Shared.Contracts;
using Adam.Shared.Data;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Adam.BrokerService.Handlers;

public sealed class AuditLogHandler : HandlerBase
{
    public AuditLogHandler(IServiceProvider serviceProvider, ILogger<AuditLogHandler> logger, AuthorizationMiddleware authz)
        : base(serviceProvider, logger, authz)
    {
    }

    public async Task<Envelope> ListAuditLogsAsync(Envelope request, CancellationToken ct)
    {
        if (!await Authz.HasPermissionAsync(request, "audit:read", ct))
            return ErrorResponse(request, ErrorCode.Forbidden, "Forbidden");

        var error = DeserializePayload<ListAuditLogsRequest>(request, out var filterReq);
        if (error != null) return error;

        using var scope = ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var query = db.AccessLogs
            .Include(l => l.User)
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrEmpty(filterReq.UserId))
            query = query.Where(l => l.UserId == Guid.Parse(filterReq.UserId));
        if (!string.IsNullOrEmpty(filterReq.Action))
            query = query.Where(l => l.Action == filterReq.Action);
        if (!string.IsNullOrEmpty(filterReq.EntityType))
            query = query.Where(l => l.EntityType == filterReq.EntityType);
        if (filterReq.FromDate.HasValue)
            query = query.Where(l => l.Timestamp >= DateTimeOffset.FromUnixTimeSeconds(filterReq.FromDate.Value));
        if (filterReq.ToDate.HasValue)
            query = query.Where(l => l.Timestamp <= DateTimeOffset.FromUnixTimeSeconds(filterReq.ToDate.Value));

        // Load to memory first, then sort — SQLite cannot ORDER BY DateTimeOffset
        var logs = (await query.ToListAsync(ct))
            .OrderByDescending(l => l.Timestamp)
            .Take(500)
            .ToList();

        var response = new ListAuditLogsResponse();
        foreach (var l in logs)
        {
            response.Items.Add(new AuditLogEntry
            {
                Id = l.Id.ToString(),
                UserId = l.UserId.ToString(),
                Username = l.User?.Username ?? "",
                Action = l.Action,
                EntityType = l.EntityType,
                EntityId = l.EntityId?.ToString(),
                Details = l.Details,
                Timestamp = l.Timestamp.ToUnixTimeSeconds()
            });
        }

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = MessageTypeCode.ListAuditLogsResponse,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(response)),
            StatusCode = 0
        };
    }


}
