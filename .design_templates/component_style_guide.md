# Component Style Guide

## AssetTile (`AssetTileControl`)

Custom templated control for gallery grid items.

```
┌─────────────────────┐
│ ★  ⚑               │  ← Toolbar row (collapsed, shows on hover)
│                     │
│     [Thumbnail]     │  ← 150×150 default, resizable 80-300px
│                     │
│   Name: sunset.jpg  │  ← TextField1 (file name)
│   Type: image/jpeg  │  ← TextField2 (MIME type)
│   Size: 2.4 MB      │  ← TextField3 (formatted size)
│ ★★★★  Red           │  ← Rating stars + color label swatch
└─────────────────────┘
```

**States:** Normal, Hover (`SelectionHoverBrush`), Selected (`SelectionBrush`), Search Highlighted (blue border)

## AssetListRow (`AssetListRowControl`)

Row-style control for gallery list view.

```
┌─────────────────────────────────────────────────────────┐
│ [🖼] │ sunset.jpg     │ image/jpeg  │ 2.4 MB  │ 1920x1080 │
├─────────────────────────────────────────────────────────┤
│              Matched: Title, Keywords                    │  ← search badge
└─────────────────────────────────────────────────────────┘
```

**States:** Normal, Hover, Selected (left accent bar with `PrimaryBrush`)

## TagEditor (`TagEditorControl`)

Keyword/category tag input with autocomplete.

```
[ beach  ✕ ] [ sunset  ✕ ] [ ocean  ✕ ] [ + Add tag...  ▼ ]
                                              │
                                    ┌─────────┴──────────┐
                                    │ sunrise             │
                                    │ golden hour         │
                                    │ horizon             │
                                    └─────────────────────┘
```

**Behavior:** Type to filter suggestions, Enter/Tab to add, ✕ to remove, autocomplete on 2+ characters.

## SearchableTreeView

TreeView with an inline filter text box for keyword/category hierarchies.

```
[ Filter...        ✕ ]
▼ Keywords
 ├─ Nature (12)
 │  ├─ Animals (5)
 │  ├─ Plants (3)
 │  └─ Landscapes (4)
 └─ People (8)
    ├─ Portraits (3)
    └─ Groups (5)
```

## LoadingSpinner

Three-dot animated pulse spinner. Used in gallery overlay, property inspector, and load-more.

```
● ● ●   (pulsing with 400ms stagger)
```

**Properties:** `DotSize` (default 8px), `IsActive`, `Foreground` (color)

## ZoomBorder

Pan/zoom container control for full-resolution image viewing.

```
┌──────────────────────────────────────────────────┐
│                                                  │
│              [Image content]                      │
│         ↕ pan: click+drag                         │
│         🔍 zoom: mouse wheel                     │
│         ⊞ fit: double-click                      │
│                                                  │
│     Zoom: 150%   [⊟ Fit]                         │
└──────────────────────────────────────────────────┘
```

**Properties:** `ZoomLevel` (0.1-20.0), `PanOffset`, `IsPanEnabled`, `IsZoomEnabled`
**Events:** `ZoomChanged`, `PanChanged` (for compare view sync)

## Toast Notification

Floating notification that auto-dismisses after 3 seconds.

```
┌─────────────────────────────────┐
│ ✓ Changes saved successfully   │  ← success (green)
└─────────────────────────────────┘

┌─────────────────────────────────┐
│ ⚠ Failed to load assets        │  ← warning (amber)
└─────────────────────────────────┘

┌─────────────────────────────────┐
│ ✗ Delete operation failed      │  ← error (red)
└─────────────────────────────────┘
```

**Levels:** `Success`, `Info`, `Warning`, `Error`
**Position:** Bottom-right, stacked
**Duration:** 3 seconds (auto-dismiss)
