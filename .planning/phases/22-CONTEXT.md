# Phase 22 — AI-Native DAM Features (Discussion Context)

**Phase:** 22 | **Milestone:** v5.0 — AI-Native DAM
**Discussed:** 2026-06-17 | **Plan:** `.planning/plans/phase-22/22-PLAN.md`

## Scope

Phase 22 delivers **3 AI-native features** building on Phases 9 and 19 infrastructure:

| Task | Feature | Effort | Status |
|------|---------|--------|--------|
| T22.1 | Smart Search Ranking (click tracking, dwell time, broker-synced) | ~320 LOC | ✅ In scope |
| T22.2 | Auto-Album Generation (HDBSCAN clustering, live smart collections) | ~350 LOC | ✅ In scope |
| T22.3 | Near-Duplicate Detection (per-asset + batch, LSH optimization) | ~350 LOC | ✅ In scope |
| T22.4 | Facial Recognition (YuNet + ArcFace ONNX pipeline) | ~500 LOC | ⏭️ **Deferred to Phase 23** |

## Locked Decisions

### T22.1 — Smart Search Ranking
- **Click tracking**: Implicit feedback only (clicks, dwell time > 2s), no explicit ratings
- **Multi-user mode**: ✅ Click logs sync through the broker using new `LogSearchClickRequest/Response` and `ReRankRequest/Response` messages
- **Standalone mode**: Local SQLite DB (same SearchClickLog model, direct DbContext access)
- **Click affinity formula**: `weight = Σ(clicks) / max_clicks_for_query + recency_bonus(7d × 3×) + dwell_bonus(>5s × 2×)`
- **Rank weights**: `0.7 × cosine_similarity + 0.3 × click_affinity`
- **Log retention**: 90-day auto-purge
- **Privacy**: Click data attributed to user but not exposed in UI
- **Ranking modes**: Relevance | Smart | Date | Rating
- **No new data models for broker** — SearchClickLog is the only new entity

### T22.2 — Auto-Album Generation
- **Algorithm**: Simplified HDBSCAN — graph-based connected components from mutual similarity neighbors
- **Embedding source**: 4096-dim image embeddings from LFM2-VL (AssetEmbedding.ImageEmbedding)
- **Fallback**: 384-dim text embeddings for non-image assets
- **Persistence model**: ✅ **Live smart collections** — generated albums are `Collection` with `IsSmart = true`; `SmartQueryJson` stores centroid + threshold; albums auto-update as new assets are added
- **Name generation**: Top 2-4 most common keywords among cluster members
- **Preview dialog**: Shows clusters before creation with quality indicator
- **Sensitivity slider**: 0.6-0.95 MinSimilarity range
- **Sampling**: For catalogs > 50K, random 10K-sample for initial clustering

### T22.3 — Near-Duplicate Detection
- **Two entry points**: Per-asset "Find Duplicates" (context menu) + bulk "Detect Duplicates" (toolbar)
- **Algorithm**: Cosine similarity on 4096-dim image embeddings with LSH bucketing optimization
- **Thresholds**: >0.95 "Near-identical", 0.92 "Edited version", 0.85 "Similar"
- **Primary identification**: Highest resolution or largest file size
- **Actions**: Keep Primary, Trash All Duplicates, Skip
- **Duplicate Review View**: New dedicated view with group-by-group navigation

### General
- **All features are opt-in**: Toggle in search settings (ranking), toolbar button (albums), context menu / toolbar (duplicates)
- **.proto schema changes**: Only new broker messages for T22.1 click logging + re-ranking
- **No new ONNX models in Phase 22**: All features use existing all-MiniLM-L6-v2 and LFM2-VL embeddings
- **Phase 23 deferred work**: Facial recognition — YuNet (2 MB) + ArcFace MobileFaceNet (8 MB) from HuggingFace; auto-tag at >0.85 confidence, flag lower for review
- **Model source**: HuggingFace hub (same pattern as existing EmbeddingService)

## Questions Resolved During Discussion

| Question | Decision |
|----------|----------|
| Face auto-tagging: require review or auto-assign? | ⏭️ Deferred to Phase 23 with auto-tag at high confidence |
| Smart ranking: standalone only or multi-user? | ✅ Both modes, broker-synced |
| Auto-album persistence: static snapshots or live? | ✅ Live smart collections (auto-updating) |
| Keep all 4 features or defer something? | ✅ Defer facial recognition to Phase 23 |
| ONNX model source strategy? | ✅ HuggingFace hub (same pattern as existing) |

## Assumptions

1. Image embeddings (4096-dim) are already stored in `AssetEmbedding.ImageEmbedding` for a representative sample of assets. If most assets lack image embeddings, T22.2 and T22.3 will produce sparse results until AI tagging is run on them.
2. The existing `SemanticSearchService.CosineSimilarity` with SIMD acceleration is performant enough for batch near-dup scanning (50K comparisons in ~1-2s).
3. The existing smart collection refresh infrastructure (`CollectionHandler.RefreshSmartCollectionAsync`) can be extended to support embedding-based queries without major refactoring.
4. Users have sufficient search activity within 90 days to build meaningful click histories. Early adopters will see cosine-only ranking until they build history.

## Open Items (Non-Blocking)

- T22.1: Need to determine the exact broker message format for LogSearchClickRequest/Response
- T22.2: Need to determine SmartQueryJson format for embedding-based collection queries (existing format supports filter criteria but not embedding vectors)
- T22.3: LSH bucket size and hash count — tune during implementation for optimal performance

## Next Steps

1. ✅ Phase 22 discussed and decisions locked
2. 🔜 Run `/gsd-plan-phase 22` to create detailed execution plan with wave allocation
3. 🔜 Run `/gsd-execute-phase 22` to begin implementation
