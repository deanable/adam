---
goal: Extract 15 expander panel booleans + save/restore logic from MainWindowViewModel into a dedicated PanelStateService.
version: 1.0
date_created: 2026-07-02
last_updated: 2026-07-02
status: 'Draft'
tags: [refactoring, mainwindow-viewmodel, panel-state, service-extraction]
---

# Refactoring 03: PanelStateService

## Problem

`MainWindowViewModel` manages 15 boolean properties for sidebar and right-panel expander states, plus two methods (`SaveSidebarPanelStatesAsync` and `RestoreSidebarPanelStatesAsync`) that total ~200 lines. This includes:

- 15 backing fields
- 15 property getters/setters, each with gating pattern (`if (!_isRestoringSidebarPanels) _ = SaveSidebarPanelStatesAsync()`)
- 1 `_isRestoringSidebarPanels` gating field
- 2 async methods for save/restore with `UserPreferenceService`

This is a cleanly separable concern that adds ~150 lines to the already-large ViewModel. It has nothing to do with navigation, command routing, or asset management — it's pure UI state persistence.

## Solution

Extract a **`PanelStateService`** that owns all 15 expander properties and handles save/restore from `IUserPreferenceService`. The service implements `INotifyPropertyChanged` so XAML bindings can bind directly to it.

## Proposed Design

### New file: `src/Adam.CatalogBrowser/Services/PanelStateService.cs`

```csharp
public sealed class PanelStateService : INotifyPropertyChanged
{
    private readonly IUserPreferenceService? _prefs;
    private readonly IUiDispatcher _dispatcher;
    private bool _isRestoring;

    // 12 sidebar expanding booleans
    public bool IsSidebarFoldersExpanded { get; set; }
    public bool IsSidebarCollectionsExpanded { get; set; }
    public bool IsSidebarSavedSearchesExpanded { get; set; }
    public bool IsSidebarRecentSearchesExpanded { get; set; }
    public bool IsSidebarKeywordsExpanded { get; set; }
    public bool IsSidebarMediaFormatExpanded { get; set; }
    public bool IsSidebarCategoriesExpanded { get; set; }
    public bool IsSidebarDateTakenExpanded { get; set; }
    public bool IsSidebarRatingExpanded { get; set; }
    public bool IsSidebarLabelExpanded { get; set; }
    public bool IsSidebarFlagExpanded { get; set; }
    public bool IsSidebarAiModelExpanded { get; set; }

    // 3 right-panel expanding booleans
    public bool IsRightMetadataExpanded { get; set; }
    public bool IsRightCommentsExpanded { get; set; }
    public bool IsRightTagsExpanded { get; set; }

    /// <summary>
    /// Loads saved states from preferences. Called once at startup.
    /// </summary>
    public async Task RestoreAsync();

    /// <summary>
    /// Persists current states to preferences.
    /// </summary>
    public async Task SaveAsync();
}
```

**Key design decisions:**

1. **Default values match current code**: Folders, Collections, SavedSearches, RecentSearches, Keywords, MediaFormat, Categories, DateTaken, AiModel, Metadata, Comments, Tags all default to `true`; Rating, Label, Flag default to `false`.

2. **Gating pattern preserved**: The `_isRestoring` field is kept inside `PanelStateService` so that save is suppressed during `RestoreAsync()`.

3. **Merge logic preserved**: The `SaveAsync()` method still merges with existing metadata-editor panel states (single-char entries A-H).

4. **No XAML changes needed**: The `x:Bind` paths change from `IsSidebarFoldersExpanded` (on MainWindowVM) to `PanelState.IsSidebarFoldersExpanded` — a single prefix change in the view.

5. **Exposed as property on MainWindowVM**: `public PanelStateService PanelState { get; }` — no XAML rewrite needed.

### Implementation Steps

| Step | Description | Effort |
|------|-------------|--------|
| 1 | Create `PanelStateService.cs` in `src/Adam.CatalogBrowser/Services/` | Medium |
| 2 | Register `PanelStateService` as singleton in DI (`App.axaml.cs`) | Tiny |
| 3 | Add `PanelState` property to `MainWindowViewModel`, set in constructor | Tiny |
| 4 | Remove all 15 expander fields + properties from `MainWindowViewModel` | Small |
| 5 | Remove `SaveSidebarPanelStatesAsync` and `RestoreSidebarPanelStatesAsync` | Small |
| 6 | Update XAML bindings in `MainWindow.axaml` from `{Binding IsSidebarFoldersExpanded}` to `{Binding PanelState.IsSidebarFoldersExpanded}` | Medium (15 bindings) |
| 7 | Verify all tests still pass | Small |

### Expected Outcome

- **~150 lines removed** from `MainWindowViewModel`
- **~140 lines added** in `PanelStateService`
- Panel state is independently testable (just mock `IUserPreferenceService`)
- `MainWindowViewModel` no longer needs to know about expander state management

## Files Changed

| File | Change |
|------|--------|
| `src/Adam.CatalogBrowser/Services/PanelStateService.cs` | **New** |
| `src/Adam.CatalogBrowser/ViewModels/MainWindowViewModel.cs` | Remove 200 lines, add 1 property |
| `src/Adam.CatalogBrowser/Views/MainWindow.axaml` | Update 15 binding paths |
| `src/Adam.CatalogBrowser/App.axaml.cs` | Register service in DI |

## Testing

```bash
dotnet test tests/Adam.CatalogBrowser.Tests
```

Key behavior to verify:
- Expander states persist across app restarts
- States restored correctly on startup
- Merge with metadata-editor panel states (A-H entries) is preserved
- Setting a property triggers save

## Modularity Assessment Findings — Cross-Reference

This refactoring addresses the **separable-UI-state-in-ViewModel** problem identified in the audit. The expander panel states are pure UI persistence — they have nothing to do with navigation, command routing, or asset management:

| Metric | Before | After |
|--------|--------|-------|
| MainWindowViewModel lines | 2,040 | ~1,890 |
| Panel-related properties | 15 bools + 2 methods | 1 `PanelState` property reference |
| Concerns in ViewModel | 10 distinct concerns | 9 distinct concerns |

**Broader modularity context:** This is the third of 4 planned extractions from MainWindowViewModel. After all 4, the ViewModel goes from 2,040 to ~1,000 lines and from 10 concerns to 6.

**Related plans:**
- `refactoring-01-navigation-service/PLAN.md` — Navigation lifecycle extraction
- `refactoring-02-bulk-operation-service/PLAN.md` — Bulk operation methods
- `refactoring-04-loadpage-decomposition/PLAN.md` — LoadPage decomposition

## Risk Assessment

- **Low-Medium**: XAML binding paths change, which could cause binding errors at runtime if any path is missed. Use the Avalonia DevTools binding inspector to verify
- **Low**: No behavior changes — the save/restore logic is copy-pasted verbatim
- **Medium**: This affects the XAML view file (MainWindow.axaml), which requires a recompile but no logic changes
