using Adam.Shared.Contracts;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Adam.BrokerService.Handlers;

public sealed class CollectionHandler
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CollectionHandler> _logger;
    private readonly AuthorizationMiddleware _authz;

    public CollectionHandler(IServiceProvider serviceProvider, ILogger<CollectionHandler> logger, AuthorizationMiddleware authz)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _authz = authz;
    }

    public async Task<Envelope> ListCollectionsAsync(Envelope request, CancellationToken ct)
    {
        if (!await _authz.HasPermissionAsync(request, "collection:read", ct))
            return ErrorResponse(request, 7, "Forbidden");

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var collections = await db.Collections
            .Include(c => c.Children)
            .ToListAsync(ct);

        var rootNodes = collections
            .Where(c => c.ParentId == null)
            .Select(c => BuildNode(c, collections))
            .ToList();

        var response = new ListCollectionsResponse();
        foreach (var node in rootNodes)
            response.Items.Add(node);

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = nameof(ListCollectionsResponse),
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(response)),
            StatusCode = 0
        };
    }

    private static CollectionNode BuildNode(Collection collection, List<Collection> allCollections)
    {
        var node = new CollectionNode
        {
            Id = collection.Id.ToString(),
            Name = collection.Name,
            Description = collection.Description ?? "",
            ParentId = collection.ParentId?.ToString() ?? "",
            AssetCount = collection.Assets?.Count ?? 0
        };

        var children = allCollections.Where(c => c.ParentId == collection.Id);
        foreach (var child in children)
            node.Children.Add(BuildNode(child, allCollections));

        return node;
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
