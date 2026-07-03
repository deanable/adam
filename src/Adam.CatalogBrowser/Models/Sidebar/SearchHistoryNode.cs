using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Adam.CatalogBrowser.Models.Sidebar;

/// <summary>
/// Represents a recent search history entry in the sidebar (Phase 19).
/// Flat list — no Children, does not extend SidableTreeNode.
/// </summary>
public sealed class SearchHistoryNode : ISidableNode
{
    private string? _queryText;
    private DateTimeOffset _executedAt;
    private bool _isActiveFilter;

    public Guid EntryId { get; set; }

    public string? QueryText
    {
        get => _queryText;
        set { _queryText = value; OnPropertyChanged(); }
    }

    public DateTimeOffset ExecutedAt
    {
        get => _executedAt;
        set
        {
            _executedAt = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TimeAgo));
        }
    }

    /// <summary>
    /// Human-readable relative time (e.g., "just now", "5 min ago", "3h ago", "2d ago", "Jan 15").
    /// </summary>
    public string TimeAgo
    {
        get
        {
            var elapsed = DateTimeOffset.UtcNow - _executedAt;
            if (elapsed.TotalSeconds < 60) return "just now";
            if (elapsed.TotalMinutes < 60) return $"{(int)elapsed.TotalMinutes} min ago";
            if (elapsed.TotalHours < 24) return $"{(int)elapsed.TotalHours}h ago";
            if (elapsed.TotalDays < 7) return $"{(int)elapsed.TotalDays}d ago";
            return _executedAt.ToString("MMM dd");
        }
    }

    /// <summary>
    /// True when this search history entry is the currently active gallery filter.
    /// </summary>
    public bool IsActiveFilter
    {
        get => _isActiveFilter;
        set { _isActiveFilter = value; OnPropertyChanged(); }
    }

    public string Name
    {
        get => QueryText ?? string.Empty;
        set => QueryText = value;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
