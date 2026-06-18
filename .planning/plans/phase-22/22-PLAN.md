---
goal: AI-Native DAM Features — smart search ranking from click behavior (standalone + multi-user), auto-album generation via embedding clustering (live smart collections), near-duplicate image detection with batch+per-asset review
version: 2.0
date_created: 2026-06-17
date_updated: 2026-06-17
status: Discussed — decisions locked
phase: 22
milestone: v5.0 — AI-Native DAM
tags: [phase-22, ai-native, smart-ranking, auto-albums, near-dup]
---

# Phase 22 — AI-Native DAM Features

**v5.0 — AI-Native DAM (Part 1 of ?)**

## Context

With all 21 previous phases complete and 1,244 tests passing, the DAM has strong AI foundations from Phases 9 (AI tagging via LiquidVision/LFM2-VL) and 19 (semantic search via all-MiniLM-L6-v2 embeddings). This phase builds directly on that infrastructure to deliver four integrated AI-powered features:

| Feature | Existing Infrastructure | Gap |
|---------|------------------------|-----|
| **Smart Search Ranking** | `SemanticSearchService` with cosine similarity ranking, FTS ranking; `SearchScore` on `AssetListItem` | No behavioral signal integration (clicks, dwell time); static ranking only |
| **Auto-Album Generation** | `AssetEmbedding` with 384-dim text + 4096-dim image vectors; smart collections `IsSmart`/`SmartQueryJson` | No clustering algorithm or UI to auto-group similar assets into collections |
| **Near-Duplicate Detection** | `SemanticSearchService.FindSimilarAsync` with cosine similarity on image embeddings | No specialized duplicate-grouping UI, no configurable sensitivity, no batch dedup workflow |
| ~~**Facial Recognition**~~ | ~~`LiquidVision.Core` model download/cache/inference pipeline~~ | ~~Deferred to Phase 23~~ |

## Architecture Overview

```
┌──────────────────────────────────────────────────────────────────────────────┐
│                        Phase 22 — AI-Native DAM                              │
│                                                                               │
│  T22.1 ─── Smart Search Ranking ────────────── Click tracking → Re-rank      │
│  T22.2 ─── Auto-Album Generation ──────────── Embedding clustering → Albums  │
│  T22.3 ─── Near-Duplicate Detection ────────── Similarity grouping → Dedup   │
│  T22.1 and T22.3 share infrastructure with SemanticSearchService              │
│  T22.2 shares infrastructure with the LiquidVision model pipeline              │
│  All three features build on existing AssetEmbedding table and ONNX Runtime    │
└──────────────────────────────────────────────────────────────────────────────┘
```

### Model Architecture Overview

```
Text Query ──→ EmbeddingService ──→ all-MiniLM-L6-v2 ──→ 384-dim vector
                                                              │
                    ┌─────────────────────────────────────────┼──────────┐
                    │                                         │          │
                    ▼                                         ▼          ▼
            T22.1 Smart Ranking                       T22.2 Auto-Albums
            (click-weight × cosine sim)                (HDBSCAN cluster)

Image ──→ LFM2-VL Vision Encoder ──→ 4096-dim vector ──→ T22.3 Near-Dup Detection
                                                              (cosine sim + threshold)
```

## Key Design Decisions

1. **Smart ranking uses implicit feedback only (no explicit ratings)** — Track which search results users click on (and dwell time > 2s). Store click-through events in a new `SearchClickLog` table. On subsequent searches, boost results that have positive click history for similar queries. This avoids complex ML model training and keeps the system simple for v1. A full Learning-to-Rank (LambdaMART) model can be added in a future phase.

2. **Auto-albums use HDBSCAN clustering** on image embeddings — HDBSCAN doesn't require specifying the number of clusters upfront, handles noise (unclustered assets), and works well with high-dimensional embedding vectors. Clusters can be computed on-demand and don't need persistence — they can be regenerated when new assets are added. Use the 4096-dim image embeddings from LFM2-VL (already stored in `AssetEmbedding.ImageEmbedding`) rather than the 384-dim text embeddings for better visual grouping.

3. **Near-dup detection is batch + per-asset** — Two entry points: (a) a "Find Duplicates" button on an asset's context menu (per-asset, returns results immediately), and (b) a "Detect Duplicates" bulk action in the gallery toolbar that scans all assets and groups near-duplicates. Both use cosine similarity on the 4096-dim image embedding with a configurable threshold (default 0.92 for near-dups, 0.85 for similar). Power users can adjust sensitivity.

4. **All features are opt-in** — Smart ranking requires an "Enable smart ranking" toggle in search settings. Auto-albums are a toolbar button ("Generate Albums"), not automatic. Near-dup detection is triggered manually. This follows the existing pattern from AI tagging (Phases 9/14).

5. **No .proto schema changes needed** — All three features are client-side or use existing broker contracts. Smart ranking click logs are synced through the broker using a new `LogSearchClickRequest`/`LogSearchClickResponse` message pair and a `ReRankRequest`/`ReRankResponse` pair.

6. **Auto-albums are live smart collections** — Generated albums create `Collection` entries with `IsSmart = true`. The `SmartQueryJson` contains embedding similarity parameters, so albums auto-update as new similar assets are ingested.

## Tasks

### T22.1 — Smart Search Ranking

