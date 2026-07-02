---
goal: Decompose the 2,573-line SidebarViewModel into focused tree-loader services and inline rename handler.
version: 1.0
date_created: 2026-07-02
last_updated: 2026-07-02
status: 'Draft'
tags: [refactoring, sidebar-viewmodel, megalith, service-extraction, tree-loading]
---

# Refactoring 05: SidebarViewModel Decomposition

## Problem

`SidebarViewModel` is **2,573 lines** — the largest file in the entire codebase. It handles **6 distinct sub-domains**, each with standalone vs multi-user branching, tree building, and CRUD operations:

| Domain | Lines | Concern |
|--------|-------|---------|
| Folder tree | ~400 | Loading from DB/broker, tree building, count propagation, reveal, rescan |
| Collection tree | ~350 | Loading, tree building, CRUD (create/rename/delete with cascade), inline rename |
| Keyword tree | ~400 | Loading, tree building, CRUD (create/rename/delete with cascade), inline rename |
| Category tree | ~350 | Loading, tree building, CRUD (create/rename/delete with cascade), inline rename |
| Date taken tree | ~150 | Loading from DB/broker, grouping by year/month, tree building |
| Media format counts | ~80 | Loading type counts from DB/broker |
| Saved searches | ~200 | Loading, deleting, toggling pin, state management |
| Search history | ~150 | Loading, clearing, state management |
| Filter activation tracking | ~200 | Multi-node-type filter state, mutual exclusion, active filter visual |
| Permission plumbing | ~100 | EvaluatePermission, RefreshPermissions, CRUD gating |
| Inline node types | ~550 | 7 node types (FolderNode, CollectionNode, KeywordNode, CategoryNode, DateTakenNode, SavedSearchNode, SearchHistoryNode) |

This is a classic **god class** — it knows too much and does too much. Every time a new sidebar panel is added, this file grows.

## Solution

Extract each domain-specific tree into a dedicated service, leaving SidebarViewModel as a coordinator that delegates to focused services.

### Proposed Service Breakdown

| New Service | Responsibility | Estimated Lines |
|-------------|---------------|-----------------|
| `FolderTreeService` | Load folders from DB/broker, build tree, propagate counts, reveal/rescan | ~300 |
| `CollectionTreeService` | Load collections, build tree, CRUD (create/rename/delete cascade), inline rename emit | ~300 |
| `KeywordTreeService` | Load keywords, build tree, CRUD (create/rename/delete cascade), inline rename emit | ~300 |
| `CategoryTreeService` | Load categories, build tree, CRUD (create/rename/delete cascade), inline rename emit | ~300 |
| `DateTakenTreeService` | Load date groups from DB/broker, group by year/month, build tree | ~120 |
| `SavedSearchService` | Load saved searches, delete, toggle pin, search history load/clear | ~200 |
| `MediaFormatService` | Load media format counts from DB/broker | ~80 |

**How the services communicate back to the ViewModel:** Each service exposes:
- An `ObservableCollection<TreeNode>` or similar reactive collection
- `async Task LoadAsync(CancellationToken)`
- Optionally, `event Action? DataChanged` for when the UI needs to be notified

### Shared Infrastructure

The tree services share a pattern:

```csharp
public sealed class FolderTreeService
{
    private readonly ModeManager _modeManager;
    private readonly ILogger<FolderTreeService> _logger;

    public ObservableCollection<FolderNode> Roots { get; } = new();

    public async Task LoadAsync(CancellationToken ct = default)
    {
        // Standalone: query DB → build tree → propagate counts
        // Multi-user: broker request → deserialize → build tree → propagate counts
        // Dispatch to Roots on UI thread
    }

    // Standalone CRUD methods (if applicable for this domain)
}

public sealed class CollectionTreeService
{
    // Same pattern: LoadAsync, Roots, CRUD commands

    // Plus: inline rename events
    public event Action<CollectionNode>? CommitRename;

    public async Task CreateAsync(string name, Guid? parentId);
    public async Task RenameAsync(CollectionNode node, string newName);
    public async Task DeleteWithCascadeAsync(CollectionNode node);
}
```

### Remaining SidebarViewModel (~600-800 lines)

After extraction, SidebarViewModel becomes a **coordinator** that:
1. Constructs the tree services (injected via DI) 
2. Wires their data into the flat sidebar layout
3. Handles cross-tree concerns (filter activation tracking, permission gating)
4. Provides `FilterChanged` event and `ApplyFilter` coordination
5. Handles `ActiveSearchQueryText` coordination

It would look like:

