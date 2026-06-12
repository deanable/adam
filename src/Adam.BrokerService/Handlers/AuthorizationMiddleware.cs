using Adam.Shared.Contracts;
using Adam.Shared.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Adam.BrokerService.Handlers;

/// <summary>
/// Authorization middleware that checks permissions against the database-stored Role.Permissions.
/// Permissions are resolved from the user's role (extracted from the JWT) and support wildcard
/// matching (e.g., "asset:*" matches "asset:read", "asset:create", etc.).
/// </summary>
public sealed class AuthorizationMiddleware
{
    private readonly IServiceProvider _serviceProvider;
    private readonly AuthHandler _authHandler;
    private readonly ILogger<AuthorizationMiddleware> _logger;

    public AuthorizationMiddleware(IServiceProvider serviceProvider, AuthHandler authHandler, ILogger<AuthorizationMiddleware> logger)
    {
        _serviceProvider = serviceProvider;
        _authHandler = authHandler;
        _logger = logger;
    }

    public async Task<bool> HasPermissionAsync(Envelope request, string requiredPermission, CancellationToken ct = default)
    {
        var roleName = _authHandler.GetUserRole(request);
        if (string.IsNullOrEmpty(roleName))
            return false;

        // Administrator has all permissions (fast-path, no DB query needed)
        if (roleName == "Administrator")
            return true;

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var role = await db.Roles
            .Include(r => r.RolePermissions)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Name == roleName, ct);

        if (role == null)
        {
            _logger.LogWarning("SECURITY: Permission denied — role '{RoleName}' not found for user '{UserId}'. Required: {Permission}. CorrelationId: {CorrelationId}",
                roleName, _authHandler.GetUserId(request), requiredPermission, request.CorrelationId);
            return false;
        }

        var allowed = role.Permissions.Any(p => MatchesPermission(p, requiredPermission));
        if (!allowed)
        {
            _logger.LogWarning("SECURITY: Permission denied — user '{UserId}' with role '{RoleName}' lacks permission '{Permission}'. CorrelationId: {CorrelationId}",
                _authHandler.GetUserId(request), roleName, requiredPermission, request.CorrelationId);
        }
        return allowed;
    }

    /// <summary>
    /// Checks if a permission pattern matches a required permission.
    /// Supports wildcard: "asset:*" matches "asset:read", "asset:create", etc.
    /// Also supports exact matches.
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
