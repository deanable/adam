namespace Adam.Shared.Models;

public sealed class SavedSearch
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? QueryText { get; set; }
    public string FiltersJson { get; set; } = "{}";
    public bool IsPinned { get; set; }
    public Guid? UserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }

    public User? User { get; set; }
}