**Files:**
- `src/Adam.Shared/Models/SearchClickLog.cs` (new)
- `src/Adam.Shared/Data/AppDbContext.cs` (add `SearchClickLogs` DbSet)
- `src/Adam.Shared/Services/SearchRankingService.cs` (new)
- `src/Adam.CatalogBrowser/ViewModels/AssetGalleryViewModel.cs` (add click tracking, re-rank integration)
- `src/Adam.CatalogBrowser/Views/AssetGalleryView.axaml.cs` (add click tracking on search result tiles)

**SearchClickLog entity:**
```csharp
public sealed class SearchClickLog
{
    public Guid Id { get; set; }
    public Guid AssetId { get; set; }
    public string QueryText { get; set; } = string.Empty;
    public string? NormalizedQuery { get; set; }
    public DateTimeOffset ClickedAt { get; set; }
    public int DwellTimeMs { get; set; } // time spent viewing before next action
    public int RankPosition { get; set; } // the rank position when clicked
    public DigitalAsset Asset { get; set; } = null!;
}

// Indexes:
// (NormalizedQuery, ClickedAt) — for query-scoped recency
// (AssetId, NormalizedQuery) — for per-asset query affinity
```

**SearchRankingService:**
```csharp
public sealed class SearchRankingService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    // Log a click event when user opens an asset from search results
    public async Task LogClickAsync(Guid assetId, string query,
        int rankPosition, int dwellTimeMs, CancellationToken ct);

    // Apply ranking boost: returns asset IDs sorted by combined score
    // score = 0.7 × cosine_similarity + 0.3 × click_affinity
    public async Task<IReadOnlyList<Guid>> ReRankAsync(
        IReadOnlyList<SemanticSearchResult> results,
        string query, CancellationToken ct);

    // Purge logs older than 90 days
    public async Task PurgeOldLogsAsync(CancellationToken ct);

    // Get click count for a specific asset+query pair
    public async Task<int> GetClickCountAsync(Guid assetId,
        string normalizedQuery, CancellationToken ct);
}

// Click affinity function:
// weight = Σ (clicks for this asset + query) / max_clicks_for_any_asset + this_query
//  + recency_bonus (clicks in last 7 days weighted 3×)
//  + dwell_bonus (dwell > 5s weighted 2×)
```

**UI integration:**

```
Search active: "sunset beach"
┌────────────────────────────────────────────────────────────┐
│  Results ranked by: [◎ Smart (combined) ▼]                 │
│                                                             │
│  1. Sunset_Beach.jpg   ████████░░ 92% match  ★★★★  12 clicks
│  2. Beach_Sunset.png   ██████░░░░ 85% match  ★★★    3 clicks
│  3. Golden_Hour.jpg    ████████░░ 88% match  ★★★    0 clicks  ← new
│                                                             │
│  [x] Enable smart ranking  [x] Use my click history         │
└────────────────────────────────────────────────────────────┘
```

**Ranking toggle:** Add a rank mode selector in the search bar's dropdown: "Relevance" (cosine-only) | "Smart" (boosted) | "Date" | "Rating". Default to "Smart" when click history is available.

**Multi-user mode:**
- New broker messages: `LogSearchClickRequest/Response` and `ReRankRequest/Response`
- Client sends click events to the broker for persistent storage in the shared DB
- Ranking query is executed against shared click logs (all users contribute)
- Authentication: click logs are attributed to the connected user via JWT claims
- Standalone mode: uses local SQLite DB directly

**Edge cases:**
- No click history → fall back to pure cosine similarity
- Query normalization: lowercase, trim, remove extra whitespace for matching
- Purge logs > 90 days to prevent unbounded growth
- Dwell time tracking: start timer on click, stop on next navigation or close
- Privacy: click data is attributed to user but not exposed in the UI

**Estimated LOC:** ~320 (includes broker messages and handler)

### T22.2 — Auto-Album Generation

**Files:**
- `src/Adam.Shared/Services/EmbeddingClusterService.cs` (new)
- `src/Adam.CatalogBrowser/ViewModels/AutoAlbumViewModel.cs` (new)
- `src/Adam.CatalogBrowser/Views/AutoAlbumDialog.axaml` (new)
- `src/Adam.CatalogBrowser/Views/AutoAlbumDialog.axaml.cs` (new)
- `src/Adam.CatalogBrowser/ViewModels/MainWindowViewModel.cs` (add `GenerateAlbumsCommand`)
- `src/Adam.CatalogBrowser/Views/MainWindow.axaml` (add button in toolbar)
- `src/Adam.Shared/Data/AppDbContext.cs` (add cluster name field to Collection if needed)

**Algorithm: EmbeddingClusterService**

```csharp
/// Clusters assets by their image embedding similarity using a simplified HDBSCAN-like
/// approach: density-based spatial clustering with adaptive threshold.
/// Falls back to text embeddings for non-image assets.
public sealed class EmbeddingClusterService
{
    // ── Configuration ──
    public double MinSimilarity { get; init; } = 0.75;  // min cos sim for cluster membership
    public int MinClusterSize { get; init; } = 3;       // minimum assets per album
    public int MaxClusters { get; init; } = 20;         // soft limit on generated albums

    public async Task<IReadOnlyList<AlbumCluster>> ClusterAsync(
        IReadOnlyList<Guid>? assetIds = null,  // null = all assets
        CancellationToken ct = default);

    public async Task<AlbumCluster> ClusterForAssetAsync(
        Guid assetId,
        CancellationToken ct = default);  // find this asset's potential cluster
}

public sealed record AlbumCluster
{
    public string SuggestedName { get; init; }
    public IReadOnlyList<AssetClusterItem> Assets { get; init; }
    public float CentroidSimilarity { get; init; }
    public IReadOnlyList<string> CommonKeywords { get; init; }
}

public sealed record AssetClusterItem
{
    public Guid AssetId { get; init; }
    public string FileName { get; init; }
    public float SimilarityToCentroid { get; init; }
}
```

