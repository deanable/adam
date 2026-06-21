namespace Adam.CatalogBrowser.Models;

/// <summary>
/// Represents a single activity entry in the recent-changes feed.
/// Mapped from ChangeNotification events (multi-user) or AccessLog records (standalone).
/// </summary>
public sealed class ActivityEntry
{
    public string Id { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty; // "Asset", "Keyword", "Collection", "Category"
    public string ChangeType { get; set; } = string.Empty;  // "Created", "Updated", "Deleted"
    public string EntityId { get; set; } = string.Empty;
    public string? AssetName { get; set; }     // Resolved from DB for display
    public string? UserName { get; set; }      // From ChangeNotification or AccessLog
    public DateTimeOffset Timestamp { get; set; }
    public bool IsRead { get; set; }

    /// <summary>Relative time display string, e.g. "2 min ago".</summary>
    public string RelativeTime => GetRelativeTime(Timestamp);

    /// <summary>Icon character for the change type.</summary>
    public string ChangeIcon => ChangeType switch
    {
        "created" => "\u2795",   // ➕
        "updated" => "\u270F\uFE0F", // ✏️
        "deleted" => "\uD83D\uDDD1\uFE0F", // 🗑️
        _ => "\u2139\uFE0F"      // ℹ️
    };

    /// <summary>Color for the change type icon.</summary>
    public string ChangeIconColor => ChangeType switch
    {
        "created" => "#2E7D32",
        "updated" => "#1565C0",
        "deleted" => "#C62828",
        _ => "#757575"
    };

    /// <summary>Display-friendly entity type.</summary>
    public string EntityTypeDisplay => EntityType switch
    {
        "Asset" or "DigitalAsset" => "asset",
        "Keyword" => "keyword",
        "Collection" => "collection",
        "Category" => "category",
        _ => EntityType.ToLowerInvariant()
    };

    /// <summary>Summary text like "Updated asset 'Sunset.png'".</summary>
    public string Summary
    {
        get
        {
            var type = EntityTypeDisplay;
            var name = !string.IsNullOrEmpty(AssetName) ? $" '{AssetName}'" : "";
            return $"{char.ToUpper(ChangeType[0]) + ChangeType[1..]} {type}{name}";
        }
    }

    private static string GetRelativeTime(DateTimeOffset timestamp)
    {
        var diff = DateTimeOffset.UtcNow - timestamp;
        if (diff.TotalMinutes < 1) return "just now";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
        if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}d ago";
        return timestamp.ToString("MMM d");
    }
}
