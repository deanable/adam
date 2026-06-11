using Adam.Shared.Contracts;
using Adam.Shared.Data;
using Adam.Shared.Services;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Adam.CatalogBrowser.Services;

public class DeleteService
{
    private readonly ModeManager _modeManager;
    private readonly ILogger<DeleteService> _logger;

    public DeleteService(ModeManager modeManager, ILogger<DeleteService>? logger = null)
    {
        _modeManager = modeManager;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<DeleteService>.Instance;
    }

    // ─────────────────────────────────────────────────────────────────
    //  SoftDeleteAsync — single asset
    // ─────────────────────────────────────────────────────────────────

    public async Task<bool> SoftDeleteAsync(Guid assetId, CancellationToken ct = default)
    {
        // Multi-user mode: route through broker
        if (_modeManager.IsMultiUser)
        {
            return await BrokerSoftDeleteAsync(assetId, ct);
        }

        // Standalone mode: direct DB access
        await using var db = _modeManager.CreateDbContext();
        var asset = await db.DigitalAssets
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == assetId, ct);

        if (asset == null) return false;

        asset.IsDeleted = true;
        asset.ModifiedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ─────────────────────────────────────────────────────────────────
    //  BulkSoftDeleteAsync — multiple assets
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Bulk soft-deletes multiple assets in a single database context and transaction.
    /// Returns the count of successfully deleted assets.
    /// </summary>
    public async Task<int> BulkSoftDeleteAsync(IEnumerable<Guid> assetIds, CancellationToken ct = default)
    {
        var ids = assetIds.ToList();
        if (ids.Count == 0) return 0;

        // Multi-user mode: send individual delete requests to broker
        if (_modeManager.IsMultiUser)
        {
            return await BulkBrokerSoftDeleteAsync(ids, ct);
        }

        // Standalone mode: direct DB access in a single transaction
        await using var db = _modeManager.CreateDbContext();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var assets = await db.DigitalAssets
            .IgnoreQueryFilters()
            .Where(a => ids.Contains(a.Id))
            .ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        foreach (var asset in assets)
        {
            asset.IsDeleted = true;
            asset.ModifiedAt = now;
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return assets.Count;
    }

    // ─────────────────────────────────────────────────────────────────
    //  Broker communication helpers
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sends a single DeleteAssetRequest to the broker.
    /// Returns true if the broker returned status code 0 (success).
    /// </summary>
    private async Task<bool> BrokerSoftDeleteAsync(Guid assetId, CancellationToken ct)
    {
        var broker = _modeManager.BrokerClient;
        if (broker == null || !broker.IsConnected)
            return false;

        var authToken = _modeManager.AuthSession?.Token ?? string.Empty;

        var request = new DeleteAssetRequest { Id = assetId.ToString() };
        var envelope = new Envelope
        {
            AuthToken = authToken,
            CorrelationId = Guid.NewGuid().ToString(),
            MessageType = MessageTypeCode.DeleteAssetRequest,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(request))
        };

        var response = await broker.SendAsync(envelope, ct);

        if (response.StatusCode != 0)
        {
            _logger.LogWarning("Broker rejected delete for asset {AssetId}: status={StatusCode}, error={Error}",
                assetId, response.StatusCode, response.ErrorMessage);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Sends individual DeleteAssetRequest messages to the broker for each asset ID.
    /// Returns the count of successful deletions.
    /// </summary>
    private async Task<int> BulkBrokerSoftDeleteAsync(List<Guid> ids, CancellationToken ct)
    {
        var succeeded = 0;
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (await BrokerSoftDeleteAsync(id, ct))
                    succeeded++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Broker soft-delete failed for asset {AssetId}", id);
            }
        }
        return succeeded;
    }

    public async Task<bool> RestoreAsync(Guid assetId, CancellationToken ct = default)
    {
        // Multi-user mode: route through broker
        if (_modeManager.IsMultiUser)
        {
            return await BrokerRestoreAsync(assetId, ct);
        }

        // Standalone mode: direct DB access
        await using var db = _modeManager.CreateDbContext();
        var asset = await db.DigitalAssets
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == assetId && a.IsDeleted, ct);

