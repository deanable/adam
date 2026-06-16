---
goal: Advanced Search & Discovery — saved searches, smart collections, search history, semantic search, visual similarity
version: 1.0
date_created: 2026-06-17
status: Planned
tags: [phase-19, search, discovery, saved-searches, smart-collections, semantic-search, embeddings, ai]
---

# Phase 19 — Advanced Search & Discovery

**v4.0 — Advanced Discovery & Experience (Part 1 of 2)**

## Features

| # | Feature | Scope | Effort |
|---|---------|-------|--------|
| T19.1 | **Saved Searches** — save/load named filter+query combinations in DB | New entity + CRUD + broker handlers + UI | Medium |
| T19.2 | **Smart Collections** — dynamic collections populated by saved search criteria | Extend `Collection` model + auto-refresh + UI indicator | Medium |
| T19.3 | **Search History** — persistent multi-user search history with auto-purge | New entity + DB storage + UI quick-recall | Small |
| T19.4 | **Semantic Search (Text)** — natural language search via ONNX text embedding model | ONNX embedding pipeline + vector storage + similarity search | Large |
| T19.5 | **Visual Similarity Search** — "Find Similar" via LiquidVision vision encoder embeddings | Image embedding extraction + similarity against catalog | Medium |

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                         Shared (Adam.Shared)                         │
│                                                                      │
│  New Entities:                                                       │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────────┐  │
│  │  SavedSearch     │  │ SearchHistory   │  │  AssetEmbedding      │  │
│  │  Id, Name, Query │  │  Id, QueryText, │  │  AssetId, TextVec,  │  │
│  │  Filters(JSON),  │  │  Filters(JSON), │  │  ImageVec, ModelVer │  │
│  │  UserId          │  │  ExecutedAt,    │  │                     │  │
│  │  CreatedAt, Mod  │  │  UserId         │  │                     │  │
│  └─────────────────┘  └─────────────────┘  └─────────────────────┘  │
│                                                                      │
│  Extended Models:                                                    │
│  ┌──────────────────────────────────────────────────────────────┐   │
│  │  Collection (extended)                                         │   │
│  │  + IsSmart: bool (default false)                               │   │
│  │  + SmartQueryJson: string? (serialized filter criteria)        │   │
│  │  + LastAutoRefreshedAt: DateTimeOffset?                        │   │
│  └──────────────────────────────────────────────────────────────┘   │
│                                                                      │
│  New Services:                                                       │
│  ┌──────────────────────┐  ┌────────────────────────┐               │
│  │  EmbeddingService     │  │  SemanticSearchService  │               │
│  │  - TextEmbedding()    │  │  - SearchByText()       │               │
│  │  - ImageEmbedding()   │  │  - FindSimilar()        │               │
│  └──────────────────────┘  └────────────────────────┘               │
└─────────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────────┐
│                       Adam.CatalogBrowser (UI)                       │
│                                                                      │
│  ┌────────────────────────────────────────────────────────────┐     │
│  │  Sidebar additions:                                          │     │
│  │  ├─ Saved Searches section (new, above Collections)          │     │
│  │  │  ├─ Click to execute saved search                         │     │
│  │  │  ├─ Right-click: Rename / Delete / Save Current           │     │
│  │  │  └─ Starred searches (pinned to top)                      │     │
│  │  ├─ Collections: ✦ indicator for smart collections           │     │
│  │  └─ Recent Searches section (collapsible, at bottom)         │     │
│  │                                                                   │
│  │  Search bar additions:                                            │     │
│  │  ├─ Recent searches in autocomplete dropdown                     │     │
│  │  ├─ "Save this search" button in search bar                      │     │
│  │  └─ Natural language toggle (FTS ↔ Semantic)                     │     │
│  │                                                                   │
│  │  Gallery context menu:                                            │     │
│  │  └─ "Find Similar" → opens gallery filtered by visual similarity │     │
│  └────────────────────────────────────────────────────────────┘     │
└─────────────────────────────────────────────────────────────────────┘
```

## Key Design Decisions

1. **Text embedding model**: Use a small ONNX embedding model (e.g., `all-MiniLM-L6-v2` in ONNX format from HuggingFace). This produces 384-dim vectors. Downloaded on first use, same infrastructure as LiquidVision model downloads. The model file is ~80 MB — much smaller than LFM2-VL.

2. **Vector storage**: Store embeddings as `byte[]` BLOB in the `AssetEmbedding` table (one row per asset). For v1, compute cosine similarity in-process at query time. This is performant up to ~100K assets (< 500ms). For larger catalogs, a future phase can add FAISS/HNSW indexing.

3. **Image embeddings**: Use the existing LFM2-VL vision encoder's pooled output as the image embedding vector. The model already has a vision encoder that produces feature vectors — we average-pool to get a single 4096-dim vector per image.

4. **Smart collections are purely dynamic**: Smart collections are always auto-populated by their query. Users cannot manually add/remove assets from a smart collection. They see the ✦ indicator and a "Refresh" button.

5. **Search history auto-purge**: Keep the last 200 entries per user, purge older entries on insert. Stored in DB for multi-user sync.

6. **Two search modes**: The search bar gets a toggle (icon button) between FTS mode and Semantic mode. In semantic mode, the natural language query is embedded and compared against all stored embeddings. The toggle remembers the last-used mode.

## Tasks

### T19.1 — SavedSearch Entity + CRUD

**Files:**
- `src/Adam.Shared/Models/SavedSearch.cs` (new)
- `src/Adam.Shared/Data/AppDbContext.cs` (add DbSet + config)
- `src/Adam.Shared/Contracts/SearchMessages.cs` (add SavedSearch protobuf contracts — new file or extend existing)
- `src/Adam.BrokerService/Handlers/SavedSearchHandler.cs` (new)
- `src/Adam.Shared/Services/SavedSearchService.cs` (new, for standalone mode)
- `src/Adam.Shared/Contracts/MessageType.cs` or equivalent (add opcodes for SavedSearch CRUD)

**SavedSearch entity:**
```csharp
public sealed class SavedSearch
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    // The text query (for FTS or semantic search)
    public string? QueryText { get; set; }
    // Serialized filter criteria (JSON: type, folder, keywords, categories,
    // date range, rating, label, flag, isSemantic, sortBy, sortDir)
    public string FiltersJson { get; set; } = "{}";
    public bool IsPinned { get; set; }
    public Guid? UserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }

    public User? User { get; set; }
}
```

**Broker operations:** CreateSavedSearch, ListSavedSearches, UpdateSavedSearch, DeleteSavedSearch, PinSavedSearch

**Standalone service:** Direct DB access via AppDbContext (same pattern as other services)

**Estimated LOC:** ~200

### T19.2 — Smart Collections (Extend Collection Model)

**Files:**
- `src/Adam.Shared/Models/Collection.cs` (add IsSmart, SmartQueryJson, LastAutoRefreshedAt)
- `src/Adam.Shared/Data/AppDbContext.cs` (add new columns config)
- `src/Adam.Shared/Contracts/CollectionMessages.cs` (add smart fields to protobuf contracts)
- `src/Adam.BrokerService/Handlers/CollectionHandler.cs` (handle smart collection refresh logic)
- `src/Adam.CatalogBrowser/ViewModels/SidebarViewModel.cs` (smart collection indicator + refresh command)

**Extension to Collection model:**
```csharp
public bool IsSmart { get; set; }
public string? SmartQueryJson { get; set; }  // Same format as saved search filters
public DateTimeOffset? LastAutoRefreshedAt { get; set; }
```

**Smart collection refresh flow:**
1. User creates a "Smart Collection" from a saved search or current filter state
2. The current filter + query is serialized to `SmartQueryJson`
3. On collection open/refresh, the query is re-executed via `SearchService`
4. Results populate the collection view
5. `LastAutoRefreshedAt` is updated

**Conversion from regular to smart:** A regular collection can be converted to smart by selecting "Convert to Smart Collection" from the context menu. This captures the current filter state and applies it going forward. The reverse is not supported (smart → manual).

**Estimated LOC:** ~150

### T19.3 — Search History

**Files:**
- `src/Adam.Shared/Models/SearchHistoryEntry.cs` (new)
- `src/Adam.Shared/Data/AppDbContext.cs` (add DbSet + config)
- `src/Adam.Shared/Contracts/SearchMessages.cs` (add SearchHistory protobuf messages)
- `src/Adam.BrokerService/Handlers/SearchHistoryHandler.cs` (new, or extend existing search handler)
- `src/Adam.Shared/Services/SearchHistoryService.cs` (new, standalone mode)

**SearchHistoryEntry entity:**
```csharp
public sealed class SearchHistoryEntry
{
    public Guid Id { get; set; }
    public string QueryText { get; set; } = string.Empty;
    public string FiltersJson { get; set; } = "{}";
    public bool IsSemantic { get; set; }
    public DateTimeOffset ExecutedAt { get; set; }
    public Guid? UserId { get; set; }
    public User? User { get; set; }
}
```

**Auto-purge:** Before inserting a new entry, delete entries older than the 200th per user. Use a SQL window function or simple count-then-delete pattern.

**UI integration:** 
- When user executes a search, the entry is auto-recorded
- Recent searches shown in search bar autocomplete dropdown (with a "Recent" header)
- A "Clear History" option in the search bar dropdown

**Estimated LOC:** ~100

### T19.4 — Semantic Search (Text Embeddings)

**Files:**
- `src/Adam.Shared/Models/AssetEmbedding.cs` (new)
- `src/Adam.Shared/Data/AppDbContext.cs` (add DbSet + config)
- `src/Adam.Shared/Services/EmbeddingService.cs` (new — ONNX text embedding pipeline)
- `src/Adam.Shared/Services/SemanticSearchService.cs` (new — orchestrates embedding + similarity)
- `src/Adam.Shared/Contracts/SearchMessages.cs` (add SemanticSearchRequest/Response)
- `src/Adam.BrokerService/Handlers/SemanticSearchHandler.cs` (new)
- `src/Adam.Shared/Infrastructure/OnnxConfig.cs` or similar (extend with embedding model config)
- Model download infrastructure (extend LiquidVision's downloader or create embedding-specific downloader)

**AssetEmbedding entity:**
```csharp
public sealed class AssetEmbedding
{
    public Guid Id { get; set; }
    public Guid AssetId { get; set; }
    public byte[] TextEmbedding { get; set; } = [];    // 384-dim float32 = 1536 bytes
    public byte[]? ImageEmbedding { get; set; }          // 4096-dim float32 = 16384 bytes (T19.5)
    public string ModelVersion { get; set; } = string.Empty;
    public DateTimeOffset ComputedAt { get; set; }

