# Layout Patterns

## Default 3-Column Layout

The main window uses a resizable 3-column layout for browsing and editing.

```
┌─ Title Bar (connection, mode, theme) ──────────────────────────┐
│ Logo  Gallery  Ingest  Metadata  Audit  Activity  [Theme ▼]    │
├──────────┬─────────────────────────────┬────────────────────────┤
│ Sidebar  │      Gallery                │  Right Panel           │
│ 260px    │      (flex)                 │  320px                 │
│          │                             │                        │
│ Folders  │  ┌──┐ ┌──┐ ┌──┐ ┌──┐      │ Metadata               │
│ ───────  │  │  │ │  │ │  │ │  │      │ ────────               │
│Colls     │  │  │ │  │ │  │ │  │      │ Name: sunset.jpg       │
│ ───────  │  └──┘ └──┘ └──┘ └──┘      │ Type: JPEG             │
│Keywords  │                             │ Size: 2.4 MB          │
│ ───────  │  [Toolbar: View ▼ |       │                        │
│Date      │   🔍 Search...  🤖 AI Tag]  │ Tags                   │
│ ───────  │                             │ ────────               │
│Saved     │  [Selection bar: ★ Rate    │ [Tags editor]          │
│Searches  │   🏷 Label ⚑ Flag ✕ Clear] │                        │
│ ───────  │                             │ Comments               │
│Recent    │                             │ ────────               │
│          │                             │ [Comment panel]        │
├──────────┴─────────────────────────────┴────────────────────────┤
│ Status: 42 of 142 assets    Local DB    Standalone              │
└─────────────────────────────────────────────────────────────────┘
```

**Grid splitters:** Both borders between columns are draggable (`GridSplitter`, 4px width).
**Minimum widths:** Sidebar 180px, right panel 180px.
**Minimum window:** 1024×600px.

## Loupe Mode

Full-window image viewing with filmstrip navigation.

```
┌─────────────────────────────────────────────────────┐
│ ← sunset.jpg   [📷 Info]  [⊞ Fit]  [+] [-]  [✕]  │  ← Toolbar
├─────────────────────────────────────────────────────┤
│                                                     │
│                                                     │
│               [Full-resolution image]               │
│          ↕ pan: click+drag   🔍 zoom: wheel         │
│                                                     │
│                                                     │
│                                                     │
├─────────────────────────────────────────────────────┤
│  [🖼] [🖼] [🖼] [🖼] [🖼] [🖼] [🖼] [🖼] [🖼] [🖼] │  ← Filmstrip
└─────────────────────────────────────────────────────┘

Info overlay (toggled with I key, shown top-left):
┌─────────────────┐
│ sunset.jpg      │
│ 6000×4000       │
│ 12.4 MB         │
│ ★★★★            │
│ Canon R5        │
│ 24-70mm f/2.8   │
│ ISO 100         │
│ 1/250s f/8      │
└─────────────────┘
```

**Keyboard shortcuts:** ← → (prev/next), + - (zoom), Ctrl+0 (fit), I (info), F (fit/fill toggle), Esc (close).

## Compare Mode

Side-by-side asset comparison with synchronized zoom/pan.

```
┌─ Compare ──────────────────────────── [🔄 Swap] [🔗] [⊞] [✕] ─┐
│                                                                    │
│  ┌────────────────────┐  ┌────────────────────┐                   │
│  │                    │  │                    │                   │
│  │   Asset A          │  │   Asset B          │                   │
│  │   (ZoomBorder)     │  │   (ZoomBorder)     │                   │
│  │   Sync: ON         │  │   Sync: ON         │                   │
│  │                    │  │                    │                   │
│  └────────────────────┘  └────────────────────┘                   │
│                                                                    │
├────────────────────────────────────────────────────────────────────┤
│  Metadata Diff                                                     │
│  ┌───────────────────────────────────────────────────────────┐    │
│  │ Name:  "Sunset.jpg"       │ "Dawn.jpg"              ✗    │    │
│  │ Rating: ★★★★              │ ★★★★                    ✓    │    │
│  │ Tags:  beach, sunset      │ ocean, dawn             ✗    │    │
│  │ Camera: Canon R5          │ Canon R5                ✓    │    │
│  │ Lens:  24-70mm            │ 24-70mm                 ✓    │    │
│  │ ISO:   100                │ 200                     ✗    │    │
│  │ Aperture: f/2.8           │ f/4                     ✗    │    │
│  └───────────────────────────────────────────────────────────┘    │
└────────────────────────────────────────────────────────────────────┘
```

**Controls:** Swap left/right assets, toggle sync on/off, toggle overlay/swipe mode.
**Overlay mode:** Right image overlaid on left with opacity slider or drag-reveal swipe.

## Responsive Behavior

The app targets a minimum window size of 1024×600px. Below those dimensions, elements begin to clip rather than reflow — the app is designed for desktop use only.

| Resolution | Behavior |
|-----------|----------|
| ≥ 1400×900 | Full 3-column with comfortable spacing (recommended) |
| 1200×768 | All 3 columns visible, narrower sidebar/right panel |
| 1024×600 | Minimum viable — panels at min-width, scroll as needed |