**Clustering algorithm (simplified HDBSCAN):**
1. Load all image embeddings (4096-dim) from `AssetEmbedding`
2. Compute pairwise cosine similarity matrix (batched to avoid O(n²) memory)
3. For each asset, find all neighbors with similarity > `MinSimilarity`
4. Build graph: edges between mutual neighbors (A→B and B→A)
5. Extract connected components — each component is a candidate cluster
6. Filter: skip clusters with < `MinClusterSize` assets
7. Generate names: find the most common keywords among cluster members, use top 2-4 as the album name
8. Sort clusters by average intra-cluster similarity (descending)
9. Limit to `MaxClusters`

**Batch optimization:**
- For catalogs with 100K+ assets, use random sampling (take 10K sample) for initial cluster discovery
- Or constrain to recently added assets (last N days)
- Report progress during computation: "Clustering 1,247 assets... (42% complete)"

**Auto-Album Dialog UI:**

```
┌─ Generate Smart Albums ───────────────────────────────────────┐
│                                                                │
│  Source: [▼ All assets  |  Last 30 days  |  Selected assets]  │
│  Sensitivity: [────────────●─────────────────]  More similar   │
│  Minimum album size: [3 ▼]   Max albums: [20 ▼]               │
│                                                                │
│  [●] Images only (using visual similarity)                     │
│  [○] All assets (using text + metadata similarity)             │
│                                                                │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │  Preview (7 clusters found)                              │   │
│  │                                                          │   │
│  │  📁 Beach Vacations ─────── 12 photos  ★★★★☆           │   │
│  │     beach, ocean, sand, sunset                           │   │
│  │                                                          │   │
│  │  📁 Family Portraits ────── 8 photos   ★★★★☆           │   │
│  │     family, portrait, studio, smiles                     │   │
│  │                                                          │   │
│  │  📁 Food & Drinks ────────── 5 photos  ★★★☆☆           │   │
│  │     food, restaurant, wine, dinner                       │   │
│  │  ...                                                     │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                │
│  [Create Albums]  [Cancel]                                     │
└────────────────────────────────────────────────────────────────┘
```

**Integration with smart collections (live):** Each generated album creates a new `Collection` with `IsSmart = true`. The `SmartQueryJson` stores embedding similarity parameters (model version, centroid vector as base64, threshold). When a user opens the collection, the smart collection refresh handler runs the clustering query and includes any new assets that fall within the similarity threshold of the cluster centroid. This means albums evolve as new assets are added — no manual regeneration needed.

**Performance considerations:**
- Embedding matrix for 10K assets = 10K × 4096 × 4 bytes ≈ 160 MB (acceptable)
- Pairwise similarity for 10K assets = ~50M comparisons (≈ 1-2 seconds with SIMD)
- Use `System.Numerics.Vector` SIMD acceleration (same pattern as `SemanticSearchService.CosineSimilarity`)
- Show progress bar during computation
- Run on background thread, never block UI

**Estimated LOC:** ~350

### T22.3 — Near-Duplicate Detection

**Files:**
- `src/Adam.Shared/Services/NearDuplicateService.cs` (new)
- `src/Adam.CatalogBrowser/ViewModels/DuplicateReviewViewModel.cs` (new)
- `src/Adam.CatalogBrowser/Views/DuplicateReviewView.axaml` (new)
- `src/Adam.CatalogBrowser/Views/DuplicateReviewView.axaml.cs` (new)
- `src/Adam.CatalogBrowser/ViewModels/MainWindowViewModel.cs` (add `FindDuplicatesCommand`, `DetectDuplicatesCommand`)
- `src/Adam.CatalogBrowser/Views/MainWindow.axaml` (add DuplicateReviewView DataTemplate)
- `src/Adam.CatalogBrowser/Views/AssetGalleryView.axaml.cs` (add "Find Duplicates" context menu item)

**NearDuplicateService:**
```csharp
public sealed class NearDuplicateService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<NearDuplicateService> _logger;

    /// Configuration
    public double NearDuplicateThreshold { get; set; } = 0.92; // almost identical
    public double SimilarThreshold { get; set; } = 0.85;       // similar (edited)

    /// Finds duplicates for a specific asset — returns grouped near-duplicates
    public async Task<IReadOnlyList<DuplicateGroup>> FindForAssetAsync(
        Guid assetId, CancellationToken ct = default);

    /// Scans entire catalog and groups all near-duplicates
    public async Task<IReadOnlyList<DuplicateGroup>> ScanAllAsync(
        IProgress<(int completed, int total)>? progress = null,
        CancellationToken ct = default);

    /// Returns statistics about duplicate density
    public async Task<DuplicateStats> GetStatsAsync(CancellationToken ct = default);
}

public sealed record DuplicateGroup
{
    public Guid GroupId { get; init; }
    public AssetClusterItem Primary { get; init; }  // highest resolution / best quality
    public IReadOnlyList<AssetClusterItem> Duplicates { get; init; }
    public float MaxScore { get; init; }
    public string GroupType { get; init; } // "Near-identical", "Edited version", "Similar"
}

public sealed record AssetClusterItem
{
    public Guid AssetId { get; init; }
    public string FileName { get; init; }
    public float SimilarityScore { get; init; }
    public long FileSize { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
}

public sealed record DuplicateStats
{
    public int TotalAssets { get; init; }
    public int AssetsWithDuplicates { get; init; }
    public int DuplicateGroups { get; init; }
    public long PotentialSavingsBytes { get; init; } // total size of duplicates
}
```

