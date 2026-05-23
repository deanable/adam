using Adam.Shared.Contracts;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Adam.Shared.Services;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Adam.BrokerService.Handlers;

public sealed class UserHandler
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<UserHandler> _logger;
    private readonly AuditLogger _auditLogger;
    private readonly AuthorizationMiddleware _authz;
    private readonly AuthHandler _authHandler;

    public UserHandler(IServiceProvider serviceProvider, ILogger<UserHandler> logger, AuditLogger auditLogger, AuthorizationMiddleware authz, AuthHandler authHandler)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _auditLogger = auditLogger;
        _authz = authz;
        _authHandler = authHandler;
    }

    public async Task<Envelope> ListUsersAsync(Envelope request, CancellationToken ct)
    {
        if (!await _authz.HasPermissionAsync(request, "user:read", ct))
            return ErrorResponse(request, 7, "Forbidden");

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var users = await db.Users.Include(u => u.Role)
            .OrderBy(u => u.Username)
            .ToListAsync(ct);

        var response = new ListUsersResponse();
        foreach (var u in users)
        {
            response.Items.Add(new UserInfo
            {
                Id = u.Id.ToString(),
                Username = u.Username,
                Email = u.Email,
                RoleId = u.RoleId.ToString(),
                RoleName = u.Role?.Name ?? "",
                IsActive = u.IsActive,
                CreatedAt = u.CreatedAt.ToUnixTimeSeconds(),
                LastLoginAt = u.LastLoginAt?.ToUnixTimeSeconds()
            });
        }

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = nameof(ListUsersResponse),
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(response)),
            StatusCode = 0
        };
    }

    public async Task<Envelope> ListRolesAsync(Envelope request, CancellationToken ct)
    {
        if (!await _authz.HasPermissionAsync(request, "role:read", ct))
            return ErrorResponse(request, 7, "Forbidden");

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var roles = await db.Roles.ToListAsync(ct);
        var response = new ListRolesResponse();
        foreach (var r in roles)
        {
            response.Items.Add(new RoleInfo
            {
                Id = r.Id.ToString(),
                Name = r.Name,
                Permissions = [.. r.Permissions]
            });
        }

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = nameof(ListRolesResponse),
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(response)),
            StatusCode = 0
        };
    }

    public async Task<Envelope> CreateUserAsync(Envelope request, CancellationToken ct)
    {
        if (!await _authz.HasPermissionAsync(request, "user:create", ct))
            return ErrorResponse(request, 7, "Forbidden");

        var createReq = ProtoHelper.Deserialize<CreateUserRequest>(request.Payload.ToByteArray());

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (await db.Users.AnyAsync(u => u.Username == createReq.Username, ct))
            return ErrorResponse(request, 6, "Username already exists");

        if (await db.Users.AnyAsync(u => u.Email == createReq.Email, ct))
            return ErrorResponse(request, 6, "Email already exists");

        var roleId = Guid.Parse(createReq.RoleId);
        if (!await db.Roles.AnyAsync(r => r.Id == roleId, ct))
            return ErrorResponse(request, 5, "Role not found");

        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = createReq.Username,
            Email = createReq.Email,
            PasswordHash = PasswordHelper.HashPassword(createReq.Password),
            RoleId = roleId,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        var userId = _authHandler.GetUserId(request);
        await _auditLogger.LogAsync(db, userId, "Create", "User", user.Id.ToString(), $"Created user {user.Username}");

        var response = new CreateUserResponse
        {
            User = new UserInfo
            {
                Id = user.Id.ToString(),
                Username = user.Username,
                Email = user.Email,
                RoleId = user.RoleId.ToString(),
                IsActive = user.IsActive,
                CreatedAt = user.CreatedAt.ToUnixTimeSeconds()
            }
        };

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = nameof(CreateUserResponse),
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(response)),
            StatusCode = 0
        };
    }

    public async Task<Envelope> UpdateUserAsync(Envelope request, CancellationToken ct)
    {
        if (!await _authz.HasPermissionAsync(request, "user:update", ct))
            return ErrorResponse(request, 7, "Forbidden");

        var updateReq = ProtoHelper.Deserialize<UpdateUserRequest>(request.Payload.ToByteArray());
        var userId = Guid.Parse(updateReq.UserId);

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user == null)
            return ErrorResponse(request, 5, "User not found");

        var changes = new List<string>();

        if (updateReq.Email != null && updateReq.Email != user.Email)
        {
            if (await db.Users.AnyAsync(u => u.Email == updateReq.Email && u.Id != userId, ct))
                return ErrorResponse(request, 6, "Email already exists");
            user.Email = updateReq.Email;
            changes.Add("email");
        }

        if (updateReq.Password != null)
        {
            user.PasswordHash = PasswordHelper.HashPassword(updateReq.Password);
            changes.Add("password");
        }

        if (updateReq.RoleId != null)
        {
            var roleId = Guid.Parse(updateReq.RoleId);
            if (!await db.Roles.AnyAsync(r => r.Id == roleId, ct))
                return ErrorResponse(request, 5, "Role not found");
            user.RoleId = roleId;
            changes.Add("role");
        }

        if (updateReq.IsActive != user.IsActive)
            changes.Add(updateReq.IsActive ? "activated" : "deactivated");
        user.IsActive = updateReq.IsActive;

        db.Users.Update(user);
        await db.SaveChangesAsync(ct);

        var callerId = _authHandler.GetUserId(request);
        await _auditLogger.LogAsync(db, callerId, "Update", "User", user.Id.ToString(),
            $"Updated user {user.Username}: {string.Join(", ", changes)}");

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = nameof(UpdateUserRequest),
            StatusCode = 0
        };
    }

    public async Task<Envelope> DeleteUserAsync(Envelope request, CancellationToken ct)
    {
        if (!await _authz.HasPermissionAsync(request, "user:delete", ct))
            return ErrorResponse(request, 7, "Forbidden");

        var deleteReq = ProtoHelper.Deserialize<DeleteUserRequest>(request.Payload.ToByteArray());
        var userId = Guid.Parse(deleteReq.UserId);

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user == null)
            return ErrorResponse(request, 5, "User not found");

        // Soft-delete: deactivate instead of removing
        user.IsActive = false;
        db.Users.Update(user);
        await db.SaveChangesAsync(ct);

        var callerId = _authHandler.GetUserId(request);
        await _auditLogger.LogAsync(db, callerId, "Delete", "User", user.Id.ToString(),
            $"Deactivated user {user.Username}");

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = nameof(DeleteUserRequest),
            StatusCode = 0
        };
    }

    private static Envelope ErrorResponse(Envelope request, int code, string message)
    {
        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = request.MessageType,
            StatusCode = code,
            ErrorMessage = message
        };
    }
}
