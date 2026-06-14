using Adam.BrokerService.Transport;
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
    private readonly ChangeNotificationService _notificationService;
    private readonly AuthHandler _authHandler;

    public CollectionHandler(IServiceProvider serviceProvider, ILogger<CollectionHandler> logger, AuthorizationMiddleware authz, ChangeNotificationService notificationService, AuthHandler authHandler)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _authz = authz;
        _notificationService = notificationService;
        _authHandler = authHandler;
    }

    public async Task<Envelope> CreateCollectionAsync(Envelope request, CancellationToken ct)
    {
        if (!await _authz.HasPermissionAsync(request, "collection:create", ct))
            return ErrorResponse(request, ErrorCode.Forbidden, "Forbidden");

        if (request.Payload == null)
            return ErrorResponse(request, ErrorCode.BadRequest, "Null payload");
        CreateCollectionRequest req;
        try
        {
            req = ProtoHelper.Deserialize<CreateCollectionRequest>(request.Payload.ToByteArray());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize {MessageType}", request.MessageType);
            return ErrorResponse(request, ErrorCode.BadRequest, "Malformed request payload");
        }

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var collection = new Collection
        {
            Id = Guid.NewGuid(),
            Name = req.Name,
            Description = req.Description,
            ParentId = string.IsNullOrEmpty(req.ParentId) ? null : Guid.Parse(req.ParentId)
        };

        db.Collections.Add(collection);
        await db.SaveChangesAsync(ct);

        // Broadcast change notification to other connected clients (T10.10)
        var userId = _authHandler.GetUserId(request);
        _ = _notificationService.NotifyCreatedAsync(collection.Id.ToString(), userId, request.ConnectionId);

        var response = new CreateCollectionResponse
        {
            Id = collection.Id.ToString()
        };

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = MessageTypeCode.CreateCollectionResponse,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(response)),
            StatusCode = 0
        };
    }

    public async Task<Envelope> ListCollectionsAsync(Envelope request, CancellationToken ct)
    {
        if (!await _authz.HasPermissionAsync(request, "collection:read", ct))
            return ErrorResponse(request, ErrorCode.Forbidden, "Forbidden");

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var collections = await db.Collections
            .Include(c => c.Children)
            .AsNoTracking()
            .AsSplitQuery()
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
            MessageType = MessageTypeCode.ListCollectionsResponse,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(response)),
            StatusCode = 0
        };
    }

    public async Task<Envelope> UpdateCollectionAsync(Envelope request, CancellationToken ct)
    {
        if (!await _authz.HasPermissionAsync(request, "collection:update", ct))
            return ErrorResponse(request, ErrorCode.Forbidden, "Forbidden");

        if (request.Payload == null)
            return ErrorResponse(request, ErrorCode.BadRequest, "Null payload");
        UpdateCollectionRequest req;
        try
        {
            req = ProtoHelper.Deserialize<UpdateCollectionRequest>(request.Payload.ToByteArray());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize {MessageType}", request.MessageType);
            return ErrorResponse(request, ErrorCode.BadRequest, "Malformed request payload");
        }

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var collection = await db.Collections.FirstOrDefaultAsync(c => c.Id == Guid.Parse(req.Id), ct);
        if (collection == null)
            return ErrorResponse(request, ErrorCode.NotFound, "Collection not found");

        if (!string.IsNullOrEmpty(req.Name))
            collection.Name = req.Name;
        if (!string.IsNullOrEmpty(req.Description))
            collection.Description = req.Description;

        await db.SaveChangesAsync(ct);

        // Broadcast change notification to other connected clients (T10.10)
        var userId = _authHandler.GetUserId(request);
        _ = _notificationService.NotifyUpdatedAsync(collection.Id.ToString(), userId, request.ConnectionId);

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = MessageTypeCode.UpdateCollectionRequest,
            StatusCode = 0
        };
    }

    public async Task<Envelope> DeleteCollectionAsync(Envelope request, CancellationToken ct)
    {
        if (!await _authz.HasPermissionAsync(request, "collection:delete", ct))
            return ErrorResponse(request, ErrorCode.Forbidden, "Forbidden");

        if (request.Payload == null)
            return ErrorResponse(request, ErrorCode.BadRequest, "Null payload");
        DeleteCollectionRequest req;
        try
        {
            req = ProtoHelper.Deserialize<DeleteCollectionRequest>(request.Payload.ToByteArray());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize {MessageType}", request.MessageType);
            return ErrorResponse(request, ErrorCode.BadRequest, "Malformed request payload");
        }

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var collection = await db.Collections.FirstOrDefaultAsync(c => c.Id == Guid.Parse(req.Id), ct);
        if (collection == null)
            return ErrorResponse(request, ErrorCode.NotFound, "Collection not found");

        if (req.CascadeChildren)
        {
            // T10.6: Load all collections and recursively delete descendants
            var allCollections = await db.Collections
                .AsNoTracking()
                .ToListAsync(ct);
            var descendantIds = CollectDescendantCollectionIds(allCollections, collection.Id);
            if (descendantIds.Count > 0)
            {
                var descendants = await db.Collections
                    .Where(c => descendantIds.Contains(c.Id))
                    .ToListAsync(ct);
                db.Collections.RemoveRange(descendants);
            }
        }

        db.Collections.Remove(collection);
        await db.SaveChangesAsync(ct);

        // Broadcast change notification to other connected clients (T10.10)
        var userId = _authHandler.GetUserId(request);
        _ = _notificationService.NotifyDeletedAsync(collection.Id.ToString(), userId, request.ConnectionId);

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = MessageTypeCode.DeleteCollectionResponse,
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

    /// <summary>
    /// Recursively collects IDs of all descendant collections (T10.6 cascade delete).
    /// </summary>
    private static List<Guid> CollectDescendantCollectionIds(List<Collection> allCollections, Guid parentId)
    {
        var ids = new List<Guid>();
        foreach (var child in allCollections.Where(c => c.ParentId == parentId))
        {
            ids.Add(child.Id);
            ids.AddRange(CollectDescendantCollectionIds(allCollections, child.Id));
        }
        return ids;
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
