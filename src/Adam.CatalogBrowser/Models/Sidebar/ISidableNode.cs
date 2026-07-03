using System.ComponentModel;

namespace Adam.CatalogBrowser.Models.Sidebar;

/// <summary>
/// Common properties shared by all sidebar node types (tree and flat).
/// </summary>
public interface ISidableNode : INotifyPropertyChanged
{
    string Name { get; set; }
    bool IsActiveFilter { get; set; }
}
