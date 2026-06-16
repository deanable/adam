namespace Adam.Shared.Models;

public sealed class SearchHistoryEntry
{
    public Guid Id { get; set; }
    public string QueryText { get; set; } = string.Empty;
    public string FiltersJson { get; set; } = "{}";
    public bool IsSemantic { get; set; }
    public DateTimeOffset ExecutedAt { get; set; }
    public Guid? UserId { get; set; }

    public User? User { get; set; }
}
