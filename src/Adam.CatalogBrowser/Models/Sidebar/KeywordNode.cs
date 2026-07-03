namespace Adam.CatalogBrowser.Models.Sidebar;

/// <summary>
/// Represents a keyword node in the sidebar keyword tree.
/// Supports inline rename.
/// </summary>
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

    public override object? GetId() => KeywordId;
}