**Algorithm:**
1. For per-asset find: load the asset's image embedding, compare against all others (same as `FindSimilarAsync` but with stricter thresholds)
2. For bulk scan: 
   - Load all image embeddings that have valid `ImageEmbedding` (4096-dim, from LFM2-VL)
   - Sort embeddings by a locality-sensitive hash (LSH) to avoid O(n²) brute force
   - For LSH: use random projection hash (signature of 32 bits from the embedding)
   - Only compare assets in the same or adjacent hash buckets
   - Group results into connected components (A similar to B, B similar to C → A, B, C are a group)
3. For each group, identify the "primary" asset (highest resolution, or largest file size)
4. Tag duplicates with a type label: "Near-identical" (>0.95), "Edited version" (0.92-0.95), "Similar" (0.85-0.92)

**Duplicate Review View UI:**

```
┌─ Near-Duplicate Review ────────── 47 groups found ── [✕ Close] ┐
│                                                                   │
│  Group 3 of 47: Near-identical  (similarity 97%)                 │
│                                                                   │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐                       │
│  │ ★ Primary│  │  Duplicate│  │ Duplicate│                       │
│  │ beach.jpg│  │ beach(2). │  │ beach(3).│                       │
│  │ 4K  2.1MB│  │ HD  1.2MB│  │ HD  1.1MB│                       │
│  └──────────┘  └──────────┘  └──────────┘                       │
│                                                                   │
│  Actions:                                                         │
│  [✦ Keep Primary]  [🗑 Trash All Duplicates]  [Skip]           │
│                                                                   │
│  ◀ Previous                           Next ▶                    │
│  [■■■■■■■□□□□□□□□□□□□□□□□]  47 groups                            │
│                                                                   │
│  Potential savings: 342 MB across 47 groups                      │
└──────────────────────────────────────────────────────────────────┘
```

**Context menu integration (per-asset):**
```
Right-click on asset:
┌─ Actions ────────────────────┐
│  Open                        │
│  Open in Loupe               │
│  Compare With...             │
│  ─────────────────────       │
│  Find Duplicates ◄── NEW     │
│  ─────────────────────       │
│  AI Tag                      │
│  Properties                  │
└──────────────────────────────┘
```

**Edge cases:**
- Assets without image embeddings → skip (non-image assets)
- Assets with only text embeddings → text-based similarity (less accurate for near-dup)
- Empty catalog → return empty
- Single-image groups → filter out (need at least 2 to be a "group")

**Estimated LOC:** ~350

### Deferred to Phase 23: Facial Recognition

Facial recognition (YuNet + ArcFace ONNX pipeline) has been deferred to Phase 23 to keep Phase 22 focused and deliverable. See `23-PLAN.md` for the facial recognition plan sketch.

**Planned scope for Phase 23:**
- Person/AssetFace data models
- FaceDetectionService (YuNet ONNX, ~2 MB model from HuggingFace)
- FaceRecognitionService (ArcFace MobileFaceNet ONNX, ~8 MB model from HuggingFace)
- PersonService with CRUD
- PersonHandler + PersonMessages for multi-user mode
- FaceTaggingView UI
- Auto-tag at high confidence (>0.85 threshold), flag lower for review

## Success Criteria

### T22.1 — Smart Search Ranking
- ✅ Click events are logged when user opens an asset from search results
- ✅ Dwell time tracked from click to next navigation
- ✅ Search results can be re-ranked by click history affinity
- ✅ Ranking mode selector in search bar (Relevance / Smart / Date / Rating)
- ✅ Click logs auto-purge after 90 days
- ✅ Zero results returned gracefully (no clicks, no history → cosine-only fallback)
- ✅ 10+ tests for SearchRankingService, click logging, re-ranking logic
- ✅ All existing tests still pass

### T22.2 — Auto-Album Generation
- ✅ HDBSCAN-style clustering groups visually similar assets into albums
- ✅ Albums generate suggested names from common keywords
- ✅ Preview dialog shows clusters before creation
- ✅ Generated albums create smart collections (auto-updating)
- ✅ Sensitivity slider adjusts the MinSimilarity threshold (0.6-0.95)
- ✅ Minimum cluster size respects user setting (2-20)
- ✅ Clustering reports progress via IProgress
- ✅ Works with 10K+ assets (sampling fallback for larger catalogs)
- ✅ 15+ tests for EmbeddingClusterService (edge cases: empty, single cluster, no clusters)
- ✅ All existing tests still pass

### T22.3 — Near-Duplicate Detection
- ✅ Per-asset "Find Duplicates" finds similar images from the same embedding space
- ✅ Bulk "Detect Duplicates" scans all assets and groups near-duplicates
- ✅ Duplicate Review View shows groups with primary+duplicate tiles
- ✅ Actions: Keep Primary (mark all others for deletion), Trash All Duplicates, Skip
- ✅ Progress bar for bulk scan
- ✅ Statistics: groups found, assets affected, potential space savings
- ✅ LSH bucketing avoids O(n²) brute-force for large catalogs
- ✅ 15+ tests for NearDuplicateService (edge cases: no images, no duplicates, all similar)
- ✅ All existing tests still pass

