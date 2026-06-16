# 20-UI-REVIEW — Phase 20: UX Modernization

**Date:** 2026-06-17
**Scope:** Adam.CatalogBrowser — all views (MainWindow, AssetGallery, Loupe, Compare, MetadataEditor, Ingestion, ActivityFeed, CommentPanel, Trash, etc.)
**Design reference:** `.design_templates/`, `DESIGN.md`, `ColorResources.axaml`, `TextBlockStyles.axaml`

---

## Scoring Key

| Score | Meaning |
|-------|---------|
| 1 | Missing or significantly deficient |
| 2 | Partial — some elements present but inconsistent |
| 3 | Good — most aspects covered with minor gaps |
| 4 | Excellent — thorough, consistent, production-quality |

---

## Pillar 1: Visual Consistency ★★★★ (4)

The application has a well-defined design token system that is consistently applied across all views.

**Strengths:**
- **Semantic brush system:** 50+ `{DynamicResource}` tokens (Primary, Surface, Text, Border, Selection, etc.) are defined in `ColorResources.axaml` and used uniformly across 19+ view files. Hard-coded color values (`#F0F0F0`, `#D0D0D0`) are rare and limited to trivial controls (rotate/flip buttons in right panel).
- **Typographic scale:** 4 TextBlock style classes (`.Title` 18px, `.Subheading` 14px, `.Body` 12px, `.Caption` 10px) are defined in `TextBlockStyles.axaml` and consistently applied — no ad-hoc font sizes in most views.
- **Component patterns:** AssetTile, AssetListRow, TagEditor, SearchableTreeView, ZoomBorder, LoadingSpinner, Toast notifications all have consistent structure, styling, and behavior.
- **Button language:** Primary, Danger, Warning, and ghost button styles are consistent — buttons use `Cursor="Hand"`, `BorderThickness="0"` for solid buttons, `CornerRadius="4"` for most, hover/pressed styles via inline `<Style>` selectors.
- **Spacing rhythm:** 4px/8px/12px/16px spacing is consistent — `Margin="8"`, `Padding="8,4"`, `Spacing="4"` appear uniformly.

**Issues:**
- **Rotate/Flip buttons (MainWindow.axaml ~line 470):** Use hard-coded `Background="#F0F0F0"` and `BorderBrush="#D0D0D0"` instead of semantic tokens. Minor but inconsistent with the rest of the system.
- **TrashView accent colors:** References `WarningHoverBackgroundBrush` and `WarningBorderBrush` — some of these may not be defined in the core palette (need verification vs `ColorResources.axaml`).
- **Loupe/Compare views:** Use hardcoded dark theme colors (`#1A1A1A`, `#333`, `#555`, `#AAA`) instead of the dynamic theme system. These work visually but bypass the `DesignThemeService` entirely.

**Recommendation:** Hard-coded colors in Rotate/Flip buttons, TrashView, LoupeView, and CompareView should be migrated to semantic tokens or theme-aware resources.

---

## Pillar 2: Layout & Hierarchy ★★★★ (4)

The 3-column layout and view-switching architecture are well-structured for a desktop DAM application.

**Strengths:**
- **3-column window:** Sidebar (260px) | Gallery (flex) | Right Panel (320px) with draggable `GridSplitter` controls (4px) and minimum column widths (180px). This matches the design template exactly.
- **MainWindow.DataTemplates:** 8 view templates mapped to ViewModel types via `DataTemplate` — clean separation, no monolithic XAML.
- **Content navigation:** Title bar buttons (Gallery, Ingest, Metadata, Audit, Activity, Server Admin) with permission gating (`IsEnabled`, `ToolTip.Tip`) — clear hierarchy of primary actions.
- **Loupe and Compare modes:** Both use dedicated full-window views with toolbar + content + filmstrip/metadata-diff bars — appropriate full-screen immersion.
- **Selection bar (AssetGallery):** Contextual bar appears on multi-select with Rate/Label/Flag/Compare/Clear actions — good progressive disclosure.
- **Right panel expanders:** Metadata, Comments, Tags sections are collapsible `Expander` controls — good space management for dense metadata editing.
- **Window default size:** 1400×900 — appropriate for the 3-column layout. Minimum seems to be 1024×600 (unconfirmed in code but documented in design templates).

**Issues:**
- **Responsive behavior:** No responsive breakpoints or adaptive layout. The app clips below 1024px. Acceptable for desktop-only, but noted.
- **Empty states:** Most views have empty states (gallery, activity feed, comments, trash), but some are inconsistent — `AssetDetailView` and `AuditLogView` may skip this.
- **Sidebar density:** The sidebar has 12 expander sections (Folders, Collections, Saved Searches, Recent Searches, Keywords, Media Format, Categories, Date Taken, Rating, Label, Flag, AI Model) — this is a lot for a 260px column. The AI Model section alone takes significant vertical space and may not be a primary sidebar concern.

**Recommendation:** Consider collapsing less-frequently-used sidebar sections by default (Rating, Label, Flag filters) to reduce scroll fatigue. The AI Model section should arguably be a Settings dialog, not a sidebar section.

---

