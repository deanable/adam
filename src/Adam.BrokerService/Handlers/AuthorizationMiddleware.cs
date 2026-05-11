using Adam.Shared.Contracts;

namespace Adam.BrokerService.Handlers;

public static class AuthorizationMiddleware
{
    public static bool HasPermission(Envelope request, string requiredPermission)
    {
        var role = AuthHandler.GetUserRole(request);
        if (string.IsNullOrEmpty(role))
            return false;

        // Administrator has all permissions
        if (role == "Administrator")
            return true;

        return role switch
        {
            "Editor" => requiredPermission switch
            {
                "asset:read" or "asset:create" or "asset:update" or "asset:delete"
                    or "collection:read" or "collection:update"
                    => true,
                _ => false
            },
            "Viewer" => requiredPermission switch
            {
                "asset:read" or "collection:read" => true,
                _ => false
            },
            _ => false
        };
    }
}
