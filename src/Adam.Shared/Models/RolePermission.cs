namespace Adam.Shared.Models;

/// <summary>
/// Join entity representing a single permission granted to a <see cref="Role"/>.
/// Replaces the previous comma-separated string storage so permissions are individually
/// queryable and structurally sound.
/// </summary>
public class RolePermission
{
    public Guid RoleId { get; set; }

    /// <summary>
    /// Permission token, e.g. "asset:read", "asset:*", "collection:update".
    /// </summary>
    public string Permission { get; set; } = string.Empty;

    public Role Role { get; set; } = null!;
}
