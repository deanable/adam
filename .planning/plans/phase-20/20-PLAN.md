---
goal: UX Modernization — theme engine, loupe view, compare view, drag-reorder collections
version: 1.1
date_created: 2026-06-17
last_updated: 2026-06-17
status: Partial — T20.1 implemented, rest planned
tags: [phase-20, ux, theming, loupe, compare-view, drag-reorder, design]
---

# Phase 20 — UX Modernization

**v4.0 — Advanced Discovery & Experience (Part 2 of 2)**

**Status:** T20.1 (Design Theme Engine) implemented and verified — 1,159 tests passing.

**Depends on:** Phase 19 (advanced search data model provides filter serialization foundation)

## Features

| # | Feature | Scope | Effort |
|---|---------|-------|--------|
| T20.1 | **Design System Theme Engine** — Dynamic theme loading from `.design/*.md` files; YAML frontmatter parsing; automatic Avalonia resource dictionary generation; theme selector dropdown in title bar; persists selection to `settings.json` | `DesignThemeService`, `YamlFrontmatterParser`, `DesignThemeResourceDictionary`, theme selector ComboBox in MainWindow | ✅ **Implemented** |
| T20.2 | **Full-Resolution Loupe View** — Dedicated full-window viewing mode with pan/zoom, filmstrip, keyboard navigation, info overlay | New `LoupeViewModel`, `ZoomBorder` control, full-res `ImageSharp` loading, filmstrip bar | Medium |
| T20.3 | **Compare View** — Side-by-side asset comparison with synchronized pan/zoom, metadata diff table, optional overlay/swipe mode | New `CompareViewModel`, sync engine, metadata diff logic, swipe overlay mode | Medium |
| T20.4 | **Drag-Reorder Collections** — Drag-reorder items within a `Collection` via `SortOrder` field, broker handler, visual drag feedback | `SortOrder` on `DigitalAssetCollection` join, broker handler, gallery drag-drop UI | Small |
| T20.5 | **Design Templates** — `.design_templates/` directory with design token reference, component style guide, layout patterns, accent palettes | Documentation files in `.design_templates/` | Small |

## Architecture Overview

```
┌──────────────────────────────────────────────────────────────────┐
│                       Adam.CatalogBrowser (UI)                    │
│                                                                   │
│  ┌──────────────────────────────────────────────────────────┐    │
│  │                    Theme Engine (T20.1)                   │    │
│  │  ┌──────────────────┐  ┌────────────────────────────┐    │    │
│  │  │ ThemeService     │  │ ThemeSettingsViewModel     │    │    │
│  │  │ - SetTheme()     │  │ - AvailableThemes          │    │    │
│  │  │ - SetAccent()    │  │ - SelectedTheme            │    │    │
│  │  │ - Load/Save()    │  │ - SelectedAccent           │    │    │
│  │  └──────────────────┘  └────────────────────────────┘    │    │
│  │  Resources:                                               │    │
│  │  ├─ ColorResources.Dark.axaml (dark palette)              │    │
│  │  ├─ AccentPalettes.axaml (5 palettes)                     │    │
│  │  └─ ThemeSettings.x (extends AdamConfig)                  │    │
│  └──────────────────────────────────────────────────────────┘    │
│                                                                   │
│  ┌──────────────────────────────────────────────────────────┐    │
│  │                    Loupe View (T20.2)                     │    │
│  │  ┌──────────────────────┐  ┌──────────────────────────┐  │    │
│  │  │ LoupeViewModel      │  │ ZoomBorder (control)     │  │    │
│  │  │ - CurrentImage      │  │ - ZoomTransform          │  │    │
│  │  │ - FilmstripItems    │  │ - PanOffset              │  │    │
│  │  │ - Prev/Next/MaxFit  │  │ - MouseWheel=zoom        │  │    │
│  │  │ - InfoOverlay       │  │ - Click+drag=pan         │  │    │
│  │  └──────────────────────┘  │ - DoubleClick=fit-to-win│  │    │
│  │                            └──────────────────────────┘  │    │
│  │  ┌──────────────────────────────────────────────────┐    │    │
│  │  │ FilmstripBar: Horizontal ScrollViewer of          │    │    │
│  │  │ thumbnail ListBox items for adjacent assets       │    │    │
│  │  └──────────────────────────────────────────────────┘    │    │
│  └──────────────────────────────────────────────────────────┘    │
│                                                                   │
│  ┌──────────────────────────────────────────────────────────┐    │
│  │                  Compare View (T20.3)                     │    │
│  │  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐  │    │
│  │  │ Left Panel   │  │ Right Panel  │  │ Metadata     │  │    │
│  │  │ (ZoomBorder) │  │ (ZoomBorder)  │  │ Diff Table  │  │    │
│  │  │ Sync Zoom    │  │ Sync Zoom    │  │ ├─ Title: ✗ │  │    │
│  │  │ Sync Pan     │  │ Sync Pan     │  │ ├─ Rating: ✓ │  │    │
│  │  └──────────────┘  └──────────────┘  │ └─ Tags: ✗  │  │    │
│  │                                      └──────────────┘  │    │
│  │  Swipe Mode: Overlay with opacity slider                │    │
│  └──────────────────────────────────────────────────────────┘    │
│                                                                   │
│  ┌──────────────────────────────────────────────────────────┐    │
│  │               Drag-Reorder Collections (T20.4)            │    │
│  │  ┌────────────────────────────┐                           │    │
│  │  │ DigitalAssetCollection    │                           │    │
│  │  │ - AssetId                 │                           │    │
│  │  │ - CollectionId            │                           │    │
│  │  │ - SortOrder (new)         │                           │    │
│  │  └────────────────────────────┘                           │    │
│  │  Gallery: DragReorderBehavior on ListBox                  │    │
│  └──────────────────────────────────────────────────────────┘    │
└──────────────────────────────────────────────────────────────────┘
```

