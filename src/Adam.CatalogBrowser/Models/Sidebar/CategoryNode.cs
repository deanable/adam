using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Adam.CatalogBrowser.Models.Sidebar;

/// <summary>
/// Represents a metadata category node in the sidebar category tree.
/// Also used for media format items (flat list).
/// Supports inline rename.
/// Note: XAML templates bind to <c>Count</c> for categories and media formats.
/// </summary>
public sealed class CategoryNode : SidableTreeNode<CategoryNode>, IRenameable
{
    private bool _isEditing;
    private string _editName = string.Empty;

    public Guid CategoryId { get; set; }

    /// <summary>
    /// XAML-compatible alias for AssetCount. Used in MetadataCategories and MediaFormats.
    /// </summary>
    public int Count
    {
        get => AssetCount;
        set { AssetCount = value; OnPropertyChanged(nameof(Count)); }
    }

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

    public override object? GetId() => CategoryId;
    protected override void OnPropertyChanged(string? n = null)
    {
        base.OnPropertyChanged(n);
        if (n == nameof(AssetCount))
            base.OnPropertyChanged(nameof(Count));
    }
}
