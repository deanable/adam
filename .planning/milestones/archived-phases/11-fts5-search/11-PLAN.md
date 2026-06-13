---
goal: Complete full-text search integration — wire existing IFtsService implementations into the UI, add dedicated search bar with suggestions, and provide multi-provider FTS compatibility.
version: 2.1
date_created: 2026-06-13
last_updated: 2026-06-13
status: 'Planned'
tags: [search, fts, sqlite, postgresql, sqlserver, performance, ui]
---

# Phase 11: Full-Text Search (FTS5)

![Status: Planned](https://img.shields.io/badge/status-Planned-blue)

Replace the LIKE-based `SearchService` with provider-specific full-text search. SQLite uses FTS5 virtual tables with trigger-based sync, PostgreSQL uses `tsvector`/`tsquery` with GIN index, SQL Server uses full-text catalog with CONTAINS. An `IFtsService` strategy pattern abstracts the provider differences.

> **Note:** The FTS infrastructure layer (IFtsService, SqliteFtsService, PostgresFtsService, SqlServerFtsService, SearchResult) is already substantially implemented in the working tree. This plan focuses on completing the integration: updating SearchService, adding the search bar UI, suggestions, highlighting, and multi-provider testing.

**Depends on:** Phases 1–9 (v1.0 codebase), Phase 6 (multi-provider infrastructure)

---

## 1. Requirements & Constraints

- **PERF-01**: Full-text search across Title, Description, FileName, and keyword names returns results within 2 seconds at 100K assets
- **META-V2-01**: Search results include relevance ranking (best match first)
- **DB-02/03/04**: FTS must work across all three providers (SQLite, PostgreSQL, SQL Server) without changing caller code
- **CATA-10**: Full-text search across all metadata fields returns results within 2 seconds at 100K assets
- **D11.1**: **Dedicated search bar** above the gallery grid with autocomplete suggestions (replaces implicit filter-based search)
- **D11.2**: `SearchService` is replaced — its LIKE logic is replaced with `IFtsService.SearchAsync()`, preserving non-FTS filter parameters

---

## 2. Current State (Working Tree Analysis)

### 2.1 Already Implemented ✅

The following are present in the working tree (untracked/new files + modifications):

**FTS Infrastructure (Adam.Shared):**
- `IFtsService` interface with `SearchAsync`, `GetSuggestionsAsync`, `IsAvailableAsync`, `EnsureReadyAsync`, `RebuildIndexAsync`
- `SqliteFtsService` — complete FTS5 implementation:
  - FTS5 virtual table creation (`digital_assets_fts`)
  - Rowid mapping table (`digital_assets_fts_map`) for Guid PK bridge
  - Sync triggers on DigitalAssets (INSERT, UPDATE, DELETE) and AssetKeywords (INSERT, DELETE)
  - bm25() relevance ranking
  - Prefix matching with `*`, phrase queries with `"..."`,
  - FTS5 compile-time detection via `sqlite3_compileoption_used('ENABLE_FTS5')`
  - `EnsureFtsReadyAsync`, `SearchAsync`, `GetSuggestionsAsync`, `IsAvailableAsync`, `RebuildIndexAsync`
- `PostgresFtsService` — tsvector/tsquery implementation
- `SqlServerFtsService` — CONTAINS implementation with full-text catalog
- `SearchResult` model with `Asset`, `Rank`, `MatchedFields`
- `ThumbnailCache.cs` (in-memory LRU — Phase 12 overlap)

**DI Registration:**
- `BrokerService/Program.cs` — registers `IFtsService` based on `DbProviderConfig.Provider`
- `CatalogBrowser/App.axaml.cs` — registers `SqliteFtsService` for standalone mode

**AssetGalleryViewModel (partial FTS integration):**
- `IFtsService` constructor parameter (optional)
- `SearchText` property with `OnSearchTextChangedAsync` handler
- 300ms debounce via `Task.Delay` + `CancellationTokenSource`
- `ExecuteSearchAsync` — queries IFtsService, populates gallery with results including highlighting metadata
- `ClearSearch()` — resets search state, reloads gallery
- `SearchSuggestions` ObservableCollection
- `IsSearchActive` property
- `searchCts` CancellationTokenSource for cancellation

**AssetListItem:**
- `HighlightText`, `MatchedFields`, `MatchedFieldsText`, `IsSearchHighlighted` properties

**ModeManager:**
- `FtsService` property set from DI in App.axaml.cs

### 2.2 Not Yet Implemented ❌

1. **SearchService update**: Still uses LIKE-based logic — needs IFtsService integration
2. **Search bar in gallery UI**: No search TextBox visible in `MainWindow.axaml` or `AssetGalleryView.axaml`
3. **Search suggestions dropdown**: `SearchSuggestions` ObservableCollection exists but no UI binding
4. **Search result highlighting in gallery tiles**: `IsSearchHighlighted`/`HighlightText` properties exist but no tile visual rendering
5. **Multi-provider FTS tests**: Only SqliteFtsServiceTests exist
6. **Keyword/Category tree search integration**: FTS in sidebar search boxes

### 2.3 Existing SearchService (to be updated)

File: `src/Adam.Shared/Services/SearchService.cs`

- `SearchAsync(query, type, collectionId, tags, minRating, maxRating, fromDate, toDate, sortBy, sortDir, page, pageSize)`
- Uses `EF.Functions.Like()` for text search on Title, Description, FileName, Keywords, CameraMake, CameraModel, Creator, Copyright
- Supports non-FTS filters: type, collection, tags, rating range, date range, sort, pagination

---

## 3. Ultra-Detailed Implementation Steps

### Wave 1: Update SearchService to Use IFtsService

#### T11.9 — Integrate IFtsService into SearchService

**Files changed:**
- `src/Adam.Shared/Services/SearchService.cs` — replace LIKE logic with IFtsService call
- `src/Adam.Shared/Services/IFtsService.cs` — verify interface covers all needed search patterns

**Detailed implementation:**

Replace the LINQ `.Contains()` / `.Like()` logic with an IFtsService call for text queries while preserving all non-FTS filter parameters:

```csharp
public class SearchService
{
    private readonly AppDbContext _context;
    private readonly IFtsService? _ftsService;  // new: nullable for backward compat

    public SearchService(AppDbContext context, IFtsService? ftsService = null)
    {
        _context = context;
        _ftsService = ftsService;
    }

    public async Task<List<DigitalAsset>> SearchAsync(
        string? query = null,
        AssetType? type = null,
        Guid? collectionId = null,
        string[]? tags = null,
        int? minRating = null,
        int? maxRating = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        string sortBy = "FileName",
        string sortDir = "asc",
        int page = 1,
        int pageSize = 100,
        CancellationToken ct = default)
    {
        // Step 1: If there's a text query and IFtsService is available, use FTS
        if (!string.IsNullOrWhiteSpace(query) && _ftsService != null)
        {
            return await SearchWithFtsAsync(query, type, collectionId, tags,
                minRating, maxRating, fromDate, toDate, sortBy, sortDir, page, pageSize, ct);
        }

        // Step 2: No text query or no FTS — use existing LINQ approach
        return await SearchWithLinqAsync(query, type, collectionId, tags,
            minRating, maxRating, fromDate, toDate, sortBy, sortDir, page, pageSize, ct);
    }

    private async Task<List<DigitalAsset>> SearchWithFtsAsync(
        string query, AssetType? type, Guid? collectionId, string[]? tags,
        int? minRating, int? maxRating, DateTime? fromDate, DateTime? toDate,
        string sortBy, string sortDir, int page, int pageSize, CancellationToken ct)
    {
        // FTS returns ranked results
        var ftsResults = await _ftsService!.SearchAsync(query, maxResults: page * pageSize, ct);

        // Get raw asset IDs from FTS results
        var ftsAssetIds = ftsResults.Select(r => r.Asset.Id).ToList();

        // Apply non-FTS filters as a post-query
        var q = _context.DigitalAssets
            .Include(a => a.Collection)
            .Include(a => a.MetadataProfile)
            .Include(a => a.Keywords)
            .Where(a => ftsAssetIds.Contains(a.Id));

        // Apply the same filter chain as SearchWithLinqAsync
        q = ApplyFilters(q, type, collectionId, tags, minRating, maxRating, fromDate, toDate);

        // Order by FTS rank (preserve FTS relevance order)
        var filtered = await q.ToListAsync(ct);

        // Re-sort by FTS rank (filtered list is in DB order, not rank order)
        var rankOrder = ftsResults
            .Select((r, i) => (r.Asset.Id, i))
            .ToDictionary(x => x.Id, x => x.i);

        return filtered.OrderBy(a => rankOrder.GetValueOrDefault(a.Id, int.MaxValue))
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();
    }

    private async Task<List<DigitalAsset>> SearchWithLinqAsync(
        string? query, AssetType? type, Guid? collectionId, string[]? tags,
        int? minRating, int? maxRating, DateTime? fromDate, DateTime? toDate,
        string sortBy, string sortDir, int page, int pageSize, CancellationToken ct)
    {
        // Original LIKE-based logic (unchanged)
        var q = _context.DigitalAssets
            .Include(a => a.Collection)
            .Include(a => a.MetadataProfile)
            .Include(a => a.Keywords)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var search = query.ToLowerInvariant();
            q = q.Where(a =>
                a.Title.ToLower().Contains(search) ||
                (a.Description != null && a.Description.ToLower().Contains(search)) ||
                a.FileName.ToLower().Contains(search) ||
                a.Keywords.Any(k => k.Name.ToLower().Contains(search)) ||
                (a.MetadataProfile != null && (
                    a.MetadataProfile.CameraMake != null && a.MetadataProfile.CameraMake.ToLower().Contains(search) ||
                    a.MetadataProfile.CameraModel != null && a.MetadataProfile.CameraModel.ToLower().Contains(search) ||
                    a.MetadataProfile.Creator != null && a.MetadataProfile.Creator.ToLower().Contains(search) ||
                    a.MetadataProfile.Copyright != null && a.MetadataProfile.Copyright.ToLower().Contains(search)
                ))
            );
        }

        q = ApplyFilters(q, type, collectionId, tags, minRating, maxRating, fromDate, toDate);
        q = ApplySorting(q, sortBy, sortDir);

        return await q
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    // Extract ApplyFilters and ApplySorting as shared methods
    private IQueryable<DigitalAsset> ApplyFilters(IQueryable<DigitalAsset> q,
        AssetType? type, Guid? collectionId, string[]? tags,
        int? minRating, int? maxRating, DateTime? fromDate, DateTime? toDate)
    {
        if (type.HasValue)
            q = q.Where(a => a.Type == type.Value);
        if (collectionId.HasValue)
            q = q.Where(a => a.CollectionId == collectionId.Value);
        if (tags is { Length: > 0 })
            q = q.Where(a => a.Keywords.Any(k => tags.Contains(k.Name)));
        if (minRating.HasValue && maxRating.HasValue)
            q = q.Where(a => a.MetadataProfile != null && a.MetadataProfile.Rating >= minRating.Value && a.MetadataProfile.Rating <= maxRating.Value);
        if (fromDate.HasValue)
            q = q.Where(a => a.CreatedAt >= fromDate.Value);
        if (toDate.HasValue)
            q = q.Where(a => a.CreatedAt <= toDate.Value);
        return q;
    }

    private IQueryable<DigitalAsset> ApplySorting(IQueryable<DigitalAsset> q, string sortBy, string sortDir)
    {
        return (sortBy, sortDir.ToLower()) switch
        {
            ("FileName", "desc") => q.OrderByDescending(a => a.FileName),
            ("FileName", _) => q.OrderBy(a => a.FileName),
            ("DateAdded", "desc") => q.OrderByDescending(a => a.CreatedAt),
            ("DateAdded", _) => q.OrderBy(a => a.CreatedAt),
            ("FileType", "desc") => q.OrderByDescending(a => a.Type),
            ("FileType", _) => q.OrderBy(a => a.Type),
            ("FileSize", "desc") => q.OrderByDescending(a => a.FileSize),
            ("FileSize", _) => q.OrderBy(a => a.FileSize),
            _ => q.OrderBy(a => a.FileName)
        };
    }
}
```

**Edge cases handled:**
- `_ftsService == null` — gracefully falls back to LIKE (e.g., FTS5 not compiled into SQLite)
- Empty query with non-FTS filters — uses LIKE path (same as today)
- FTS returns 0 results — returns empty list, skips post-filtering
- Rank-order preservation after post-filtering — re-sorts by FTS rank after LINQ filters

**Registration update:** In `App.axaml.cs` and `Program.cs`, register `SearchService` with `IFtsService`:
```csharp
services.AddTransient<SearchService>(sp =>
    new SearchService(sp.GetRequiredService<AppDbContext>(), sp.GetService<IFtsService>()));
```

### Wave 2: Search Bar UI

#### T11.12 — Dedicated Search Bar in Gallery

**Files changed:**
- `src/Adam.CatalogBrowser/Views/AssetGalleryView.axaml` — add search bar
- `src/Adam.CatalogBrowser/ViewModels/AssetGalleryViewModel.cs` — already has SearchText property

**A. AssetGalleryView.axaml — add search bar at top:**

```xml
<!-- Search bar above the gallery grid -->
<Border Grid.Row="0" Padding="8,4" Background="{StaticResource SurfaceBrush}">
  <Grid ColumnDefinitions="*,Auto" VerticalAlignment="Center">
    <TextBox x:Name="SearchBox"
             Text="{Binding SearchText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
             WatermarkTemplate="{StaticResource SearchWatermark}"
             FontSize="14" Padding="8,6" CornerRadius="6"
             BorderBrush="#D0D0D0" BorderThickness="1"
             ClearButtonVisible="True">
      <TextBox.Styles>
        <Style Selector="TextBox:focus">
          <Setter Property="BorderBrush" Value="#1976D2" />
          <Setter Property="BorderThickness" Value="2" />
        </Style>
      </TextBox.Styles>
    </TextBox>
    <Button Grid.Column="1" Content="Search" Margin="6,0,0,0"
            Command="{Binding SearchCommand}" IsVisible="{Binding IsSearchActive}"
            FontSize="13" Padding="10,6" CornerRadius="6" />
  </Grid>
</Border>
```

**B. Search watermark template** (in `AssetGalleryView.axaml` Resources):
```xml
<Window.Resources>
  <DataTemplate x:Key="SearchWatermark">
    <StackPanel Orientation="Horizontal" Spacing="4">
      <TextBlock Text="🔍" FontSize="12" />
      <TextBlock Text="Search by name, description, keywords, camera..." FontSize="13" Foreground="#888" />
      <TextBlock Text="Ctrl+F" FontSize="10" Foreground="#AAA" Margin="8,0,0,0" VerticalAlignment="Center" />
    </StackPanel>
  </DataTemplate>
</Window.Resources>
```

**C. Add SearchCommand to AssetGalleryViewModel** (if not present):

```csharp
public ICommand SearchCommand { get; }
public ICommand ClearSearchCommand { get; } // already exists per working tree
```

Initialize:
```csharp
SearchCommand = new RelayCommand(async _ => await ExecuteSearchAsync(_searchText?.Trim() ?? "", CancellationToken.None),
    _ => !string.IsNullOrWhiteSpace(_searchText));
```

**D. Search autocomplete suggestions dropdown:**

Add a ListBox/ComboBox that appears below the search bar when suggestions are available:

```xml
<Popup IsOpen="{Binding IsSearchActive}" Placement="Bottom" PlacementTarget="{Binding ElementName=SearchBox}" 
       StaysOpen="False" Width="{Binding ElementName=SearchBox, Path=Bounds.Width}">
  <ListBox ItemsSource="{Binding SearchSuggestions}" MaxHeight="200" Background="White" BorderBrush="#D0D0D0" BorderThickness="1">
    <ListBox.ItemTemplate>
      <DataTemplate>
        <TextBlock Text="{Binding}" FontSize="13" Padding="8,4" />
      </DataTemplate>
    </ListBox.ItemTemplate>
  </ListBox>
</Popup>
```

#### T11.10 — Search Result Highlighting in Gallery Tiles

**Files changed:**
- `src/Adam.CatalogBrowser/Views/AssetTileControl.axaml` — add highlighting visuals
- `src/Adam.CatalogBrowser/Controls/AssetTileControl.cs` — expose highlight properties

**A. AssetTileControl.axaml — add highlight indicator:**

```xml
<!-- In the tile, when IsSearchHighlighted, show matched fields badge -->
<Border Background="#E3F2FD" CornerRadius="3" Padding="4,2" Margin="0,2,0,0"
        IsVisible="{Binding IsSearchHighlighted}">
  <TextBlock Text="{Binding MatchedFieldsText}" FontSize="9" Foreground="#1976D2"
             TextTrimming="CharacterEllipsis" />
</Border>
```

**B. Bold matching text in tile title:**

In `AssetTileControl.cs`, add a method to generate `FormattedText` with highlighted portions:
```csharp
/// <summary>
/// Generates an InlineCollection that highlights matching text portions.
/// </summary>
public IEnumerable<Inline> GetHighlightedTitle()
{
    if (string.IsNullOrEmpty(HighlightText) || string.IsNullOrEmpty(Title))
    {
        yield return new Run(Title);
        yield break;
    }

    var lowerTitle = Title.ToLowerInvariant();
    var lowerQuery = HighlightText.ToLowerInvariant();
    var terms = lowerQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    // Simple approach: wrap the first matching term in bold
    int idx = -1;
    foreach (var term in terms)
    {
        idx = lowerTitle.IndexOf(term, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0) break;
    }

    if (idx < 0)
    {
        yield return new Run(Title);
        yield break;
    }

    if (idx > 0)
        yield return new Run(Title[..idx]);
    yield return new Run(Title.Substring(idx, term.Length)) { FontWeight = FontWeight.Bold, Foreground = Brushes #1976D2 };
    if (idx + term.Length < Title.Length)
        yield return new Run(Title[(idx + term.Length)..]);
}
```

### Wave 3: Multi-Provider & Tests

#### T11.13 — Multi-Provider FTS Validation

**Files changed:**
- `tests/Adam.Shared.Tests/Services/SqliteFtsServiceTests.cs` — already exists, needs expansion
- `tests/Adam.Shared.Tests/Services/` — new PostgresFtsServiceTests.cs, SqlServerFtsServiceTests.cs

**Test coverage:**

| Test ID | Test Name | What It Verifies | File |
|---------|-----------|------------------|------|
| T11-T1 | `Search_ByTitle_ReturnsCorrectAsset` | FTS search by title returns the matching asset | `FtsServiceTests.cs` |
| T11-T2 | `Search_ByKeywordName_ReturnsAssetWithKeyword` | FTS search by keyword name returns assets with that keyword | `FtsServiceTests.cs` |
| T11-T3 | `Search_WithPrefix_ReturnsPartialMatches` | "sun" matches "sunset" and "sunrise" | `FtsServiceTests.cs` |
| T11-T4 | `Search_WithPhrase_ReturnsExactPhraseMatches` | "golden hour" in quotes matches only exact phrase | `FtsServiceTests.cs` |
| T11-T5 | `Search_WithNoMatch_ReturnsEmpty` | Non-matching query returns empty list | `FtsServiceTests.cs` |
| T11-T6 | `Search_Ranking_BestMatchFirst` | Title match ranks higher than Description match | `FtsServiceTests.cs` |
| T11-T7 | `Search_EmptyQuery_ReturnsEmpty` | Empty string returns empty | `FtsServiceTests.cs` |
| T11-T8 | `Search_MaxResults_LimitsResults` | maxResults=5 returns at most 5 results | `FtsServiceTests.cs` |
| T11-T9 | `GetSuggestions_ByPrefix_ReturnsMatches` | "sun" returns "sunset", "sunrise" suggestions | `FtsServiceTests.cs` |
| T11-T10 | `GetSuggestions_ShortPrefix_ReturnsEmpty` | Single-character prefix returns empty (min 2 chars) | `FtsServiceTests.cs` |
| T11-T11 | `IsAvailable_WhenTableExists_ReturnsTrue` | After EnsureReadyAsync, IsAvailableAsync returns true | `FtsServiceTests.cs` |
| T11-T12 | `RebuildIndex_AfterDataChange_ReflectsChanges` | RebuildIndexAsync picks up new/removed assets | `FtsServiceTests.cs` |
| T11-T13 | `Search_AfterKeywordAddedToAsset_IncludesNewKeyword` | Adding keyword triggers sync triggers; search finds it | `FtsServiceTests.cs` |
| T11-T14 | `Search_AfterAssetDeleted_ExcludesDeleted` | Deleting asset removes it from FTS index | `FtsServiceTests.cs` |

#### T11.14 — FTS5 Compile Check & Fallback

**Already implemented** in SqliteFtsService:
- `IsFts5CompiledAsync()` checks `sqlite3_compileoption_used('ENABLE_FTS5')`
- `IsAvailableAsync()` returns false if FTS5 not compiled → fallback to LIKE in SearchService

**Test:**
| Test ID | Test Name | Verifies |
|---------|-----------|----------|
| T11-T15 | `SearchService_Fallback_WhenFtsUnavailable` | When IFtsService is null, SearchService uses LIKE path |

---

## 4. Execution Waves

| Wave | Tasks | Depends On | Rationale |
|------|-------|------------|-----------|
| **Wave 1 — SearchService** | T11.9 | — | Core integration: IFtsService → SearchService. Self-contained. |
| **Wave 2 — Search UI** | T11.10, T11.11, T11.12 | Wave 1 | Search bar, highlighting, suggestions. Needs Wave 1 for data. |
| **Wave 3 — Tests** | T11.13, T11.14 | Wave 1 + 2 | Comprehensive tests for the entire FTS stack. |

---

## 5. File Change Matrix

| # | File | Change Type | Details |
|---|------|-------------|---------|
| 1 | `src/Adam.Shared/Services/SearchService.cs` | Modify | Add IFtsService constructor parameter; split into SearchWithFtsAsync + SearchWithLinqAsync + ApplyFilters + ApplySorting |
| 2 | `src/Adam.CatalogBrowser/Views/AssetGalleryView.axaml` | Modify | Add search bar above gallery grid; add search suggestions Popup |
| 3 | `src/Adam.CatalogBrowser/ViewModels/AssetGalleryViewModel.cs` | Verify | SearchText, OnSearchTextChangedAsync, ExecuteSearchAsync already exist — verify and extend with SearchCommand |
| 4 | `src/Adam.CatalogBrowser/Controls/AssetTileControl.axaml` | Modify | Add IsSearchHighlighted matched-fields badge |
| 5 | `src/Adam.CatalogBrowser/Controls/AssetTileControl.cs` | Modify | Add highlight rendering logic for matched text |
| 6 | `src/Adam.CatalogBrowser/App.axaml.cs` | Verify | DI registration already complete |
| 7 | `src/Adam.BrokerService/Program.cs` | Verify | DI registration already complete |
| 8 | `tests/Adam.Shared.Tests/Services/SqliteFtsServiceTests.cs` | Extend | Expand existing test suite (currently exists in working tree) |
| 9 | `tests/Adam.Shared.Tests/Services/FtsServiceTests.cs` | Add | New comprehensive FTS tests |

---

## 6. Testing Strategy

### 6.1 Automated Tests

Run: `dotnet test tests/Adam.Shared.Tests --filter "FullyQualifiedName~Fts"`

Expected: 15+ tests covering all FTS operations, edge cases, ranking, suggestions, fallback.

### 6.2 Manual Tests

| Test | Steps | Expected |
|------|-------|----------|
| Search bar appears | 1. Open app. 2. Look at gallery top. | Search bar visible with placeholder text |
| Search by title | 1. Type "sunset". 2. Wait 300ms. | Gallery filters to assets with "sunset" in title/desc/keywords |
| Search suggestions | 1. Type "sun". 2. Watch dropdown. | Suggestions like "sunset", "sunrise" appear within 300ms |
| Click suggestion | 1. Type "sun". 2. Click "sunset". | Search executes for "sunset" |
| Clear search | 1. Execute search. 2. Click clear button / delete text. | Gallery returns to unfiltered view |
| Ctrl+F shortcut | 1. Press Ctrl+F. | Search box focused |
| Highlighting in tiles | 1. Search for "coast". | Tiles showing matching assets have "coast" bolded in title; "Matched in: Title" badge |
| Performance | 1. Load 100K assets. 2. Search "beach". | Results within 2 seconds |
| FTS5 not available | 1. Build with old SQLite. 2. Search. | Falls back to LIKE search (no crash) |

---

## 7. Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| FTS5 not compiled into SQLite | FTS unavailable; falls back to LIKE | SqliteFtsService checks at startup; IsAvailableAsync returns false; SearchService gracefully falls back |
| Trigger sync performance on bulk operations | Write-heavy operations (ingest, import) slower | Triggers are lightweight (single INSERT/UPDATE/DELETE per asset); test at 10K+ batch |
| PostgreSQL GIN index creation on large tables | Table lock during CREATE INDEX | Use non-concurrent index creation during initial migration only (acceptable for setup) |
| SQL Server full-text catalog population time | First search after catalog creation is slow until population completes | Call EnsureReadyAsync during startup; population runs async |
| Search bar conflicts with existing filter system | Two filtering UIs confuse users | Search bar is additive — FTS results are further filtered by sidebar filters; search + filter combination is natural |

---

## 8. Dependencies

- **DEP-001**: SQLite build must have FTS5 compiled (`sqlite3_compileoption_used('ENABLE_FTS5')`)
- **DEP-002**: PostgreSQL 12+ (tsvector/tsquery with GIN)
- **DEP-003**: SQL Server 2016+ (full-text search with CONTAINS)
- **DEP-004**: EF Core 10 (for IDbContextFactory pattern used by FTS services)

---

## 9. Rollout Order

1. **Wave 1**: Update SearchService → integration test with all providers
2. **Wave 2**: Search bar UI → end-to-end manual test (type → debounce → FTS → results)
3. **Wave 3**: Comprehensive tests → verify all 15+ tests pass across all providers
