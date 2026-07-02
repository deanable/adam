---
goal: Break up the 300-line LoadPageAsync method in AssetGalleryViewModel into focused, testable methods.
version: 1.0
date_created: 2026-07-02
last_updated: 2026-07-02
status: 'Draft'
tags: [refactoring, asset-gallery-viewmodel, loadpage, method-decomposition]
---

# Refactoring 04: LoadPageAsync Decomposition

## Problem

`AssetGalleryViewModel.LoadPageAsync` is a ~300-line method that handles:

1. **Standalone mode** (~180 lines):
   - Create `AppDbContext`
   - Apply filters
   - Count total records (page 0 only)
   - Keyset pagination for Date Added sort (two-query workaround)
   - Keyset pagination for other sorts (single-query WHERE)
   - Store last-item seek values
   - Build `AssetListItem` objects with thumbnails
   - Dispatch items to UI thread
   - Load visible thumbnails + batch-load remaining
   - Backfill missing thumbnails
   - Update `_page` and `_hasMore` state

2. **Multi-user mode** (~100 lines):
   - Build `ListAssetsRequest` with filters
   - Send via broker
   - Deserialize response
   - Build `AssetListItem` objects
   - Dispatch to UI thread

The method violates the Single Responsibility Principle — it handles DB queries, pagination, thumbnail loading, UI dispatching, and error handling all in one method. This makes it:
- Hard to unit test (mock dependencies are complex)
- Hard to understand (standalone vs multi-user branches are interspersed with pagination logic)
- Hard to modify (any change risks breaking pagination, thumbnail loading, or filtering)

## Solution

Decompose `LoadPageAsync` into focused methods with clear responsibilities.

### Proposed Method Breakdown

#### Phase 1 — Without changing behavior, extract these methods:

| New Method | Lines Extracted | Responsibility |
|-----------|----------------|----------------|
| `LoadPageStandaloneAsync(ct)` | ~180 | Database query + pagination for standalone mode |
| `LoadPageMultiUserAsync(ct)` | ~100 | Broker request + response handling for multi-user mode |
| `ResolveAssetsFromQueryAsync(query, ct)` | ~60 | Apply pagination strategy, load assets, store seek values |
| `BuildAssetItemsAsync(assets)` | ~50 | Map `DigitalAsset` → `AssetListItem` (shared between standalone + search) |
| `UpdateGalleryAsync(newItems)` | ~25 | UI thread dispatch, `HasMore`, `StatusText` updates |

#### Future phases (after this refactoring):

| Method | Purpose |
|--------|---------|
| `ApplyDateAddedPaginationAsync(query, ct)` | Isolate the two-query SQLite workaround |
| `ApplyKeysetPaginationForSort(query)` | General keyset WHERE clause logic (already exists as `ApplyKeysetPagination`) |

### Detailed Design

