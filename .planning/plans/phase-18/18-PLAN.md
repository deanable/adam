---
goal: Add a plugin system for third-party metadata extractors (INTG-V2-01) with sample plugin
version: 1.0
date_created: 2026-06-14
last_updated: 2026-06-14
status: 'Planned'
tags: [phase-18, plugins, integration, metadata, extensibility]
---

# Phase 18 — Integration: Plugin System for Metadata Extractors

**INTG-V2-01**: Integration with third-party metadata extractors via a plugin system.

## Architecture Summary

```
┌────────────────────────────────────────────────────────────────┐
│                      Shared (Adam.Shared)                       │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │             IMetadataExtractor (interface)               │   │
│  │  Priority, CanExtract, ExtractTextAsync, ExtractAsync   │   │
│  └─────────────────────────────────────────────────────────┘   │
│  ┌──────────────────┐  ┌──────────────────────────┐            │
│  │ ImageExtractor   │  │ OfficeExtractor (adapter) │            │
│  │ (wraps Metadata  │  │ (wraps OfficeDocument     │            │
│  │  ExtractorSvc)   │  │  Extractor)               │            │
│  └──────────────────┘  └──────────────────────────┘            │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │              PluginLoaderService                         │   │
│  │  Scan directory → load assemblies → discover IMetadata  │   │
│  │  Extractors → composite extraction pipeline             │   │
│  └─────────────────────────────────────────────────────────┘   │
└────────────────────────────────────────────────────────────────┘
                              │
         ┌────────────────────┼────────────────────┐
         │                    │                    │
   IngestionVM          FolderWatcher         FileIndexer
         │                    │                    │
    PluginLoaderService ──────┴────────────────────┘
         │
         ▼
┌─────────────────────────────────────────────────────────┐
│  plugins/SampleMetadataPlugin (standalone project)       │
│  ┌─────────────────────────────────────────────────┐     │
│  │ SampleVowelCounterExtractor → counts vowels      │     │
│  │ as keywords, demonstrates Priority, CanExtract   │     │
│  └─────────────────────────────────────────────────┘     │
│  Outputs: .dll in plugins/ directory                    │
└─────────────────────────────────────────────────────────┘
```

## Tasks

### T18.1 — IMetadataExtractor Interface

**Files:**
- `src/Adam.Shared/Extractors/IMetadataExtractor.cs` (new)

The contract abstraction, modeled after the existing `IThumbnailExtractor` pattern:

```csharp
namespace Adam.Shared.Extractors;

/// <summary>
/// Extracts metadata from a file. Each implementation handles one or more file types.
/// Registered extractors are discovered via PluginLoaderService.
/// </summary>
public interface IMetadataExtractor
{
    /// <summary>
    /// Lower values are tried first. Built-in: 100 (image), 200 (office), 300 (XMP sidecar).
    /// Third-party plugins should use 1000+ to allow future built-in tiers.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Display name shown in plugin management UI.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Whether this extractor can handle the given file.
    /// </summary>
    bool CanExtract(string filePath, string mimeType);

    /// <summary>
    /// Extracts text metadata (Title, Description, Keywords, Categories, Rating).
    /// Returns null if no relevant metadata was found (allows pipeline to continue).
    /// </summary>
    Task<ExtractedTextMetadata?> ExtractTextAsync(string filePath, CancellationToken ct);

    /// <summary>
    /// Extracts rich metadata profile (camera settings, GPS, EXIF, XMP).
    /// Returns null if no rich metadata was found.
    /// </summary>
    Task<MetadataProfile?> ExtractAsync(string filePath, CancellationToken ct);
}
```

**Estimated LOC:** ~30

### T18.2 — ImageExtractor (Built-in Adapter)

**Files:**
- `src/Adam.Shared/Extractors/ImageExtractor.cs` (new)

Wraps the existing `MetadataExtractorService` as an `IMetadataExtractor` implementation:

