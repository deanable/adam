using Adam.Shared.Contracts;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Adam.Shared.Services;
using Adam.BrokerService.Transport;
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
    private readonly ConnectionRegistry _connectionRegistry;

    public UserHandler(IServiceProvider serviceProvider, ILogger<UserHandler> logger, AuditLogger auditLogger, AuthorizationMiddleware authz, AuthHandler authHandler, ConnectionRegistry connectionRegistry)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _auditLogger = auditLogger;
        _authz = authz;
        _authHandler = authHandler;
        _connectionRegistry = connectionRegistry;
    }

    public async Task<Envelope> ListUsersAsync(Envelope request, CancellationToken ct)
    {
        if (!await _authz.HasPermissionAsync(request, "user:read", ct))
            return ErrorResponse(request, ErrorCode.Forbidden, "Forbidden");

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
            MessageType = MessageTypeCode.ListUsersResponse,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(response)),
            StatusCode = 0
        };
    }

    public async Task<Envelope> ListRolesAsync(Envelope request, CancellationToken ct)
    {
        if (!await _authz.HasPermissionAsync(request, "role:read", ct))
            return ErrorResponse(request, ErrorCode.Forbidden, "Forbidden");

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var roles = await db.Roles.Include(r => r.RolePermissions).ToListAsync(ct);
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
            MessageType = MessageTypeCode.ListRolesResponse,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(response)),
            StatusCode = 0
        };
    }

    public async Task<Envelope> CreateUserAsync(Envelope request, CancellationToken ct)
    {
        if (!await _authz.HasPermissionAsync(request, "user:create", ct))
            return ErrorResponse(request, ErrorCode.Forbidden, "Forbidden");

        if (request.Payload == null)
            return ErrorResponse(request, ErrorCode.BadRequest, "Null payload");
        CreateUserRequest createReq;
        try
        {
            createReq = ProtoHelper.Deserialize<CreateUserRequest>(request.Payload.ToByteArray());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize {MessageType}", request.MessageType);
            return ErrorResponse(request, ErrorCode.BadRequest, "Malformed request payload");
        }

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (await db.Users.AnyAsync(u => u.Username == createReq.Username, ct))
            return ErrorResponse(request, ErrorCode.Conflict, "Username already exists");

        if (await db.Users.AnyAsync(u => u.Email == createReq.Email, ct))
            return ErrorResponse(request, ErrorCode.Conflict, "Email already exists");

        var roleId = Guid.Parse(createReq.RoleId);
        if (!await db.Roles.AnyAsync(r => r.Id == roleId, ct))
            return ErrorResponse(request, ErrorCode.NotFound, "Role not found");

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
            MessageType = MessageTypeCode.CreateUserResponse,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(response)),
            StatusCode = 0
        };
    }

    public async Task<Envelope> UpdateUserAsync(Envelope request, CancellationToken ct)
    {
        if (!await _authz.HasPermissionAsync(request, "user:update", ct))
            return ErrorResponse(request, ErrorCode.Forbidden, "Forbidden");

        if (request.Payload == null)
            return ErrorResponse(request, ErrorCode.BadRequest, "Null payload");
        UpdateUserRequest updateReq;
        try
        {
            updateReq = ProtoHelper.Deserialize<UpdateUserRequest>(request.Payload.ToByteArray());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize {MessageType}", request.MessageType);
            return ErrorResponse(request, ErrorCode.BadRequest, "Malformed request payload");
        }
        var userId = Guid.Parse(updateReq.UserId);

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user == null)
            return ErrorResponse(request, ErrorCode.NotFound, "User not found");

        var changes = new List<string>();

        if (updateReq.Email != null && updateReq.Email != user.Email)
        {
            if (await db.Users.AnyAsync(u => u.Email == updateReq.Email && u.Id != userId, ct))
                return ErrorResponse(request, ErrorCode.Conflict, "Email already exists");
            user.Email = updateReq.Email;
            changes.Add("email");
        }

        if (updateReq.Password != null)
        {
            user.PasswordHash = PasswordHelper.HashPassword(updateReq.Password);
            changes.Add("password");
        }

        bool roleChanged = false;

        if (updateReq.RoleId != null)
        {
            var roleId = Guid.Parse(updateReq.RoleId);
            if (!await db.Roles.AnyAsync(r => r.Id == roleId, ct))
                return ErrorResponse(request, ErrorCode.NotFound, "Role not found");
            user.RoleId = roleId;
            changes.Add("role");
            roleChanged = true;
        }

        if (updateReq.IsActive != user.IsActive)
            changes.Add(updateReq.IsActive ? "activated" : "deactivated");
        user.IsActive = updateReq.IsActive;

        db.Users.Update(user);
        await db.SaveChangesAsync(ct);

        // Phase 7: Notify affected connections when role is changed or account deactivated (T7.5)
        if (roleChanged || !user.IsActive)
        {
            await NotifySessionInvalidatedAsync(user.Id.ToString(), ct);
        }

        var callerId = _authHandler.GetUserId(request);
        await _auditLogger.LogAsync(db, callerId, "Update", "User", user.Id.ToString(),
            $"Updated user {user.Username}: {string.Join(", ", changes)}");

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = MessageTypeCode.UpdateUserRequest,
            StatusCode = 0
        };
    }

    public async Task<Envelope> DeleteUserAsync(Envelope request, CancellationToken ct)
    {
        if (!await _authz.HasPermissionAsync(request, "user:delete", ct))
            return ErrorResponse(request, ErrorCode.Forbidden, "Forbidden");

        if (request.Payload == null)
            return ErrorResponse(request, ErrorCode.BadRequest, "Null payload");
        DeleteUserRequest deleteReq;
        try
        {
            deleteReq = ProtoHelper.Deserialize<DeleteUserRequest>(request.Payload.ToByteArray());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize {MessageType}", request.MessageType);
            return ErrorResponse(request, ErrorCode.BadRequest, "Malformed request payload");
        }
        var userId = Guid.Parse(deleteReq.UserId);
        var callerId = _authHandler.GetUserId(request);

        // Prevent self-deactivation: admin cannot deactivate their own account
        if (userId == Guid.Parse(callerId))
            return ErrorResponse(request, ErrorCode.Forbidden, "Cannot deactivate your own account");

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user == null)
            return ErrorResponse(request, ErrorCode.NotFound, "User not found");

        // Soft-delete: deactivate instead of removing
        user.IsActive = false;
        db.Users.Update(user);
        await db.SaveChangesAsync(ct);

        await _auditLogger.LogAsync(db, callerId, "Delete", "User", user.Id.ToString(),
            $"Deactivated user {user.Username}");

        // Phase 7: Notify affected connections when account is deactivated (T7.5)
        await NotifySessionInvalidatedAsync(user.Id.ToString(), ct);

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = MessageTypeCode.DeleteUserRequest,
            StatusCode = 0
        };
    }

    /// <summary>
    /// Notifies all active connections for the given user that their session has been
    /// invalidated (role changed or account deactivated). The client should call
    /// ValidateTokenAsync to refresh the user profile (Phase 7 T7.5).
    /// </summary>
    private async Task NotifySessionInvalidatedAsync(string userId, CancellationToken ct)
    {
        var connectionIds = _connectionRegistry.GetConnectionIdsByUserId(userId);
        if (connectionIds.Count == 0)
        {
            _logger.LogDebug("No active connections found for user {UserId} — no invalidation sent", userId);
            return;
        }

        var envelope = new Envelope
        {
            CorrelationId = Guid.NewGuid().ToString(),
            MessageType = MessageTypeCode.SessionInvalidated,
            Payload = Google.Protobuf.ByteString.Empty
        };

        _logger.LogInformation("Notifying {Count} connection(s) for user {UserId} of session invalidation", connectionIds.Count, userId);

        foreach (var connId in connectionIds)
        {
            try
            {
                await _connectionRegistry.SendAsync(connId, envelope, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send session invalidation to connection {ConnectionId}", connId);
            }
        }
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
