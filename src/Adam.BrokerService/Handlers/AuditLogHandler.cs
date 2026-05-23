using Adam.Shared.Contracts;
using Adam.Shared.Data;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Adam.BrokerService.Handlers;

public sealed class AuditLogHandler
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AuditLogHandler> _logger;
    private readonly AuthorizationMiddleware _authz;

    public AuditLogHandler(IServiceProvider serviceProvider, ILogger<AuditLogHandler> logger, AuthorizationMiddleware authz)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _authz = authz;
    }

    public async Task<Envelope> ListAuditLogsAsync(Envelope request, CancellationToken ct)
    {
        if (!await _authz.HasPermissionAsync(request, "audit:read", ct))
            return ErrorResponse(request, 7, "Forbidden");

        var filterReq = ProtoHelper.Deserialize<ListAuditLogsRequest>(request.Payload.ToByteArray());

        using var scope = _serviceProvider.CreateScope();
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

        var logs = await query.OrderByDescending(l => l.Timestamp).Take(500).ToListAsync(ct);

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

    private static Envelope ErrorResponse(Envelope request, int code, string message)
    {
        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = request.MessageType,
            StatusCode = code,
            ErrorMessage = message
        };
    }
}
