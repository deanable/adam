using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Adam.CatalogBrowser.Models.Sidebar;

/// <summary>
/// Base class for sidebar tree nodes with common properties and tree algorithms.
/// </summary>
/// <typeparam name="T">The concrete node type (CRTP pattern for Children collection).</typeparam>
public abstract class SidableTreeNode<T> : ISidableNode
    where T : SidableTreeNode<T>
{
    private string _name = string.Empty;
    private bool _isExpanded;
    private bool _isSelected;
    private int _assetCount;
    private bool _isActiveFilter;

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
    /// Override in types that need cascade-delete support.
    /// </summary>
    public virtual void CollectDescendantIds(List<object> ids)
    {
        foreach (var child in Children)
        {
            var id = child.GetId();
            if (id != null)
                ids.Add(id);
            child.CollectDescendantIds(ids);
        }
    }

    /// <summary>
    /// Returns the unique identifier for this node, or null if no identifier applies.
    /// Override in types that need cascade-delete or tree-building support.
    /// </summary>
    public virtual object? GetId() => null;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