```csharp
public sealed class ImageExtractor : IMetadataExtractor
{
    public int Priority => 100;
    public string Name => "Image EXIF/XMP Extractor";
    public bool CanExtract(string filePath, string mimeType) =>
        mimeType.StartsWith("image/");

    public Task<ExtractedTextMetadata?> ExtractTextAsync(string filePath, CancellationToken ct)
    {
        var result = _service.ExtractTextMetadata(filePath);
        return Task.FromResult(result.HasAnyContent ? result : null);
    }

    public Task<MetadataProfile?> ExtractAsync(string filePath, CancellationToken ct)
    {
        var result = _service.Extract(filePath);
        return Task.FromResult(result.HasAnyContent ? result : null);  // Add HasAnyContent to MetadataProfile
    }
}
```

**Design note:** The adapter itself doesn't add new logic — it wraps the existing `MetadataExtractorService` so the ingestion pipeline can treat all extractors uniformly. The `HasAnyContent` property is a small addition to `MetadataProfile`.

**Estimated LOC:** ~40

### T18.3 — OfficeExtractor (Built-in Adapter)

**Files:**
- `src/Adam.Shared/Extractors/OfficeExtractor.cs` (new)

Wraps `OfficeDocumentExtractor` as an `IMetadataExtractor`:

```csharp
public sealed class OfficeExtractor : IMetadataExtractor
{
    public int Priority => 200;
    public string Name => "Office Document Extractor";
    public bool CanExtract(string filePath, string mimeType) =>
        OfficeDocumentExtractor.SupportedExtensions.Contains(
            Path.GetExtension(filePath).ToLowerInvariant());

    public Task<ExtractedTextMetadata?> ExtractTextAsync(string filePath, CancellationToken ct)
    {
        var result = _service.Extract(filePath);
        return Task.FromResult(result.HasAnyContent ? result : null);
    }

    public Task<MetadataProfile?> ExtractAsync(string filePath, CancellationToken ct)
        => Task.FromResult<MetadataProfile?>(null); // Office docs don't have rich metadata profiles
}
```

**Estimated LOC:** ~35

### T18.4 — PluginLoaderService

**Files:**
- `src/Adam.Shared/Services/PluginLoaderService.cs` (new)

The core plugin discovery and loading mechanism:

| Member | Description |
|--------|-------------|
| `PluginLoaderService(IOptions<PluginConfig> config, ILogger<PluginLoaderService> logger)` | Constructor |
| `IReadOnlyList<IMetadataExtractor> Extractors` | Discovered extractors (built-in + plugins), sorted by Priority |
| `IReadOnlyList<PluginInfo> LoadedPlugins` | Metadata about loaded plugin assemblies |
| `Task LoadPluginsAsync(CancellationToken ct)` | Scans plugin directory, loads assemblies, discovers implementations via reflection |
| `IMetadataExtractor? GetExtractor(string filePath, string mimeType)` | Returns the highest-priority extractor that `CanExtract()` |
| `Task<ExtractionResult> ExtractAllAsync(string filePath, string mimeType, CancellationToken ct)` | Runs extractors in priority order until one returns non-null for each extraction type |

**Plugin directory:** `%LOCALAPPDATA%/Adam/plugins/` (Windows) or `~/.local/share/Adam/plugins/` (Linux) — configurable via `appsettings.json` or `settings.json`.

**Plugin loading process:**
1. If the plugin directory doesn't exist, create it
2. Read all `.dll` files in the directory
3. For each assembly, load it via `Assembly.LoadFrom()`
4. Find all types implementing `IMetadataExtractor` with a parameterless constructor
5. Instantiate and register each one, sorted by `Priority`
6. Log each discovered plugin (name, priority, assembly)
7. Catch and log individual plugin loading failures — a bad plugin never crashes the app

**Built-in extractors:**
- Always registered regardless of plugin directory contents
- `ImageExtractor` (Priority 100)
- `OfficeExtractor` (Priority 200)
- Registered after built-in extractors so built-ins always win at equal priority

**Estimated LOC:** ~180

### T18.5 — Ingestion Pipeline Integration