## Pillar 3: Typography ★★★☆ (3)

A 4-class type scale exists and is mostly consistent, but has gaps.

**Strengths:**
- **4-class system:** `.Title` (18px Bold), `.Subheading` (14px SemiBold), `.Body` (12px Normal), `.Caption` (10px Normal) — clean, minimal, and consistently used across all views.
- **Font family:** Uses platform default (Segoe UI on Windows) — appropriate for a desktop app that should feel native.
- **Weight usage:** Bold for titles, SemiBold for subheadings, Medium for emphasis within captions. Good hierarchy.

**Issues:**
- **No letter-spacing or line-height control:** The type scale only sets `FontSize`. No `LineHeight`, `LetterSpacing`, or `FontFamily` overrides are applied, unlike the detailed DESIGN.md reference (which specifies SF Pro Display at 56px with negative tracking). The application's typographic refinement is minimal.
- **Body font size (12px) is small:** For a desktop app with high-resolution displays, 12px body text can be hard to read. The DESIGN.md uses 17px for body text. The application uses roughly 71% of that size.
- **Title font size (18px) is modest:** Window titles use 18px Bold, which is appropriate but not dramatic. The DESIGN.md reference goes up to 56px for hero headlines — not directly applicable to a DAM UI, but 18px feels modest for primary window titles.
- **No font-weight variation in the type scale:** All classes use Bold or SemiBold for headings, Normal for body. There's no Light (300) or Medium (500) weight usage.

**Recommendation:** Add `LineHeight` to the type scale classes for better readability. Consider bumping `.Body` from 12px to 13px for a more comfortable reading experience.

---

## Pillar 4: Color & Contrast ★★★☆ (3)

The semantic color palette is comprehensive, but dark mode is incomplete and some contrast values are borderline.

