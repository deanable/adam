using Adam.Shared.Contracts;
using Adam.Shared.Data;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Adam.BrokerService.Handlers;

public sealed class ChangeHandler
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ChangeHandler> _logger;
    private readonly AuthorizationMiddleware _authz;

    public ChangeHandler(IServiceProvider serviceProvider, ILogger<ChangeHandler> logger, AuthorizationMiddleware authz)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _authz = authz;
    }

    public async Task<Envelope> GetChangesAsync(Envelope request, CancellationToken ct = default)
    {
        if (!await _authz.HasPermissionAsync(request, "asset:read", ct))
            return ErrorResponse(request, ErrorCode.Forbidden, "Forbidden");

        if (request.Payload == null)
            return ErrorResponse(request, ErrorCode.BadRequest, "Null payload");
        GetChangesRequest req;
        try
        {
            req = ProtoHelper.Deserialize<GetChangesRequest>(request.Payload.ToByteArray());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize {MessageType}", request.MessageType);
            return ErrorResponse(request, ErrorCode.BadRequest, "Malformed request payload");
        }
        var since = DateTimeOffset.FromUnixTimeSeconds(req.SinceTimestamp);

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var changedAssets = await db.DigitalAssets
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(a => a.ModifiedAt > since)
            .OrderBy(a => a.ModifiedAt)
            .ToListAsync(ct);

        var response = new GetChangesResponse();
        foreach (var asset in changedAssets)
        {
            response.Changes.Add(new ChangeEvent
            {
                EntityId = asset.Id.ToString(),
                Action = asset.IsDeleted ? "deleted" : "updated",
                Timestamp = asset.ModifiedAt.ToUnixTimeSeconds()
            });
        }

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = MessageTypeCode.GetChangesResponse,
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
