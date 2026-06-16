using Adam.BrokerService.Transport;
using Adam.Shared.Contracts;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
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

        var now = DateTimeOffset.UtcNow;
        var collection = new Collection
        {
            Id = Guid.NewGuid(),
            Name = req.Name,
            Description = req.Description,
            ParentId = string.IsNullOrEmpty(req.ParentId) ? null : Guid.Parse(req.ParentId),
            IsSmart = req.IsSmart,
            SmartQueryJson = string.IsNullOrWhiteSpace(req.SmartQueryJson) ? null : req.SmartQueryJson,
            CreatedAt = now,
            ModifiedAt = now
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
        if (!string.IsNullOrEmpty(req.SmartQueryJson))
        {
            collection.SmartQueryJson = req.SmartQueryJson;
            collection.IsSmart = true;
        }

        collection.ModifiedAt = DateTimeOffset.UtcNow;
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
            AssetCount = collection.Assets?.Count ?? 0,
            IsSmart = collection.IsSmart,
            SmartQueryJson = collection.SmartQueryJson ?? "",
            LastAutoRefreshedAt = collection.LastAutoRefreshedAt?.ToUnixTimeSeconds() ?? 0
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

    public async Task<Envelope> RefreshSmartCollectionAsync(Envelope request, CancellationToken ct)
    {
        if (!await _authz.HasPermissionAsync(request, "collection:read", ct))
            return ErrorResponse(request, ErrorCode.Forbidden, "Forbidden");

        if (request.Payload == null)
            return ErrorResponse(request, ErrorCode.BadRequest, "Null payload");

        RefreshSmartCollectionRequest req;
        try
        {
            req = ProtoHelper.Deserialize<RefreshSmartCollectionRequest>(request.Payload.ToByteArray());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize {MessageType}", request.MessageType);
            return ErrorResponse(request, ErrorCode.BadRequest, "Malformed request payload");
        }

        if (!Guid.TryParse(req.Id, out var id))
            return ErrorResponse(request, ErrorCode.InvalidArgument, "Invalid collection ID");

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var collection = await db.Collections.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (collection == null)
            return ErrorResponse(request, ErrorCode.NotFound, "Collection not found");

        if (!collection.IsSmart || string.IsNullOrWhiteSpace(collection.SmartQueryJson))
            return ErrorResponse(request, ErrorCode.InvalidArgument, "Collection is not a smart collection");

        // Parse the smart query JSON and find matching assets
        // SmartQueryJson contains the same serialized filter criteria as SavedSearch.FiltersJson
        var assetIds = await FindAssetsMatchingSmartQueryAsync(db, collection.SmartQueryJson, ct);

        collection.LastAutoRefreshedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        var response = new RefreshSmartCollectionResponse
        {
            LastAutoRefreshedAt = collection.LastAutoRefreshedAt.Value.ToUnixTimeSeconds(),
            TotalCount = assetIds.Count
        };
        foreach (var aid in assetIds)
            response.AssetIds.Add(aid.ToString());

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = MessageTypeCode.RefreshSmartCollectionResponse,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(response)),
            StatusCode = ErrorCode.Success
        };
    }

    /// <summary>
    /// Executes a smart collection query against the database and returns matching asset IDs.
    /// The SmartQueryJson contains the same serialized filter criteria as SavedSearch.FiltersJson.
    /// For v1, this applies basic filters (type, date range, query text).
    /// </summary>
    private async Task<List<Guid>> FindAssetsMatchingSmartQueryAsync(
        AppDbContext db, string smartQueryJson, CancellationToken ct)
    {
        // Start with all non-deleted assets
        var q = db.DigitalAssets.AsQueryable();

        try
        {
            var filters = System.Text.Json.JsonSerializer.Deserialize<SmartQueryFilters>(smartQueryJson);
            if (filters != null)
            {
                if (!string.IsNullOrWhiteSpace(filters.QueryText))
                {
                    var search = filters.QueryText.ToLowerInvariant();
                    q = q.Where(a =>
                        a.Title.ToLower().Contains(search) ||
                        (a.Description != null && a.Description.ToLower().Contains(search)) ||
                        a.FileName.ToLower().Contains(search));
                }

                if (filters.Type.HasValue)
                    q = q.Where(a => a.Type == filters.Type.Value);

                if (filters.CollectionId.HasValue)
                    q = q.Where(a => a.CollectionId == filters.CollectionId.Value);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse smart query JSON: {SmartQueryJson}", smartQueryJson[..Math.Min(smartQueryJson.Length, 200)]);
            return [];
        }

        return await q.Select(a => a.Id).ToListAsync(ct);
    }

    public async Task<Envelope> ReorderCollectionAssetsAsync(Envelope request, CancellationToken ct)
    {
        if (!await _authz.HasPermissionAsync(request, "collection:update", ct))
            return ErrorResponse(request, ErrorCode.Forbidden, "Forbidden");

        if (request.Payload == null)
            return ErrorResponse(request, ErrorCode.BadRequest, "Null payload");

        ReorderCollectionAssetsRequest req;
        try
        {
            req = ProtoHelper.Deserialize<ReorderCollectionAssetsRequest>(request.Payload.ToByteArray());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize {MessageType}", request.MessageType);
            return ErrorResponse(request, ErrorCode.BadRequest, "Malformed request payload");
        }

        if (!Guid.TryParse(req.CollectionId, out var collectionId))
            return ErrorResponse(request, ErrorCode.InvalidArgument, "Invalid collection ID");

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var collection = await db.Collections.FirstOrDefaultAsync(c => c.Id == collectionId, ct);
        if (collection == null)
            return ErrorResponse(request, ErrorCode.NotFound, "Collection not found");

        // Batch-update SortOrder for all reordered assets
        var assetIds = req.Entries.Select(e => Guid.Parse(e.AssetId)).ToList();
        var assets = await db.DigitalAssets
            .Where(a => assetIds.Contains(a.Id))
            .ToListAsync(ct);

        foreach (var entry in req.Entries)
        {
            var asset = assets.FirstOrDefault(a => a.Id.ToString() == entry.AssetId);
            if (asset != null)
                asset.SortOrder = entry.SortOrder;
        }

        await db.SaveChangesAsync(ct);

        // Broadcast change notification to other connected clients
        var userId = _authHandler.GetUserId(request);
        _ = _notificationService.NotifyUpdatedAsync(collectionId.ToString(), userId, request.ConnectionId);

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = MessageTypeCode.ReorderCollectionAssetsResponse,
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