```csharp
// ── Original: 300 lines, 3 concerns ──
private async Task LoadPageAsync(CancellationToken ct)
{
    IsLoadingMore = true;
    try
    {
        if (_modeManager.IsStandalone)
            await LoadPageStandaloneAsync(ct);
        else if (_modeManager.IsMultiUser)
            await LoadPageMultiUserAsync(ct);
    }
    finally
    {
        IsLoadingMore = false;
    }
}

// ── New: ~80 lines ──
private async Task LoadPageStandaloneAsync(CancellationToken ct)
{
    await using var db = await _modeManager.CreateDbContextAsync(ct).ConfigureAwait(false);
    var query = ApplyFilters(db.DigitalAssets.AsQueryable());

    if (_page == 0)
        _totalCount = await query.CountAsync(ct).ConfigureAwait(false);

    var assets = await ResolveStandalonePaginationAsync(query, ct).ConfigureAwait(false);
    var newItems = BuildAssetItems(assets);

    await Dispatcher.UIThread.InvokeAsync(() =>
    {
        foreach (var item in newItems)
            Assets.Add(item);
    });

    LoadVisibleThumbnails(newItems);
    _ = BatchLoadRemainingThumbnailsAsync(newItems);

    _page++;
    _hasMore = assets.Count >= _pageSize;

    _ = BackfillMissingThumbnailsAsync(newItems, GetThumbnailDir(), ct);
}

// ── New: ~50 lines ──
private async Task<List<DigitalAsset>> ResolveStandalonePaginationAsync(
    IQueryable<DigitalAsset> query, CancellationToken ct)
{
    List<DigitalAsset> assets;

    if (_sortBy == "Date Added")
    {
        // Two-query approach to avoid SQLite DateTimeOffset ORDER BY limitation
        var idQuery = query.Select(a => new { a.Id, a.CreatedAt });

        if (_page > 0)
        {
            idQuery = idQuery.Where(a =>
                a.CreatedAt < _lastSeekCreatedAt ||
                (a.CreatedAt == _lastSeekCreatedAt && a.Id > _lastSeekId));
        }

        var sortedIds = (await idQuery.ToListAsync(ct).ConfigureAwait(false))
            .OrderByDescending(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .Take(_pageSize)
            .Select(x => x.Id)
            .ToList();

        assets = await PaginationHelper.LoadInOrderAsync(query, sortedIds, ct).ConfigureAwait(false);
    }
    else
    {
        var orderedQuery = _sortBy switch
        {
            "File Type" => query.OrderBy(a => a.MimeType).ThenBy(a => a.Id),
            "File Size" => query.OrderBy(a => a.FileSize).ThenBy(a => a.Id),
            _ => query.OrderBy(a => a.FileName).ThenBy(a => a.Id)
        };

        if (_page > 0)
            orderedQuery = ApplyKeysetPagination(orderedQuery);

        assets = await orderedQuery
            .Take(_pageSize)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    // Store seek values for next page
    if (assets.Count > 0)
    {
        var last = assets[^1];
        _lastSeekFileName = last.FileName;
        _lastSeekCreatedAt = last.CreatedAt;
        _lastSeekModifiedAt = last.ModifiedAt;
        _lastSeekMimeType = last.MimeType;
        _lastSeekFileSize = last.FileSize;
        _lastSeekId = last.Id;
    }

    return assets;
}

// ── New: ~30 lines ──
private List<AssetListItem> BuildAssetItems(List<DigitalAsset> assets)
{
    var thumbnailDir = GetThumbnailDir();
    var newItems = new List<AssetListItem>(assets.Count);

    foreach (var asset in assets)
    {
        var thumbnailPath = _thumbnailService.GetThumbnailPath(asset.StoragePath, thumbnailDir);
        var (colorLabel, colorBrush) = AssetListItem.MapLabelToDisplay(asset.Label);

        var item = new AssetListItem
        {
            Id = asset.Id,
            Title = asset.Title,
            FileName = asset.FileName,
            StoragePath = asset.StoragePath,
            FileType = asset.MimeType,
            FileSize = asset.FileSize,
            Width = asset.Width,
            Height = asset.Height,
            CreatedAt = asset.CreatedAt,
            ThumbnailPath = thumbnailPath,
            Rating = asset.Rating,
            ColorLabel = colorLabel,
            ColorBrush = colorBrush,
            IsFlagged = asset.Flag != AssetFlag.Unflagged
        };

        AddToolbarActions(item);
        newItems.Add(item);
    }

    return newItems;
}

private string GetThumbnailDir() =>
    Path.Combine(
        Path.GetDirectoryName(_modeManager.DbPath) ?? ".",
        "thumbnails");
```

Note that `BuildAssetItems` is also used by `ExecuteSearchAsync` and `ExecuteSemanticSearchAsync` — both have near-identical `AssetListItem` construction code. The refactoring should consolidate those too (suggested as a follow-up).

### Implementation Steps