    public DigitalAsset Asset { get; set; } = null!;
}
```

**EmbeddingService:**
```csharp
public sealed class EmbeddingService
{
    // Text embedding using all-MiniLM-L6-v2 ONNX model
    public Task<float[]> GetTextEmbeddingAsync(string text, CancellationToken ct);

    // Image embedding using LiquidVision vision encoder
    public Task<float[]> GetImageEmbeddingAsync(string imagePath, CancellationToken ct);

    // Bulk computation for catalog
    public Task ComputeAllEmbeddingsAsync(IProgress<(int, int)> progress, CancellationToken ct);
    public Task ComputeAssetEmbeddingAsync(Guid assetId, CancellationToken ct);
}
```

**SemanticSearchService:**
```csharp
public sealed class SemanticSearchService
{
    // Search by natural language text
    public Task<IReadOnlyList<SearchResult>> SearchByTextAsync(
        string query, int maxResults = 50, CancellationToken ct = default);

    // Find visually similar assets (T19.5)
    public Task<IReadOnlyList<SearchResult>> FindSimilarAsync(
        Guid assetId, int maxResults = 20, CancellationToken ct = default);
}
```

**Text embedding ONNX model:** `all-MiniLM-L6-v2` in ONNX format (~80 MB)
- Downloaded from HuggingFace (or a mirror) on first use
- Cached in `%APPDATA%/Adam/models/` alongside LiquidVision models
- Produces 384-dim float32 vectors
- Input: tokenized text (max 256 tokens). We use chunking for longer texts (e.g., description + keywords).

**Similarity computation:**
```csharp
public static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
{
    float dot = 0, normA = 0, normB = 0;
    for (int i = 0; i < a.Length; i++)
    {
        dot += a[i] * b[i];
        normA += a[i] * a[i];
        normB += b[i] * b[i];
    }
    return dot / (MathF.Sqrt(normA) * MathF.Sqrt(normB));
}
```

**Performance notes:**
- 100K assets × 384-dim = 153 MB of float data loaded from DB
- Cosine similarity over 100K vectors takes ~100-200ms in C# (SIMD-optimized)
- Total round-trip with DB read: < 500ms for 100K assets

**Embedding computation timing:**
- Text embeddings for existing assets: Batch background job triggered on first semantic search use (with progress bar)
- New assets during ingest: Text embedding computed in a post-pass (similar to AI tagging's EnableAiTagging pattern)
- On-demand: If an asset doesn't have an embedding yet, compute it lazily when semantic search runs

**Estimated LOC:** ~350

### T19.5 — Visual Similarity Search

**Files:**
- `src/Adam.Shared/Services/VisualSimilarityService.cs` (new, or extend SemanticSearchService)
- `src/LiquidVision.Core/ILiquidVisionAnalyzer.cs` or a new interface (extend to expose raw vision features)

**Approach:** Use the LFM2-VL vision encoder's output. The vision encoder already produces feature vectors during the normal analysis pipeline. We add a method to extract just the vision features:

```csharp
// New method on ILiquidVisionAnalyzer (or a separate interface):
Task<float[]> ExtractVisionFeaturesAsync(string imagePath, CancellationToken ct);
```

The vision features are pooled (average over all image tokens) to produce a single 4096-dim vector, stored in `AssetEmbedding.ImageEmbedding`.

**Storage:** 4096 × 4 bytes = 16 KB per image. 100K images = ~1.6 GB. This is significant — consider storing only for images and making it opt-in (like AI tagging). For v1, store all image embeddings and accept the storage cost.

**Flow:**
1. User right-clicks an image → "Find Similar"
2. The image's embedding is loaded from DB
3. Cosine similarity is computed against all other image embeddings
4. Top 20 results displayed in the gallery, sorted by similarity
5. A similarity score badge on each result tile

**Embedding computation:**
- During AI tagging post-pass: also compute and store the image embedding
- For existing assets: batch compute on first use (with progress bar)
- Only for `AssetType.Image` assets

**Estimated LOC:** ~150

### T19.6 — Broker-Side Search Handlers

**Files:**
- `src/Adam.BrokerService/Handlers/SearchHandler.cs` (new or extended)
- `src/Adam.Shared/Contracts/SearchMessages.cs` (add search + history + saved search messages)

**New message types:**
```
ExecuteSavedSearchRequest / Response
ListSavedSearchesRequest / Response
CreateSavedSearchRequest / Response
UpdateSavedSearchRequest / Response
DeleteSavedSearchRequest / Response
PinSavedSearchRequest / Response