        if (asset == null) return false;

        asset.IsDeleted = false;
        asset.ModifiedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ─────────────────────────────────────────────────────────────────
    //  Broker restore helper
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sends a single RestoreAssetRequest to the broker.
    /// Returns true if the broker returned status code 0 (success).
    /// </summary>
    private async Task<bool> BrokerRestoreAsync(Guid assetId, CancellationToken ct)
    {
        var broker = _modeManager.BrokerClient;
        if (broker == null || !broker.IsConnected)
            return false;

        var authToken = _modeManager.AuthSession?.Token ?? string.Empty;

        var request = new RestoreAssetRequest { Id = assetId.ToString() };
        var envelope = new Envelope
        {
            AuthToken = authToken,
            CorrelationId = Guid.NewGuid().ToString(),
            MessageType = MessageTypeCode.RestoreAssetRequest,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(request))
        };

        var response = await broker.SendAsync(envelope, ct);

        if (response.StatusCode != 0)
        {
            _logger.LogWarning("Broker rejected restore for asset {AssetId}: status={StatusCode}, error={Error}",
                assetId, response.StatusCode, response.ErrorMessage);
            return false;
        }

        return true;
    }

    public async Task<List<Adam.Shared.Models.DigitalAsset>> GetDeletedAssetsAsync(CancellationToken ct = default)
    {
        // Multi-user mode: route through broker
        if (_modeManager.IsMultiUser)
        {
            return await BrokerGetDeletedAssetsAsync(ct);
        }

        // Standalone mode: direct DB access
        await using var db = _modeManager.CreateDbContext();
        return await db.DigitalAssets
            .IgnoreQueryFilters()
            .Where(a => a.IsDeleted)
            .ToListAsync(ct);
    }

    // ─────────────────────────────────────────────────────────────────
    //  Broker GetDeletedAssets helper
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sends a ListDeletedAssetsRequest to the broker and deserializes the response
    /// into DigitalAsset models. Returns all deleted assets.
    /// </summary>
    private async Task<List<Adam.Shared.Models.DigitalAsset>> BrokerGetDeletedAssetsAsync(CancellationToken ct)
    {
        var broker = _modeManager.BrokerClient;
        if (broker == null || !broker.IsConnected)
            return [];

        var authToken = _modeManager.AuthSession?.Token ?? string.Empty;

        var request = new ListDeletedAssetsRequest();
        var envelope = new Envelope
        {
            AuthToken = authToken,
            CorrelationId = Guid.NewGuid().ToString(),
            MessageType = MessageTypeCode.ListDeletedAssetsRequest,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(request))
        };

        var response = await broker.SendAsync(envelope, ct);

        if (response.StatusCode != 0)
        {
            _logger.LogWarning("Broker rejected ListDeletedAssets: status={StatusCode}, error={Error}",
                response.StatusCode, response.ErrorMessage);
            return [];
        }

        var payload = ProtoHelper.Deserialize<ListDeletedAssetsResponse>(response.Payload.ToByteArray());

        return payload.Items.Select(item => new Adam.Shared.Models.DigitalAsset
        {
            Id = Guid.Parse(item.Id),
            IsDeleted = true,
            FileName = item.FileName,
            MimeType = item.MimeType,
            FileSize = item.FileSize,
            Title = item.Title,
            ModifiedAt = DateTimeOffset.FromUnixTimeSeconds(item.ModifiedAt),
            CreatedAt = DateTimeOffset.FromUnixTimeSeconds(item.CreatedAt),
            Rating = item.Rating,
            Label = (Adam.Shared.Models.AssetLabel)item.Label,
            Flag = (Adam.Shared.Models.AssetFlag)item.Flag
        }).ToList();
    }

    public async Task<bool> PermanentlyDeleteAsync(Guid assetId, CancellationToken ct = default)
    {
        // Multi-user mode: route through broker
        if (_modeManager.IsMultiUser)
        {
            return await BrokerPermanentDeleteAsync(assetId, ct);
        }

        // Standalone mode: direct DB access
        await using var db = _modeManager.CreateDbContext();
        var asset = await db.DigitalAssets
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == assetId && a.IsDeleted, ct);

        if (asset == null) return false;

        db.DigitalAssets.Remove(asset);
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ─────────────────────────────────────────────────────────────────
    //  Broker PermanentDelete helper
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sends a PermanentDeleteAssetRequest to the broker.
    /// Returns true if the broker returned status code 0 (success).
    /// </summary>
    private async Task<bool> BrokerPermanentDeleteAsync(Guid assetId, CancellationToken ct)
    {
        var broker = _modeManager.BrokerClient;
        if (broker == null || !broker.IsConnected)
            return false;

        var authToken = _modeManager.AuthSession?.Token ?? string.Empty;

        var request = new PermanentDeleteAssetRequest { Id = assetId.ToString() };
        var envelope = new Envelope
        {
            AuthToken = authToken,
            CorrelationId = Guid.NewGuid().ToString(),
            MessageType = MessageTypeCode.PermanentDeleteAssetRequest,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(request))
        };

        var response = await broker.SendAsync(envelope, ct);

        if (response.StatusCode != 0)
        {
            _logger.LogWarning("Broker rejected permanent delete for asset {AssetId}: status={StatusCode}, error={Error}",
                assetId, response.StatusCode, response.ErrorMessage);
            return false;
        }

        return true;
    }

    // ─────────────────────────────────────────────────────────────────
    //  BulkPermanentDeleteAsync — permanently delete multiple assets
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Bulk permanently deletes multiple soft-deleted assets.
    /// In multi-user mode, sends a single BulkPermanentDeleteAssetRequest to the broker.
    /// Returns the count of successfully deleted assets.
    /// </summary>
    public async Task<int> BulkPermanentDeleteAsync(IEnumerable<Guid> assetIds, CancellationToken ct = default)
    {
        var ids = assetIds.ToList();
        if (ids.Count == 0) return 0;

        if (_modeManager.IsMultiUser)
        {
            return await BrokerBulkPermanentDeleteAsync(ids, ct);
        }

        // Standalone mode: direct DB access in a single transaction
        await using var db = _modeManager.CreateDbContext();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var assets = await db.DigitalAssets
            .IgnoreQueryFilters()
            .Where(a => ids.Contains(a.Id) && a.IsDeleted)
            .ToListAsync(ct);

        if (assets.Count == 0) return 0;

        db.DigitalAssets.RemoveRange(assets);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return assets.Count;
    }

    // ─────────────────────────────────────────────────────────────────
    //  Broker BulkPermanentDelete helper
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sends a single BulkPermanentDeleteAssetRequest to the broker.
    /// Returns the count of successfully deleted assets.
    /// </summary>
    private async Task<int> BrokerBulkPermanentDeleteAsync(List<Guid> ids, CancellationToken ct)
    {
        var broker = _modeManager.BrokerClient;
        if (broker == null || !broker.IsConnected)
            return 0;

        var authToken = _modeManager.AuthSession?.Token ?? string.Empty;

        var request = new BulkPermanentDeleteAssetRequest();
        foreach (var id in ids)
            request.Ids.Add(id.ToString());

        var envelope = new Envelope
        {
            AuthToken = authToken,
            CorrelationId = Guid.NewGuid().ToString(),
            MessageType = MessageTypeCode.BulkPermanentDeleteAssetRequest,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(request))
        };

        var response = await broker.SendAsync(envelope, ct);

        if (response.StatusCode != 0)
        {
            _logger.LogWarning("Broker rejected bulk permanent delete for {Count} assets: status={StatusCode}, error={Error}",
                ids.Count, response.StatusCode, response.ErrorMessage);
            return 0;
        }

        var payload = ProtoHelper.Deserialize<BulkPermanentDeleteAssetResponse>(response.Payload.ToByteArray());
        return payload.DeletedCount;
    }
}