**Strengths:**
- **Semantic palette:** 50+ color tokens with clear naming and distinct roles (Primary, Danger, Warning, Success, AiTag, Admin, Text, Surface, Border, Selection, Overlay). Well-organized.
- **Primary blue (#1976D2):** Good accessibility contrast against white text (4.6:1+). Large text and interactive elements pass WCAG AA.
- **Semantic color usage:** Success (green) for confirm/restore, Danger (red) for delete, Warning (orange/amber) for caution, AiTag (purple) for AI-related actions — color carries meaning consistently.
- **Text hierarchy:** 6 text color tokens (Primary → Secondary → Muted → Tertiary → Placeholder → Disabled) provide good visual hierarchy with diminishing contrast.

**Issues:**
- **No dark mode:** The `DesignThemeService` and `.design/*.md` system is built for dynamic theme switching, but no dark theme files exist in the `.design/` directory. The `ColorResources.axaml` only defines light-mode colors. The design templates document light and dark token values, but dark values are not implemented.
- **Border contrast is low:** `BorderBrush` (#E0E0E0) on white (#FFFFFF) gives only 1.2:1 contrast — fails WCAG AA minimum (3:1 for non-text). This is acceptable for subtle dividers but too low for interactive control borders.
- **TextPlaceholderBrush (#999999):** On white (#FFFFFF) gives 2.8:1 contrast — fails WCAG AA for text (4.5:1 minimum). Should be darker (#767676 or similar) for accessible placeholders.
- **Loupe/Compare views:** Use hardcoded dark colors (#1A1A1A background, #333 buttons, #AAA text) — these bypass the theme system entirely. If a light theme is applied, loupe/compare won't adapt.

**Recommendation:** Ship at least one dark theme (`.design/dark.md`) to validate the `DesignThemeService` pipeline. Tighten `TextPlaceholderBrush` to at least #767676 for WCAG AA compliance.

---

## Pillar 5: Interaction & Feedback ★★★★ (4)

Rich interaction patterns with clear feedback across all views.

**Strengths:**
- **Hover states:** All clickable elements (buttons, ListBoxItems, TreeViewItems) have hover state styling via `:pointerover` selectors. Asset tiles show toolbar on hover.
- **Press states:** Primary, Danger, and AiTag buttons have `:pressed` state styling with color darkening (10-20%).
- **Disabled states:** Buttons use reduced opacity (0.35) or muted colors for disabled state — clear visual feedback.
- **Selected states:** ListView uses `SelectionHoverBrush` / `SelectionBrush` / `SelectionActiveBrush` tri-state — hover, selected, and selected+hover are visually distinct. Left accent bar appears on selected rows.
- **Loading states:** Gallery, loupe, compare, metadata editor, property inspector, and sidebar all show `LoadingSpinner` during async operations. Startup has a dedicated overlay.
- **Empty states:** Gallery, activity feed, comments, trash, and metadata editor all have empty state messaging.
- **Toast notifications:** Success, warning, error, and info toasts with auto-dismiss (3s) and stacked display — good feedback mechanism.
- **Keyboard shortcuts:** 15+ shortcuts defined (Delete, Ctrl+A/C/E, D0-D5, P/X, F2, Enter, Ctrl+F) in `Window.KeyBindings`.
- **Drag-drop:** Gallery-to-sidebar drag with ghost overlay preview and Win32 cursor polling — sophisticated interaction.
- **Search debounce:** 300ms debounce on search input with autocomplete suggestions.
- **Context menus:** All gallery items have right-click context menus with Rate sub-menu, bulk actions, and permission gating.
- **Selection bar:** Multi-select shows a contextual action bar with rate/label/flag/compare/clear — good feedback for batch operations.
- **Toolbar actions:** Asset tiles show inline rate/flag buttons on hover — good micro-interaction.

**Issues:**
- **No transition animations on sidebars/toolbars:** While `AssetGalleryView` has `BrushTransition` for item selection (150ms), no transitions exist for panel visibility toggles, expander open/close, or view switches.
- **No focus-visible styles:** Keyboard focus indicators (outlines, focus rings) are not explicitly styled — users navigating via keyboard may struggle to see which element has focus.
- **No Skeleton/placeholder loading:** Loading states use a spinner overlay, not skeleton placeholders. The gallery overlay hides content entirely during loading, which is jarring.
- **No confirmation for some destructive actions:** The Trash "Permanently Delete" button has no secondary confirmation dialog — a single click permanently deletes assets.

**Recommendation:** Add focus-visible styles for keyboard accessibility. Add confirmation dialog to permanent delete in TrashView.

---

## Pillar 6: Accessibility ★★☆☆ (2)

Accessibility is the weakest pillar — the UI relies heavily on visual interaction patterns with limited accommodations.

**Strengths:**
- **Keyboard shortcuts:** 15+ keyboard shortcuts provide power-user keyboard navigation.
- **Tooltips:** Permission-gated buttons have descriptive tooltips explaining why they're disabled (e.g., "Requires Editor or Administrator role").
- **Semantic color usage:** Color carries meaning (red = danger, green = success, etc.), which helps users who can distinguish colors.

**Issues:**
- **No focus-visible styling:** As noted above, keyboard focus indicators are not explicitly managed. Tab navigation relies on default Avalonia behavior.
- **No high-contrast mode support:** The app doesn't test or accommodate Windows High Contrast Mode. Hardcoded colors in Loupe/Compare views may break.
- **No screen reader annotations:** `AutomationProperties` are not used on controls. ListBox items, TreeView nodes, and custom controls lack `Name`/`HelpText` for screen readers.
- **Small font sizes in key views:** `.Body` at 12px and `.Caption` at 10px are below the WCAG recommended minimum of 12px for captions and 16px for body text. The loupe info overlay and filmstrip use similarly small sizes.
- **Color-only indicators:** The left accent bar on selected ListView rows is the only selection indicator in list view — colorblind users who cannot distinguish blue may miss selection state.
- **No text scaling support:** Font sizes are hardcoded in pixels (10-18px) with no support for system font scaling or DPI overrides beyond basic DPI awareness.
- **Touch targets:** Some buttons (filmstrip items at 60×54px, toolbar icon buttons at 24×26px) fall below the recommended 44×44px minimum touch target.
- **No aria/automation properties on custom controls:** `AssetTileControl`, `AssetListRowControl`, `SearchableTreeView`, `TagEditorControl`, `ZoomBorder`, and `LoadingSpinner` do not expose accessibility properties.

**Recommendation:** This is the highest-priority improvement area. Start with: (1) Add `FocusVisualStyle` or `:focus-visible` selectors, (2) Add `AutomationProperties.Name` to all ListBox items, (3) Bump minimum font size to 11px for captions, (4) Add a secondary selection indicator (underline or bold) in list view rows.

---

## Summary

| Pillar | Score | Summary |
|--------|:-----:|---------|
| Visual Consistency | ★★★★ 4 | Excellent token system, minor hard-coded color gaps |
| Layout & Hierarchy | ★★★★ 4 | Clean 3-column layout, good view switching, dense sidebar |
| Typography | ★★★☆ 3 | Clean 4-class scale, but small body font, no line-height |
| Color & Contrast | ★★★☆ 3 | Comprehensive palette, but no dark mode, borderline placeholder contrast |
| Interaction & Feedback | ★★★★ 4 | Rich hover/press/select/loading states, keyboard shortcuts, toasts |
| Accessibility | ★★☆☆ 2 | Significant gaps — no focus styles, no screen reader support, small fonts |

**Overall:** 3.3 / 4 — **Good**. The application has a well-designed and consistently applied design system, rich interactions, and clear visual hierarchy. The primary gaps are (1) dark mode not shipped despite the theme engine being built, (2) accessibility needing significant attention, and (3) some hard-coded colors bypassing the theme system.

## Top 5 Immediate Improvements

1. **Ship a dark theme** — Create `.design/dark.md` to validate the `DesignThemeService` pipeline
2. **Add keyboard focus indicators** — `FocusVisualStyle` or `:focus-visible` on all interactive controls
3. **Fix `TextPlaceholderBrush` contrast** — Darken from #999999 to at least #767676 for WCAG AA
4. **Add confirmation to permanent delete** — Secondary dialog before `PermanentlyDeleteSelectedCommand`
5. **Migrate Loupe/Compare to theme tokens** — Replace hardcoded `#1A1A1A`/`#333` with dynamic resources
