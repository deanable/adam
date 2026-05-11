namespace Adam.Shared.Models;

public class Keyword
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }

    public Keyword? Parent { get; set; }
    public ICollection<Keyword> Children { get; set; } = [];
    public ICollection<DigitalAsset> Assets { get; set; } = [];
}