**Files modified:**
- `src/Adam.CatalogBrowser/ViewModels/IngestionViewModel.cs`
- `src/Adam.BrokerService/Services/FolderWatcherService.cs`
- `src/Adam.BrokerService/Services/FolderWatcherHostedService.cs`
- `src/Adam.Shared/Services/FileIndexer.cs`
- `src/Adam.CatalogBrowser/App.axaml.cs` (DI registration)

**Changes:**

Replace direct `new MetadataExtractorService()` and `new OfficeDocumentExtractor()` instantiations with `PluginLoaderService` injection.

**IngestionViewModel changes:**
```csharp
// Before:
private readonly MetadataExtractorService _metadataExtractor = new();
private readonly OfficeDocumentExtractor _officeExtractor = new();

// After:
private readonly PluginLoaderService _pluginLoader;
// uses _pluginLoader.GetExtractor(filePath, mimeType) instead
```

**DI registration in App.axaml.cs:**
```csharp
services.AddSingleton<PluginLoaderService>();
services.AddSingleton<IMetadataExtractor>(sp =>
    sp.GetRequiredService<PluginLoaderService>().Extractors[0]); // or similar
```

**FolderWatcherService** and **FolderWatcherHostedService** — same replacement pattern.

**FileIndexer** — same replacement pattern.

**Extraction flow (simplified):**
```
GetExtractor(filePath, mimeType)?
  ├── Yes → extractText + extractProfile
  ├── No  → null/empty result
```

**Estimated LOC:** ~80 (modifications across 5 files)

### T18.6 — Sample Plugin Project

**Files:**
- `plugins/SampleMetadataPlugin/SampleMetadataPlugin.csproj` (new)
- `plugins/SampleMetadataPlugin/SampleVowelCounterExtractor.cs` (new)
- `plugins/SampleMetadataPlugin/README.md` (new)

A standalone class library project that demonstrates the plugin interface:

```csharp
public sealed class SampleVowelCounterExtractor : IMetadataExtractor
{
    public int Priority => 1000;
    public string Name => "Vowel Counter Sample";
    public bool CanExtract(string filePath, string mimeType)
        => mimeType.StartsWith("text/") || filePath.EndsWith(".md");

    public Task<ExtractedTextMetadata?> ExtractTextAsync(string filePath, CancellationToken ct)
    {
        var text = File.ReadAllText(filePath, ct);
        var vowels = text.Count(c => "aeiouAEIOU".Contains(c));
        var result = new ExtractedTextMetadata
        {
            Title = $"Vowel Analysis: {vowels} vowels found",
            Description = $"The file '{Path.GetFileName(filePath)}' contains {vowels} vowels."
        };
        result.Keywords.Add($"vowel-count:{vowels}");
        return Task.FromResult(result);
    }

    public Task<MetadataProfile?> ExtractAsync(string filePath, CancellationToken ct)
        => Task.FromResult<MetadataProfile?>(null);
}
```

**csproj pattern:**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/Adam.Shared/Adam.Shared.csproj" />
  </ItemGroup>
</Project>
```

**README.md** explains:
- How to create a plugin (implement `IMetadataExtractor`)
- How to build (`dotnet build`)
- Where to place the DLL (`plugins/` directory)
- Priority system and CanExtract conventions

**Estimated LOC:** ~50 (csproj + code + readme)

### T18.7 — Plugin Management UI

**Files:**
- `src/Adam.CatalogBrowser/ViewModels/PluginManagerViewModel.cs` (new)
- `src/Adam.CatalogBrowser/Views/PluginManagerView.axaml` (new)
- `src/Adam.CatalogBrowser/Views/PluginManagerView.axaml.cs` (new code-behind)
- `src/Adam.CatalogBrowser/ViewModels/SettingsViewModel.cs` (wire into settings)

**PluginManagerViewModel:**
```csharp
public sealed class PluginManagerViewModel : INotifyPropertyChanged
{
    public ObservableCollection<PluginItem> Plugins { get; }
    public bool HasPlugins => Plugins.Count > 0;
    public ICommand RefreshCommand { get; }
    public ICommand OpenPluginFolderCommand { get; }
}

