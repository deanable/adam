namespace Adam.CatalogBrowser.Models.Sidebar;

/// <summary>
/// Represents a year or month node in the sidebar date-taken tree.
/// </summary>
public sealed class DateTakenNode : SidableTreeNode<DateTakenNode>
{
    public int? Year { get; set; }
    public int? Month { get; set; }

    /// <summary>
    /// Whether this node represents a year (true) or a month (false).
    /// </summary>
    public bool IsYear => Year.HasValue && !Month.HasValue;
}