RecordSearchHistoryRequest (fire-and-forget)
ListSearchHistoryRequest / Response
ClearSearchHistoryRequest

SemanticSearchRequest / Response
FindSimilarRequest / Response
RecomputeEmbeddingsRequest / Response (admin)
```

**Estimated LOC:** ~120

### T19.7 — UI Integration

**Files:**
- `src/Adam.CatalogBrowser/ViewModels/SidebarViewModel.cs` (Saved Searches section, Recent Searches, smart collection ✦, Find Similar)
- `src/Adam.CatalogBrowser/ViewModels/AssetGalleryViewModel.cs` (semantic search mode, save search, Find Similar mode)
- `src/Adam.CatalogBrowser/Views/AssetGalleryView.axaml` (search mode toggle, save button)
- `src/Adam.CatalogBrowser/Views/SidebarView.axaml` (new sections)

**Sidebar additions:**

```
┌─ All Keywords ─────────────────────┐
│  (keyword tree...)                  │
├─ Saved Searches ───────────────────┤
│  ★ Sunset photos          [▶] [×]  │
│  ★ Recent imports         [▶] [×]  │
│  All from last month      [▶] [×]  │
│  ─────────────────────────────────  │
│  [+ Save Current Filter]           │
├─ Collections ──────────────────────┤
│  ✦ Smart: Nature shots    (142)    │
│  Vacation photos          (89)     │
│  ...                                │
├─ Recent Searches ──────────────────┤
│  sunset beach ― 2 min ago          │
│  camera model Nikon ― 15 min ago   │
│  [ Clear History ]                 │
└────────────────────────────────────┘
```

**Search bar additions:**
- Toggle button: 🔍 (FTS) ↔ ✦ (Semantic). Changes the search icon.
- In semantic mode: search bar placeholder text changes to "Describe what you're looking for..."
- "💾 Save" button appears when the gallery has active filters + query
- Recent searches shown as a separate section in the autocomplete dropdown

**Gallery context menu:**
- "Find Similar" on image assets → switches to similarity mode
- Gallery header shows "Showing results similar to: [filename]" with "✕ Clear" button
- Each result tile shows a similarity badge (e.g., "92% match")

**Smart collection indicator:**
- ✦ icon before the collection name
- "Refresh" command in collection context menu
- "Convert to Smart Collection" in collection context menu (captures current filter)

**Estimated LOC:** ~300

### T19.8 — Tests

**Files:**
- `tests/Adam.Shared.Tests/Services/SavedSearchServiceTests.cs`
- `tests/Adam.Shared.Tests/Services/SearchHistoryServiceTests.cs`
- `tests/Adam.Shared.Tests/Services/EmbeddingServiceTests.cs`
- `tests/Adam.Shared.Tests/Services/SemanticSearchServiceTests.cs`
- `tests/Adam.Shared.Tests/Services/VisualSimilarityServiceTests.cs`
- `tests/Adam.CatalogBrowser.Tests/ViewModels/SidebarSearchTests.cs`

**Test coverage:**

| Test | What it covers |
|------|---------------|
| SavedSearch CRUD | Create, list, update, delete, pin |
| SavedSearch unique name | Duplicate name detection per user |
| SearchHistory auto-purge | Only 200 most recent entries kept |
| SearchHistory recent first | Results ordered by ExecutedAt desc |
| EmbeddingService dimension | Text embedding produces 384-dim vector |
| EmbeddingService normalize | Output vector has unit length |
| EmbeddingService deterministic | Same input produces similar output |
| SemanticSearchService rank | Most similar results returned first |
| SemanticSearchService empty | Empty query returns empty results |
| CosineSimilarity identical | Same vector → 1.0 |
| CosineSimilarity opposite | Opposite vector → -1.0 |
| CosineSimilarity orthogonal | Orthogonal vector → 0.0 |
| SmartCollection refresh | Executes saved query and populates |
| SmartCollection indicator | IsSmart flag visible in UI |
| Sidebar saved searches | Shows/hides sections correctly |
| FindSimilar image-only | Non-image assets excluded |
| Search mode toggle | FTS ↔ semantic mode switch works |

**Estimated LOC:** ~300 (20 tests)

## Success Criteria

- ✅ Saved searches can be created, renamed, deleted, and pinned from the UI
- ✅ Saved searches persist across sessions and sync in multi-user mode
- ✅ Smart collections auto-populate from their saved query (dynamic content)
- ✅ ✦ indicator distinguishes smart collections from manual ones
- ✅ Search history persists locally and in multi-user mode, auto-purging to 200 entries
- ✅ Recent searches appear in the search bar autocomplete dropdown
- ✅ Natural language search produces relevant results (e.g., "sunset photos from last summer")
- ✅ "Find Similar" on an image asset returns visually similar results ranked by similarity
- ✅ Similarity scores shown as badges on result tiles
- ✅ Model download for the embedding model shows progress (same pattern as LiquidVision)
- ✅ Text embeddings computed for existing assets on first semantic search use (progress bar)
- ✅ Image embeddings computed during AI tagging post-pass
- ✅ All existing tests still pass
- ✅ 20+ new tests pass

## Execution Order (Waves)

```
Wave 1 ─── T19.1 SavedSearch entity + CRUD ───────────────── independent
           T19.3 SearchHistory entity + CRUD ───────────────── independent (parallel with Wave 1)

