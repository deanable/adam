# Phase 8 — UI Review

**Audited:** 2026-06-10
**Baseline:** Abstract 6-pillar standards (no UI-SPEC.md exists). Cross-referenced against `.planning/ROADMAP.md`, `.planning/STATE.md`, and `.planning/phases/08-v1-0-polish-ship/08-PLAN.md`.
**Screenshots:** Not captured — this is an Avalonia 12 desktop app (XAML/MVVM), not a web app. No dev server applies. Audit is code-grounded against `.axaml` views/styles and their ViewModels.
**Scope note:** Phase 8 PLAN.md has **three work streams — Performance, Documentation, Packaging — and zero UI work stream.** The user's stated concern (interaction incompleteness) is currently unrepresented in Phase 8 and must be folded in.

---

## Pillar Scores

| Pillar | Score | Key Finding |
|--------|-------|-------------|
| 1. Copywriting | 3/4 | Mostly clear, purposeful labels; weak/duplicated empty states and a few generic strings ("Loading...", bare "Clear"). |
| 2. Visuals | 2/4 | Functional 3-column DAM layout, but tiles render no rating/flag/color/action affordances despite the control supporting them; icon-glyph buttons (↻ ⇄ 🤖) carry no tooltips. |
| 3. Color | 2/4 | ~60+ hardcoded hex literals across every view; no shared resource palette; blue accent (#1976D2/#005A9E) competes with green/purple/orange CTAs — accent discipline is weak. |
| 4. Typography | 2/4 | 9+ distinct font sizes (10/11/12/13/14/16/18/20) with hardcoded pixel values and no type scale; weights are reasonable. |
| 5. Spacing | 3/4 | Generally consistent 4/8/12/16 rhythm via `Spacing`/`Margin`, but values are inline literals with no tokens and some ad-hoc paddings. |
| 6. Experience Design | 1/4 | **BLOCKER.** Zero context menus/flyouts app-wide; implemented commands (delete, export, rotate, AI-tag, rating/label/flag) have no right-click or keyboard path; `DeleteService` fully built + DI-registered + tested but wired to **no** UI; sidebar trees have no rename/new/delete. |

**Overall: 13/24**

---

## Top Priority Fixes (ordered)

1. **No context menus anywhere (BLOCKER).** Verified: `grep` for `ContextMenu|ContextFlyout|MenuFlyout|MenuItem` returns **zero** matches across all `.axaml` and `.cs`. Add `ContextFlyout` to gallery items (`AssetGalleryView.axaml:113-126`, `:165-189`) exposing Rate / Label / Flag / Tag / AI-Tag / Export / Reveal-in-folder / Copy path / Add-to-collection / Delete, and to sidebar tree nodes (`MainWindow.axaml:217-292`, `SearchableTreeView.axaml:25-37`) exposing New / Rename / Delete / Filter. This single change closes most of the perceived "incompleteness."

2. **Delete is fully implemented but unreachable (BLOCKER).** `DeleteService` (`Services/DeleteService.cs`) exposes `SoftDeleteAsync`/`RestoreAsync`/`PermanentlyDeleteAsync`, is registered at `App.axaml.cs:77`, and has tests — yet **no ViewModel references it** and no view binds a delete command. Users cannot remove an asset at all. Add a `DeleteSelectedCommand` (+ confirmation dialog + `Delete` key binding + context-menu entry) and a "Deleted/Trash" view to surface `GetDeletedAssetsAsync`/`RestoreAsync`.

3. **Gallery tiles render dead capability (WARNING→BLOCKER for perceived completeness).** `AssetTileControl` exposes `Rating`, `ColorLabel`, `ColorBrush`, `IsFlagged`, and `ToolbarActions` (`Controls/AssetTileControl.cs`) and the template draws all of them (`AssetTileControlStyles.axaml:43-122`), but the gallery instantiates the tile binding **only** Thumbnail + 3 text fields (`AssetGalleryView.axaml:115-124`). Every tile shows an empty rating slot, an empty color swatch, a disabled unchecked flag box, and no toolbar buttons. Bind these properties (and populate `ToolbarActions`) so inline rate/flag/label affordances appear on hover.

4. **Almost no keyboard model.** Only two key bindings exist and both map to Save (`MainWindow.axaml:41-42`). No `Delete`, no `F2` rename, no `Ctrl+A`/`Ctrl+C`, no `Ctrl+E` export, no rating digit keys (`0–5`), no flag/reject (`P`/`X`) — all standard DAM accelerators. Icon-only buttons (`↻ 90°`, `↺ 90°`, `⇄`, `⇅` at `MainWindow.axaml:481-484`; `🤖` AI-tag) also lack tooltips, so their meaning is non-discoverable.

5. **No confirmations, no toasts/notifications.** No `MessageBox`/confirmation dialog exists anywhere (`grep` confirms). Destructive or bulk actions (delete, AI-tag N assets, export) complete or would complete silently with only terse status-bar text (`MainWindow.axaml:582`). Add a reusable confirmation dialog for destructive actions and a transient toast/notification surface for success/failure feedback.

---

## Missing Interaction Elements — Concrete Phase-8 Work Items

These are implementable, command-grounded items. Many target commands that **already exist** in a ViewModel but have no affordance.

### A. Gallery asset context menu (`ContextFlyout` on `ListBoxItem` in `AssetGalleryView.axaml`)
| Item | Backing command status |
|------|------------------------|
| Rate (0–5) | Exists for single asset via `PropertyInspector.SelectedAssetRating` combo; **no multi-select command**, no per-tile path |
| Set color label | Exists via right-panel combo only |
| Set flag (Pick/Reject) | Exists via right-panel combo only |
| Add/edit keywords & categories | Exists via right-panel `TagEditorControl` only |
| AI-tag selected | `AiTagSelectedCommand` exists (`MainWindowViewModel.cs:68`) — toolbar button only |
| Export… | `ExportCommand` exists (`MainWindowViewModel.cs:115`) — right-panel button only |
| Rotate / Flip | `RotateClockwise/CCW`, `FlipHorizontal/Vertical` exist (`MainWindowViewModel.cs:117-120`) — right-panel only |
| **Delete / move to trash** | `DeleteService` exists but **no command at all** — must be created |
| **Reveal in folder / Open** | **No command exists** — `StoragePath` is available on `AssetListItem`; add `Process.Start` reveal |
| **Copy file path / Copy file** | **No command exists** — no clipboard usage anywhere |
| **Add to collection** | **No command exists** — collections are read-only in the UI |

### B. Sidebar tree context menus (`MainWindow.axaml` Folders/Collections/Keywords/Categories; `SearchableTreeView.axaml`)
- New / Rename / Delete for **Collections, Keywords, Categories** — **none of these commands exist** in `SidebarViewModel` (it is purely read/filter). Full CRUD commands + inline rename (`TextBox` swap or rename dialog) need to be added.
- "Filter by this" / "Clear filter" explicit entries (currently filter is implicit on selection only).
- Folders: "Reveal in Explorer", "Re-scan folder".

### C. Keyboard accelerators (`Window.KeyBindings`, currently only Save×2)
- `Delete` → delete selected; `F2` → rename (sidebar node / asset title); `Ctrl+A` → select all; `Ctrl+C` → copy path; `Ctrl+E` → export; `Enter` → open detail; digits `0–5` → rating; `P`/`X` → pick/reject flag; `Ctrl+F` → focus search.

### D. Hover / discoverability affordances
- Tooltips on icon-only buttons: rotate/flip glyphs (`MainWindow.axaml:481-484`), AI-tag emoji button.
- Hover-reveal action overlay on gallery tiles (wire `ToolbarActions`).
- Drag-and-drop is implemented for keyword/category assignment (`AssignKeywordDropCommand`/`AssignCategoryDropCommand`) but has **no visual hint** that tree nodes are drop targets and no drop-hover highlight discoverability.

### E. Multi-select & bulk actions
- `SelectionMode="Multiple"` is set (`AssetGalleryView.axaml:88,135`) and `SelectedAssets` exists, but only AI-tag and Export consume it. No bulk rate/label/flag/delete/keyword UI beyond the aggregated tag editor.

### F. Feedback surfaces
- Confirmation dialog component (destructive actions). None exists.
- Toast/notification component for async success/failure (AI-tag, export, ingest, delete). Currently only status-bar text.

---

## Detailed Findings

### Pillar 1: Copywriting (3/4)
Labels are generally specific and task-oriented: "AI Tag Selected", "Start Ingestion", "Server Admin", and good permission tooltips ("Sign in to ingest assets", "Session expired — re-login required…", `MainWindowViewModel.cs:673-727`). Empty states read well in the gallery ("No assets found" / "Select a root folder to scan…", `AssetGalleryView.axaml:223-230`).

Weaknesses:
- **Duplicated / inconsistent empty copy** for the same concept: "No selection" (`MainWindow.axaml:346`), "Select an asset to edit its metadata" (`MainWindow.axaml:555`), and again "Select an asset to edit its metadata" (`MetadataEditorView.axaml:15`). Pick one voice.
- **Generic single-word buttons**: "Clear" (`IngestionView.axaml:72`, `AuditLogView.axaml:56`), bare "Refresh", "Save" vs "Save Changes" vs "Save" inconsistency (`MetadataEditorView.axaml:55` vs `MainWindow.axaml:511`).
- **Generic progress strings**: "Loading...", "Loading more...", "Loading assets..." are acceptable but undifferentiated; no error-state copy exists in views (errors only go to logs / status bar).
- No destructive-action confirmation copy exists because no confirmations exist (see Pillar 6).

### Pillar 2: Visuals (2/4)
The three-pane DAM layout (sidebar / gallery / inspector with `GridSplitter`s, `MainWindow.axaml:209-323`) is a sensible, conventional structure with a clear center focal area. Loading overlays and a custom `LoadingSpinner` give reasonable visual feedback.

Weaknesses:
- **Tiles advertise affordances that never render.** The tile template paints a rating value, color swatch, color label, a (disabled) flag checkbox, and a toolbar action row (`AssetTileControlStyles.axaml:43-122`), but the gallery binds none of them (`AssetGalleryView.axaml:115-124`). Result: every tile shows a permanently empty/disabled metadata row — visually noisy and signals "broken/incomplete."
- **Icon-only controls without labels/tooltips**: `↻ 90° ↺ 90° ⇄ ⇅` (`MainWindow.axaml:481-484`) and `🤖 AI Tag Selected` rely on glyph recognition; the rotate/flip set has no tooltip.
- **Visual hierarchy is flat** in the right inspector: ~10 stacked label/combo rows at identical 10–11px with no grouping weight, making the primary actions (Save/Export) hard to locate.
- No focus-visual customization; relies on Fluent defaults (acceptable but unverified for keyboard users).

### Pillar 3: Color (2/4)
There is a recognizable brand blue (`#005A9E` title bar, `#1976D2` primary actions/selection accents) and a semantic mode-color system (green local / blue service / status dot). Selection feedback in lists is well-considered (`#E3EDFA` selected, `#EFF2F7` hover, left accent bar, `AssetGalleryView.axaml:93-159`).

Weaknesses:
- **No shared color resources.** `App.axaml` defines only theme includes (`App.axaml:4-9`); there is **no `ResourceDictionary` palette**. Every color is a hardcoded hex literal repeated across views — 60+ occurrences (e.g. `#1976D2` alone appears in MainWindow, AssetGalleryView, MetadataEditorView, AuditLogView, ExportDialog, LoginDialog). Theming/dark-mode is effectively impossible without a refactor.
- **Accent overuse / competing accents.** Primary blue competes with green CTAs (`#388E3C` Connect/Export, `#2E7D32`), purple (`#7B1FA2` AI-tag), and orange/red (`#E65100` Logout, `#D32F2F` Disconnect). The 10% accent slot is spread across 4+ hues, diluting the 60/30/10 discipline.
- Hardcoded grays (`#333/#555/#666/#888/#999/#AAA/#BBB/#CCC`) proliferate with no neutral ramp tokens.

### Pillar 4: Typography (2/4)
Weights are restrained and sensible (Normal/Medium/SemiBold/Bold). Monospace is correctly used for the operation log (`AuditLogView.axaml:64`).

Weaknesses:
- **Too many sizes, no scale.** Distinct `FontSize` values in use: 10, 11, 12, 13, 14, 16, 18, 20 (e.g. `MainWindow.axaml` mixes 10/11/12/16; dialog headers 18; view titles 20). That is 8+ steps with no defined type scale and all as inline literals — exceeds the ≤4-size guideline substantially.
- Heavy reliance on **10–11px text** for primary metadata in the inspector and tiles, which is small for a desktop content app and hurts readability.
- No shared `TextBlock` style classes; every size is repeated per element.

### Pillar 5: Spacing (3/4)
Spacing is the strongest visual pillar: consistent use of `Spacing="4/6/8/12/16"` and matching margins/paddings produces a coherent rhythm (e.g. `IngestionView.axaml`, `LoginDialog.axaml`, `ExportDialog.axaml`). `ColumnSpacing`/`RowSpacing` used appropriately in grids.

Weaknesses:
- All values are **inline literals** with no spacing tokens/resources, so consistency is by convention only and easy to drift.
- A few ad-hoc paddings (`Padding="8,4"`, `"6,4"`, `"4,2"`, `"10,2"`, `"12,4"`, `"16,6"`, `"20,8"`) introduce many one-off pairs rather than a small padding set.
- Minor: status bar / empty-state both sit in `Grid.Row="2"` overlapping concerns in `AssetGalleryView.axaml:216-237` (two borders in the same row).

### Pillar 6: Experience Design (1/4)
This is the failing pillar and the core of the user's concern.

State coverage is partially good: loading overlays exist (startup, gallery, inspector, metadata editor, audit), a "load more" indicator, progress bars for ingest/export/AI-tag, and a gallery empty state. Permission/disabled states are thoughtfully handled with tooltips (Phase 7 RBAC gating).

But interaction completeness is severely lacking:
- **Context menus: zero.** `grep ContextMenu|ContextFlyout|MenuFlyout|MenuItem` → no matches in any `.axaml` or `.cs`. Right-click does nothing anywhere in the app. (BLOCKER)
- **Delete unreachable.** `DeleteService` is implemented, DI-registered (`App.axaml.cs:77`), and unit-tested, but referenced by **no** ViewModel and **no** view. There is no way to delete, trash, or restore an asset in the running UI. (BLOCKER)
- **Command-to-affordance gaps.** Export, Rotate/Flip, and AI-tag exist only as single buttons in the right panel or toolbar — not on the items they act upon, not on right-click, not on keyboard.
- **Sidebar is read-only.** No create/rename/delete for collections, keywords, or categories — those commands do not exist in `SidebarViewModel`.
- **No keyboard model.** Only `Ctrl+S`/`Ctrl+Shift+S` (both Save). No Delete/F2/Ctrl+A/Ctrl+C/rating keys.
- **No confirmations and no notifications.** No confirmation dialog or toast component exists; destructive/bulk operations would complete silently except for status-bar text.
- **Drag-drop is undiscoverable.** Keyword/category drop assignment works (`AssignKeywordDropCommand`) but nothing signals tree nodes are drop targets.
- **Tile affordances dead** (see Pillar 2/3 fix) — inline rate/flag/label is impossible despite control support.

Registry audit: `components.json` not present (not a shadcn project) — registry safety section skipped.

---

## Files Audited
- `src/Adam.CatalogBrowser/App.axaml`, `App.axaml.cs`
- `src/Adam.CatalogBrowser/Views/MainWindow.axaml`
- `src/Adam.CatalogBrowser/Views/AssetGalleryView.axaml`
- `src/Adam.CatalogBrowser/Views/MetadataEditorView.axaml`
- `src/Adam.CatalogBrowser/Views/AssetDetailView.axaml`
- `src/Adam.CatalogBrowser/Views/IngestionView.axaml`
- `src/Adam.CatalogBrowser/Views/AuditLogView.axaml`
- `src/Adam.CatalogBrowser/Views/ExportDialog.axaml`
- `src/Adam.CatalogBrowser/Views/LoginDialog.axaml`
- `src/Adam.CatalogBrowser/Controls/SearchableTreeView.axaml`
- `src/Adam.CatalogBrowser/Controls/AssetTileControl.cs`
- `src/Adam.CatalogBrowser/Controls/Themes/AssetTileControlStyles.axaml`
- `src/Adam.CatalogBrowser/Controls/Themes/AssetListRowControlStyles.axaml`
- `src/Adam.CatalogBrowser/ViewModels/MainWindowViewModel.cs`
- `src/Adam.CatalogBrowser/ViewModels/AssetGalleryViewModel.cs`
- `src/Adam.CatalogBrowser/ViewModels/SidebarViewModel.cs`
- `src/Adam.CatalogBrowser/Services/DeleteService.cs`
- `.planning/phases/08-v1-0-polish-ship/08-PLAN.md` (baseline / scope cross-reference)
- Grep sweeps: `ContextMenu|ContextFlyout|MenuFlyout|MenuItem` (zero), `KeyBinding|Gesture|ToolTip.Tip`, `DeleteService|Clipboard|MessageBox|Reveal`.
