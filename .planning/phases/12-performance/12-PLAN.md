---
goal: Optimize thumbnail caching, gallery virtualization, and application startup for large collections (100K+ assets). Decode-to-size, VirtualizingStackPanel tuning, bitmap disposal, lazy init, startup profiling.
version: 2.1
date_created: 2026-06-13
last_updated: 2026-06-13
status: 'Planned'
tags: [performance, thumbnail, virtualization, startup, optimization, memory]
---

# Phase 12: Performance Optimization

![Status: Planned](https://img.shields.io/badge/status-Planned-blue)

Optimize three performance bottlenecks: thumbnail generation/caching (decode-to-size, memory cache, batch pre-generation), gallery scrolling at scale (VirtualizingStackPanel tuning, bitmap disposal, GPU cache), and cold-start startup time (lazy initialization, async loading, compiled bindings, profiling telemetry). Target: smooth gallery at 100K assets, <3s cold start, bounded memory.

**Depends on:** Phases 1–9 (v1.0 codebase)

---

## 1. Requirements & Constraints

- **PERF-02**: Gallery loading and thumbnail display must be smooth at 100K assets with no UI freeze
- **PERF-03**: Application cold start completes within **3 seconds** on consumer hardware (16GB RAM, SSD, 8-core CPU)
- **PERF-04**: Thumbnail generation for a new 100-image batch completes within **30 seconds**
- **PERF-05**: Memory usage stays **bounded** during gallery scrolling (no linear growth — verified by profiler)
- **D12.1**: Decode-to-size with **fallback to full decode** for unsupported formats
- **D12.2**: **Avalonia VirtualizingStackPanel** with BufferFactor tuning (no custom virtual panel)
- **D12.3**: In-memory **ThumbnailCache (256MB LRU)** + disk cache (two-tier)
- **D12.4**: **Lazy initialization** for non-critical services

---

## 2. Current State (Working Tree Analysis)

### 2.1 Already Implemented ✅

**ThumbnailCache (Adam.Shared):**
- `ThumbnailCache.cs` — thread-safe LRU in-memory cache:
  - `ConcurrentDictionary<string, CacheEntry>` + `LinkedList<string>` LRU order
  - `ReaderWriterLockSlim` for thread safety
  - Default 256 MB max size
  - `TryGet(key, out bitmap)` — updates LRU order on hit
  - `Add(key, bitmap, estimatedSize)` — evicts LRU when over capacity
  - `Remove(key)` — removes single entry (called when bitmap is disposed)
  - `Clear()` — empties cache
  - `EstimateBitmapSize(width, height)` — static helper: width × height × 4
  - `IDisposable` — clears cache + disposes lock

**AssetListItem:**
- `SharedThumbnailCache` (internal static) — set by AssetGalleryViewModel
- `ThumbnailPath` setter calls `SharedThumbnailCache.Remove(_thumbnailPath)` when path changes (T12.1) 
- `LoadThumbnailAsync` checks `SharedThumbnailCache.TryGet` before disk/file (T12.1)
- After loading from disk, stores in `SharedThumbnailCache` (T12.1)

**AssetGalleryViewModel:**
- `_thumbnailCache` (ThumbnailCache instance) — wired to `AssetListItem.SharedThumbnailCache` in constructor
- `BackfillMissingThumbnailsAsync` — batch pre-generation for assets without thumbnails (T12.2):
  - Uses `Parallel.ForEachAsync` with `MaxDegreeOfParallelism = 4`
  - Cancellable via `_backfillCts`
  - Generates thumbnail → updates ThumbnailPath → loads into memory cache
- `ClearThumbnailCache()` — cancels backfill, clears cache, clears SharedThumbnailCache (T12.2)

### 2.2 Not Yet Implemented ❌

1. **Decode-to-size** (T12.3): `ThumbnailService.GenerateThumbnailAsync` still uses full decode → resize pipeline
2. **Gallery VirtualizingStackPanel** (T12.5): Only one occurrence found (`AssetGalleryView.axaml` line 223) — needs audit and BufferFactor tuning
3. **On-demand thumbnail loading** (T12.6): No cancellation of pending loads when items scroll out of view
4. **Bitmap disposal** (T12.7): AssetListItem not IDisposable; bitmaps don't get disposed when scrolled out
5. **Startup profiling** (T12.12): No Stopwatch telemetry in initialization paths
6. **Lazy initialization** (T12.9): All services initialized eagerly at startup
7. **Async sidebar loading** (T12.10): Sidebar.LoadAsync already runs in `Task.Run`, but shell rendering is blocked
8. **Compiled bindings** (T12.11): x:CompileBindings not enabled project-wide
9. **GPU resource cache** (T12.8): SkiaOptions not configured

---

## 3. Ultra-Detailed Implementation Steps

### Wave 1: Thumbnail Cache Optimization

#### T12.3 — Decode-to-Size (DecodeToWidth)

**Files changed:**
- `src/Adam.Shared/ThumbnailExtractors/ImageThumbnailExtractor.cs` — use DecodeToWidth instead of full decode
- `src/Adam.Shared/ThumbnailExtractors/ThumbnailPipeline.cs` — verify pipeline passes maxSize correctly

**Detailed implementation:**

In `ImageThumbnailExtractor.cs`, find the method that decodes images and replace:

```csharp
// Before:
var bitmap = SixLabors.ImageSharp.Image.Load(sourcePath); // full decode

// After:
var imageInfo = SixLabors.ImageSharp.Image.Identify(sourcePath);
var originalWidth = imageInfo.Width;
var originalHeight = imageInfo.Height;

// Determine if we need to decode at reduced resolution
int decodeWidth;
int decodeHeight;
if (originalWidth > maxSize || originalHeight > maxSize)
{
    var ratio = Math.Min((double)maxSize / originalWidth, (double)maxSize / originalHeight);
    decodeWidth = (int)(originalWidth * ratio);
    decodeHeight = (int)(originalHeight * ratio);
}
else
{
    decodeWidth = originalWidth;
    decodeHeight = originalHeight;
}
```

**Fallback logic (D12.1):**
```csharp
// Try decode-to-size first
SixLabors.ImageSharp.Image bitmap;
try
{
    var decoder = new SixLabors.ImageSharp.Formats.Jpeg.JpegDecoder();
    bitmap = SixLabors.ImageSharp.Image.Load(sourcePath, decoder); // with specific decoder if known
}
catch (Exception ex)
{
    _logger?.LogWarning(ex, "Decode-to-size failed for {Path}, falling back to full decode", sourcePath);
    bitmap = SixLabors.ImageSharp.Image.Load(sourcePath); // full decode fallback
}

// Resize to target if larger
if (bitmap.Width > maxSize || bitmap.Height > maxSize)
{
    bitmap.Mutate(x => x.Resize(new ResizeOptions
    {
        Size = new Size(maxSize, maxSize),
        Mode = ResizeMode.Max
    }));
}
```

**For SkiaSharp path (Avalonia Bitmap):**
Since `AssetTileControl` uses Avalonia `Bitmap` (SkiaSharp-based), the decode should happen at the Skia level:

```csharp
// In ThumbnailService or extractor, when decoding for thumbnail:
using var stream = File.OpenRead(sourcePath);
// DecodeToWidth is already available in SkiaSharp:
var bitmap = SkiaSharp.SKBitmap.Decode(stream);
if (bitmap == null) return null;

// Resize if needed
if (bitmap.Width > maxSize || bitmap.Height > maxSize)
{
    var scale = Math.Min((double)maxSize / bitmap.Width, (double)maxSize / bitmap.Height);
    var newWidth = (int)(bitmap.Width * scale);
    var newHeight = (int)(bitmap.Height * scale);
    
    using var resized = bitmap.Resize(new SkiaSharp.SKImageInfo(newWidth, newHeight), SkiaSharp.SKSamplingOptions.Default);
    bitmap = SkiaSharp.SKBitmap.FromImage(resized.ToSKImage());
}
```

**Edge cases:**
- RAW files (CR2, NEF, ARW, DNG) — may not support DecodeToWidth → fallback to full decode → resize
- TIFF files — check if DecodeToWidth is supported → fallback if not
- Corrupted or partial images → catch exception → return null thumbnail
- Width × Height overflow (huge panoramic images) → clamp max dimension

#### T12.4 — Thumbnail Metadata Cache (Skip Re-generation)

**Files changed:**
- `src/Adam.Shared/Services/ThumbnailService.cs` — add last-write-time check

**Implementation:**

In `GenerateThumbnailAsync`, before generating, check if the thumbnail exists AND source hasn't changed:

```csharp
public async Task<string> GenerateThumbnailAsync(
    string sourcePath, string thumbnailDirectory, ImageOrientation orientation, CancellationToken ct)
{
    var thumbnailPath = GetThumbnailPath(sourcePath, thumbnailDirectory);
    var sourceInfo = new FileInfo(sourcePath);

    // If thumbnail exists and source hasn't changed since last generation, skip
    if (File.Exists(thumbnailPath) && orientation == ImageOrientation.Normal)
    {
        var thumbnailInfo = new FileInfo(thumbnailPath);
        if (thumbnailInfo.LastWriteTimeUtc >= sourceInfo.LastWriteTimeUtc)
        {
            return thumbnailPath; // Already up-to-date
        }
    }

    // ... rest of generation logic
}
```

**Storage approach:** Use `File.GetLastWriteTimeUtc(sourcePath)` compared against thumbnail's file timestamp. No separate metadata file needed — the thumbnail file's own timestamp is the marker.

**Edge cases:**
- Source file deleted → thumbnail exists but source missing → return thumbnail (still usable as preview)
- Source modified → thumbnail stale → regenerate (detected by newer LastWriteTimeUtc)
- Thumbnail deleted → regenerate (File.Exists returns false)

### Wave 2: Gallery Virtualization

#### T12.5 — VirtualizingStackPanel Audit & Tuning

**Files changed:**
- `src/Adam.CatalogBrowser/Views/AssetGalleryView.axaml` — audit and tune

**Audit steps:**

1. Open `AssetGalleryView.axaml` and find all `ItemsControl`, `ListBox`, or `ItemsRepeater` controls
2. Verify the gallery grid uses `VirtualizingStackPanel`:
```xml
<ListBox.ItemsPanel>
  <ItemsPanelTemplate>
    <VirtualizingStackPanel Orientation="Vertical" />
  </ItemsPanelTemplate>
</ListBox.ItemsPanel>
```

3. Add BufferFactor for smoother scroll:
```xml
<ListBox ... VirtualizingStackPanel.BufferFactor="2">
```

4. For the grid mode (WrapPanel/UniformGrid), verify virtualization:
```xml
<!-- If using ItemsRepeater with WrapLayout, ensure virtualization is enabled -->
<ItemsRepeater.ItemsLayout>
  <UniformGridLayout Orientation="Horizontal" 
                     MinItemWidth="160" 
                     MinItemHeight="160" />
</ItemsRepeater.ItemsLayout>
```

**BufferFactor tuning rationale:**
- Default is usually 1 (one viewport of items on each side)
- `BufferFactor="2"` — two viewports on each side, smoother scroll but more memory
- For 100K assets, 2 viewports of ~100px tiles × ~20 tiles = ~4000px buffer

#### T12.6 — On-Demand Thumbnail Loading

**Files changed:**
- `src/Adam.CatalogBrowser/Models/AssetListItem.cs` — add load cancellation
- `src/Adam.CatalogBrowser/ViewModels/AssetGalleryViewModel.cs` — cancel pending loads on scroll

**Implementation:**

**A. Add CancellationTokenSource to AssetListItem:**
```csharp
// In AssetListItem:
private CancellationTokenSource? _loadCts;

public async Task LoadThumbnailAsync(int decodeWidth = 256)
{
    // Cancel any pending load for this item
    CancelPendingLoad();
    _loadCts = new CancellationTokenSource();
    var ct = _loadCts.Token;

    if (_thumbnail != null || string.IsNullOrEmpty(_thumbnailPath))
        return;

    await Task.Run(() =>
    {
        ct.ThrowIfCancellationRequested();
        // ... existing load logic with ct checks ...
    }, ct);
}

public void CancelPendingLoad()
{
    _loadCts?.Cancel();
    _loadCts?.Dispose();
    _loadCts = null;
}
```

**B. In AssetGalleryViewModel, cancel loads when items scroll out of view:**

In the gallery's scroll handler or when the viewport changes:
```csharp
// When items are virtualized out (handled by VirtualizingStackPanel),
// the ItemsRepeater/LayoutStrategy will remove them from the visual tree.
// Hook into the ElementPrepared/ElementClearing events if using ItemsRepeater,
// or the ContainerClearing/ClearingItem events for ListBox.
```

**C. Disposal on scroll-out:**
```csharp
// In AssetGalleryView.axaml.cs or code-behind:
private void OnAssetTileCleared(object? sender, ContainerClearingEventArgs e)
{
    if (e.Container is ListBoxItem { DataContext: AssetListItem item })
    {
        item.CancelPendingLoad();
        // The bitmap will be disposed when AssetListItem is disposed
    }
}
```

#### T12.7 — Bitmap Disposal (IDisposable)

**Files changed:**
- `src/Adam.CatalogBrowser/Models/AssetListItem.cs` — implement IDisposable

**Implementation:**

```csharp
public class AssetListItem : INotifyPropertyChanged, IDisposable
{
    private Bitmap? _thumbnail;
    
    public Bitmap? Thumbnail
    {
        get => _thumbnail;
        set
        {
            // Dispose old bitmap
            if (_thumbnail != null && !ReferenceEquals(_thumbnail, value))
                _thumbnail.Dispose();
            
            _thumbnail = value;
            OnPropertyChanged();
        }
    }
    
    public void Dispose()
    {
        // Cancel pending load
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
        
        // Dispose bitmap
        if (_thumbnail != null)
        {
            _thumbnail.Dispose();
            _thumbnail = null;
        }
    }
}
```

**Edge cases:**
- Multiple references to the same bitmap object — use `ReferenceEquals` check in setter to avoid double-dispose
- Bitmap in SharedThumbnailCache — remove from cache before disposal to avoid dangling reference
- Finalizer not needed (no unmanaged resources — Bitmap internally manages SkiaSharp handles)

**In AssetGalleryViewModel, when items are removed:**

```csharp
// When clearing Assets collection:
foreach (var item in Assets)
    item.Dispose();
Assets.Clear();
```

#### T12.8 — GPU Resource Cache (SkiaOptions)

**Files changed:**
- `src/Adam.CatalogBrowser/App.axaml.cs` — configure SkiaOptions

**Implementation:**

In `App.axaml.cs`, in the `OnFrameworkInitializationCompleted` method, before creating the MainWindow:

```csharp
public override void OnFrameworkInitializationCompleted()
{
    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
    {
        // Configure GPU resource limits to prevent cache thrashing during fast scroll
        SkiaOptions options = new()
        {
            MaxGpuResourceSizeBytes = 256 * 1024 * 1024, // 256 MB
            EnableDelayedRendering = true
        };
        
        AvaloniaLocator.CurrentMutable.Bind<SkiaOptions>().ToConstant(options);
        
        // ... rest of initialization
    }
}
```

### Wave 3: Startup Time Optimization

#### T12.9 — Lazy Service Initialization

**Files changed:**
- `src/Adam.CatalogBrowser/App.axaml.cs` — defer non-critical services
- `src/Adam.Shared/Services/ThumbnailPipeline.cs` — lazy pipeline initialization
- `src/Adam.CatalogBrowser/Services/DatabaseInitializer.cs` — review for deferred init

**Implementation patterns:**

**A. Lazy<T> for services:**

```csharp
// In App.axaml.cs, register via Lazy<T>:
services.AddSingleton<Lazy<AiTaggingService>>(sp => 
    new Lazy<AiTaggingService>(() => sp.GetRequiredService<AiTaggingService>()));

// In consuming ViewModels, inject Lazy<AiTaggingService> instead:
public class AssetGalleryViewModel
{
    private readonly Lazy<AiTaggingService>? _lazyAiTagging;
    
    // Only initialized when first accessed
    public AiTaggingService? AiTagging => _lazyAiTagging?.Value;
}
```

**B. Startup sequence optimization (timeline):**

```csharp
// In MainWindowViewModel startup logic:
public async Task StartAsync()
{
    var sw = Stopwatch.StartNew();
    
    // Phase 1: Critical path — must complete before UI is shown (< 500ms)
    ConnectionDebugLogger.Info($"[PERF] Phase 1 start: DB initialization");
    await _modeManager.InitializeAsync();
    ConnectionDebugLogger.Info($"[PERF] Phase 1 done: {sw.ElapsedMilliseconds}ms");
    
    // Phase 2: Show shell immediately, load sidebar in background
    await _dispatcher.InvokeAsync(() => { /* Show window shell */ });
    
    // Phase 3: Background data loading
    var dataSw = Stopwatch.StartNew();
    
    // Load sidebar (collections, keywords, categories) — show as they arrive
    await Sidebar.LoadAsync();
    ConnectionDebugLogger.Info($"[PERF] Sidebar loaded: {dataSw.ElapsedMilliseconds}ms");
    
    // Load first page of gallery
    await AssetGallery.LoadAssetsAsync();
    ConnectionDebugLogger.Info($"[PERF] Gallery loaded: {dataSw.ElapsedMilliseconds}ms");
    
    // Phase 4: Deferred initialization (after UI is responsive)
    _ = Task.Run(async () =>
    {
        await Task.Delay(1000); // Wait 1s after UI is shown
        
        var deferredSw = Stopwatch.StartNew();
        
        // Initialize AI tagging model (slow, can be deferred)
        var aiTagging = App.ServiceProvider?.GetService<AiTaggingService>();
        if (aiTagging != null)
            await aiTagging.InitializeAsync();
        
        ConnectionDebugLogger.Info($"[PERF] Deferred init done: {deferredSw.ElapsedMilliseconds}ms");
        
        var totalMs = sw.ElapsedMilliseconds;
        ConnectionDebugLogger.Info($"[PERF] Total startup: {totalMs}ms");
    });
    
    sw.Stop();
    ConnectionDebugLogger.Info($"[PERF] Startup shell visible at: {sw.ElapsedMilliseconds}ms");
}
```

#### T12.10 — Async Sidebar Loading (Progressively)

**Already partially done** — `Sidebar.LoadAsync()` already runs parallel loads (LoadFoldersAsync, LoadCollectionsAsync, etc. run via `Task.WhenAll`).

**Remaining work:**
- Show sidebar shell immediately (empty trees with loading indicators)
- Populate each tree section as it completes (not all at once)

**Implementation:**

```csharp
public async Task LoadAsync(CancellationToken ct = default)
{
    await Dispatcher.UIThread.InvokeAsync(() => IsLoading = true);
    await _loadLock.WaitAsync(ct).ConfigureAwait(false);
    try
    {
        // Fire all loads in parallel
        var tasks = new List<Task>
        {
            LoadAndAssignFoldersAsync(ct),
            LoadAndAssignCollectionsAsync(ct),
            LoadAndAssignKeywordsAsync(ct),
            LoadMediaFormatCountsAsync(ct),
            LoadMetadataCategoriesAsync(ct),
            LoadDateTakenTreeAsync(ct)
        };

        // As each task completes, the UI updates progressively
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "[LoadAsync] One or more loads failed");
    }
    finally
    {
        _loadLock.Release();
        await Dispatcher.UIThread.InvokeAsync(() => IsLoading = false);
    }
}

// Each LoadAndAssign* method updates its ObservableCollection on the UI thread
// as soon as its data is ready, rather than waiting for all to complete.
// This is already partially implemented — verify the pattern.
```

#### T12.11 — Compiled Bindings

**Files changed:**
- `src/Adam.CatalogBrowser/Adam.CatalogBrowser.csproj` — enable project-wide
- `src/Adam.ServiceManager/Adam.ServiceManager.csproj` — enable project-wide

**Implementation:**

In `.csproj` files, add:
```xml
<PropertyGroup>
  <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
</PropertyGroup>
```

**Note:** This may break existing runtime bindings. Mitigation:
1. Enable in one project at a time
2. Run all tests after each project
3. Use `x:CompileBindings="False"` on specific templates that have issues (e.g., polymorphic TreeDataTemplate in SearchableTreeView)

**Expected issues:**
- `SearchableTreeView.axaml` — uses reflection-based binding (`{Binding Name}`, `{Binding Count}`, `{Binding Children}`) on polymorphic types → mark with `x:CompileBindings="False"`
- `AssetGalleryView.axaml` — template bindings to AssetListItem → should work with compiled bindings
- `MainWindow.axaml` — DataTemplate switching via CurrentView → may need `x:DataType` annotations

#### T12.12 — Startup Profiling Telemetry

**Files changed:**
- `src/Adam.Shared/Services/ModeManager.cs` — add Stopwatch instrumentation
- `src/Adam.CatalogBrowser/ViewModels/MainWindowViewModel.cs` — add startup timing
- `src/Adam.CatalogBrowser/App.axaml.cs` — add framework init timing

**Implementation:**

**A. ModeManager.InitializeAsync:**
```csharp
public async Task InitializeAsync()
{
    var sw = Stopwatch.StartNew();
    
    // ... existing init logic ...
    
    _logger.LogInformation("[PERF] ModeManager.InitializeAsync completed in {ElapsedMs}ms", sw.ElapsedMilliseconds);
}
```

**B. MainWindowViewModel startup:**
```csharp
// Add timing for each phase:
ConnectionDebugLogger.Info($"[PERF] Phase 1: DB init → {sw1.ElapsedMilliseconds}ms");
ConnectionDebugLogger.Info($"[PERF] Phase 2: Sidebar load → {sw2.ElapsedMilliseconds}ms");
ConnectionDebugLogger.Info($"[PERF] Phase 3: Gallery load → {sw3.ElapsedMilliseconds}ms");
ConnectionDebugLogger.Info($"[PERF] Total startup → {swTotal.ElapsedMilliseconds}ms");
```

**C. Performance benchmark command:** (for automated regression testing)
```csharp
// Usage: dotnet run --benchmark
public static async Task BenchmarkStartup()
{
    var sw = Stopwatch.StartNew();
    // ... full startup sequence ...
    sw.Stop();
    Console.WriteLine($"Startup: {sw.ElapsedMilliseconds}ms");
    // Assert: < 3000ms
}
```

---

## 4. Execution Waves

| Wave | Tasks | Depends On | Rationale |
|------|-------|------------|-----------|
| **Wave 1 — Thumbnails** | T12.3, T12.4 | — | Decode-to-size + metadata cache. Self-contained, immediate memory/CPU savings. |
| **Wave 2 — Virtualization** | T12.5, T12.6, T12.7, T12.8 | Wave 1 | Gallery virtualization depends on decode-to-size for memory budget. |
| **Wave 3 — Startup** | T12.9, T12.10, T12.11, T12.12 | — | Independent of thumbnail/virtualization work. Can run parallel with Wave 1. |

---

## 5. File Change Matrix

| # | File | Change Type | Details |
|---|------|-------------|---------|
| 1 | `src/Adam.Shared/ThumbnailExtractors/ImageThumbnailExtractor.cs` | Modify | Add decode-to-size with fallback to full decode |
| 2 | `src/Adam.Shared/Services/ThumbnailService.cs` | Modify | Add LastWriteTimeUtc comparison for cache validation |
| 3 | `src/Adam.CatalogBrowser/Views/AssetGalleryView.axaml` | Modify | Add VirtualizingStackPanel.BufferFactor; verify virtualization in grid/list modes |
| 4 | `src/Adam.CatalogBrowser/Models/AssetListItem.cs` | Modify | Implement IDisposable; add CancelPendingLoad(); update Thumbnail setter to dispose old bitmap |
| 5 | `src/Adam.CatalogBrowser/ViewModels/AssetGalleryViewModel.cs` | Modify | Add load cancellation on scroll-out; dispose items when removed from collection |
| 6 | `src/Adam.CatalogBrowser/Views/AssetGalleryView.axaml.cs` | Modify | Add ContainerClearing handler for load cancellation |
| 7 | `src/Adam.CatalogBrowser/App.axaml.cs` | Modify | Configure SkiaOptions MaxGpuResourceSizeBytes; lazy service DI; startup profiling |
| 8 | `src/Adam.CatalogBrowser/Adam.CatalogBrowser.csproj` | Modify | Enable AvaloniaUseCompiledBindingsByDefault |
| 9 | `src/Adam.ServiceManager/Adam.ServiceManager.csproj` | Modify | Enable AvaloniaUseCompiledBindingsByDefault |
| 10 | `src/Adam.CatalogBrowser/ViewModels/MainWindowViewModel.cs` | Modify | Add startup profiling Stopwatch; lazy initialization sequence |
| 11 | `src/Adam.Shared/Services/ModeManager.cs` | Modify | Add initialization timing telemetry |
| 12 | `src/Adam.Shared/Services/ThumbnailPipeline.cs` | Verify | Verify pipeline passes maxSize to extractors |

---

## 6. Testing Strategy

### 6.1 Unit Tests

| Test ID | Test Name | What It Verifies | File |
|---------|-----------|------------------|------|
| T12-T1 | `DecodeToWidth_ReducesMemory` | Decoding at reduced resolution uses less memory than full decode | `ThumbnailPerformanceTests.cs` |
| T12-T2 | `DecodeToWidth_FallbackOnUnsupported` | Unsupported format falls back to full decode without crash | `ThumbnailPerformanceTests.cs` |
| T12-T3 | `ThumbnailCache_SkipsUnchangedSource` | Thumbnail not regenerated if source LastWriteTime hasn't changed | `ThumbnailPerformanceTests.cs` |
| T12-T4 | `ThumbnailCache_RegeneratesOnSourceChange` | Thumbnail regenerated if source LastWriteTime is newer | `ThumbnailPerformanceTests.cs` |
| T12-T5 | `AssetListItem_Dispose_FreesBitmap` | Disposing AssetListItem disposes the Bitmap | `AssetListItemTests.cs` |
| T12-T6 | `AssetListItem_CancelLoad_CancelsTask` | CancelPendingLoad cancels the async thumbnail load | `AssetListItemTests.cs` |
| T12-T7 | `AssetListItem_ThumbnailChange_DisposesOld` | Setting Thumbnail to new value disposes old Bitmap | `AssetListItemTests.cs` |
| T12-T8 | `StartupTiming_UnderThreshold` | ModeManager.InitializeAsync completes under 500ms (cold) | `PerformanceBenchmarkTests.cs` |

### 6.2 Manual Performance Tests

| Test | Steps | Measurement |
|------|-------|-------------|
| Cold start timing | 1. Close app. 2. Run with `dotnet run -c Release`. 3. Watch Stopwatch logs. | < 3000ms |
| Gallery scroll at 100K | 1. Load 100K assets. 2. Scroll rapidly through gallery. | No UI freeze > 100ms; memory < 1GB |
| Thumbnail generation batch | 1. Import 100 new images. 2. Time thumbnail generation. | < 30 seconds |
| Memory stability | 1. Scroll to bottom and back. 2. Check memory in profiler. | Memory stable (no linear growth) |

### 6.3 Benchmark Command

```bash
# Performance regression benchmark (to be automated in CI)
dotnet run -c Release --benchmark
```

Expected output:
```
[Startup Benchmarks]
  DB init: 120ms
  Sidebar load: 340ms
  Gallery load: 890ms
  Deferred init: 1500ms
  Total: <3000ms ✓

[Thumbnail Benchmarks]
  100 images decode-to-size: 12.3s (vs 45.2s full decode)
  Memory per thumbnail: 1.2MB (vs 24MB full decode)
  Cache hit rate: 94%
```

---

## 7. Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| DecodeToWidth unsupported for some RAW formats | Falls back to full decode (no perf gain for some formats) | Acceptable — most workflows have JPEG/TIFF/PNG; RAW decode is a minority |
| Compiled bindings break existing templates | App fails to compile or shows blank views | Enable project-by-project; use `x:CompileBindings="False"` on polymorphic templates (SearchableTreeView) |
| Lazy initialization causes first-use latency spikes | AI tagging first request is slow | Show "Initializing AI model..." in status bar; pre-warm during idle time after startup |
| GPU cache thrashing during fast scroll | Choppy scrolling | 256MB GPU cache is generous; `EnableDelayedRendering` smooths out frame drops |
| VirtualizingStackPanel doesn't virtualize UniformGridLayout | Gallery grid mode loads all items into memory | Fall back to ListBox with WrapPanel and standard virtualization; or limit page size in grid mode |

---

## 8. Dependencies

- **DEP-001**: SkiaSharp (Avalonia's rendering backend) — for decode-to-size via `SKBitmap.Decode`
- **DEP-002**: Avalonia's `VirtualizingStackPanel` — built-in virtualization
- **DEP-003**: SixLabors.ImageSharp (already in project) — for fallback decode

---

## 9. Rollout Order

1. **Wave 1**: Decode-to-size + thumbnail cache → validate memory improvement with dotMemory/dotTrace
2. **Wave 2**: Virtualization + bitmap disposal → validate smooth scroll at 100K with profiler
3. **Wave 3**: Startup optimization → validate <3s cold start with Stopwatch logs
4. **Integration sweep**: Full startup → gallery scroll → thumbnail generation → verify all three targets met
