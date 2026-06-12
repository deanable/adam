namespace Adam.Shared.Models;

public class Role
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Navigation to the normalised permission rows.
    /// Use this collection when reading or modifying permissions on a tracked entity.
    /// </summary>
    public ICollection<RolePermission> RolePermissions { get; set; } = [];

    /// <summary>
    /// Flattened permission tokens derived from <see cref="RolePermissions"/>.
    /// Read-only convenience accessor — do not assign to this property.
    /// Matches the previous <c>string[]</c> surface so all existing
    /// <c>role.Permissions.Any(…)</c> call sites continue to compile unchanged.
    /// </summary>
    public string[] Permissions => RolePermissions.Select(p => p.Permission).ToArray();

    public ICollection<User> Users { get; set; } = [];
}
