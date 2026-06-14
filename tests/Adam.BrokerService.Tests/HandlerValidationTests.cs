using Adam.BrokerService.Handlers;
using Adam.BrokerService.Transport;
using Adam.Shared.Contracts;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Adam.Shared.Services;
using FluentAssertions;
using Google.Protobuf;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Adam.BrokerService.Tests;

/// <summary>
/// Validates that all broker handler methods properly guard against null and
/// malformed payloads (T13-T2, T13-T3). Every handler method that calls
/// <see cref="ProtoHelper.Deserialize{T}(byte[])"/> must return
/// <see cref="ErrorCode.BadRequest"/> with appropriate error messages
/// when the payload is null or contains invalid data.
/// </summary>
public sealed class HandlerValidationTests : IAsyncLifetime
{
    private ServiceProvider _serviceProvider = null!;
    private SqliteConnection _keepAliveConnection = null!;

    private AuthHandler _authHandler = null!;
    private AssetHandler _assetHandler = null!;
    private CollectionHandler _collectionHandler = null!;
    private UserHandler _userHandler = null!;
    private AuditLogHandler _auditLogHandler = null!;
    private ChangeHandler _changeHandler = null!;
    private SidebarHandler _sidebarHandler = null!;
    private WatchedFolderHandler _watchedFolderHandler = null!;

    private string _adminToken = null!;

    /// <summary>Random bytes that cannot be parsed as any protobuf message.</summary>
    private static readonly ByteString MalformedBytes =
        ByteString.CopyFrom(new byte[] { 0xFF, 0xFE, 0xFD, 0xFC, 0xFB, 0xFA });

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

        // Register all handlers and their dependencies
        services.AddSingleton<AuthHandler>();
        services.AddSingleton<AssetHandler>();
        services.AddSingleton<CollectionHandler>();
        services.AddSingleton<UserHandler>();
        services.AddSingleton<AuditLogHandler>();
        services.AddSingleton<ChangeHandler>();
        services.AddSingleton<SidebarHandler>();
        services.AddSingleton<WatchedFolderHandler>();
        services.AddSingleton<AuthorizationMiddleware>();
        services.AddSingleton<ChangeNotificationService>();
        services.AddSingleton<ConnectionRegistry>();
        services.AddSingleton<LoginRateLimiter>();
        services.AddSingleton<KeywordService>();
        services.AddSingleton<MetadataWritebackService>();

        // StatusHandler needs IServiceInstaller — register null installer for non-service tests
        services.AddSingleton<AuditLogger>();
        services.AddSingleton<StatusHandler>();

        _serviceProvider = services.BuildServiceProvider();

        // Seed the database with default roles and an admin user
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();

        var adminRoleId = Guid.Parse("00000000-0000-0000-0000-000000000003");
        var adminUserId = Guid.NewGuid();

        db.Users.Add(new User
        {
            Id = adminUserId,
            Username = "admin",
            Email = "admin@test.com",
            PasswordHash = "hash",
            RoleId = adminRoleId,
            IsActive = true
        });

        await db.SaveChangesAsync();

        _authHandler = scope.ServiceProvider.GetRequiredService<AuthHandler>();
        _assetHandler = scope.ServiceProvider.GetRequiredService<AssetHandler>();
        _collectionHandler = scope.ServiceProvider.GetRequiredService<CollectionHandler>();
        _userHandler = scope.ServiceProvider.GetRequiredService<UserHandler>();
        _auditLogHandler = scope.ServiceProvider.GetRequiredService<AuditLogHandler>();
        _changeHandler = scope.ServiceProvider.GetRequiredService<ChangeHandler>();
        _sidebarHandler = scope.ServiceProvider.GetRequiredService<SidebarHandler>();
        _watchedFolderHandler = scope.ServiceProvider.GetRequiredService<WatchedFolderHandler>();