### Overall (Phase 22)
- ✅ All 1,244 existing tests still pass
- ✅ No regressions in existing search, tagging, or gallery features
- ✅ Code review passed

## Execution Order (Waves)

### Wave 1 — Foundation Layer (Shared Infrastructure)

**Goal:** Establish shared data models, broker contracts, DI registrations, and core services that Wave 2 and Wave 3 will build on.

**Estimated time:** ~2-3 hours

| Step | File(s) | Description | LOC |
|------|---------|-------------|-----|
| 1.1 | `src/Adam.Shared/Contracts/MessageTypeCode.cs` | Add 4 new opcodes: `LogSearchClickRequest = 170`, `LogSearchClickResponse = 171`, `ReRankRequest = 172`, `ReRankResponse = 173`. Insert in the 170-179 range (after the Search History block). Values must never be reassigned — check no collisions with existing entries. | ~8 |
| 1.2 | `src/Adam.Shared/Contracts/SearchMessages.cs` | Add `LogSearchClickRequest/Response` protobuf messages (AssetId, QueryText, NormalizedQuery, RankPosition, DwellTimeMs) and `ReRankRequest/Response` messages (AssetIds, Query, Scores). Follow the existing `IProtoSerializable` pattern with `[ProtoField]` attributes. | ~80 |
| 1.3 | `src/Adam.Shared/Models/SearchClickLog.cs` | New file. Entity with Id, AssetId, QueryText, NormalizedQuery, ClickedAt, DwellTimeMs, RankPosition, UserId (nullable for standalone). Add JSON serialization attributes. | ~30 |
| 1.4 | `src/Adam.Shared/Data/AppDbContext.cs` | Add `SearchClickLogs` DbSet property. Add entity config: key, indexes on `(NormalizedQuery, ClickedAt)` and `(AssetId, NormalizedQuery)`, foreign key to DigitalAsset, relation to User (optional). Add to `OnModelCreating`. | ~25 |
| 1.5 | `src/Adam.CatalogBrowser/App.axaml.cs` | Register `SearchRankingService`, `EmbeddingClusterService`, `NearDuplicateService` as singletons in DI container. | ~5 |
| 1.6 | `src/Adam.BrokerService/Program.cs` | Register `SearchRankingService` and `SearchRankingHandler` as singletons in DI container. | ~4 |
| 1.7 | `src/Adam.Shared/Services/SearchRankingService.cs` | NEW. Core service: `LogClickAsync`, `ReRankAsync`, `PurgeOldLogsAsync`, `GetClickCountAsync`. Uses `IDbContextFactory<AppDbContext>` for thread-safe DB access. Click affinity: `weight = Σ(clicks) / max_clicks_for_query + recency_bonus(7d × 3×) + dwell_bonus(>5s × 2×)`. | ~120 |
| 1.8 | `src/Adam.Shared/Services/NearDuplicateService.cs` | NEW. Core service: `FindForAssetAsync`, `ScanAllAsync`, `GetStatsAsync`. LSH-bucketed similarity, configurable thresholds. Uses `IDbContextFactory<AppDbContext>`. | ~150 |
| 1.9 | `src/Adam.Shared/Services/EmbeddingClusterService.cs` | NEW. Core service: `ClusterAsync`, `ClusterForAssetAsync`. Simplified HDBSCAN: connected components from mutual similarity neighbors. Uses existing `SemanticSearchService.CosineSimilarity` for SIMD acceleration. | ~140 |

**Verification:** `dotnet build` — all projects compile without errors.

---

### Wave 2 — Broker Handlers & Multi-User Support

**Goal:** Implement multi-user broker handlers for smart ranking (click logging + re-ranking) and register them in the connection dispatcher.

**Estimated time:** ~1-2 hours

| Step | File(s) | Description | LOC |
|------|---------|-------------|-----|
| 2.1 | `src/Adam.BrokerService/Handlers/SearchRankingHandler.cs` | NEW. Handler with `LogClickAsync` and `ReRankAsync` methods. Follows the `HandlerBase` pattern from existing handlers. `LogClickAsync`: deserializes `LogSearchClickRequest`, calls `_rankingService.LogClickAsync`, returns `LogSearchClickResponse`. `ReRankAsync`: deserializes `ReRankRequest`, calls `_rankingService.ReRankAsync`, returns `ReRankResponse` with reordered asset IDs and scores. | ~80 |
| 2.2 | `src/Adam.BrokerService/Handlers/ConnectionHandler.cs` | Add 2 new dispatcher entries: `MessageTypeCode.LogSearchClickRequest → rankingHandler.LogClickAsync`, `MessageTypeCode.ReRankRequest → rankingHandler.ReRankAsync`. Inject `SearchRankingHandler` into constructor. | ~5 |

**Verification:** `dotnet build` — all projects compile without errors.

---

### Wave 3 — Smart Search Ranking UI (T22.1)

**Goal:** Implement click tracking, dwell time, and re-ranking in the gallery UI. Add ranking mode selector.

**Estimated time:** ~2-3 hours

