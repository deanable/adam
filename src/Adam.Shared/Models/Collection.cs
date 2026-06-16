namespace Adam.Shared.Models;

/// <summary>
/// Serializable filter criteria used by SavedSearch.FiltersJson and Collection.SmartQueryJson.
/// </summary>
public sealed class SmartQueryFilters
{
    public string? QueryText { get; set; }
    public AssetType? Type { get; set; }
    public Guid? CollectionId { get; set; }
    public string[]? Keywords { get; set; }
    public int? MinRating { get; set; }
    public int? MaxRating { get; set; }
    public long? FromDateUnix { get; set; }
    public long? ToDateUnix { get; set; }
    public string? SortBy { get; set; }
    public string? SortDir { get; set; }
}

public class Collection
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? ParentId { get; set; }
    public bool IsSmart { get; set; }
    public string? SmartQueryJson { get; set; }
    public DateTimeOffset? LastAutoRefreshedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }

    public Collection? Parent { get; set; }
    public ICollection<Collection> Children { get; set; } = [];
    public ICollection<DigitalAsset> Assets { get; set; } = [];
}