| Step | Description | Effort |
|------|-------------|--------|
| 1 | Extract `ResolveStandalonePaginationAsync` — move the DB query + pagination + seek storage | Medium |
| 2 | Extract `BuildAssetItems` — move the `DigitalAsset → AssetListItem` mapping | Medium |
| 3 | Extract `GetThumbnailDir` helper | Tiny |
| 4 | Extract `LoadPageStandaloneAsync` and `LoadPageMultiUserAsync` from `LoadPageAsync` | Medium |
| 5 | Simplify `LoadPageAsync` to delegate to the two mode methods + `finally` block | Small |
| 6 | Consolidate duplicate `AssetListItem` construction in `ExecuteSearchAsync` and `ExecuteSemanticSearchAsync` to reuse `BuildAssetItems` | Medium |
| 7 | Verify all tests still pass | Small |

### Expected Outcome

- **LoadPageAsync** reduces from ~300 lines to ~25 lines (delegation + finally)
- **LoadPageStandaloneAsync** — ~80 lines
- **ResolveStandalonePaginationAsync** — ~50 lines
- **BuildAssetItems** — ~30 lines (shared between LoadPage + 2 search methods)
- **LoadPageMultiUserAsync** — ~100 lines
- Clear responsibility boundaries for each method
- Future pagination changes only affect `ResolveStandalonePaginationAsync`

## Files Changed

| File | Change |
|------|--------|
| `src/Adam.CatalogBrowser/ViewModels/AssetGalleryViewModel.cs` | Method extraction + consolidation |

## Testing

```bash
dotnet test tests/Adam.CatalogBrowser.Tests
```

All tests should pass without modification since no behavior changes are made — only code motion.

## Risk Assessment

- **Very Low**: Pure code motion — no logic changes
- **Low**: The `BuildAssetItems` consolidation in `ExecuteSearchAsync`/`ExecuteSemanticSearchAsync` must preserve the `HighlightText` and `MatchedFields` and `SearchScore` property assignments that are specific to search results
- **Low**: The `ExecuteSearchAsync`/`ExecuteSemanticSearchAsync` methods have slightly different `AssetListItem` construction — those differences need to be preserved via optional parameters or post-processing

### Important details for `BuildAssetItems` search compatibility

The `BuildAssetItems` method extracted from `LoadPageAsync` creates items with:
- `Width`/`Height` (from asset)
- `SearchHighlightText`/`MatchedFields` NOT set
- `SearchScore` NOT set

The search methods additionally set:
- `HighlightText = query`
- `MatchedFields = result.MatchedFields`
- `SearchScore = result.Score` (semantic search)

These can be handled via optional parameters on `BuildAssetItems`, or by setting them after construction. The simplest approach:

```csharp
// BuildAssetItems is the base factory
private List<AssetListItem> BuildAssetItems(List<DigitalAsset> assets)
{
    ...
}

// Search methods set additional properties after construction
var items = BuildAssetItems(assets);
foreach (var item in items.Zip(results, (item, result) => (item, result)))
{
    item.item.HighlightText = query;
    item.item.MatchedFields = item.result.MatchedFields;
}
```

## Modularity Assessment Findings — Cross-Reference

This refactoring addresses the **AssetGalleryViewModel monolithic method** problem identified in the audit:

| Metric | Before | After |
|--------|--------|-------|
| `LoadPageAsync` lines | ~300 | ~25 (delegate-only) |
| `BuildAssetItems` duplication | 3 copies (LoadPage + 2 search executors) | 1 shared method |
| AssetGalleryViewModel lines | 1,568 | ~1,400 |
| Concern mixing | DB query + pagination + UI dispatch + thumbnail loading | Each in its own method |

**Broader modularity context:** This is the fourth planned extraction. Together with the other 3 MainWindowVM extractions, it addresses the 4 largest files in the CatalogBrowser ViewModel layer. The `BuildAssetItems` consolidation also removes code duplication that spans across `ExecuteSearchAsync` and `ExecuteSemanticSearchAsync`.

**Related plans:**
- `refactoring-01-navigation-service/PLAN.md`, `refactoring-02-bulk-operation-service/PLAN.md`, `refactoring-03-panel-state-service/PLAN.md` — The other 3 extractions
