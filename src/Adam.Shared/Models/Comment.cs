namespace Adam.Shared.Models;

public class Comment
{
    public Guid Id { get; set; }
    public Guid AssetId { get; set; }
    public Guid? ParentCommentId { get; set; }
    public Guid UserId { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? EditedAt { get; set; }
    public bool IsDeleted { get; set; }
    public int Version { get; set; } = 1;

    // Navigation
    public DigitalAsset Asset { get; set; } = null!;
    public Comment? ParentComment { get; set; }
    public ICollection<Comment> Replies { get; set; } = [];
    public User User { get; set; } = null!;
}
