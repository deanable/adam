using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Adam.CatalogBrowser.Models.Sidebar;

/// <summary>
/// Represents a collection node in the sidebar collection tree.
/// Supports inline rename and smart collection status.
/// </summary>
public sealed class CollectionNode : SidableTreeNode<CollectionNode>, IRenameable
{
    private bool _isEditing;
    private string _editName = string.Empty;
    private bool _isSmart;

    public Guid Id { get; set; }
    public Guid? ParentId { get; set; }

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

    /// <summary>
    /// True when this collection is a smart collection (auto-refreshed from a saved query).
    /// </summary>
    public bool IsSmart
    {
        get => _isSmart;
        set { _isSmart = value; OnPropertyChanged(); OnPropertyChanged(nameof(SmartIcon)); }
    }

    /// <summary>
    /// Display icon for smart collection status: ✦ when smart, empty when manual.
    /// </summary>
    public string SmartIcon => _isSmart ? "✦" : string.Empty;

    public void BeginRename() { EditName = Name; IsEditing = true; }
    public void CommitRename() { if (!string.IsNullOrWhiteSpace(EditName)) Name = EditName; IsEditing = false; }
    public void CancelRename() { IsEditing = false; EditName = Name; }

    public override object? GetId() => Id;
}
