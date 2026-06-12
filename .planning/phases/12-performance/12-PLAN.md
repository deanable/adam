---
goal: Optimize thumbnail caching, gallery virtualization, and application startup for large collections (100K+ assets).
version: 2.0
date_created: 2026-06-12
last_updated: 2026-06-12
status: 'Planned'
tags: [performance, thumbnail, virtualization, startup, optimization]
---

# Phase 12: Performance Optimization

![Status: Planned](https://img.shields.io/badge/status-Planned-blue)

Optimize three performance bottlenecks identified during the v1.0 benchmark (T8.1): thumbnail generation/caching, gallery scrolling at scale, and cold-start startup time. Target: smooth gallery at 100K assets, <3s cold start.

**Depends on:** Phases 1–9 (v1.0 codebase)

---

## 1. Requirements & Constraints

- **PERF-02**: Gallery loading and thumbnail display must be smooth at 100K assets with no UI freeze
- **PERF-03**: Application cold start completes within 3 seconds on consumer hardware (16GB RAM, SSD)
- **PERF-04**: Thumbnail generation for a new 100-image batch completes within 30 seconds
- **PERF-05**: Memory usage stays bounded during gallery scrolling (no linear growth)

---

## 2. Current State

- **ThumbnailService** (`src/Adam.Shared/Services/ThumbnailService.cs`): Generates thumbnails on-demand, caches to disk via SHA256 hash filenames. No in-memory cache, no batch pre-generation, no lazy loading.
- **AssetGalleryViewModel**: Page-based loading (50 items/page) with `LoadMoreAsync()`. Thumbnails loaded async after items added. No virtualization optimization.
- **Startup**: `ModeManager.InitializeAsync` creates DB context, runs migrations, loads sidebar data. All synchronous on UI thread initially.

---

## 3. Implementation Steps

### Work Stream 1: Thumbnail Cache Optimization

**GOAL:** Reduce thumbnail generation overhead and add in-memory caching for frequently accessed thumbnails.

| Task | Description | Status |
|------|-------------|--------|
| T12.1 | **Memory cache layer** — Add `MemoryCache<Bitmap>` (bounded to 256MB) in front of disk cache. Check memory → disk → generate. Evict LRU when at capacity. | ⬜ |
| T12.2 | **Batch pre-generation** — During ingest, generate thumbnails in parallel (within the existing `Parallel.ForEachAsync` batch) rather than sequentially in a post-pass. Cap concurrency to `Environment.ProcessorCount`. | ⬜ |
| T12.3 | **Decode-to-size** — Use `Bitmap.DecodeToHeight(sourceStream, targetSize)` instead of full-resolution decode. Reduces memory per thumbnail from MB to KB. | ⬜ |
| T12.4 | **Thumbnail metadata cache** — Store `File.LastWriteTime` alongside thumbnail hash. Skip regeneration if source file hasn't changed (avoids re-hashing on every gallery load). | ⬜ |

### Work Stream 2: Gallery Virtualization

**GOAL:** Ensure smooth scrolling at 100K+ items without memory pressure.

| Task | Description | Status |
|------|-------------|--------|
| T12.5 | **VirtualizingStackPanel audit** — Verify `AssetGalleryView` uses `VirtualizingStackPanel` in both Grid and List modes. Add `VirtualizingStackPanel.BufferFactor="1"` for smoother scroll. | ⬜ |
| T12.6 | **On-demand thumbnail loading** — Bind thumbnail display to a `Task<Bitmap>` pattern. Show placeholder immediately, load actual bitmap async. Cancel pending loads when item scrolls out of view. | ⬜ |
| T12.7 | **Bitmap disposal** — Implement `IDisposable` on `AssetListItem`. When items scroll out of view (removed from virtualized panel), dispose their `Bitmap` to free GPU memory. | ⬜ |
| T12.8 | **GPU resource cache** — Set `SkiaOptions.MaxGpuResourceSizeBytes = 256 * 1024 * 1024` (256MB) in `App.axaml.cs` to prevent GPU cache thrashing during fast scroll. | ⬜ |

### Work Stream 3: Startup Time Optimization

**GOAL:** Reduce cold-start time to <3 seconds.

| Task | Description | Status |
|------|-------------|--------|
| T12.9 | **Lazy service initialization** — Move non-critical service initialization (ThumbnailPipeline, AiTaggingService model download) to lazy/deferred. Only initialize when first used. | ⬜ |
| T12.10 | **Async sidebar loading** — Move `SidebarViewModel.LoadAsync()` to background thread. Show shell immediately, populate sidebar progressively. | ⬜ |
| T12.11 | **Compiled bindings** — Enable `x:CompileBindings="True"` project-wide in `.csproj`. Eliminates runtime binding reflection. | ⬜ |
| T12.12 | **Startup profiling** — Add `Stopwatch`-based telemetry to `ModeManager.InitializeAsync`, `App.OnFrameworkInitializationCompleted`, and first `MainWindowViewModel` construction. Log timings to diagnose regressions. | ⬜ |

---

## 4. Execution Waves

| Wave | Tasks | Depends On | Rationale |
|------|-------|------------|-----------|
| **Wave 1 — Thumbnails** | T12.1, T12.2, T12.3, T12.4 | — | Thumbnail optimization is self-contained and delivers immediate scroll improvement. |
| **Wave 2 — Virtualization** | T12.5, T12.6, T12.7, T12.8 | Wave 1 | Gallery virtualization depends on thumbnail decode-to-size (T12.3) for memory savings. |
| **Wave 3 — Startup** | T12.9, T12.10, T12.11, T12.12 | — | Independent of thumbnail/virtualization work. Can run in parallel. |

---

## 5. Files

| File | Role |
|------|------|
| `src/Adam.Shared/Services/ThumbnailService.cs` | Add memory cache layer, batch pre-generation, decode-to-size |
| `src/Adam.CatalogBrowser/ViewModels/AssetGalleryViewModel.cs` | On-demand thumbnail loading, bitmap disposal |
| `src/Adam.CatalogBrowser/Models/AssetListItem.cs` | Add IDisposable, Task<Bitmap> binding |
| `src/Adam.CatalogBrowser/Views/AssetGalleryView.axaml` | VirtualizingStackPanel config, placeholder template |
| `src/Adam.CatalogBrowser/App.axaml.cs` | SkiaOptions, lazy service init, compiled bindings |
| `src/Adam.CatalogBrowser/ViewModels/SidebarViewModel.cs` | Async sidebar loading |
| `src/Adam.Shared/Services/ModeManager.cs` | Startup profiling telemetry |

---

## 6. Testing

| Test | Type | Command |
|------|------|---------|
| Thumbnail cache hit rate | Automated | Unit test: generate same thumbnail twice, verify second call returns cached |
| Memory cache eviction | Automated | Unit test: fill cache beyond capacity, verify LRU eviction |
| Decode-to-size memory | Automated | Unit test: verify decoded bitmap dimensions match target size |
| Gallery scroll performance | Manual | Load 100K assets, scroll gallery, verify no UI freezes |
| Cold-start timing | Manual | `dotnet run -c Release` with Stopwatch logging, verify <3s |
| Memory profiler | Manual | Attach dotMemory/dotTrace, scroll 10K items, verify bounded memory |

---

## 7. Risks

- **RISK-001**: `Bitmap.DecodeToHeight` may not support all image formats (RAW, TIFF). Mitigation: fall back to full decode for unsupported formats; these are typically fewer in number.
- **RISK-002**: Compiled bindings may break existing runtime bindings. Mitigation: enable incrementally, project-by-project, running tests after each.
- **RISK-003**: Lazy initialization may cause first-use latency spikes. Mitigation: warm up critical services during idle time after startup (pre-fetch first page of thumbnails).
