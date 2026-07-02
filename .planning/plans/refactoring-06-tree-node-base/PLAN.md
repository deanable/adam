---
goal: Extract the 7 inline node types from SidebarViewModel into separate files and create a shared TreeNode<T> base class to eliminate tree-building boilerplate.
version: 1.0
date_created: 2026-07-02
last_updated: 2026-07-02
status: 'Draft'
tags: [refactoring, sidebar-viewmodel, node-types, tree-abstraction, boilerplate]
---

# Refactoring 06: Tree Node Type Extraction & Pattern Abstraction

## Problem

`SidebarViewModel.cs` currently defines **7 node types inline** in the same file (~550 lines total):

| Node Type | Lines | Has Inline Rename? | Has IsActiveFilter? |
|-----------|-------|--------------------|---------------------|
| `FolderNode` | ~80 | ❌ | ✅ |
| `CollectionNode` | ~90 | ✅ | ✅ |
| `KeywordNode` | ~90 | ✅ | ✅ |
| `CategoryNode` | ~90 | ✅ | ✅ |
| `DateTakenNode` | ~70 | ❌ | ✅ |
| `SavedSearchNode` | ~60 | ❌ | ✅ |
| `SearchHistoryNode` | ~70 | ❌ | ✅ |

Each node type has near-identical `INotifyPropertyChanged` boilerplate:

```csharp
public class XxxNode : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isSelected;
    private int _assetCount;
    private bool _isActiveFilter;

    public bool IsExpanded { get => _isExpanded; set { _isExpanded = value; OnPropertyChanged(); } }
    public bool IsSelected { get => _isSelected; set { _isSelected = value; OnPropertyChanged(); } }
    public int AssetCount { get => _assetCount; set { _assetCount = value; OnPropertyChanged(); } }
    public bool IsActiveFilter { get => _isActiveFilter; set { _isActiveFilter = value; OnPropertyChanged(); } }

    public ObservableCollection<XxxNode> Children { get; } = [];

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
```

Additionally, the **tree building + count propagation pattern** repeats 5 times across folders, collections, keywords, categories, and date-taken trees:

```csharp
// Repeats in 5 different methods with different types:
// 1. BuildTree/Folders — FolderNode
// 2. LoadCollections/BuildTree — CollectionNode
// 3. LoadKeywords/BuildTree + PropagateKeywordCounts — KeywordNode
// 4. LoadMetadataCategories/BuildTree + PropagateCategoryCounts — CategoryNode
// 5. LoadDateTakenTree — DateTakenNode (slightly different: year/month grouping)
```

The cascade delete helpers (`CountDescendantKeywords`, `CollectDescendantKeywordIds`, etc.) repeat the same tree-walk pattern for keywords, collections, and categories:

```csharp
// Repeats 6 times (3 types × 2 patterns: count + collect):
private static int CountDescendantKeywords(KeywordNode node) { ... }
private static void CollectDescendantKeywordIds(KeywordNode node, List<Guid> ids) { ... }
private static int CountDescendantCollections(CollectionNode node) { ... }
private static void CollectDescendantCollectionIds(CollectionNode node, List<Guid> ids) { ... }
private static int CountDescendantCategories(CategoryNode node) { ... }
private static void CollectDescendantCategoryIds(CategoryNode node, List<Guid> ids) { ... }
```

## Solution

### Phase 1: Extract Node Types to Individual Files

Move each node type to its own file under `src/Adam.CatalogBrowser/Models/Sidebar/`. No behavior changes — just code motion.

New files:
- `Models/Sidebar/FolderNode.cs`
- `Models/Sidebar/CollectionNode.cs`
- `Models/Sidebar/KeywordNode.cs`
- `Models/Sidebar/CategoryNode.cs`
- `Models/Sidebar/DateTakenNode.cs`
- `Models/Sidebar/SavedSearchNode.cs`
- `Models/Sidebar/SearchHistoryNode.cs`

### Phase 2: Create a `SidableTreeNode<T>` Base Class 

Extract the common pattern into a generic base class:

