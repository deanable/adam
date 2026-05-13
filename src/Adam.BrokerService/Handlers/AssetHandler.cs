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

    public AssetHandler(IServiceProvider serviceProvider, ILogger<AssetHandler> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<Envelope> ListAssetsAsync(Envelope request, CancellationToken ct)
    {
        var req = ProtoHelper.Deserialize<ListAssetsRequest>(request.Payload.ToByteArray());

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var query = db.DigitalAssets
            .Include(a => a.Collection)
            .Include(a => a.Keywords)
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

        var total = await query.CountAsync(ct);
        var assets = await query
            .OrderBy(a => a.FileName)
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
                CreatedAt = asset.CreatedAt.ToUnixTimeSeconds()
            });
        }

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = nameof(ListAssetsResponse),
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(response)),
            StatusCode = 0
        };
    }

    public async Task<Envelope> GetAssetAsync(Envelope request, CancellationToken ct)
    {
        var req = ProtoHelper.Deserialize<GetAssetRequest>(request.Payload.ToByteArray());

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var asset = await db.DigitalAssets
            .Include(a => a.Collection)
            .Include(a => a.MetadataProfile)
            .Include(a => a.Keywords)
            .FirstOrDefaultAsync(a => a.Id == Guid.Parse(req.Id), ct);

        if (asset == null)
        {
            return new Envelope
            {
                CorrelationId = request.CorrelationId,
                MessageType = nameof(AssetDetail),
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
            ModifiedAt = asset.ModifiedAt.ToUnixTimeSeconds()
        };

        foreach (var kw in asset.Keywords)
            detail.Tags.Add(kw.Name);

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = nameof(AssetDetail),
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(detail)),
            StatusCode = 0
        };
    }

    public async Task<Envelope> UpdateAssetAsync(Envelope request, CancellationToken ct)
    {
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
                MessageType = nameof(UpdateAssetResponse),
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
                MessageType = nameof(UpdateAssetResponse),
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(conflict)),
                StatusCode = 0
            };
        }

        asset.Title = req.Title;
        asset.Description = req.Description;
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
            MessageType = nameof(UpdateAssetResponse),
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(response)),
            StatusCode = 0
        };
    }
}
