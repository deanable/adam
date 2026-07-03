namespace Adam.CatalogBrowser.Models.Sidebar;

/// <summary>
/// Shared extension methods for sidebar tree operations.
/// </summary>
public static class TreeExtensions
{
    /// <summary>
    /// Builds a tree by linking children to parents using the provided parent ID selector.
    /// Planned for use in Plan 05 (SidebarViewModel decomposition into tree services).
    /// </summary>
    public static List<T> BuildTree<T>(
        this IEnumerable<T> flatNodes,
        Func<T, object?> getParentId,
        Func<T, bool> isRoot)
        where T : SidableTreeNode<T>
    {
        var nodeDict = flatNodes.ToDictionary(n => n.GetId()!);
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
    /// Recursively clears IsActiveFilter on all nodes in a tree.
    /// </summary>
    public static void ClearSubTree<T>(this SidableTreeNode<T> node) where T : SidableTreeNode<T>
    {
        node.IsActiveFilter = false;
        foreach (var child in node.Children)
            child.ClearSubTree();
    }

    /// <summary>
    /// Clears IsActiveFilter on all items in a flat list (used for SavedSearchNode, SearchHistoryNode).
    /// </summary>
    public static void ClearFlatList<T>(this IEnumerable<T> items) where T : ISidableNode
    {
        foreach (var item in items)
            item.IsActiveFilter = false;
    }
}