```csharp
namespace Adam.CatalogBrowser.Models.Sidebar;

/// <summary>
/// Base class for sidebar tree nodes with common properties.
/// </summary>
public abstract class SidableTreeNode<T> : INotifyPropertyChanged
    where T : SidableTreeNode<T>
{
    private bool _isExpanded;
    private bool _isSelected;
    private int _assetCount;
    private bool _isActiveFilter;
    private string _name = string.Empty;

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; OnPropertyChanged(); }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public int AssetCount
    {
        get => _assetCount;
        set { _assetCount = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// True when this node is the currently active gallery filter.
    /// </summary>
    public bool IsActiveFilter
    {
        get => _isActiveFilter;
        set { _isActiveFilter = value; OnPropertyChanged(); }
    }

    public ObservableCollection<T> Children { get; } = [];

    /// <summary>
    /// Recursively sums children counts into this node's AssetCount.
    /// Returns the total after propagation.
    /// </summary>
    public int PropagateCounts()
    {
        var childSum = 0;
        foreach (var child in Children)
            childSum += child.PropagateCounts();
        AssetCount += childSum;
        return AssetCount;
    }

    /// <summary>
    /// Recursively collects all descendant IDs into the provided list.
    /// </summary>
    public void CollectDescendantIds(List<object> ids)
    {
        foreach (var child in Children)
        {
            ids.Add(child.GetId());
            child.CollectDescendantIds(ids);
        }
    }

    /// <summary>
    /// Must return the unique identifier for this node type.
    /// </summary>
    protected abstract object GetId();

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>
/// Mix-in interface for nodes that support inline rename (T10.2).
/// </summary>
public interface IRenameable
{
    bool IsEditing { get; set; }
    string EditName { get; set; }
    void BeginRename();
    void CommitRename();
    void CancelRename();
}
```

Then each node type becomes minimal:

```csharp
public sealed class KeywordNode : SidableTreeNode<KeywordNode>, IRenameable
{
    private bool _isEditing;
    private string _editName = string.Empty;

    public Guid KeywordId { get; set; }
    public string Path { get; set; } = string.Empty;

    public bool IsEditing
    {
        get => _isEditing;
        set { _isEditing = value; OnPropertyChanged(); }
    }

    public string EditName
    {
        get => _editName;
        set { _editName = value; OnPropertyChanged(); }
    }

    public void BeginRename() { EditName = Name; IsEditing = true; }
    public void CommitRename() { if (!string.IsNullOrWhiteSpace(EditName)) Name = EditName; IsEditing = false; }
    public void CancelRename() { IsEditing = false; EditName = Name; }

    protected override object GetId() => KeywordId;
}
```

### Phase 3: Tree Building Extension Methods

Create shared helpers for tree building:

```csharp
// TreeExtensions.cs
public static class TreeExtensions
{
    /// <summary>
    /// Builds a tree by linking children to parents using the provided parent ID selector.
    /// </summary>
    public static List<T> BuildTree<T>(
        this IEnumerable<T> flatNodes,
        Func<T, object?> getParentId,
        Func<T, bool> isRoot)
        where T : SidableTreeNode<T>
    {
        var nodeDict = flatNodes.ToDictionary(n => n.GetId());
        var roots = new List<T>();

        foreach (var node in flatNodes)
        {
            var parentId = getParentId(node);
            if (parentId != null && nodeDict.TryGetValue(parentId, out var parent))
                parent.Children.Add(node);
            else if (isRoot(node))
                roots.Add(node);
        }

        return roots;
    }

    /// <summary>
    /// Recursively clears IsActiveFilter on all nodes in the tree.
    /// </summary>
    public static void ClearActiveFilters<T>(this IEnumerable<T> roots)
        where T : SidableTreeNode<T>
    {
        foreach (var root in roots)
            ClearActiveFiltersRecursive(root);
    }

    private static void ClearActiveFiltersRecursive<T>(T node) where T : SidableTreeNode<T>
    {
        node.IsActiveFilter = false;
        foreach (var child in node.Children)
            ClearActiveFiltersRecursive(child);
    }
}
```

### Implementation Steps