| Step | File(s) | Description | LOC |
|------|---------|-------------|-----|
| 3.1 | `src/Adam.CatalogBrowser/ViewModels/AssetGalleryViewModel.cs` | Add `_rankingService` field (optional, resolved from DI). Add `RankMode` property with values: `Relevance`, `Smart`, `Date`, `Rating`. Add `IsSmartRankingEnabled` toggle. In `ExecuteSemanticSearchAsync`: after getting results, call `_rankingService.ReRankAsync` if `RankMode == "Smart"` and reorder results by combined score. In `ExecuteSearchAsync`: trigger click logging when user opens a result (via `SearchResultOpened` event). | ~60 |
| 3.2 | `src/Adam.CatalogBrowser/Views/AssetGalleryView.axaml` | Add rank mode ComboBox next to search bar: `[Relevance ▼]` with options Relevance, Smart, Date, Rating. Add `Enable Smart Ranking` toggle checkbox. Only visible when semantic search is active. | ~15 |
| 3.3 | `src/Adam.CatalogBrowser/Views/AssetGalleryView.axaml.cs` | Add click-tracking on search result tiles: when user clicks/taps a search result, start dwell timer. On navigating away from the result (click another, close search, clear), stop timer and fire `LogSearchClick` via the ViewModel. Add rank mode selector change handler. | ~40 |
| 3.4 | `src/Adam.CatalogBrowser/ViewModels/MainWindowViewModel.cs` | Resolve `SearchRankingService` from DI and pass to `AssetGalleryViewModel`. Add search result click event handling. | ~10 |
| 3.5 | `tests/Adam.Shared.Tests/Services/SearchRankingServiceTests.cs` | NEW. Tests: `LogClick_StoresClickEvent`, `ReRank_NoHistory_ReturnsOriginalOrder`, `ReRank_WithHistory_BoostsClickedAssets`, `PurgeOldLogs_RemovesExpiredEntries`, `GetClickCount_ReturnsCorrectCount`. Use in-memory SQLite with `IDbContextFactory<AppDbContext>`. | ~100 |
| 3.6 | `tests/Adam.BrokerService.Tests/Handlers/SearchRankingHandlerTests.cs` | NEW. Tests: `LogClickAsync_ValidRequest_ReturnsSuccess`, `LogClickAsync_MalformedPayload_ReturnsError`, `ReRankAsync_ValidRequest_ReturnsResults`, `ReRankAsync_EmptyHistory_ReturnsEmpty`. Use `HandlerTestBase` pattern from existing tests. | ~80 |

**Verification:** `dotnet test tests/Adam.Shared.Tests --filter "FullyQualifiedName~SearchRanking"` — 5+ tests pass.

---

### Wave 4 — Near-Duplicate Detection UI (T22.3)

**Goal:** Implement per-asset "Find Duplicates" context menu and bulk "Detect Duplicates" toolbar action with the Duplicate Review View.

**Estimated time:** ~3-4 hours

| Step | File(s) | Description | LOC |
|------|---------|-------------|-----|
| 4.1 | `src/Adam.CatalogBrowser/ViewModels/DuplicateReviewViewModel.cs` | NEW. Manages duplicate review state: `Groups` (list of `DuplicateGroup`), `CurrentGroupIndex`, `CurrentGroup` (computed), `IsScanning`, `ProgressText`, `Stats` (DuplicateStats). Commands: `NextGroupCommand`, `PreviousGroupCommand`, `KeepPrimaryCommand`, `TrashAllDuplicatesCommand`, `SkipGroupCommand`, `CloseCommand`. Uses `NearDuplicateService`. Commands call into `_deleteService` for trash operations and navigate to the next group. | ~150 |
| 4.2 | `src/Adam.CatalogBrowser/Views/DuplicateReviewView.axaml` | NEW. XAML layout with: navigation header ("Group X of Y"), side-by-side thumbnail display (primary + first duplicate), action buttons (Keep Primary, Trash All, Skip), progress bar during scan, stats footer ("Potential savings: X MB"), prev/next navigation. Uses shared converters (`BoolToVisibilityConverter`, etc.). | ~80 |
| 4.3 | `src/Adam.CatalogBrowser/Views/DuplicateReviewView.axaml.cs` | NEW. Code-behind with keyboard shortcuts (←/→ for navigation, Esc to close). No additional logic — all state managed by ViewModel. | ~15 |
| 4.4 | `src/Adam.CatalogBrowser/Views/AssetGalleryView.axaml.cs` | Add "Find Duplicates" to `BuildContextMenu()` — new `MenuItem { Header = "Find Duplicates", Command = vm.FindDuplicatesCommand }`. Insert before the separator above AI Tag. | ~3 |
| 4.5 | `src/Adam.CatalogBrowser/ViewModels/MainWindowViewModel.cs` | Add `FindDuplicatesCommand` and `DetectDuplicatesCommand` as public ICommand properties. `FindDuplicatesCommand`: gets selected asset ID, creates `DuplicateReviewViewModel`, calls `FindForAssetAsync`, sets `CurrentView = duplicateReview`. `DetectDuplicatesCommand`: same but calls `ScanAllAsync` with progress. | ~30 |
| 4.6 | `src/Adam.CatalogBrowser/Views/MainWindow.axaml` | Add DataTemplate mapping `DuplicateReviewViewModel → DuplicateReviewView` in the Window.DataTemplates section (near the LoupeViewModel/CompareViewModel templates). | ~2 |
| 4.7 | `tests/Adam.Shared.Tests/Services/NearDuplicateServiceTests.cs` | NEW. Tests: `FindForAsset_WithEmbedding_ReturnsGroups`, `FindForAsset_NoEmbedding_ReturnsEmpty`, `ScanAll_FindsDuplicateGroups`, `ScanAll_NoDuplicates_ReturnsEmpty`, `GetStats_ReturnsCorrectCounts`. | ~120 |
| 4.8 | `tests/Adam.CatalogBrowser.Tests/ViewModels/DuplicateReviewViewModelTests.cs` | NEW. Tests: `Constructor_InitialState`, `NextGroup_NavigatesForward`, `PreviousGroup_NavigatesBackward`, `KeepPrimary_CallsDeleteOnDuplicates`, `TrashAll_CallsDeleteOnAll`. Use `SyncUiDispatcher` for Avalonia-free execution. | ~100 |