        _adminToken = _authHandler.GenerateTokenForUser(
            await db.Users.Include(u => u.Role).FirstAsync(u => u.Id == adminUserId));
    }

    public async Task DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();
        _keepAliveConnection?.Close();
        _keepAliveConnection?.Dispose();
    }

    // ─────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────

    private static Envelope Request(MessageTypeCode type, ByteString? payload, string? token) => new()
    {
        AuthToken = token ?? "",
        CorrelationId = Guid.NewGuid().ToString(),
        MessageType = type,
        Payload = payload
    };

    /// <summary>
    /// Asserts the handler returns <see cref="ErrorCode.BadRequest"/> when
    /// the payload is null.
    /// </summary>
    private static async Task ExpectNullPayloadError(
        Func<Envelope, CancellationToken, Task<Envelope>> handler,
        MessageTypeCode type,
        string token)
    {
        var envelope = Request(type, payload: null, token);
        var response = await handler(envelope, default);
        response.StatusCode.Should().Be(ErrorCode.BadRequest,
            "null payload should return BadRequest");
        response.ErrorMessage.Should().Be("Null payload",
            "null payload must produce the exact error message 'Null payload'");
    }

    /// <summary>
    /// Asserts the handler returns <see cref="ErrorCode.BadRequest"/> when
    /// the payload contains random bytes that cannot be deserialized.
    /// </summary>
    private static async Task ExpectMalformedPayloadError(
        Func<Envelope, CancellationToken, Task<Envelope>> handler,
        MessageTypeCode type,
        string token)
    {
        var envelope = Request(type, MalformedBytes, token);
        var response = await handler(envelope, default);
        response.StatusCode.Should().Be(ErrorCode.BadRequest,
            "malformed payload should return BadRequest");
        response.ErrorMessage.Should().Be("Malformed request payload",
            "malformed payload must produce the exact error message 'Malformed request payload'");
    }

    /// <summary>
    /// Runs both null and malformed payload checks for a single handler method.
    /// </summary>
    private static async Task VerifyGuards(
        Func<Envelope, CancellationToken, Task<Envelope>> handler,
        MessageTypeCode type,
        string token)
    {
        await ExpectNullPayloadError(handler, type, token);
        await ExpectMalformedPayloadError(handler, type, token);
    }

    // ─────────────────────────────────────────────────────────────
    //  AuthHandler
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AuthHandler_LoginAsync_Guards() =>
        await VerifyGuards(_authHandler.LoginAsync, MessageTypeCode.LoginRequest, token: "");

    // ─────────────────────────────────────────────────────────────
    //  AssetHandler
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AssetHandler_ListAssetsAsync_Guards() =>
        await VerifyGuards(_assetHandler.ListAssetsAsync, MessageTypeCode.ListAssetsRequest, _adminToken);

    [Fact]
    public async Task AssetHandler_GetAssetAsync_Guards() =>
        await VerifyGuards(_assetHandler.GetAssetAsync, MessageTypeCode.GetAssetRequest, _adminToken);

    [Fact]
    public async Task AssetHandler_UpdateAssetAsync_Guards() =>
        await VerifyGuards(_assetHandler.UpdateAssetAsync, MessageTypeCode.UpdateAssetRequest, _adminToken);

    [Fact]
    public async Task AssetHandler_CreateAssetAsync_Guards() =>
        await VerifyGuards(_assetHandler.CreateAssetAsync, MessageTypeCode.CreateAssetRequest, _adminToken);

    [Fact]
    public async Task AssetHandler_GetFileAsync_Guards() =>
        await VerifyGuards(_assetHandler.GetFileAsync, MessageTypeCode.GetFileRequest, _adminToken);

    [Fact]
    public async Task AssetHandler_RestoreAssetAsync_Guards() =>
        await VerifyGuards(_assetHandler.RestoreAssetAsync, MessageTypeCode.RestoreAssetRequest, _adminToken);

    [Fact]
    public async Task AssetHandler_ListDeletedAssetsAsync_Guards() =>
        await VerifyGuards(_assetHandler.ListDeletedAssetsAsync, MessageTypeCode.ListDeletedAssetsRequest, _adminToken);

    [Fact]
    public async Task AssetHandler_DeleteAssetAsync_Guards() =>
        await VerifyGuards(_assetHandler.DeleteAssetAsync, MessageTypeCode.DeleteAssetRequest, _adminToken);

    [Fact]
    public async Task AssetHandler_GetFileChunkAsync_Guards() =>
        await VerifyGuards(_assetHandler.GetFileChunkAsync, MessageTypeCode.GetFileChunkRequest, _adminToken);

    [Fact]
    public async Task AssetHandler_PermanentDeleteAssetAsync_Guards() =>
        await VerifyGuards(_assetHandler.PermanentDeleteAssetAsync, MessageTypeCode.PermanentDeleteAssetRequest, _adminToken);

    [Fact]
    public async Task AssetHandler_BulkPermanentDeleteAssetAsync_Guards() =>
        await VerifyGuards(_assetHandler.BulkPermanentDeleteAssetAsync, MessageTypeCode.BulkPermanentDeleteAssetRequest, _adminToken);

    // ─────────────────────────────────────────────────────────────
    //  CollectionHandler
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CollectionHandler_CreateCollectionAsync_Guards() =>
        await VerifyGuards(_collectionHandler.CreateCollectionAsync, MessageTypeCode.CreateCollectionRequest, _adminToken);

    [Fact]
    public async Task CollectionHandler_UpdateCollectionAsync_Guards() =>
        await VerifyGuards(_collectionHandler.UpdateCollectionAsync, MessageTypeCode.UpdateCollectionRequest, _adminToken);

    [Fact]
    public async Task CollectionHandler_DeleteCollectionAsync_Guards() =>
        await VerifyGuards(_collectionHandler.DeleteCollectionAsync, MessageTypeCode.DeleteCollectionRequest, _adminToken);

    // ─────────────────────────────────────────────────────────────
    //  UserHandler
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task UserHandler_CreateUserAsync_Guards() =>
        await VerifyGuards(_userHandler.CreateUserAsync, MessageTypeCode.CreateUserRequest, _adminToken);

    [Fact]
    public async Task UserHandler_UpdateUserAsync_Guards() =>
        await VerifyGuards(_userHandler.UpdateUserAsync, MessageTypeCode.UpdateUserRequest, _adminToken);

    [Fact]
    public async Task UserHandler_DeleteUserAsync_Guards() =>
        await VerifyGuards(_userHandler.DeleteUserAsync, MessageTypeCode.DeleteUserRequest, _adminToken);

    // ─────────────────────────────────────────────────────────────
    //  AuditLogHandler
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AuditLogHandler_ListAuditLogsAsync_Guards() =>
        await VerifyGuards(_auditLogHandler.ListAuditLogsAsync, MessageTypeCode.ListAuditLogsRequest, _adminToken);

    // ─────────────────────────────────────────────────────────────
    //  ChangeHandler
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChangeHandler_GetChangesAsync_Guards() =>
        await VerifyGuards(_changeHandler.GetChangesAsync, MessageTypeCode.GetChangesRequest, _adminToken);

    // ─────────────────────────────────────────────────────────────
    //  SidebarHandler
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task SidebarHandler_CreateKeywordAsync_Guards() =>
        await VerifyGuards(_sidebarHandler.CreateKeywordAsync, MessageTypeCode.CreateKeywordRequest, _adminToken);

    [Fact]
    public async Task SidebarHandler_UpdateKeywordAsync_Guards() =>
        await VerifyGuards(_sidebarHandler.UpdateKeywordAsync, MessageTypeCode.UpdateKeywordRequest, _adminToken);

    [Fact]
    public async Task SidebarHandler_DeleteKeywordAsync_Guards() =>
        await VerifyGuards(_sidebarHandler.DeleteKeywordAsync, MessageTypeCode.DeleteKeywordRequest, _adminToken);

    [Fact]
    public async Task SidebarHandler_CreateCategoryAsync_Guards() =>
        await VerifyGuards(_sidebarHandler.CreateCategoryAsync, MessageTypeCode.CreateCategoryRequest, _adminToken);

    [Fact]
    public async Task SidebarHandler_UpdateCategoryAsync_Guards() =>
        await VerifyGuards(_sidebarHandler.UpdateCategoryAsync, MessageTypeCode.UpdateCategoryRequest, _adminToken);

    [Fact]
    public async Task SidebarHandler_DeleteCategoryAsync_Guards() =>
        await VerifyGuards(_sidebarHandler.DeleteCategoryAsync, MessageTypeCode.DeleteCategoryRequest, _adminToken);

    // ─────────────────────────────────────────────────────────────
    //  WatchedFolderHandler
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task WatchedFolderHandler_CreateAsync_Guards() =>
        await VerifyGuards(_watchedFolderHandler.CreateAsync, MessageTypeCode.CreateWatchedFolderRequest, _adminToken);

    [Fact]
    public async Task WatchedFolderHandler_UpdateAsync_Guards() =>
        await VerifyGuards(_watchedFolderHandler.UpdateAsync, MessageTypeCode.UpdateWatchedFolderRequest, _adminToken);

    [Fact]
    public async Task WatchedFolderHandler_DeleteAsync_Guards() =>
        await VerifyGuards(_watchedFolderHandler.DeleteAsync, MessageTypeCode.DeleteWatchedFolderRequest, _adminToken);
}
