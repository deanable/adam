namespace Adam.Shared.Models;

public class Role
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string[] Permissions { get; set; } = [];

    public ICollection<User> Users { get; set; } = [];
}