**Verification:** `dotnet test tests/Adam.Shared.Tests --filter "FullyQualifiedName~NearDuplicate"` — 3+ tests pass. `dotnet test tests/Adam.CatalogBrowser.Tests --filter "FullyQualifiedName~DuplicateReview"` — 4+ tests pass.

---

### Wave 5 — Auto-Album Generation UI (T22.2)

**Goal:** Implement the auto-album preview dialog, clustering algorithm, and live smart collection creation.

**Estimated time:** ~3-4 hours

| Step | File(s) | Description | LOC |
|------|---------|-------------|-----|
| 5.1 | `src/Adam.CatalogBrowser/ViewModels/AutoAlbumViewModel.cs` | NEW. Manages album generation state: `SourceScope` (All / Last30Days / Selected), `Sensitivity` (0.6-0.95 slider), `MinClusterSize` (2-20), `MaxClusters` (5-50), `Mode` (ImagesOnly / AllAssets), `PreviewClusters` (list of `AlbumCluster`), `IsComputing`, `ProgressText`. Commands: `ComputePreviewCommand`, `CreateAlbumsCommand`, `CloseCommand`. Uses `EmbeddingClusterService`. | ~130 |
| 5.2 | `src/Adam.CatalogBrowser/Views/AutoAlbumDialog.axaml` | NEW. XAML dialog with: source scope dropdown, sensitivity slider, min/max album size numeric up-downs, mode radio buttons, preview list with cluster cards (name, photo count, quality stars, keyword tags), progress bar during computation, Create Albums + Cancel buttons. Reuses existing `AssetTileControl` for thumbnail previews. | ~80 |
| 5.3 | `src/Adam.CatalogBrowser/Views/AutoAlbumDialog.axaml.cs` | NEW. Code-behind with keyboard shortcuts (Enter to confirm, Esc to cancel). | ~10 |
| 5.4 | `src/Adam.CatalogBrowser/ViewModels/MainWindowViewModel.cs` | Add `GenerateAlbumsCommand` as public ICommand. Creates `AutoAlbumViewModel`, shows dialog via `ShowDialog` pattern. On `CreateAlbumsCommand` completion: creates `Collection` entries with `IsSmart = true` and populates `SmartQueryJson` with embedding centroid + threshold. Refreshes sidebar. | ~40 |
| 5.5 | `src/Adam.BrokerService/Handlers/CollectionHandler.cs` | Extend `RefreshSmartCollectionAsync` to support embedding-based smart collections. When `SmartQueryJson` contains a `Centroid` field (base64-encoded float array), compute similarity against the stored centroid and include all assets above the threshold. | ~30 |
| 5.6 | `tests/Adam.Shared.Tests/Services/EmbeddingClusterServiceTests.cs` | NEW. Tests: `ClusterAsync_WithSimilarAssets_ReturnsClusters`, `ClusterAsync_NoSimilar_ReturnsEmpty`, `ClusterAsync_SingleAsset_ReturnsEmpty`, `ClusterAsync_MinClusterSize_FiltersSmallClusters`, `ClusterAsync_MaxClusters_RespectsLimit`. | ~100 |
| 5.7 | `tests/Adam.CatalogBrowser.Tests/ViewModels/AutoAlbumViewModelTests.cs` | NEW. Tests: `Constructor_DefaultValues`, `ComputePreview_ReturnsClusters`, `CreateAlbums_CreatesCollections`, `Sensitivity_UpdatesSimilarity`. | ~80 |

**Verification:** `dotnet test tests/Adam.Shared.Tests --filter "FullyQualifiedName~EmbeddingCluster"` — 3+ tests pass. `dotnet test tests/Adam.CatalogBrowser.Tests --filter "FullyQualifiedName~AutoAlbum"` — 3+ tests pass.

---

### Wave 6 — Integration & Verification

**Goal:** Full test suite run, edge case hardening, code review, plan completion.

**Estimated time:** ~1-2 hours

| Step | Action | Description |
|------|--------|-------------|
| 6.1 | `dotnet test` | Run full test suite (1,244 + new = ~1,350 tests). All must pass. |
| 6.2 | `dotnet build` (all projects) | Verify no warnings or errors. |
| 6.3 | Edge case hardening | Check: empty catalogs (no embeddings → graceful fallbacks), 100K+ catalogs (sampling fallback for clustering), no click history (cosine-only fallback), LSH bucket tuning for near-dup performance. |
| 6.4 | Code review | Spawn `code-reviewer-deepseek-flash` to review all new/modified files. |
| 6.5 | Update STATE.md | Mark Phase 22 as complete. Archive v5.0 milestone (or note Phase 23 facial recognition remaining). |

**Verification:** `dotnet test` — 0 failures. Code review — no HIGH/MEDIUM concerns.

## Wave Dependency Graph

