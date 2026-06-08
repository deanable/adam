namespace Adam.Shared.Services;

/// <summary>
/// Client-side permission evaluator that maps role names to their known permission sets
/// and supports wildcard matching (e.g., "asset:*" matches "asset:read", "asset:create", etc.).
///
/// This mirrors the server-side <c>AuthorizationMiddleware</c> logic but operates purely on
/// the client — no database or network call required. The permission sets are hardcoded to
/// match the three seeded roles in <c>AppDbContext.SeedData</c>:
///
/// <list type="table">
///   <item><term>Viewer</term>        <description>asset:read, collection:read</description></item>
///   <item><term>Editor</term>        <description>asset:read, asset:create, asset:update, collection:read, collection:update</description></item>
///   <item><term>Administrator</term> <description>asset:*, collection:*, user:*, role:*, audit:read</description></item>
/// </list>
///
/// Unknown role names return <c>false</c> for all permission checks.
/// </summary>
public static class PermissionEvaluator
{
    private static readonly Dictionary<string, string[]> RolePermissions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Viewer"] = ["asset:read", "collection:read"],
        ["Editor"] = ["asset:read", "asset:create", "asset:update", "collection:read", "collection:update"],
        ["Administrator"] = ["asset:*", "collection:*", "user:*", "role:*", "audit:read"]
    };

    /// <summary>
    /// Returns the known permission list for the given role, or an empty array if the role is unknown.
    /// </summary>
    public static string[] GetPermissions(string roleName)
    {
        return RolePermissions.TryGetValue(roleName, out var perms) ? perms : [];
    }

    /// <summary>
    /// Returns all known role names (case-insensitive dictionary keys).
    /// </summary>
    public static IEnumerable<string> KnownRoles => RolePermissions.Keys;

    /// <summary>
    /// Checks whether the specified role has a given permission.
    /// Supports wildcard patterns: "asset:*" matches "asset:read", "asset:create", etc.
    /// Unknown roles return <c>false</c>.
    /// </summary>
    public static bool HasPermission(string roleName, string requiredPermission)
    {
        if (string.IsNullOrWhiteSpace(roleName) || string.IsNullOrWhiteSpace(requiredPermission))
            return false;

        // Administrator has all permissions (matches server-side AuthorizationMiddleware fast-path)
        if (string.Equals(roleName, "Administrator", StringComparison.OrdinalIgnoreCase))
            return true;

        var permissions = GetPermissions(roleName);
        return permissions.Any(p => MatchesPermission(p, requiredPermission));
    }

    /// <summary>
    /// Checks if a permission pattern matches a required permission.
    /// Supports exact match and wildcard suffix (e.g., "asset:*" matches "asset:read").
    /// Mirror of <c>AuthorizationMiddleware.MatchesPermission</c>.
    /// </summary>
    private static bool MatchesPermission(string pattern, string required)
    {
        if (pattern == required)
            return true;

        // Wildcard: e.g., "asset:*" matches "asset:read"
        if (pattern.EndsWith('*'))
        {
            var prefix = pattern.TrimEnd('*');
            return required.StartsWith(prefix, StringComparison.Ordinal);
        }

        return false;
    }
}
