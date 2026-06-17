using Adam.Shared.Contracts;
using Adam.Shared.Data;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Adam.BrokerService.Handlers;

public sealed class ChangeHandler : HandlerBase
{
    public ChangeHandler(IServiceProvider serviceProvider, ILogger<ChangeHandler> logger, AuthorizationMiddleware authz)
        : base(serviceProvider, logger, authz)
    {
    }

    public async Task<Envelope> GetChangesAsync(Envelope request, CancellationToken ct = default)
    {
        if (!await Authz.HasPermissionAsync(request, "asset:read", ct))
            return ErrorResponse(request, ErrorCode.Forbidden, "Forbidden");

        var error = DeserializePayload<GetChangesRequest>(request, out var req);
        if (error != null) return error;
        var since = DateTimeOffset.FromUnixTimeSeconds(req.SinceTimestamp);

        using var scope = ServiceProvider.CreateScope();
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


}
