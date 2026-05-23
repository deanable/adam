namespace Adam.Shared.Models;

public class WatchedFolder
{
    public Guid Id { get; set; }
    public string Path { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.UtcNow;
}
