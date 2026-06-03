using Adam.BrokerService.Handlers;
using Adam.Shared.Contracts;
using Adam.Shared.Data;
using Adam.Shared.Models;
using FluentAssertions;
using Google.Protobuf;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.BrokerService.Tests;

/// <summary>
/// Unit tests for <see cref="AuthorizationMiddleware"/> — the permission-based
/// access control layer that checks role permissions against database-stored roles.
/// </summary>
[Collection("AuthorizationMiddlewareTests")]
public sealed class AuthorizationMiddlewareTests : IAsyncLifetime
{
    private ServiceProvider _serviceProvider = null!;
    private AuthorizationMiddleware _authz = null!;
    private AuthHandler _authHandler = null!;
    private SqliteConnection _keepAliveConnection = null!;

    private Guid _adminUserId;
    private Guid _editorUserId;
    private Guid _viewerUserId;
    private string _adminToken = null!;
    private string _editorToken = null!;
    private string _viewerToken = null!;

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();

        var config = new Microsoft.Extensions.Configuration.ConfigurationManager();
        config["Jwt:SigningKey"] = "dGVzdC1zaWduaW5nLWtleS1mb3ItdGVzdGluZy1vbmx5LTMyLWJ5dGVz";
        config["Jwt:TokenExpiryHours"] = "24";

        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(config);
        services.AddLogging();

        _keepAliveConnection = new SqliteConnection("Data Source=:memory:");
        _keepAliveConnection.Open();

        services.AddDbContext<AppDbContext>(opts =>
            opts.UseSqlite(_keepAliveConnection));

        services.AddSingleton<AuthHandler>();
        services.AddSingleton<AuthorizationMiddleware>();

        _serviceProvider = services.BuildServiceProvider();

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();

        // EnsureCreated() seeds default roles (Administrator, Editor, Viewer) via HasData().
        // Use those seed role GUIDs for our test users.
        var adminRoleId = Guid.Parse("00000000-0000-0000-0000-000000000003");
        var editorRoleId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var viewerRoleId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        _adminUserId = Guid.NewGuid();
        _editorUserId = Guid.NewGuid();
        _viewerUserId = Guid.NewGuid();

        db.Users.AddRange(
            new User { Id = _adminUserId, Username = "admin", Email = "admin@test.com", PasswordHash = "hash", RoleId = adminRoleId, IsActive = true },
            new User { Id = _editorUserId, Username = "editor", Email = "editor@test.com", PasswordHash = "hash", RoleId = editorRoleId, IsActive = true },
            new User { Id = _viewerUserId, Username = "viewer", Email = "viewer@test.com", PasswordHash = "hash", RoleId = viewerRoleId, IsActive = true }
        );

        await db.SaveChangesAsync();

        _authHandler = scope.ServiceProvider.GetRequiredService<AuthHandler>();
        _authz = scope.ServiceProvider.GetRequiredService<AuthorizationMiddleware>();

