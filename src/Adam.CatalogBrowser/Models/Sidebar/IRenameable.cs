namespace Adam.CatalogBrowser.Models.Sidebar;

/// <summary>
/// Mix-in interface for sidebar tree nodes that support inline rename (T10.2).
/// </summary>
public interface IRenameable
{
    bool IsEditing { get; set; }
    string EditName { get; set; }
    void BeginRename();
    void CommitRename();
    void CancelRename();
}
