using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Adam.CatalogBrowser.Models.Sidebar;

/// <summary>
/// Represents a saved search node in the sidebar (Phase 19).
/// Flat list — no Children, does not extend SidableTreeNode.
/// </summary>
public sealed class SavedSearchNode : ISidableNode
{
    private string _name = string.Empty;
    private string? _queryText;
    private bool _isPinned;
    private bool _isActiveFilter;

    public Guid SearchId { get; set; }

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    public string? QueryText
    {
        get => _queryText;
        set { _queryText = value; OnPropertyChanged(); }
    }

    public bool IsPinned
    {
        get => _isPinned;
        set
        {
            _isPinned = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PinIcon));
        }
    }

    /// <summary>
    /// Pin icon: 📌 when pinned, empty when not pinned.
    /// </summary>
    public string PinIcon => _isPinned ? "\U0001F4CC" : string.Empty;

    /// <summary>
    /// True when this saved search is the currently active gallery filter.
    /// </summary>
    public bool IsActiveFilter
    {
        get => _isActiveFilter;
        set { _isActiveFilter = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
