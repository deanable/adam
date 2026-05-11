namespace Adam.Shared.Models;

public class Collection
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? ParentId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }

    public Collection? Parent { get; set; }
    public ICollection<Collection> Children { get; set; } = [];
    public ICollection<DigitalAsset> Assets { get; set; } = [];
}