```
Wave 1 (Foundation) ───────────────────────────────────────────
      │                          │                          │
      ▼                          ▼                          ▼
Wave 2 (Broker)          Wave 4 (Near-Dup UI)       Wave 5 (Auto-Album UI)
      │                                                    
      ▼                                                    
Wave 3 (Smart Ranking UI) ───── (depends on Wave 2)        
      │                          │                          │
      └──────────────────────┬──────────────────────────────┘
                             ▼
                    Wave 6 (Integration)
```

**Key dependency notes:**
- Wave 2 depends on Wave 1 (needs SearchRankingService + Messages)  
- Wave 3 depends on Wave 2 (needs broker handlers for multi-user)  
- Waves 4 and 5 only depend on Wave 1 (can run in parallel with Waves 2-3)  
- Wave 6 depends on all previous waves

## Parallelization Strategy

With 2-3 developers working in parallel:
- **Dev A:** Waves 1 → 2 → 3 (Smart Ranking — end-to-end, includes broker)  
- **Dev B:** Waves 1 → 4 (Near-Dup Detection — needs core service only)  
- **Dev C:** Waves 1 → 5 (Auto-Albums — needs core service only)  
- **All:** Wave 6 (integration and verification)

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Embedding clustering quality is subjective | Medium | Medium | Show preview before creating albums; let users merge/split/rename generated albums; sensitivity slider gives user control |
| Smart ranking bias toward recently viewed assets | Medium | Low | Decay function for click weight (exponential decay over 90 days); always include pure cosine results as a baseline |
| Large catalog (>50K images) clustering is slow | Medium | Medium | Random sampling fallback; show "Processing X of Y" progress; consider background processing with cache |
| Broker message overhead from click logging | Low | Low | Batched log submission (every 10 clicks or 30s, whichever comes first) |

## Files Created

| # | File | Task |
|---|------|------|
| 1 | `src/Adam.Shared/Models/SearchClickLog.cs` | T22.1 |
| 2 | `src/Adam.Shared/Services/SearchRankingService.cs` | T22.1 |
| 3 | `src/Adam.Shared/Services/EmbeddingClusterService.cs` | T22.2 |
| 4 | `src/Adam.CatalogBrowser/ViewModels/AutoAlbumViewModel.cs` | T22.2 |
| 5 | `src/Adam.CatalogBrowser/Views/AutoAlbumDialog.axaml` | T22.2 |
| 6 | `src/Adam.CatalogBrowser/Views/AutoAlbumDialog.axaml.cs` | T22.2 |
| 7 | `src/Adam.Shared/Services/NearDuplicateService.cs` | T22.3 |
| 8 | `src/Adam.CatalogBrowser/ViewModels/DuplicateReviewViewModel.cs` | T22.3 |
| 9 | `src/Adam.CatalogBrowser/Views/DuplicateReviewView.axaml` | T22.3 |
| 10 | `src/Adam.CatalogBrowser/Views/DuplicateReviewView.axaml.cs` | T22.3 |
| 11 | `src/Adam.Shared/Contracts/SearchMessages.cs` | T22.1 | Add `LogSearchClickRequest/Response` and `ReRankRequest/Response` broker messages |

## Files Modified

| # | File | Change |
|---|------|--------|
| 1 | `src/Adam.Shared/Data/AppDbContext.cs` | Add SearchClickLogs DbSet + config |
| 2 | `src/Adam.CatalogBrowser/Views/AssetGalleryView.axaml.cs` | Add click tracking, "Find Duplicates" context menu, ranking mode selector |
| 3 | `src/Adam.CatalogBrowser/ViewModels/AssetGalleryViewModel.cs` | Integrate SearchRankingService for re-ranking |
| 4 | `src/Adam.CatalogBrowser/ViewModels/MainWindowViewModel.cs` | Add GenerateAlbumsCommand, FindDuplicatesCommand, DetectDuplicatesCommand |
| 5 | `src/Adam.CatalogBrowser/Views/MainWindow.axaml` | Add DataTemplates for AutoAlbumDialog, DuplicateReviewView |
| 6 | `src/Adam.BrokerService/Handlers/ConnectionHandler.cs` | Register SearchRankingHandler |
| 7 | `src/Adam.BrokerService/Handlers/SearchRankingHandler.cs` | NEW — broker handler for click logging and re-ranking |
| 8 | `src/Adam.BrokerService/Program.cs` | DI registration for SearchRankingService, SearchRankingHandler |
| 9 | `src/Adam.CatalogBrowser/App.axaml.cs` | DI registration for SearchRankingService, EmbeddingClusterService, NearDuplicateService |
| 10 | `src/Adam.Shared/Contracts/MessageTypeCode.cs` | Add LogSearchClickRequest, LogSearchClickResponse, ReRankRequest, ReRankResponse |

## Estimated New Test Files

| # | File | Task |
|---|------|------|
| 1 | `tests/Adam.Shared.Tests/Services/SearchRankingServiceTests.cs` | T22.1 |
| 2 | `tests/Adam.Shared.Tests/Services/EmbeddingClusterServiceTests.cs` | T22.2 |
| 3 | `tests/Adam.CatalogBrowser.Tests/ViewModels/AutoAlbumViewModelTests.cs` | T22.2 |
| 4 | `tests/Adam.Shared.Tests/Services/NearDuplicateServiceTests.cs` | T22.3 |
| 5 | `tests/Adam.CatalogBrowser.Tests/ViewModels/DuplicateReviewViewModelTests.cs` | T22.3 |
| 6 | `tests/Adam.BrokerService.Tests/Handlers/SearchRankingHandlerTests.cs` | T22.1 |