Wave 2 ─── T19.2 Smart Collections (depends on T19.1 query/filter serialization)

Wave 3 ─── T19.4 Semantic Search foundation (embedding model + vector storage + search service)
           T19.5 Visual Similarity (depends on T19.4 embedding infrastructure)

Wave 4 ─── T19.6 Broker handlers (depends on T19.1, T19.3, T19.4)

Wave 5 ─── T19.7 UI integration (depends on all above)

Wave 6 ─── T19.8 Tests (depends on all code)

Wave 7 ─── Full test suite, code review, plan status update
```

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| all-MiniLM-L6-v2 ONNX model unavailable/failed download | Low | Medium | Fall back to FTS-only; embedding model download retries same as LiquidVision |
| Embedding storage size (1.6 GB for 100K images) | Medium | Medium | Make image embeddings opt-in with confirmation dialog; add compression |
| Semantic search latency > 1s at 100K assets | Low | Medium | Add SIMD-optimized cosine similarity; cache embeddings in memory |
| ONNX Runtime dependency conflicts | Low | High | Pin exact ORT version matching LiquidVision's dependency; test in isolation |
| Smart collection sync issues (multi-user) | Low | Medium | Smart collections are refreshed on open (not live); staleness is bounded by refresh |

## Files Created

| # | File | Task |
|---|------|------|
| 1 | `src/Adam.Shared/Models/SavedSearch.cs` | T19.1 |
| 2 | `src/Adam.Shared/Models/SearchHistoryEntry.cs` | T19.3 |
| 3 | `src/Adam.Shared/Models/AssetEmbedding.cs` | T19.4 |
| 4 | `src/Adam.Shared/Services/EmbeddingService.cs` | T19.4 |
| 5 | `src/Adam.Shared/Services/SemanticSearchService.cs` | T19.4 |
| 6 | `src/Adam.Shared/Services/VisualSimilarityService.cs` | T19.5 |
| 7 | `src/Adam.Shared/Services/SavedSearchService.cs` | T19.1 |
| 8 | `src/Adam.Shared/Services/SearchHistoryService.cs` | T19.3 |
| 9 | `src/Adam.Shared/Contracts/SearchMessages.cs` | T19.6 |
| 10 | `src/Adam.BrokerService/Handlers/SavedSearchHandler.cs` | T19.1 |
| 11 | `src/Adam.BrokerService/Handlers/SearchHistoryHandler.cs` | T19.3 |
| 12 | `src/Adam.BrokerService/Handlers/SemanticSearchHandler.cs` | T19.4 |
| 13 | `tests/Adam.Shared.Tests/Services/SavedSearchServiceTests.cs` | T19.8 |
| 14 | `tests/Adam.Shared.Tests/Services/SearchHistoryServiceTests.cs` | T19.8 |
| 15 | `tests/Adam.Shared.Tests/Services/EmbeddingServiceTests.cs` | T19.8 |
| 16 | `tests/Adam.Shared.Tests/Services/SemanticSearchServiceTests.cs` | T19.8 |
| 17 | `tests/Adam.Shared.Tests/Services/VisualSimilarityServiceTests.cs` | T19.8 |
| 18 | `tests/Adam.CatalogBrowser.Tests/ViewModels/SidebarSearchTests.cs` | T19.8 |

## Files Modified

| # | File | Change |
|---|------|--------|
| 1 | `src/Adam.Shared/Models/Collection.cs` | Add IsSmart, SmartQueryJson, LastAutoRefreshedAt |
| 2 | `src/Adam.Shared/Data/AppDbContext.cs` | Add DbSet<SavedSearch>, DbSet<SearchHistoryEntry>, DbSet<AssetEmbedding> + config |
| 3 | `src/Adam.Shared/Contracts/CollectionMessages.cs` | Add smart fields to protobuf contracts |
| 4 | `src/Adam.BrokerService/Handlers/CollectionHandler.cs` | Smart collection refresh logic |
| 5 | `src/Adam.CatalogBrowser/ViewModels/SidebarViewModel.cs` | Saved searches section, recent searches, smart indicator, Find Similar |
| 6 | `src/Adam.CatalogBrowser/ViewModels/AssetGalleryViewModel.cs` | Semantic search mode, save search, Find Similar mode |
| 7 | `src/Adam.CatalogBrowser/Views/AssetGalleryView.axaml` | Search mode toggle, save button |
| 8 | `src/Adam.CatalogBrowser/Views/SidebarView.axaml` | New Saved Searches + Recent Searches sections |
| 9 | `src/Adam.CatalogBrowser/App.axaml.cs` | DI registration for new services |
| 10 | Various opcode/MessageType files | Add new message type codes |

## New NuGet Dependencies

- `Microsoft.ML.OnnxRuntime` (already referenced by LiquidVision.Core — no new dependency needed for runtime)
- Text embedding model: `all-MiniLM-L6-v2.onnx` (~80 MB, downloaded on first use, cached in model directory)