public sealed record PluginItem(
    string Name,
    string AssemblyName,
    int Priority,
    bool IsBuiltIn,
    string Status   // "Loaded", "Load Error: ..."
);
```

**UI layout (simplified):**

```
┌─ Plugin Manager ──────────────────────────────┐
│                                                 │
│  Plugin Directory: ~/.local/share/Adam/plugins/ │
│  [Open Folder]                           [↻]   │
│                                                 │
│  Built-in Extractors:                           │
│  ├─ Image EXIF/XMP Extractor  (Priority 100)  │
│  └─ Office Document Extractor (Priority 200)  │
│                                                 │
│  Third-Party Plugins:                           │
│  └─ Vowel Counter Sample     (Priority 1000)  │
│       Assembly: SampleMetadataPlugin.dll       │
│                                                 │
│  [Close]                                        │
└─────────────────────────────────────────────────┘
```

**Wiring into Settings:**
- Accessible from Settings dialog → "Plugins" tab
- Or as a standalone menu item in the app menu

**Estimated LOC:** ~180 (ViewModel + XAML + code-behind)

### T18.8 — Tests

**Files:**
- `tests/Adam.Shared.Tests/Extractors/ImageExtractorTests.cs` (new, 4 tests)
- `tests/Adam.Shared.Tests/Extractors/OfficeExtractorTests.cs` (new, 3 tests)
- `tests/Adam.Shared.Tests/Services/PluginLoaderServiceTests.cs` (new, 8 tests)
- `tests/Adam.CatalogBrowser.Tests/ViewModels/PluginManagerViewModelTests.cs` (new, 5 tests)

**Test coverage:**

| Test | What it covers |
|------|---------------|
| `ImageExtractor.CanExtract` | Returns true for image/jpeg, image/png; false for text/plain |
| `ImageExtractor.ExtractTextAsync` | Delegates to MetadataExtractorService correctly (mock the service) |
| `OfficeExtractor.CanExtract` | Returns true for .docx, .xlsx, .pptx; false for .pdf |
| `OfficeExtractor.ExtractAsync` | Returns null (office docs don't have rich profiles) |
| `PluginLoaderService.Load_NoPlugins` | Only built-in extractors registered |
| `PluginLoaderService.Load_ValidPlugin` | Scans a temp dir with a mock .dll, verifies discovery |
| `PluginLoaderService.Load_CorruptAssembly` | Logs error, doesn't crash, continues loading |
| `PluginLoaderService.GetExtractor_Priority` | Returns highest priority extractor for a mime type |
| `PluginLoaderService.ExtractAll` | Runs pipeline, returns first non-null result |
| `PluginManagerViewModel.Load_State` | Shows built-in + plugin extractors correctly |
| `PluginManagerViewModel.Refresh` | Reloads plugins, updates ObservableCollection |

**Estimated LOC:** ~250 (20 tests)

## Success Criteria

- ✅ `IMetadataExtractor` interface defined with Priority, CanExtract, and async extraction methods
- ✅ `ImageExtractor` wraps existing `MetadataExtractorService` — no functionality lost
- ✅ `OfficeExtractor` wraps existing `OfficeDocumentExtractor` — no functionality lost
- ✅ `PluginLoaderService` discovers both built-in and plugin extractors via priority chain
- ✅ Plugin directory is configurable, auto-created on first run
- ✅ Bad plugin assemblies don't crash the app (graceful degradation with logged errors)
- ✅ Ingestion pipeline uses `PluginLoaderService` instead of direct `new()` calls
- ✅ Sample plugin project builds and produces a .dll that loads correctly
- ✅ Plugin management UI shows loaded plugins with status indicators
- ✅ `Open Plugin Folder` button opens the plugin directory in file manager
- ✅ All existing tests still pass with refactored pipeline
- ✅ 20+ new tests pass (adapters, loader, ViewModel)

## Execution Order (Waves)

```
Wave 1 ─── T18.1 Interface + T18.2 ImageExtractor + T18.3 OfficeExtractor ─ independent, parallel
Wave 2 ─── T18.4 PluginLoaderService (depends on T18.1)
Wave 3 ─── T18.5 Pipeline integration (depends on T18.2, T18.3, T18.4)
Wave 4 ─── T18.6 Sample plugin (independent of everything except T18.1)
Wave 5 ─── T18.7 Plugin management UI (depends on T18.4)
Wave 6 ─── T18.8 Tests (after all code)
Wave 7 ─── Full test suite, UAT document, plan status update
```

## Files Created

| # | File | Task |
|---|------|------|
| 1 | `src/Adam.Shared/Extractors/IMetadataExtractor.cs` | T18.1 |
| 2 | `src/Adam.Shared/Extractors/ImageExtractor.cs` | T18.2 |
| 3 | `src/Adam.Shared/Extractors/OfficeExtractor.cs` | T18.3 |
| 4 | `src/Adam.Shared/Services/PluginLoaderService.cs` | T18.4 |
| 5 | `plugins/SampleMetadataPlugin/SampleMetadataPlugin.csproj` | T18.6 |
| 6 | `plugins/SampleMetadataPlugin/SampleVowelCounterExtractor.cs` | T18.6 |
| 7 | `plugins/SampleMetadataPlugin/README.md` | T18.6 |
| 8 | `src/Adam.CatalogBrowser/ViewModels/PluginManagerViewModel.cs` | T18.7 |
| 9 | `src/Adam.CatalogBrowser/Views/PluginManagerView.axaml` | T18.7 |
| 10 | `src/Adam.CatalogBrowser/Views/PluginManagerView.axaml.cs` | T18.7 |
| 11+ | 4 test files | T18.8 |

## Files Modified

| # | File | Change |
|---|------|--------|
| 1 | `src/Adam.CatalogBrowser/ViewModels/IngestionViewModel.cs` | Replace direct `new MetadataExtractorService()` with `PluginLoaderService` |
| 2 | `src/Adam.BrokerService/Services/FolderWatcherService.cs` | Same replacement |
| 3 | `src/Adam.BrokerService/Services/FolderWatcherHostedService.cs` | Same replacement |
| 4 | `src/Adam.Shared/Services/FileIndexer.cs` | Same replacement |
| 5 | `src/Adam.CatalogBrowser/App.axaml.cs` | Register `PluginLoaderService` in DI |
| 6 | `src/Adam.Shared/Models/MetadataProfile.cs` | Add `HasAnyContent` helper property |
| 7 | `src/Adam.Shared/Models/ExtractedTextMetadata.cs` | Add `HasAnyContent` helper property |

## Key Decisions

1. **Interface over abstract class**: `IMetadataExtractor` is an interface (not abstract class) for maximum flexibility. Plugins can implement it without inheriting from a base class.

2. **Assembly.LoadFrom over AssemblyLoadContext**: For v1, use simple `Assembly.LoadFrom()` for plugin loading. An `AssemblyLoadContext`-based isolation layer can be added in a future phase if plugin conflicts arise.

3. **No dynamic compilation or scripting**: V1 loads only pre-compiled assemblies. No script-based or runtime-compiled plugins.

4. **Priority convention**: Built-in extractors use 100/200/300. Plugins should use 1000+. This ensures built-in extractors always run first for their supported file types, while plugins get a chance for unsupported file types.

5. **Two-phase extraction**: `ExtractTextAsync` (Title/Description/Keywords) and `ExtractAsync` (rich EXIF/XMP profile) are separate methods. This matches the existing codebase where text metadata and profile metadata are extracted differently and used at different points in the pipeline.

6. **Plugin directory auto-creation**: If the plugin directory doesn't exist, it's created on first `LoadPluginsAsync()` call. No error is raised — the system simply runs with built-in extractors only.

7. **Plugin dependencies bundled by the plugin author**: Plugins that require additional NuGet packages should publish a self-contained output or place their dependency DLLs next to the plugin DLL. The `PluginLoaderService` loads all DLLs in the plugin directory, so dependencies are naturally resolved.

8. **No plugin sandboxing**: V1 runs plugins in-process with full trust. Sandboxing (AppDomain isolation, permission restrictions) can be added in a future phase if needed.
