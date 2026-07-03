namespace Adam.CatalogBrowser.Models.Sidebar;

/// <summary>
/// Represents a folder node in the sidebar folder tree (read-only, no inline rename).
/// </summary>
public sealed class FolderNode : SidableTreeNode<FolderNode>
{
    public string Path { get; set; } = string.Empty;

    public override object? GetId() => Path;
}
