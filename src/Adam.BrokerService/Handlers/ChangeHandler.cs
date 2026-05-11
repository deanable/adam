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

    public ChangeHandler(IServiceProvider serviceProvider, ILogger<ChangeHandler> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<Envelope> GetChangesAsync(Envelope request, CancellationToken ct = default)
    {
        var req = ProtoHelper.Deserialize<GetChangesRequest>(request.Payload.ToByteArray());
        var since = DateTimeOffset.FromUnixTimeSeconds(req.SinceTimestamp);

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var changedAssets = await db.DigitalAssets
            .IgnoreQueryFilters()
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
            MessageType = nameof(GetChangesResponse),
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(response)),
            StatusCode = 0
        };
    }
}
