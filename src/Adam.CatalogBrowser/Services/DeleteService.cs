using Adam.Shared.Data;
using Adam.Shared.Services;
using Microsoft.EntityFrameworkCore;

namespace Adam.CatalogBrowser.Services;

public class DeleteService
{
    private readonly ModeManager _modeManager;

    public DeleteService(ModeManager modeManager)
    {
        _modeManager = modeManager;
    }

    public async Task<bool> SoftDeleteAsync(Guid assetId, CancellationToken ct = default)
    {
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

    public async Task<bool> RestoreAsync(Guid assetId, CancellationToken ct = default)
    {
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

    public async Task<List<Adam.Shared.Models.DigitalAsset>> GetDeletedAssetsAsync(CancellationToken ct = default)
    {
        await using var db = _modeManager.CreateDbContext();
        return await db.DigitalAssets
            .IgnoreQueryFilters()
            .Where(a => a.IsDeleted)
            .ToListAsync(ct);
    }

    public async Task<bool> PermanentlyDeleteAsync(Guid assetId, CancellationToken ct = default)
    {
        await using var db = _modeManager.CreateDbContext();
        var asset = await db.DigitalAssets
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == assetId && a.IsDeleted, ct);

        if (asset == null) return false;

        db.DigitalAssets.Remove(asset);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
