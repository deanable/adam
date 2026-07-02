---
goal: Extract 6 near-identical bulk operation methods from MainWindowViewModel into a dedicated BulkAssetOperationService.
version: 1.0
date_created: 2026-07-02
last_updated: 2026-07-02
status: 'Draft'
tags: [refactoring, mainwindow-viewmodel, bulk-operations, service-extraction]
---

# Refactoring 02: BulkAssetOperationService

## Problem

`MainWindowViewModel` contains 6 methods that follow nearly the same pattern:

| Method | Lines | Purpose |
|--------|-------|---------|
| `RateSelectedAsync()` | ~30 | Cycle rating on all selected assets |
| `SetLabelSelectedAsync()` | ~35 | Cycle color label on all selected assets |
| `SetFlagSelectedAsync()` | ~30 | Cycle flag on all selected assets |
| `SetRatingByKeyAsync()` | ~30 | Set explicit rating on all selected assets |
| `SetFlagByKeyAsync()` | ~25 | Set explicit flag on all selected assets |
| `RenameAssetAsync()` | ~35 | Rename single selected asset |

The common pattern is:
```
Load selected IDs → query DB → mutate entities → SaveChangesAsync → update in-memory tiles → show toast
```

These 6 methods total ~200 lines, are tightly coupled to `MainWindowViewModel`'s dependencies, and each reopens a database context independently. They cannot be unit tested without instantiating the entire MainWindowViewModel.

## Solution

Extract a **`BulkAssetOperationService`** that encapsulates all bulk metadata operations. The service takes `ModeManager`, `ToastService`, and `ILogger` as dependencies and exposes clean async methods.

## Proposed Design

### New file: `src/Adam.CatalogBrowser/Services/BulkAssetOperationService.cs`

```csharp
public sealed class BulkAssetOperationService
{
    private readonly ModeManager _modeManager;
    private readonly ToastService _toastService;
    private readonly ILogger<BulkAssetOperationService> _logger;

    public BulkAssetOperationService(
        ModeManager modeManager,
        ToastService toastService,
        ILogger<BulkAssetOperationService> logger)
    { ... }

    /// <summary>Cycle rating (0→1→2→3→4→5→0) on all selected assets.</summary>
    public async Task RateSelectedAsync(IReadOnlyList<AssetListItem> selected, Action<AssetListItem, int>? onTileUpdated = null);

    /// <summary>Cycle color label on all selected assets.</summary>
    public async Task SetLabelSelectedAsync(IReadOnlyList<AssetListItem> selected, Action<AssetListItem, AssetLabel>? onTileUpdated = null);

    /// <summary>Cycle flag (Unflagged→Pick→Reject→Unflagged) on all selected assets.</summary>
    public async Task SetFlagSelectedAsync(IReadOnlyList<AssetListItem> selected, Action<AssetListItem, AssetFlag>? onTileUpdated = null);

    /// <summary>Set explicit rating on all selected assets.</summary>
    public async Task SetRatingByKeyAsync(IReadOnlyList<AssetListItem> selected, int rating, Action<AssetListItem, int>? onTileUpdated = null);

    /// <summary>Set explicit flag on all selected assets.</summary>
    public async Task SetFlagByKeyAsync(IReadOnlyList<AssetListItem> selected, AssetFlag flag, Action<AssetListItem, AssetFlag>? onTileUpdated = null);

    /// <summary>Rename a single asset's title.</summary>
    public async Task<bool> RenameAssetAsync(AssetListItem asset, string newTitle);
}
```

Each method follows the same internal structure:

```csharp
public async Task SetFlagByKeyAsync(IReadOnlyList<AssetListItem> selected, AssetFlag flag, Action<AssetListItem, AssetFlag>? onTileUpdated = null)
{
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

        // Update in-memory tiles
        foreach (var item in selected)
        {
            var dbMatch = dbAssets.FirstOrDefault(d => d.Id == item.Id);
            if (dbMatch != null)
                onTileUpdated?.Invoke(item, dbMatch.Flag);
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
```

The `onTileUpdated` callbacks for the MainWindowViewModel adapters would be:

```csharp
// Rate:
(item, rating) => item.Rating = rating

// Label:
(item, label) => { (item.ColorLabel, item.ColorBrush) = AssetListItem.MapLabelToDisplay(label); }

// Flag:
(item, flag) => item.IsFlagged = flag != AssetFlag.Unflagged
```

### Implementation Steps

| Step | Description | Effort |
|------|-------------|--------|
| 1 | Create `BulkAssetOperationService.cs` in `src/Adam.CatalogBrowser/Services/` | Medium |
| 2 | Register `BulkAssetOperationService` in DI (`App.axaml.cs`) | Tiny |
| 3 | Update `MainWindowViewModel` to inject and use the service | Medium |
| 4 | Remove the 6 private methods and their command bodies | Small |
| 5 | Clean up unused `using` directives | Tiny |
| 6 | Verify all tests still pass | Small |

### Expected Outcome

- **~200 lines removed** from `MainWindowViewModel`
- **~150 lines added** in `BulkAssetOperationService`
- Bulk operations are independently testable (just mock `ModeManager`)
- `MainWindowViewModel` loses 6 private methods and ~13 `CanExecute` refresh calls in selection change handlers

## Files Changed

| File | Change |
|------|--------|
| `src/Adam.CatalogBrowser/Services/BulkAssetOperationService.cs` | **New** |
| `src/Adam.CatalogBrowser/ViewModels/MainWindowViewModel.cs` | Replace 6 methods + commands with service calls |
| `src/Adam.CatalogBrowser/App.axaml.cs` | Register service in DI |

## Testing

```bash
dotnet test tests/Adam.CatalogBrowser.Tests
dotnet test tests/Adam.Shared.Tests
```

The bulk operations are tested indirectly through existing gallery integration tests. New unit tests for `BulkAssetOperationService` can be added in a follow-up PR.

## Modularity Assessment Findings — Cross-Reference

This refactoring directly addresses the **MainWindowViewModel command bloat** identified in the audit. The 6 bulk operation methods represent a pattern of near-identical code that should never live in a ViewModel:

| Metric | Before | After |
|--------|--------|-------|
| MainWindowViewModel lines | 2,040 | ~1,840 |
| Duplicate data-access patterns | 6 methods | 0 (extracted to service) |
| CanExecute refresh lines | 13 lines per selection event | 2 calls to centralized service |
| Testability | Requires full MainWindowVM | Mock ModeManager + ToastService only |

**Broader modularity context:** The `QuickRateAssetAsync` and `ToggleFlagAssetAsync` methods in `AssetGalleryViewModel` follow the same pattern. A future refactoring should consolidate those too, potentially merging them into the same `BulkAssetOperationService`.

**Related plans:**
- `refactoring-01-navigation-service/PLAN.md` — Navigation lifecycle extraction
- `refactoring-03-panel-state-service/PLAN.md` — Panel state persistence extraction

## Risk Assessment

- **Low**: Pure extraction — no behavior changes, no API surface changes for XAML bindings
- **Low**: The `onTileUpdated` callback pattern preserves the existing in-place UI update behavior
- **Low**: Toast messages are identical