| Phase | Step | Description | Effort |
|-------|------|-------------|--------|
| 1a | Create `Models/Sidebar/` directory | Tiny |
| 1b | Create `SidableTreeNode<T>` base class | Medium |
| 1c | Create `IRenameable` interface | Small |
| 1d | Create 7 node type files, each inheriting from `SidableTreeNode<T>` | Medium |
| 1e | Remove inline node types from `SidebarViewModel.cs` | Medium |
| 1f | Create `TreeExtensions.cs` with BuildTree + ClearActiveFilters | Medium |
| 1g | Update usings across the codebase | Medium |
| 2a | Replace 5 `PropagateCounts` implementations with `node.PropagateCounts()` | Small |
| 2b | Replace 3 cascade-delete helpers with `node.CollectDescendantIds()` or typed `GetDescendantIds(node, n => n.KeywordId)` | Small |
| 2c | Replace `ClearActiveFilterStates` tree walking with `tree.ClearActiveFilters()` | Small |
| 3 | Update `SidebarViewModel` to use the shared extensions | Small |
| 4 | Verify all tests still pass | Small |

### Expected Outcome

| Metric | Before | After |
|--------|--------|-------|
| SidebarViewModel.cs lines | 2,573 | ~2,100 |
| Node type boilerplate lines | ~550 | ~150 |
| Duplicate INotifyPropertyChanged patterns | 7 copies | 1 base class |
| Duplicate PropagateCounts implementations | 3 | 0 (shared) |
| Duplicate CountDescendant implementations | 6 | 0 (shared via GetId) |
| Duplicate ClearActiveFilter tree walking | 1 method with 7 switch cases | 1 generic extension |

## Files Changed

| File | Change |
|------|--------|
| `src/Adam.CatalogBrowser/Models/Sidebar/SidableTreeNode.cs` | **New** — Generic base class |
| `src/Adam.CatalogBrowser/Models/Sidebar/IRenameable.cs` | **New** — Rename interface |
| `src/Adam.CatalogBrowser/Models/Sidebar/FolderNode.cs` | **New** (extracted) |
| `src/Adam.CatalogBrowser/Models/Sidebar/CollectionNode.cs` | **New** (extracted) |
| `src/Adam.CatalogBrowser/Models/Sidebar/KeywordNode.cs` | **New** (extracted) |
| `src/Adam.CatalogBrowser/Models/Sidebar/CategoryNode.cs` | **New** (extracted) |
| `src/Adam.CatalogBrowser/Models/Sidebar/DateTakenNode.cs` | **New** (extracted) |
| `src/Adam.CatalogBrowser/Models/Sidebar/SavedSearchNode.cs` | **New** (extracted) |
| `src/Adam.CatalogBrowser/Models/Sidebar/SearchHistoryNode.cs` | **New** (extracted) |
| `src/Adam.CatalogBrowser/Models/Sidebar/TreeExtensions.cs` | **New** — Generic helper methods |
| `src/Adam.CatalogBrowser/ViewModels/SidebarViewModel.cs` | Remove 7 inline types, use extensions |

## Testing

```bash
dotnet test tests/Adam.CatalogBrowser.Tests
```

Key tests affected:
- `SidebarCrudTests.cs` — Tests CRUD operations for keywords, collections, categories
- `SidebarSavedSearchTests.cs` — Tests saved search load/delete/pin
- `MainWindowPanelPersistenceTests.cs` — Tests sidebar panel state
- All gallery ViewModel tests that depend on sidebar filtering

## Risk Assessment

- **Low (Phase 1)**: Pure code motion — moving types to separate files cannot break behavior
- **Medium (Phase 2)**: The base class changes `: INotifyPropertyChanged` to `: SidableTreeNode<T>`. If any XAML binding accesses a property that moves to the base class, it will still work. If any code casts to a specific node type and accesses a field directly, the fields still exist.
- **Low**: The `CollectDescendantIds` change from typed `List<Guid>` to `List<object>` is the only type change, and it's internal.

## Modularity Assessment — Cross-Reference

This refactoring addresses two findings from the codebase audit:

1. **7-inline-node-types problem** — Having 7 types with near-identical INotifyPropertyChanged boilerplate in one file violates the Single Responsibility Principle for that file
2. **Tree algorithm duplication** — BuildTree + PropagateCounts + ClearActiveFilters repeats across 5 tree types
3. **Boilerplate maintenance cost** — Adding a new tree node type requires copying ~80 lines of boilerplate; with the base class, it's ~15 lines

**Related plans:**
- `refactoring-05-sidebar-decomposition/PLAN.md` — Decomposes SidebarViewModel into 7 services. This plan (06) is a prerequisite for making each tree service small and focused.
- After both 05 and 06, the SidebarViewModel goes from 2,573 lines with 7 inline types to ~400 lines of pure coordination logic.
