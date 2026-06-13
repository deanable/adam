# Phase 12 — Performance Optimization: Context

> Generated: 2026-06-13 via discuss-phase (Codebuff)

## Decisions

| ID | Decision | Value | Rationale |
|----|----------|-------|-----------|
| D12.1 | **Fallback to full decode** when decode-to-size unsupported | Try `Bitmap.DecodeToWidth/Height` first; on failure, decode full resolution then resize | Safe, universal; handles all formats (including RAW via embedded JPEG) |
| D12.2 | **Avalonia's built-in VirtualizingStackPanel** with BufferFactor tuning | Leverage existing platform support | Tailored for this application; avoids custom panel complexity |
| D12.3 | **In-memory ThumbnailCache (256MB LRU) + disk cache** | Two-tier caching | Already implemented in working tree (ThumbnailCache.cs) |
| D12.4 | **Lazy initialization for non-critical services** | Defer ThumbnailPipeline, AiTaggingService, FTS initialization | Improves cold-start time by only loading what's needed immediately |

## Current State

- `ThumbnailCache.cs` — already implemented (in-memory LRU with 256MB default max)
- `AssetListItem.SharedThumbnailCache` — already wired
- `AssetGalleryViewModel.BackfillMissingThumbnailsAsync` — already implemented
- `AssetGalleryViewModel.ClearThumbnailCache` — already implemented
- `AssetGalleryViewModel.LoadAssetsAsync` — already uses page-based loading

## Remaining Work

1. **Decode-to-size optimization**: Use `Bitmap.DecodeToWidth` instead of full decode in thumbnail generation pipeline
2. **Gallery virtualization audit**: Verify VirtualizingStackPanel is used, add BufferFactor tuning
3. **Bitmap disposal**: Implement IDisposable on AssetListItem, dispose bitmaps when scrolled out of view
4. **Startup profiling**: Add Stopwatch telemetry to initialization paths
5. **Compiled bindings**: Enable x:CompileBindings project-wide
6. **GPU resource cache**: Configure SkiaOptions