## Key Design Decisions

1. **Design-file-driven themes**: Instead of hardcoded light/dark with preset accents, the theme engine reads `.design/*.md` files with YAML frontmatter at startup. Each file is a theme. The `DesignThemeService` parses color tokens, maps them to XAML `{DynamicResource}` keys via a `ResourceMap` with fallback chains, and generates a `ResourceDictionary` merged into `Application.Current.Resources`. Users can drop new `.md` files into `.design/` to extend themes.

2. **Theme color mapping**: The `ResourceMap` maps ~40 XAML resource keys (PrimaryBrush, TextPrimaryBrush, SurfaceBrush, etc.) to design file color token names with fallback chains (e.g., `TextPrimaryBrush` → `"ink"` → `"body"` → `"charcoal"` → `"primary"`). Derived resources (hover/pressed states, borders, selections) are generated programmatically via `ApplyDerivedResources` using darken/lighten logic.

2. **ZoomBorder control pattern**: The loupe uses a standard Avalonia custom control pattern: a `Border` wraps an `Image` element, with `RenderTransform` (ScaleTransform + TranslateTransform) applied for zoom/pan. Mouse wheel changes scale, click-drag translates. `ScrollViewer` is intentionally avoided for the image area (ScrollViewer's scroll bars would fight with the pan gesture).

3. **Synchronized pan/zoom**: Compare view uses a shared `SyncState` object. When either `ZoomBorder` updates, it sets `SyncState` values and raises an event. The other `ZoomBorder` listens and applies the same transforms. A debounce (16ms / 60fps) prevents infinite loops.

4. **Drag-reorder as gallery behavior**: The drag-reorder is implemented as an attached behavior on the gallery `ListBox`. It doesn't require a new control — Avalonia's `DragDrop` attached events are used for drag initiation, visual feedback (insertion line), and drop commit.

5. **No new NuGet dependencies**: All features use existing Avalonia APIs (`RenderTransform`, `DragDrop`, `MergedDictionaries`, `RequestedThemeVariant`). No third-party charting or UI libraries needed.

6. **`.design/` folder is the user extension point**: Users can download or create their own `.md` design files (with proper YAML frontmatter) and drop them into `.design/`. The app detects them on next launch. No code changes needed.

## Tasks

### T20.1 — Design System Theme Engine ✅ _Implemented_

**Live implementation — see these files:**
- `src/Adam.CatalogBrowser/Services/YamlFrontmatterParser.cs` — Parses YAML frontmatter from `.design/*.md` files; handles hex (#RGB, #RRGGBB, #AARRGGBB) and rgba() color formats
- `src/Adam.CatalogBrowser/Services/DesignThemeService.cs` — Core engine: scans `.design/`, LoadThemes() + ApplyTheme(), maps design colors to 40+ XAML `{DynamicResource}` keys, generates `DesignThemeResourceDictionary`, applies via `Application.Current.Resources.MergedDictionaries`
- `src/Adam.CatalogBrowser/Services/AdamConfig.cs` — Added `DesignThemeFile` property
- `src/Adam.CatalogBrowser/ViewModels/MainWindowViewModel.cs` — Injects `DesignThemeService`, exposes as property
- `src/Adam.CatalogBrowser/Views/MainWindow.axaml` — Theme selector ComboBox in title bar (Theme: label + dropdown)
- `src/Adam.CatalogBrowser/Views/MainWindow.axaml.cs` — `OnThemeSelectionChanged` handler
- `src/Adam.CatalogBrowser/App.axaml.cs` — Registers `DesignThemeService` as singleton; calls `LoadThemes()` + `ApplyTheme()` on startup

**Color token mapping (ResourceMap):**
Design file tokens → XAML resource keys with fallback chains:
- `primary`/`accent`/`ink` → `PrimaryBrush`, `TitleBarBrush`
- `ink`/`body`/`charcoal` → `TextPrimaryBrush`
- `canvas`/`surface-soft` → `SurfaceBrush`
- `success`/`green` → `SuccessBrush`, `SuccessHoverBrush`
- `danger`/`red` → `DangerBrush`, `DangerHoverBrush`, `DangerPressedBrush`
- `warning`/`orange` → `WarningBrush`, `WarningTextBrush`, `WarningHoverBrush`
- 20+ derived resources generated in `ApplyDerivedResources`: hover/pressed states, surface variants, borders, selections, overlays

**Design themes available at startup:**
| Theme | File | Style |
|-------|------|-------|
| OpenCode-design-analysis | `.design/DESIGN-opencode.ai.md` | Terminal-native: Berkeley Mono, cream canvas, near-black ink, Apple Blue accent |
| (future) | `.design/DESIGN-tesla.md` | Needs YAML frontmatter — currently prose-only, skipped by parser |

**Extensibility:**
- Drop a new `.md` file with YAML frontmatter into `.design/` → appears in dropdown on next launch
- Theme selection persists to `settings.json` via `AdamConfig.DesignThemeFile`
- Selection resets to first available theme if the saved file is deleted

**Verified:** All 1,159 tests pass with zero regressions.

### T20.2 — Full-Resolution Loupe View

**Files:**
- `src/Adam.CatalogBrowser/Controls/ZoomBorder.cs` (new — custom control for pan/zoom)
- `src/Adam.CatalogBrowser/ViewModels/LoupeViewModel.cs` (new)
- `src/Adam.CatalogBrowser/Views/LoupeView.axaml` (new)
- `src/Adam.CatalogBrowser/Views/LoupeView.axaml.cs` (new — keyboard hookup)
- `src/Adam.CatalogBrowser/ViewModels/MainWindowViewModel.cs` (add LoupeMode navigation)
- `src/Adam.CatalogBrowser/Views/MainWindow.axaml` (add LoupeView to ContentControl binding)

**Mode switching:**
`MainWindowViewModel` gets a new `CurrentViewMode` enum: `Gallery | Loupe | Compare` (default `Gallery`). The `ContentControl` in `MainWindow.axaml` switches templates based on the mode:
```xml
<ContentControl Content="{Binding CurrentViewModel}">
  <ContentControl.ContentTemplate>
    <DataTemplate DataType="viewModels:LoupeViewModel">
      <views:LoupeView />
    </DataTemplate>
  </ContentControl.ContentTemplate>
</ContentControl>
```

**ZoomBorder control:**
```csharp
public sealed class ZoomBorder : ContentControl
{
    // Zoom level (1.0 = 100%, 0.1-20.0 range)
    public static readonly DirectProperty<ZoomBorder, double> ZoomLevelProperty;
    public double ZoomLevel { get; set; }

    // Pan offset in pixels
    public static readonly StyledProperty<Vector> PanOffsetProperty;
    public Vector PanOffset { get; set; }

    // Fit to window
    public void FitToWindow();
    public void ZoomTo(double factor, Point? center = null);
    public void PanBy(Vector delta);

    // Events for sync (compare view)
    public event EventHandler<ZoomChangedEventArgs>? ZoomChanged;
    public event EventHandler<PanChangedEventArgs>? PanChanged;
}
```

**ZoomBorder implementation:**
- Override `OnTemplateApplied` to find the `Border` and `Image` parts
- Subscribe `PointerWheelChanged` → adjust `ZoomLevel` scaled by wheel delta, centered on pointer position
- Subscribe `PointerPressed` + `PointerMoved` + `PointerReleased` → pan tracking
- On double-click → `FitToWindow()`: compute scale to fit image in available space
- `RenderTransform` is a `TransformGroup` with `ScaleTransform` + `TranslateTransform`
- Clamp pan to prevent going beyond image edges

**LoupeViewModel:**
```csharp
public sealed class LoupeViewModel : ViewModelBase
{
    public Bitmap? FullResImage { get; }
    public DigitalAsset? CurrentAsset { get; }
    public ObservableCollection<FilmstripItem> FilmstripItems { get; }

    public ICommand NextCommand { get; }
    public ICommand PrevCommand { get; }
    public ICommand FitToWindowCommand { get; }
    public ICommand ZoomInCommand { get; }
    public ICommand ZoomOutCommand { get; }
    public ICommand CloseCommand { get; }
    public ICommand ToggleInfoOverlayCommand { get; }

    // Info overlay
    public bool ShowInfoOverlay { get; set; }
    public string? FileName { get; }
    public string? Dimensions { get; }
    public string? FileSize { get; }
    public string? Rating { get; }
    public string? Tags { get; }
    public string? CameraModel { get; }
    public string? LensModel { get; }
    public string? Iso { get; }
    public string? Aperture { get; }
    public string? ShutterSpeed { get; }
}
```

**Image loading:**
- `FullResImage` is loaded asynchronously using `ImageSharp` → `WriteToStream` → `Avalonia.Media.Imaging.Bitmap` (same pipeline used for thumbnails, but without decode-to-size constraints)
- Loading spinner shown while image loads
- Cancellation: changing assets cancels the previous load task
- Memory: `FullResImage` is disposed when navigating away

**Filmstrip:**
- Horizontal `ScrollViewer` containing a `ListBox` with `ItemsLayout="Horizontal"` (Avalonia 12 VirtualizingStackPanel)
- Shows thumbnails for surrounding assets (up to 20 before/after current position)
- Current asset highlighted with accent border
- Click navigates to that asset
- Filmstrip height: ~60px

**Keyboard shortcuts:**
| Key | Action |
|-----|--------|
| ← / → | Previous / Next asset |
| + / = | Zoom in |
| - / _ | Zoom out |
| Ctrl+0 | Fit to window |
| I | Toggle info overlay |
| Esc | Close loupe, return to gallery |
| F | Toggle fit/fill mode (fit=contain, fill=cover) |

**Estimated LOC:** ~350

### T20.3 — Compare View

**Files:**
- `src/Adam.CatalogBrowser/ViewModels/CompareViewModel.cs` (new)
- `src/Adam.CatalogBrowser/Views/CompareView.axaml` (new)
- `src/Adam.CatalogBrowser/Views/CompareView.axaml.cs` (new)
- `src/Adam.CatalogBrowser/ViewModels/MainWindowViewModel.cs` (add CompareMode navigation)

**CompareViewModel:**
```csharp
public sealed class CompareViewModel : ViewModelBase
{
    public DigitalAsset? LeftAsset { get; }
    public DigitalAsset? RightAsset { get; }
    public Bitmap? LeftImage { get; }
    public Bitmap? RightImage { get; }
    public bool IsSyncEnabled { get; set; } = true;

    // Mode toggle
    public CompareViewMode ViewMode { get; set; } // SideBySide | Overlay
    public double OverlayOpacity { get; set; } = 0.5;

    // Metadata diff
    public ObservableCollection<MetadataDiffItem> DiffItems { get; }

    public ICommand SelectLeftCommand { get; }
    public ICommand SelectRightCommand { get; }
    public ICommand SwapCommand { get; }
    public ICommand ToggleSyncCommand { get; }
    public ICommand ToggleOverlayCommand { get; }
    public ICommand CloseCommand { get; }

    // Shared sync state (referenced by both ZoomBorder instances)
    public ZoomSyncState SyncState { get; }
}

public sealed record MetadataDiffItem(
    string Field,
    string? LeftValue,
    string? RightValue,
    bool IsDifferent
);

public sealed class ZoomSyncState : INotifyPropertyChanged
{
    public double ZoomLevel { get; set; }
    public Vector PanOffset { get; set; }
}
```

**UI layout (side-by-side mode):**
```
┌─ Compare: asset1 vs asset2 ──────────────────── [✕ Close] ─┐
│                                                              │
│  ┌────────────────────┐  ┌────────────────────┐              │
│  │                    │  │                    │              │
│  │   Left Image       │  │   Right Image      │              │
│  │   (ZoomBorder)     │  │   (ZoomBorder)     │              │
│  │                     │  │                     │              │
│  │                     │  │                     │              │
│  └────────────────────┘  └────────────────────┘              │
│                                                              │
│  [🔄 Swap] [🔗 Sync:ON] [⊞ Overlay]   ═══════ Metadata ═══  │
│  Name:     "Sunset.jpg"    │  "Dawn.jpg"         ✗           │
│  Rating:   ★★★★            │  ★★★★               ✓           │
│  Tags:     beach, sunset   │  ocean, dawn        ✗           │
│  Camera:   Canon R5        │  Canon R5            ✓           │
│  Lens:     24-70mm         │  24-70mm             ✓           │
│  ISO:      100             │  200                 ✗           │
│  Aperture: f/2.8           │  f/4                 ✗           │
└──────────────────────────────────────────────────────────────┘
```

**Overlay/swipe mode:**
```
┌─ Compare (Overlay) ──────────────────────────── [✕ Close] ─┐
│                                                              │
│     ┌─ Swipe ──────────────────────────────────────┐         │
│     │  ← Drag to reveal right image                │         │
│     │  ┌──────┰─────────────────┐                  │         │
│     │  │ Left  ┃   Right         │                  │         │
│     │  │       ┃   (underneath)  │                  │         │
│     │  └──────┸─────────────────┘                  │         │
│     │                                               │         │
│     └──────────────────────────────────────────────┘         │
│                                                              │
│  [⊞ Side-by-side] Opacity: ═══●═══════                       │
└──────────────────────────────────────────────────────────────┘
```

**Synchronization engine:**
- When `IsSyncEnabled` is true and either `ZoomBorder` changes zoom/pan:
  1. The changed `ZoomBorder` updates `ZoomSyncState`
  2. `ZoomSyncState.PropertyChanged` fires
  3. The other `ZoomBorder` listens and applies the same values
  4. A `_isUpdating` guard prevents re-entrant updates (set both sides' `ZoomBorder` references before wiring)

**Asset selection:**
- From gallery: user selects 2 assets and clicks "Compare" (context menu or toolbar)
- From loupe: "Compare with... / Select another asset" opens a picker
- `CompareViewModel` has `SelectLeftCommand`/`SelectRightCommand` that opens a selection dialog

**Metadata diff:**
- Compares all metadata fields from both assets' `MetadataProfile`
- Generates a list of `MetadataDiffItem` with `IsDifferent` flag
- `DiffItems` is displayed in a right panel table
- Visual indicators: ✓ (same) in green, ✗ (different) in orange

**Estimated LOC:** ~400

### T20.4 — Drag-Reorder Collections

**Files:**
- `src/Adam.Shared/Data/AppDbContext.cs` (add SortOrder to DigitalAssetCollection config)
- `src/Adam.Shared/Contracts/CollectionMessages.cs` (add ReorderAssets message)
- `src/Adam.BrokerService/Handlers/CollectionHandler.cs` (add ReorderAssets handler)
- `src/Adam.CatalogBrowser/ViewModels/AssetGalleryViewModel.cs` (add reorder command + drag behavior)
- `src/Adam.CatalogBrowser/Views/AssetGalleryView.axaml` (add DragDrop attached events on ListBox)

**Entity change:**
The existing `DigitalAssetCollection` join entity needs a `SortOrder` column:
```csharp
public int SortOrder { get; set; }
```

**Broker message:**
```
ReorderCollectionAssetsRequest {
    Guid CollectionId,
    repeated AssetOrder Entries = [
        { AssetId = ..., SortOrder = 0 },
        { AssetId = ..., SortOrder = 1 },
        ...
    ]
}
```

**Drag-reorder UI:**
- Drag initiation: `DragDrop.DragStarting` on each gallery tile (requires `CanDrag="True"`)
- Drag data: the `DigitalAsset` being moved
- Drop target: the gallery `ListBox` itself (or the tile being dropped on)
- Visual feedback: insertion line indicator between items (a thin colored `Border` shown/hidden via code-behind)
- Drop commit: reorder all items, send updated order to broker/DB
- After reorder: gallery reloads to reflect new order

**Sort order in queries:**
When a collection is selected and its items are retrieved, the query orders by `SortOrder`:
```csharp
items = await _context.DigitalAssetCollections
    .Where(dac => dac.CollectionId == collectionId)
    .OrderBy(dac => dac.SortOrder)
    .Select(dac => dac.Asset)
    .ToListAsync();
```

**Estimated LOC:** ~100

### T20.5 — Design Templates

**Files (all new in `.design_templates/` directory):**
- `.design_templates/README.md` — Overview of what the templates are and how to use them
- `.design_templates/design_token_reference.md` — All `{DynamicResource}` tokens mapped to light/dark values
- `.design_templates/component_style_guide.md` — Visual reference for all custom controls
- `.design_templates/layout_patterns.md` — 3-column layout, loupe mode, compare mode patterns
- `.design_templates/accent_palettes.md` — 5 preset accent color palettes with hex values

**Design Token Reference content:**
| Token | Light Value | Dark Value | Usage |
|-------|-------------|------------|-------|
| `BackgroundBase` | `#FFFFFF` | `#1E1E1E` | Main window background |
| `BackgroundElevated` | `#F5F5F5` | `#2D2D2D` | Card, panel backgrounds |
| `TextPrimary` | `#212121` | `#E0E0E0` | Body text, labels |
| `TextSecondary` | `#757575` | `#A0A0A0` | Captions, secondary info |
| `BorderSubtle` | `#E0E0E0` | `#404040` | Dividers, non-interactive borders |
| `AccentPrimary` | `#1976D2` | `#90CAF9` | Buttons, selection, links |
| ... | ... | ... | ... |

**Component Style Guide content:**
Each custom control gets a section:
- `AssetTile` — thumbnail, info overlay, selection highlight, badge positions
- `TagEditor` — tag input, suggestion dropdown, tag chip styling
- `SearchableTreeView` — text box, tree structure, filter state indicators
- `ZoomBorder` — zoom level indicator, pan bounds, cursor modes

**Layout Patterns content:**
- **Default 3-column**: sidebar (260px) | gallery (flex) | metadata (320px)
- **Loupe mode**: full-center with floating info panel
- **Compare mode**: split-center with metadata diff right panel
- **Responsive breakpoints**: minimum width 1024px, minimum height 600px

**Estimated LOC:** ~200 (5 markdown files)

### T20.6 — Tests

**Files:**
- `tests/Adam.CatalogBrowser.Tests/Services/ThemeServiceTests.cs` (new)
- `tests/Adam.CatalogBrowser.Tests/ViewModels/LoupeViewModelTests.cs` (new)
- `tests/Adam.CatalogBrowser.Tests/ViewModels/CompareViewModelTests.cs` (new)
- `tests/Adam.CatalogBrowser.Tests/ViewModels/ThemeSettingsViewModelTests.cs` (new)
- `tests/Adam.Shared.Tests/Services/ReorderServiceTests.cs` (new)

**Test coverage:**

| Test | What it covers |
|------|---------------|
| ThemeService.LoadSettings | Loads ThemeName from config and applies it |
| ThemeService.SetTheme | Toggles light/dark, updates CurrentSettings |
| ThemeService.SetAccent | Switches accent, updates CurrentSettings |
| ThemeService.PersistSettings | Saves theme/accent to AdamConfig |
| ThemeSettingsViewModel.DefaultSelection | Theme matches current system/app theme |
| ThemeSettingsViewModel.SelectTheme | Changes theme via command, preview updates |
| ThemeSettingsViewModel.SelectAccent | Changes accent via swatch click |
| LoupeViewModel.LoadAsset | Loads asset, creates filmstrip items |
| LoupeViewModel.Navigation | Next/Prev commands navigate correctly |
| LoupeViewModel.Close | Disposes current image, returns to gallery |
| LoupeViewModel.FilmstripCurrent | Current asset highlighted in filmstrip |
| LoupeViewModel.ImageError | Handles failed image load gracefully |
| CompareViewModel.SelectAssets | Two assets selected, images loaded |
| CompareViewModel.Swap | Left/right assets swap |
| CompareViewModel.SyncToggle | Sync enabled/disabled doesn't crash |
| CompareViewModel.MetadataDiff | Correct diff items generated for different assets |
| CompareViewModel.OverlayMode | Switches between side-by-side and overlay |
| ReorderService.ReorderAssets | Updates SortOrder for collection items |
| ReorderService.InsertsCorrectOrder | First item has SortOrder=0, second=1, etc. |

**Estimated LOC:** ~280 (20 tests)

## Success Criteria

- ✅ Dark theme renders correctly across all views (sidebar, gallery, metadata editor, loupe, compare)
- ✅ Accent color can be changed from dialog and applies immediately to all accent-colored elements
- ✅ Theme preference persists across app restarts in `settings.json`
- ✅ Loupe view opens from double-click or Enter on gallery asset
- ✅ Full-resolution image loads asynchronously with loading indicator
- ✅ Mouse wheel zooms centered on cursor position; click-drag pans
- ✅ ← / → keyboard navigation advances through gallery assets in loupe
- ✅ Esc closes loupe and returns to gallery at same scroll position
- ✅ Compare view shows two assets side-by-side with synchronized pan/zoom
- ✅ Overlay/swipe mode allows drag-reveal comparison
- ✅ Metadata diff table highlights differences between the two assets
- ✅ Drag-reorder in collection gallery updates SortOrder in DB
- ✅ Insertion line visual feedback during drag-reorder
- ✅ `.design_templates/` directory exists with all 5 reference documents
- ✅ All existing tests still pass
- ✅ 20+ new tests pass

## Execution Order (Waves)

```
Wave 1 ─── T20.1 Theme Engine ─────────────────── independent
Wave 2 ─── T20.2 Loupe View ───────────────────── independent (parallel with Wave 1)
Wave 3 ─── T20.3 Compare View ─────────────────── depends on T20.2 (reuses ZoomBorder)
Wave 4 ─── T20.4 Drag-Reorder ─────────────────── independent (parallel with Wave 2)
Wave 5 ─── T20.5 Design Templates ─────────────── independent (parallel with Wave 2-4)
Wave 6 ─── T20.6 Tests ────────────────────────── after all code
Wave 7 ─── Full test suite, code review, plan status update
```

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Memory pressure from full-resolution images in loupe | Medium | Medium | Dispose image on navigate; cancel pending loads; limit filmstrip to 40 visible thumbnails |
| Dark theme misses some controls | Low | Medium | Systematic review of all View/Window axaml files; test with a checklist |
| Compare view sync infinite loop | Low | High | Guard via `_isUpdating` boolean; unit test the sync state machine |
| Drag-reorder conflicts with existing gallery drag-drop | Low | Medium | Only enable reorder drag when a specific collection is active; disable in search/all mode |
| Avalonia DragDrop API differences in headless test | Medium | Low | Test reorder via direct handler invocation; integration test via Playwright or manual |

## Files Created

| # | File | Task |
|---|------|------|
| 1 | `.design_templates/README.md` | T20.5 |
| 2 | `.design_templates/design_token_reference.md` | T20.5 |
| 3 | `.design_templates/component_style_guide.md` | T20.5 |
| 4 | `.design_templates/layout_patterns.md` | T20.5 |
| 5 | `.design_templates/accent_palettes.md` | T20.5 |
| 6 | `src/Adam.CatalogBrowser/Services/ThemeService.cs` | T20.1 |
| 7 | `src/Adam.CatalogBrowser/ViewModels/ThemeSettingsViewModel.cs` | T20.1 |
| 8 | `src/Adam.CatalogBrowser/Views/ThemeSettingsDialog.axaml` | T20.1 |
| 9 | `src/Adam.CatalogBrowser/Views/ThemeSettingsDialog.axaml.cs` | T20.1 |
| 10 | `src/Adam.CatalogBrowser/Controls/Themes/ColorResources.Dark.axaml` | T20.1 |
| 11 | `src/Adam.CatalogBrowser/Controls/Themes/AccentPalettes.axaml` | T20.1 |
| 12 | `src/Adam.CatalogBrowser/Controls/Themes/AccentPalettes.cs` | T20.1 |
| 13 | `src/Adam.CatalogBrowser/Controls/ZoomBorder.cs` | T20.2 |
| 14 | `src/Adam.CatalogBrowser/ViewModels/LoupeViewModel.cs` | T20.2 |
| 15 | `src/Adam.CatalogBrowser/Views/LoupeView.axaml` | T20.2 |
| 16 | `src/Adam.CatalogBrowser/Views/LoupeView.axaml.cs` | T20.2 |
| 17 | `src/Adam.CatalogBrowser/ViewModels/CompareViewModel.cs` | T20.3 |
| 18 | `src/Adam.CatalogBrowser/Views/CompareView.axaml` | T20.3 |
| 19 | `src/Adam.CatalogBrowser/Views/CompareView.axaml.cs` | T20.3 |
| 20 | `tests/Adam.CatalogBrowser.Tests/Services/ThemeServiceTests.cs` | T20.6 |
| 21 | `tests/Adam.CatalogBrowser.Tests/ViewModels/LoupeViewModelTests.cs` | T20.6 |
| 22 | `tests/Adam.CatalogBrowser.Tests/ViewModels/CompareViewModelTests.cs` | T20.6 |
| 23 | `tests/Adam.CatalogBrowser.Tests/ViewModels/ThemeSettingsViewModelTests.cs` | T20.6 |
| 24 | `tests/Adam.Shared.Tests/Services/ReorderServiceTests.cs` | T20.6 |

## Files Modified

| # | File | Change |
|---|------|--------|
| 1 | `src/Adam.Shared/Data/AppDbContext.cs` | Add SortOrder to DigitalAssetCollection config |
| 2 | `src/Adam.Shared/Contracts/CollectionMessages.cs` | Add ReorderAssets message and smart fields |
| 3 | `src/Adam.BrokerService/Handlers/CollectionHandler.cs` | Add ReorderAssets handler |
| 4 | `src/Adam.CatalogBrowser/ViewModels/AssetGalleryViewModel.cs` | Add reorder command, compare selection, loupe open |
| 5 | `src/Adam.CatalogBrowser/ViewModels/MainWindowViewModel.cs` | Add view mode switching, theme/open theme commands |
| 6 | `src/Adam.CatalogBrowser/Views/MainWindow.axaml` | Add LoupeView/CompareView DataTemplates to ContentControl |
| 7 | `src/Adam.CatalogBrowser/Views/AssetGalleryView.axaml` | Add DragDrop attached events, Compare button |
| 8 | `src/Adam.CatalogBrowser/App.axaml` | Load dark resources conditionally |
| 9 | `src/Adam.CatalogBrowser/App.axaml.cs` | DI registration for ThemeService |
| 10 | `src/Adam.Shared/Models/AdamConfig.cs` or settings | Add ThemeName, AccentName, LastThemeVariant |

## New NuGet Dependencies

- **None** — all features use existing Avalonia APIs (`RenderTransform`, `DragDrop`, `MergedDictionaries`, `RequestedThemeVariant`) and existing `ImageSharp`/`SkiaSharp` dependencies for image loading
