using Adam.BrokerService.Transport;
using Adam.Shared.Contracts;
using Adam.Shared.Data;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Adam.BrokerService.Handlers;

public sealed class AssetHandler
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AssetHandler> _logger;
    private readonly AuthorizationMiddleware _authz;
    private readonly ChangeNotificationService _notificationService;
    private readonly AuthHandler _authHandler;

    public AssetHandler(IServiceProvider serviceProvider, ILogger<AssetHandler> logger, AuthorizationMiddleware authz, ChangeNotificationService notificationService, AuthHandler authHandler)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _authz = authz;
        _notificationService = notificationService;
        _authHandler = authHandler;
    }

    public async Task<Envelope> ListAssetsAsync(Envelope request, CancellationToken ct)
    {
        if (!await _authz.HasPermissionAsync(request, "asset:read", ct))
            return ErrorResponse(request, 7, "Forbidden");

        var req = ProtoHelper.Deserialize<ListAssetsRequest>(request.Payload.ToByteArray());

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var query = db.DigitalAssets
            .Include(a => a.Collection)
            .Include(a => a.Keywords)
            .AsNoTracking()
            .AsSplitQuery()
            .AsQueryable();

        if (!string.IsNullOrEmpty(req.Search))
            query = query.Where(a =>
                a.Title.Contains(req.Search) || a.Description!.Contains(req.Search) || a.Keywords.Any(k => k.Name.Contains(req.Search)));
        if (!string.IsNullOrEmpty(req.Type))
            query = query.Where(a => a.Type.ToString() == req.Type);
        if (!string.IsNullOrEmpty(req.CollectionId))
            query = query.Where(a => a.CollectionId == Guid.Parse(req.CollectionId));
        if (req.Tags.Count > 0)
            query = query.Where(a => a.Keywords.Any(k => req.Tags.Contains(k.Name)));

        if (!string.IsNullOrEmpty(req.FolderPath))
        {
            var prefix = req.FolderPath.Replace('\\', '/');
            if (!prefix.EndsWith("/"))
                prefix += "/";
            query = query.Where(a => a.StoragePath.StartsWith(prefix));
        }

        if (req.KeywordIds.Count > 0)
        {
            var keywordGuids = req.KeywordIds.Select(Guid.Parse).ToList();
            query = query.Where(a => a.Keywords.Any(k => keywordGuids.Contains(k.Id)));
        }

        if (req.CategoryIds.Count > 0)
        {
            var categoryGuids = req.CategoryIds.Select(Guid.Parse).ToList();
            query = query.Where(a => a.Categories.Any(c => categoryGuids.Contains(c.Id)));
        }

        if (req.FromDate != 0)
            query = query.Where(a => a.MetadataProfile != null && a.MetadataProfile.DateTaken >= DateTimeOffset.FromUnixTimeSeconds(req.FromDate).DateTime);

        if (req.ToDate != 0)
            query = query.Where(a => a.MetadataProfile != null && a.MetadataProfile.DateTaken < DateTimeOffset.FromUnixTimeSeconds(req.ToDate).DateTime);

        var total = await query.CountAsync(ct);

        // Apply sort from request, defaulting to FileName ascending
        query = (req.SortBy, req.SortDir.ToLowerInvariant()) switch
        {
            ("Date Added", "desc") => query.OrderByDescending(a => a.CreatedAt).ThenBy(a => a.Id),
            ("Date Added", _) => query.OrderBy(a => a.CreatedAt).ThenBy(a => a.Id),
            ("File Type", "desc") => query.OrderByDescending(a => a.MimeType).ThenBy(a => a.Id),
            ("File Type", _) => query.OrderBy(a => a.MimeType).ThenBy(a => a.Id),
            ("File Size", "desc") => query.OrderByDescending(a => a.FileSize).ThenBy(a => a.Id),
            ("File Size", _) => query.OrderBy(a => a.FileSize).ThenBy(a => a.Id),
            ("File Name", "desc") => query.OrderByDescending(a => a.FileName).ThenBy(a => a.Id),
            _ => query.OrderBy(a => a.FileName).ThenBy(a => a.Id)
        };

        var assets = await query
            .Skip((req.Page - 1) * req.PageSize)
            .Take(req.PageSize)
            .ToListAsync(ct);

        var response = new ListAssetsResponse
        {
            TotalCount = total,
            Page = req.Page,
            PageSize = req.PageSize
        };

        foreach (var asset in assets)
        {
            response.Items.Add(new AssetSummary
            {
                Id = asset.Id.ToString(),
                FileName = asset.FileName,
                MimeType = asset.MimeType,
                FileSize = asset.FileSize,
                Title = asset.Title,
                Type = asset.Type.ToString(),
                CollectionId = asset.CollectionId?.ToString() ?? "",
                UploadedBy = asset.UploadedByUserId?.ToString() ?? "",
                CreatedAt = asset.CreatedAt.ToUnixTimeSeconds(),
                Rating = asset.Rating,
                Label = (int)asset.Label,
                Flag = (int)asset.Flag
            });
        }

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = MessageTypeCode.ListAssetsResponse,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(response)),
            StatusCode = 0
        };
    }

    public async Task<Envelope> GetAssetAsync(Envelope request, CancellationToken ct)
    {
        if (!await _authz.HasPermissionAsync(request, "asset:read", ct))
            return ErrorResponse(request, 7, "Forbidden");

        var req = ProtoHelper.Deserialize<GetAssetRequest>(request.Payload.ToByteArray());

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var asset = await db.DigitalAssets
            .Include(a => a.Collection)
            .Include(a => a.MetadataProfile)
            .Include(a => a.Keywords)
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == Guid.Parse(req.Id), ct);

        if (asset == null)
        {
            return new Envelope
            {
                CorrelationId = request.CorrelationId,
                MessageType = MessageTypeCode.AssetDetail,
                StatusCode = 5,
                ErrorMessage = "Asset not found"
            };
        }

        var detail = new AssetDetail
        {
            Id = asset.Id.ToString(),
            FileName = asset.FileName,
            FileExtension = asset.FileExtension,
            MimeType = asset.MimeType,
            FileSize = asset.FileSize,
            ChecksumSha256 = asset.ChecksumSha256,
            Title = asset.Title,
            Description = asset.Description ?? "",
            Type = asset.Type.ToString(),
            Width = asset.Width ?? 0,
            Height = asset.Height ?? 0,
            Duration = asset.Duration ?? 0,
            CollectionId = asset.CollectionId?.ToString() ?? "",
            CollectionName = asset.Collection?.Name ?? "",
            UploadedBy = asset.UploadedByUserId?.ToString() ?? "",
            Version = asset.Version,
            CreatedAt = asset.CreatedAt.ToUnixTimeSeconds(),
            ModifiedAt = asset.ModifiedAt.ToUnixTimeSeconds(),
            Rating = asset.Rating,
            Label = (int)asset.Label,
            Flag = (int)asset.Flag,
            GpsLatitude = asset.GpsLatitude ?? 0,
            GpsLongitude = asset.GpsLongitude ?? 0,
            Copyright = asset.Copyright ?? ""
        };

        foreach (var kw in asset.Keywords)
            detail.Tags.Add(kw.Name);

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = MessageTypeCode.AssetDetail,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(detail)),
            StatusCode = 0
        };
    }

    public async Task<Envelope> UpdateAssetAsync(Envelope request, CancellationToken ct)
    {
        if (!await _authz.HasPermissionAsync(request, "asset:update", ct))
            return ErrorResponse(request, 7, "Forbidden");

        var req = ProtoHelper.Deserialize<UpdateAssetRequest>(request.Payload.ToByteArray());

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var asset = await db.DigitalAssets
            .Include(a => a.Keywords)
            .FirstOrDefaultAsync(a => a.Id == Guid.Parse(req.Id), ct);

        if (asset == null)
        {
            return new Envelope
            {
                CorrelationId = request.CorrelationId,
                MessageType = MessageTypeCode.UpdateAssetResponse,
                StatusCode = 5,
                ErrorMessage = "Asset not found"
            };
        }

        if (req.ExpectedVersion > 0 && asset.Version != req.ExpectedVersion)
        {
            var conflict = new UpdateAssetResponse
            {
                Id = asset.Id.ToString(),
                NewVersion = asset.Version,
                ModifiedAt = asset.ModifiedAt.ToUnixTimeSeconds(),
                Conflict = true
            };

            return new Envelope
            {
                CorrelationId = request.CorrelationId,
                MessageType = MessageTypeCode.UpdateAssetResponse,
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(conflict)),
                StatusCode = 0
            };
        }

        asset.Title = req.Title;
        asset.Description = req.Description;
        asset.Rating = req.Rating;
        asset.Label = (Adam.Shared.Models.AssetLabel)req.Label;
        asset.Flag = (Adam.Shared.Models.AssetFlag)req.Flag;
        if (req.GpsLatitude != 0) asset.GpsLatitude = req.GpsLatitude;
        else asset.GpsLatitude = null;
        if (req.GpsLongitude != 0) asset.GpsLongitude = req.GpsLongitude;
        else asset.GpsLongitude = null;
        asset.Copyright = req.Copyright;
        asset.Keywords.Clear();
        if (req.Tags.Count > 0)
        {
            await db.AssociateKeywordsAsync(asset, req.Tags);
        }
        if (req.CollectionId is { Length: > 0 })
            asset.CollectionId = Guid.Parse(req.CollectionId);
        else
            asset.CollectionId = null;

        await db.SaveChangesAsync(ct);

        // Broadcast change to all other connected clients
        var userId = _authHandler.GetUserId(request);
        _ = _notificationService.NotifyUpdatedAsync(asset.Id.ToString(), userId, request.ConnectionId);

        var response = new UpdateAssetResponse
        {
            Id = asset.Id.ToString(),
            NewVersion = asset.Version,
            ModifiedAt = asset.ModifiedAt.ToUnixTimeSeconds(),
            Conflict = false
        };

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = MessageTypeCode.UpdateAssetResponse,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(response)),
            StatusCode = 0
        };
    }

    public async Task<Envelope> CreateAssetAsync(Envelope request, CancellationToken ct)
    {
        if (!await _authz.HasPermissionAsync(request, "asset:create", ct))
            return ErrorResponse(request, 7, "Forbidden");

        var req = ProtoHelper.Deserialize<CreateAssetRequest>(request.Payload.ToByteArray());

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var asset = new Adam.Shared.Models.DigitalAsset
        {
            Id = Guid.NewGuid(),
            FileName = req.FileName,
            Title = req.Title,
            Description = req.Description,
            CollectionId = string.IsNullOrEmpty(req.CollectionId) ? null : Guid.Parse(req.CollectionId),
            Version = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow,
            Rating = req.Rating,
            Label = (Adam.Shared.Models.AssetLabel)req.Label,
            Flag = (Adam.Shared.Models.AssetFlag)req.Flag,
            GpsLatitude = req.GpsLatitude != 0 ? req.GpsLatitude : null,
            GpsLongitude = req.GpsLongitude != 0 ? req.GpsLongitude : null,
            Copyright = req.Copyright
        };

        if (req.Tags.Count > 0)
            await db.AssociateKeywordsAsync(asset, req.Tags, ct);

        db.DigitalAssets.Add(asset);
        await db.SaveChangesAsync(ct);

        var userId = _authHandler.GetUserId(request);
        _ = _notificationService.NotifyCreatedAsync(asset.Id.ToString(), userId, request.ConnectionId);

        var response = new CreateAssetResponse
        {
            Id = asset.Id.ToString(),
            Checksum = asset.ChecksumSha256
        };

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = MessageTypeCode.CreateAssetResponse,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(response)),
            StatusCode = 0
        };
    }

    public async Task<Envelope> DeleteAssetAsync(Envelope request, CancellationToken ct)
    {
        if (!await _authz.HasPermissionAsync(request, "asset:delete", ct))
            return ErrorResponse(request, 7, "Forbidden");

        var req = ProtoHelper.Deserialize<DeleteAssetRequest>(request.Payload.ToByteArray());

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var asset = await db.DigitalAssets.FirstOrDefaultAsync(a => a.Id == Guid.Parse(req.Id), ct);
        if (asset == null)
            return ErrorResponse(request, 5, "Asset not found");

        asset.IsDeleted = true;
        asset.ModifiedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        var userId = _authHandler.GetUserId(request);
        _ = _notificationService.NotifyDeletedAsync(asset.Id.ToString(), userId, request.ConnectionId);

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = MessageTypeCode.DeleteAssetResponse,
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