        _adminToken = _authHandler.GenerateTokenForUser(await db.Users.Include(u => u.Role).FirstAsync(u => u.Id == _adminUserId));
        _editorToken = _authHandler.GenerateTokenForUser(await db.Users.Include(u => u.Role).FirstAsync(u => u.Id == _editorUserId));
        _viewerToken = _authHandler.GenerateTokenForUser(await db.Users.Include(u => u.Role).FirstAsync(u => u.Id == _viewerUserId));
    }

    public async Task DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();
        _keepAliveConnection?.Close();
        _keepAliveConnection?.Dispose();
    }

    private Envelope Request(string token) => new()
    {
        AuthToken = token,
        CorrelationId = Guid.NewGuid().ToString(),
        MessageType = MessageTypeCode.ListAssetsRequest,
        Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new ListAssetsRequest()))
    };

    // =========================================================================
    // Administrator — fast-path (role name check, no DB query)
    // =========================================================================

    [Fact]
    public async Task Admin_HasPermission_AssetRead_ReturnsTrue() =>
        (await _authz.HasPermissionAsync(Request(_adminToken), "asset:read")).Should().BeTrue();

    [Fact]
    public async Task Admin_HasPermission_AssetCreate_ReturnsTrue() =>
        (await _authz.HasPermissionAsync(Request(_adminToken), "asset:create")).Should().BeTrue();

    [Fact]
    public async Task Admin_HasPermission_UserDelete_ReturnsTrue() =>
        (await _authz.HasPermissionAsync(Request(_adminToken), "user:delete")).Should().BeTrue();

    [Fact]
    public async Task Admin_HasPermission_AuditRead_ReturnsTrue() =>
        (await _authz.HasPermissionAsync(Request(_adminToken), "audit:read")).Should().BeTrue();

    [Fact]
    public async Task Admin_HasPermission_AnyUnknown_ReturnsTrue() =>
        (await _authz.HasPermissionAsync(Request(_adminToken), "some:unknown:permission")).Should().BeTrue();

    // =========================================================================
    // Editor
    // =========================================================================

    [Fact]
    public async Task Editor_HasPermission_AssetRead_ReturnsTrue() =>
        (await _authz.HasPermissionAsync(Request(_editorToken), "asset:read")).Should().BeTrue();

    [Fact]
    public async Task Editor_HasPermission_AssetCreate_ReturnsTrue() =>
        (await _authz.HasPermissionAsync(Request(_editorToken), "asset:create")).Should().BeTrue();

    [Fact]
    public async Task Editor_HasPermission_AssetUpdate_ReturnsTrue() =>
        (await _authz.HasPermissionAsync(Request(_editorToken), "asset:update")).Should().BeTrue();

    [Fact]
    public async Task Editor_HasPermission_CollectionRead_ReturnsTrue() =>
        (await _authz.HasPermissionAsync(Request(_editorToken), "collection:read")).Should().BeTrue();

    [Fact]
    public async Task Editor_DoesNotHavePermission_AssetDelete_ReturnsFalse() =>
        (await _authz.HasPermissionAsync(Request(_editorToken), "asset:delete")).Should().BeFalse();

    [Fact]
    public async Task Editor_DoesNotHavePermission_UserRead_ReturnsFalse() =>
        (await _authz.HasPermissionAsync(Request(_editorToken), "user:read")).Should().BeFalse();

    [Fact]
    public async Task Editor_DoesNotHavePermission_AuditRead_ReturnsFalse() =>
        (await _authz.HasPermissionAsync(Request(_editorToken), "audit:read")).Should().BeFalse();

    // =========================================================================
    // Viewer
    // =========================================================================

    [Fact]
    public async Task Viewer_HasPermission_AssetRead_ReturnsTrue() =>
        (await _authz.HasPermissionAsync(Request(_viewerToken), "asset:read")).Should().BeTrue();

    [Fact]
    public async Task Viewer_HasPermission_CollectionRead_ReturnsTrue() =>
        (await _authz.HasPermissionAsync(Request(_viewerToken), "collection:read")).Should().BeTrue();

    [Fact]
    public async Task Viewer_DoesNotHavePermission_AssetCreate_ReturnsFalse() =>
        (await _authz.HasPermissionAsync(Request(_viewerToken), "asset:create")).Should().BeFalse();

    [Fact]
    public async Task Viewer_DoesNotHavePermission_AssetUpdate_ReturnsFalse() =>
        (await _authz.HasPermissionAsync(Request(_viewerToken), "asset:update")).Should().BeFalse();

    [Fact]
    public async Task Viewer_DoesNotHavePermission_AssetDelete_ReturnsFalse() =>
        (await _authz.HasPermissionAsync(Request(_viewerToken), "asset:delete")).Should().BeFalse();

    [Fact]
    public async Task Viewer_DoesNotHavePermission_UserRead_ReturnsFalse() =>
        (await _authz.HasPermissionAsync(Request(_viewerToken), "user:read")).Should().BeFalse();

    [Fact]
    public async Task Viewer_DoesNotHavePermission_AuditRead_ReturnsFalse() =>
        (await _authz.HasPermissionAsync(Request(_viewerToken), "audit:read")).Should().BeFalse();

    // =========================================================================
    // Edge cases
    // =========================================================================

    [Fact]
    public async Task EmptyToken_ReturnsFalse() =>
        (await _authz.HasPermissionAsync(Request(""), "asset:read")).Should().BeFalse();

    [Fact]
    public async Task InvalidToken_ReturnsFalse() =>
        (await _authz.HasPermissionAsync(Request("this-is-not-a-valid-jwt-token"), "asset:read")).Should().BeFalse();

    [Fact]
    public async Task RoleNotFound_ReturnsFalse()
    {
        // Create user+role, generate token, then delete the role from DB
        var fakeUserId = Guid.NewGuid();
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var orphanRole = new Role { Id = Guid.NewGuid(), Name = "OrphanRole", Permissions = ["asset:*"] };
        db.Roles.Add(orphanRole);
        var orphanUser = new User { Id = fakeUserId, Username = "orphan", Email = "orphan@test.com", PasswordHash = "hash", RoleId = orphanRole.Id, IsActive = true };
        db.Users.Add(orphanUser);
        await db.SaveChangesAsync();

        var token = _authHandler.GenerateTokenForUser(orphanUser);

        db.Roles.Remove(orphanRole);
        await db.SaveChangesAsync();

        var result = await _authz.HasPermissionAsync(Request(token), "asset:read");
        result.Should().BeFalse();
    }

    [Fact]
    public async Task RoleWithEmptyPermissions_DeniesAll()
    {
        var emptyRoleUser = Guid.NewGuid();
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var emptyRole = new Role { Id = Guid.NewGuid(), Name = "EmptyPermRole", Permissions = [] };
        db.Roles.Add(emptyRole);
        db.Users.Add(new User { Id = emptyRoleUser, Username = "emptyperm", Email = "empty@test.com", PasswordHash = "hash", RoleId = emptyRole.Id, IsActive = true });
        await db.SaveChangesAsync();

        var token = _authHandler.GenerateTokenForUser(
            await db.Users.Include(u => u.Role).FirstAsync(u => u.Id == emptyRoleUser));

        var result = await _authz.HasPermissionAsync(Request(token), "asset:read");
        result.Should().BeFalse();
    }
}