```csharp
public sealed class SidebarViewModel : INotifyPropertyChanged
{
    private readonly FolderTreeService _folders;
    private readonly CollectionTreeService _collections;
    private readonly KeywordTreeService _keywords;
    private readonly CategoryTreeService _categories;
    private readonly DateTakenTreeService _dateTaken;
    private readonly SavedSearchService _savedSearches;
    private readonly MediaFormatService _mediaFormats;

    public SidebarViewModel(
        FolderTreeService folders,
        CollectionTreeService collections,
        ...)
    {
        _folders = folders;
        // Expose their roots directly for XAML binding
    }

    // Each root collection exposed as a property
    public ObservableCollection<FolderNode> Folders => _folders.Roots;
    public ObservableCollection<CollectionNode> Collections => _collections.Roots;
    // etc.

    // Filter activation tracking (cross-cutting)
    // Permission gating (cross-cutting)
    // FilterChanged event
}
```

### Implementation Steps

| Step | Description | Effort | Risk |
|------|-------------|--------|------|
| 1 | Create `FolderTreeService` — move all folder-loading code from SidebarVM | Large | Medium |
| 2 | Create `CollectionTreeService` — move all collection CRUD + loading | Large | Medium |
| 3 | Create `KeywordTreeService` — move all keyword CRUD + loading | Large | Medium |
| 4 | Create `CategoryTreeService` — move all category CRUD + loading | Large | Medium |
| 5 | Create `DateTakenTreeService` — move date tree loading | Medium | Low |
| 6 | Create `SavedSearchService` — move saved search + search history | Medium | Low |
| 7 | Create `MediaFormatService` — move format count loading | Small | Low |
| 8 | Refactor `SidebarViewModel` to delegate to services | Medium | High |
| 9 | Register all services in DI (`App.axaml.cs`) | Small | Low |
| 10 | Update XAML bindings if naming conventions changed | Small | Low |
| 11 | Verify all tests still pass | Small | Low |

### Expected Outcome

| Metric | Before | After | Change |
|--------|--------|-------|--------|
| SidebarViewModel lines | 2,573 | ~700 | **-1,873** |
| SidebarViewModel concerns | 10+ | 3 (coordination + filter tracking + permissions) | Focused |
| Number of files for sidebar | 1 | 9 (1 VM + 7 services + 1 shared) | Extensible |
| Testability | Near-impossible (2573-line god class) | Each service independently testable | Huge improvement |
| New panel addition cost | Add code to existing 2573-line file | Add new TreeService, implement LoadAsync | Low |

## Files Changed

| File | Change |
|------|--------|
| `src/Adam.CatalogBrowser/Services/FolderTreeService.cs` | **New** |
| `src/Adam.CatalogBrowser/Services/CollectionTreeService.cs` | **New** |
| `src/Adam.CatalogBrowser/Services/KeywordTreeService.cs` | **New** |
| `src/Adam.CatalogBrowser/Services/CategoryTreeService.cs` | **New** |
| `src/Adam.CatalogBrowser/Services/DateTakenTreeService.cs` | **New** |
| `src/Adam.CatalogBrowser/Services/SavedSearchService.cs` | **New** |
| `src/Adam.CatalogBrowser/Services/MediaFormatService.cs` | **New** |
| `src/Adam.CatalogBrowser/ViewModels/SidebarViewModel.cs` | Major reduction (2,573 → ~700) |
| `src/Adam.CatalogBrowser/App.axaml.cs` | Register 7 new services in DI |

## Testing

```bash
dotnet test tests/Adam.CatalogBrowser.Tests --filter "FullyQualifiedName~Sidebar"
```

The sidebar tests and all dependent tests must pass. Because this is pure code extraction (no logic changes), existing tests should continue to validate the same behavior through the ViewModel.

## Risk Assessment

- **Medium**: XAML bindings reference `Folders`, `Collections`, `Keywords` directly on SidebarViewModel. If the property names change, bindings break. Solution: keep the property names identical.
- **Medium**: The `FilterChanged` event and `OnFilterChanged`/`OnMediaFormatChanged` orchestration is cross-cutting — must be preserved exactly.
- **High**: This is the largest refactoring (7 new services, 1 heavily modified VM). Should be done in stages, not all at once. Recommended order: (1) MediaFormatService (lowest risk), (2) DateTakenTreeService, (3) SavedSearchService, then the CRUD services one at a time.

## Modularity Assessment — Cross-Reference

This refactoring directly addresses the **#1 megalith** in the codebase audit:

| Finding | How This Refactoring Addresses It |
|---------|-----------------------------------|
| SidebarViewModel is 2,573 lines (largest file) | Reduces to ~700 lines |
| Standalone vs multi-user branching duplicated 6x | Each service owns its own branching — no longer repeated |
| Tree building + PropagateCounts algorithm repeats 5x | Each service has its own version, but they can later be unified via a shared base (see refactoring-06) |
| CRUD patterns repeat 3x (keywords, collections, categories) | Each CRUD set lives in its own service — no longer mixed in one file |
| ProtoHelper used in ViewModel (layer violation) | Tree services own the serialization — not exposed to the VM |
| Adding a new panel requires editing a 2,573-line file | Add a new service + one property on the VM |

**Related plans:**
- `refactoring-06-tree-node-base/PLAN.md` — Abstracting the common node pattern into a base class
- This refactoring is independent of the 4 MainWindowVM extractions (plans 01-04) and can be done in parallel
