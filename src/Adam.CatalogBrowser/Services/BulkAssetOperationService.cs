using Adam.CatalogBrowser.Models;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Adam.Shared.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Adam.CatalogBrowser.Services;

/// <summary>
/// Encapsulates bulk metadata operations on digital assets (rating, label, flag, rename).
/// Each method follows the same pattern: load selected IDs, query DB, mutate, save,
/// update in-memory tiles via callbacks, show toast.
/// </summary>
public sealed class BulkAssetOperationService
{
    private readonly ModeManager _modeManager;
    private readonly ToastService _toastService;
    private readonly ILogger<BulkAssetOperationService> _logger;

    public BulkAssetOperationService(
        ModeManager modeManager,
        ToastService toastService,
        ILogger<BulkAssetOperationService> logger)
    {
        _modeManager = modeManager;
        _toastService = toastService;
        _logger = logger;
    }

    /// <summary>
    /// Cycles the rating on all selected assets (0→1→2→3→4→5→0).
    /// </summary>
    public async Task RateSelectedAsync(
        IReadOnlyList<AssetListItem> selected,
        Action<AssetListItem, int>? onTileUpdated = null)
    {
        if (selected.Count == 0) return;

        try
        {
            await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);
            var ids = selected.Select(a => a.Id).ToList();
            var dbAssets = await db.DigitalAssets
                .Where(a => ids.Contains(a.Id))
                .ToListAsync().ConfigureAwait(false);

            foreach (var dbAsset in dbAssets)
                dbAsset.Rating = (dbAsset.Rating + 1) % 6;

            await db.SaveChangesAsync().ConfigureAwait(false);

            foreach (var item in selected)
            {
                var dbMatch = dbAssets.FirstOrDefault(d => d.Id == item.Id);
                if (dbMatch != null)
                {
                    item.Rating = dbMatch.Rating;
                    onTileUpdated?.Invoke(item, dbMatch.Rating);
                }
            }

            _toastService.Show(selected.Count == 1
                ? $"Rated '{selected[0].Title}' {dbAssets.FirstOrDefault()?.Rating}/5"
                : $"Rated {dbAssets.Count} asset(s)", ToastLevel.Success);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to rate assets");
        }
    }

    /// <summary>
    /// Cycles the color label on all selected assets.
    /// </summary>
    public async Task SetLabelSelectedAsync(
        IReadOnlyList<AssetListItem> selected,
        Action<AssetListItem, AssetLabel>? onTileUpdated = null)
    {
        if (selected.Count == 0) return;

        try
        {
            await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);
            var ids = selected.Select(a => a.Id).ToList();
            var dbAssets = await db.DigitalAssets
                .Where(a => ids.Contains(a.Id))
                .ToListAsync().ConfigureAwait(false);

            var labels = Enum.GetValues<AssetLabel>();
            foreach (var dbAsset in dbAssets)
            {
                var currentIndex = Array.IndexOf(labels, dbAsset.Label);
                dbAsset.Label = labels[(currentIndex + 1) % labels.Length];
            }

            await db.SaveChangesAsync().ConfigureAwait(false);

            foreach (var item in selected)
            {
                var dbMatch = dbAssets.FirstOrDefault(d => d.Id == item.Id);
                if (dbMatch != null)
                {
                    (item.ColorLabel, item.ColorBrush) = AssetListItem.MapLabelToDisplay(dbMatch.Label);
                    onTileUpdated?.Invoke(item, dbMatch.Label);
                }
            }

            var labelName = dbAssets.FirstOrDefault()?.Label.ToString() ?? "None";
            _toastService.Show(selected.Count == 1
                ? $"Label set to '{labelName}' for '{selected[0].Title}'"
                : $"Label set to '{labelName}' on {dbAssets.Count} asset(s)", ToastLevel.Success);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set label");
        }
    }

    /// <summary>
    /// Cycles the flag on all selected assets (Unflagged→Pick→Reject→Unflagged).
    /// </summary>
    public async Task SetFlagSelectedAsync(
        IReadOnlyList<AssetListItem> selected,
        Action<AssetListItem, AssetFlag>? onTileUpdated = null)
    {
        if (selected.Count == 0) return;

        try
        {
            await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);
            var ids = selected.Select(a => a.Id).ToList();
            var dbAssets = await db.DigitalAssets
                .Where(a => ids.Contains(a.Id))
                .ToListAsync().ConfigureAwait(false);

            foreach (var dbAsset in dbAssets)
            {
                dbAsset.Flag = dbAsset.Flag switch
                {
                    AssetFlag.Unflagged => AssetFlag.Pick,
                    AssetFlag.Pick => AssetFlag.Reject,
                    AssetFlag.Reject => AssetFlag.Unflagged,
                    _ => AssetFlag.Unflagged
                };
            }

            await db.SaveChangesAsync().ConfigureAwait(false);

            foreach (var item in selected)
            {
                var dbMatch = dbAssets.FirstOrDefault(d => d.Id == item.Id);
                if (dbMatch != null)
                {
                    item.IsFlagged = dbMatch.Flag != AssetFlag.Unflagged;
                    onTileUpdated?.Invoke(item, dbMatch.Flag);
                }
            }

            var flagName = dbAssets.FirstOrDefault()?.Flag.ToString() ?? "Unflagged";
            _toastService.Show(selected.Count == 1
                ? $"Flag set to '{flagName}' for '{selected[0].Title}'"
                : $"Flag set to '{flagName}' on {dbAssets.Count} asset(s)", ToastLevel.Success);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set flag");
        }
    }

    /// <summary>
    /// Sets the rating of all selected assets to a specific value (0-5).
    /// </summary>
    public async Task SetRatingByKeyAsync(
        IReadOnlyList<AssetListItem> selected,
        int rating,
        Action<AssetListItem, int>? onTileUpdated = null)
    {
        if (selected.Count == 0) return;
        rating = Math.Clamp(rating, 0, 5);

        try
        {
            await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);
            var ids = selected.Select(a => a.Id).ToList();
            var dbAssets = await db.DigitalAssets
                .Where(a => ids.Contains(a.Id))
                .ToListAsync().ConfigureAwait(false);

            foreach (var dbAsset in dbAssets)
                dbAsset.Rating = rating;

            await db.SaveChangesAsync().ConfigureAwait(false);

            foreach (var item in selected)
            {
                var dbMatch = dbAssets.FirstOrDefault(d => d.Id == item.Id);
                if (dbMatch != null)
                {
                    item.Rating = dbMatch.Rating;
                    onTileUpdated?.Invoke(item, dbMatch.Rating);
                }
            }

            _toastService.Show(selected.Count == 1
                ? (rating > 0 ? $"Rated '{selected[0].Title}' {rating}/5" : $"Rating cleared for '{selected[0].Title}'")
                : $"Set {dbAssets.Count} asset(s) to {rating}/5", ToastLevel.Success);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set rating via keyboard");
        }
    }

    /// <summary>
    /// Sets the flag of all selected assets to a specific value.
    /// </summary>
    public async Task SetFlagByKeyAsync(
        IReadOnlyList<AssetListItem> selected,
        AssetFlag flag,
        Action<AssetListItem, AssetFlag>? onTileUpdated = null)
    {
        if (selected.Count == 0) return;

        try
        {
            await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);
            var ids = selected.Select(a => a.Id).ToList();
            var dbAssets = await db.DigitalAssets
                .Where(a => ids.Contains(a.Id))
                .ToListAsync().ConfigureAwait(false);

            foreach (var dbAsset in dbAssets)
                dbAsset.Flag = flag;

            await db.SaveChangesAsync().ConfigureAwait(false);

            foreach (var item in selected)
            {
                var dbMatch = dbAssets.FirstOrDefault(d => d.Id == item.Id);
                if (dbMatch != null)
                {
                    item.IsFlagged = dbMatch.Flag != AssetFlag.Unflagged;
                    onTileUpdated?.Invoke(item, dbMatch.Flag);
                }
            }

            var flagName = flag.ToString();
            _toastService.Show(selected.Count == 1
                ? $"Flag set to '{flagName}' for '{selected[0].Title}'"
                : $"Flag set to '{flagName}' on {dbAssets.Count} asset(s)", ToastLevel.Success);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set flag via keyboard");
        }
    }

    /// <summary>
    /// Renames a single asset's title. Returns true if renamed, false if skipped or failed.
    /// </summary>
    public async Task<bool> RenameAssetAsync(
        AssetListItem asset,
        string newTitle)
    {
        if (string.IsNullOrWhiteSpace(newTitle) || newTitle == asset.Title)
            return false;

        try
        {
            await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);
            var dbAsset = await db.DigitalAssets.FirstOrDefaultAsync(a => a.Id == asset.Id).ConfigureAwait(false);
            if (dbAsset == null) return false;

            dbAsset.Title = newTitle.Trim();
            await db.SaveChangesAsync().ConfigureAwait(false);

            asset.Title = dbAsset.Title;
            _toastService.Show($"Renamed to '{dbAsset.Title}'", ToastLevel.Success);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to rename asset");
            _toastService.Show("Failed to rename asset", ToastLevel.Error);
            return false;
        }
    }
}
