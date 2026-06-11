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
/// Unit tests for <see cref="SidebarHandler"/> keyword and category CRUD operations.
/// Uses an in-memory SQLite database with seeded roles and JWT-authenticated users.
/// </summary>
public sealed class SidebarHandlerTests : IAsyncLifetime
{
    private ServiceProvider _serviceProvider = null!;
    private SidebarHandler _handler = null!;
    private AuthorizationMiddleware _authz = null!;
    private AuthHandler _authHandler = null!;
    private SqliteConnection _keepAliveConnection = null!;

    private string _editorToken = null!;
    private string _viewerToken = null!;
    private string _correlationId = null!;

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
        services.AddSingleton<SidebarHandler>();

        _serviceProvider = services.BuildServiceProvider();

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();

        // EnsureCreated seeds default roles via HasData():
        //   Administrator (0003):  asset:*, collection:*, user:*, role:*, audit:read
        //   Editor       (0002):  asset:read, asset:create, asset:update, collection:read, collection:update
        //   Viewer       (0001):  asset:read, collection:read

        var editorRoleId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var viewerRoleId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        var editorUserId = Guid.NewGuid();
        var viewerUserId = Guid.NewGuid();

        db.Users.AddRange(
            new User
            {
                Id = editorUserId,
                Username = "editor",
                Email = "editor@test.com",
                PasswordHash = "hash",
                RoleId = editorRoleId,
                IsActive = true
            },
            new User
            {
                Id = viewerUserId,
                Username = "viewer",
                Email = "viewer@test.com",
                PasswordHash = "hash",
                RoleId = viewerRoleId,
                IsActive = true
            });

        await db.SaveChangesAsync();

        _authHandler = scope.ServiceProvider.GetRequiredService<AuthHandler>();
        _authz = scope.ServiceProvider.GetRequiredService<AuthorizationMiddleware>();
        _handler = scope.ServiceProvider.GetRequiredService<SidebarHandler>();

        _editorToken = _authHandler.GenerateTokenForUser(
            await db.Users.Include(u => u.Role).FirstAsync(u => u.Id == editorUserId));
        _viewerToken = _authHandler.GenerateTokenForUser(
            await db.Users.Include(u => u.Role).FirstAsync(u => u.Id == viewerUserId));

        _correlationId = Guid.NewGuid().ToString();
    }

    public async Task DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();
        _keepAliveConnection?.Close();
        _keepAliveConnection?.Dispose();
    }

    // ──────────────────────────────────────────────
    //  Helper: create an envelope with auth token
    // ──────────────────────────────────────────────

    private static Envelope Request(string token, MessageTypeCode type, ByteString payload) => new()
    {
        AuthToken = token,
        CorrelationId = Guid.NewGuid().ToString(),
        MessageType = type,
        Payload = payload
    };

    // ──────────────────────────────────────────────
    //  CreateKeywordAsync
    // ──────────────────────────────────────────────

    [Fact]
    public async Task CreateKeywordAsync_Editor_Success()
    {
        var req = new CreateKeywordRequest { Name = "TestKeyword" };
        var envelope = Request(_editorToken, MessageTypeCode.CreateKeywordRequest,
            ByteString.CopyFrom(ProtoHelper.Serialize(req)));

        var response = await _handler.CreateKeywordAsync(envelope, default);

        response.StatusCode.Should().Be(0);
        response.MessageType.Should().Be(MessageTypeCode.CreateKeywordResponse);

        var payload = ProtoHelper.Deserialize<CreateKeywordResponse>(response.Payload.ToByteArray());
        Guid.TryParse(payload.Id, out var kwId).Should().BeTrue();
        kwId.Should().NotBeEmpty();

        // Verify it persisted
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var keyword = await db.Keywords.FindAsync(new object[] { kwId });
        keyword.Should().NotBeNull();
        keyword!.Name.Should().Be("TestKeyword");
        keyword.NormalizedName.Should().Be("TESTKEYWORD");
    }

    [Fact]
    public async Task CreateKeywordAsync_Viewer_Forbidden()
    {
        var req = new CreateKeywordRequest { Name = "TestKeyword" };
        var envelope = Request(_viewerToken, MessageTypeCode.CreateKeywordRequest,
            ByteString.CopyFrom(ProtoHelper.Serialize(req)));

        var response = await _handler.CreateKeywordAsync(envelope, default);

        response.StatusCode.Should().Be(7);
        response.ErrorMessage.Should().Be("Forbidden");
    }

    [Fact]
    public async Task CreateKeywordAsync_InvalidToken_Forbidden()
    {
        var req = new CreateKeywordRequest { Name = "TestKeyword" };
        var envelope = Request("bad-token", MessageTypeCode.CreateKeywordRequest,
            ByteString.CopyFrom(ProtoHelper.Serialize(req)));

        var response = await _handler.CreateKeywordAsync(envelope, default);

        response.StatusCode.Should().Be(7);
    }

    // ──────────────────────────────────────────────
    //  UpdateKeywordAsync
    // ──────────────────────────────────────────────

    [Fact]
    public async Task UpdateKeywordAsync_Editor_Success()
    {
        // Arrange: create a keyword first
        using var setupScope = _serviceProvider.CreateScope();
        var setupDb = setupScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var kw = new Keyword { Id = Guid.NewGuid(), Name = "Original", NormalizedName = "ORIGINAL" };
        setupDb.Keywords.Add(kw);
        await setupDb.SaveChangesAsync();

        var req = new UpdateKeywordRequest { Id = kw.Id.ToString(), Name = "Renamed" };
        var envelope = Request(_editorToken, MessageTypeCode.UpdateKeywordRequest,
            ByteString.CopyFrom(ProtoHelper.Serialize(req)));

        var response = await _handler.UpdateKeywordAsync(envelope, default);

        response.StatusCode.Should().Be(0);
        response.CorrelationId.Should().Be(envelope.CorrelationId);

        // Verify it updated
        using var verifyScope = _serviceProvider.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var updated = await verifyDb.Keywords.FindAsync(new object[] { kw.Id });
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("Renamed");
        updated.NormalizedName.Should().Be("RENAMED");
    }

    [Fact]
    public async Task UpdateKeywordAsync_NotFound_ReturnsError()
    {
        var req = new UpdateKeywordRequest { Id = Guid.NewGuid().ToString(), Name = "Ghost" };
        var envelope = Request(_editorToken, MessageTypeCode.UpdateKeywordRequest,
            ByteString.CopyFrom(ProtoHelper.Serialize(req)));

        var response = await _handler.UpdateKeywordAsync(envelope, default);

        response.StatusCode.Should().Be(5);
        response.ErrorMessage.Should().Be("Keyword not found");
    }

    [Fact]
    public async Task UpdateKeywordAsync_Viewer_Forbidden()
    {
        var req = new UpdateKeywordRequest { Id = Guid.NewGuid().ToString(), Name = "Nope" };
        var envelope = Request(_viewerToken, MessageTypeCode.UpdateKeywordRequest,
            ByteString.CopyFrom(ProtoHelper.Serialize(req)));

        var response = await _handler.UpdateKeywordAsync(envelope, default);

        response.StatusCode.Should().Be(7);
    }

    // ──────────────────────────────────────────────
    //  DeleteKeywordAsync
    // ──────────────────────────────────────────────

    [Fact]
    public async Task DeleteKeywordAsync_Editor_Success()
    {
        // Arrange: create a keyword first
        using var setupScope = _serviceProvider.CreateScope();
        var setupDb = setupScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var kw = new Keyword { Id = Guid.NewGuid(), Name = "ToDelete", NormalizedName = "TODELETE" };
        setupDb.Keywords.Add(kw);
        await setupDb.SaveChangesAsync();

        var req = new DeleteKeywordRequest { Id = kw.Id.ToString() };
        var envelope = Request(_editorToken, MessageTypeCode.DeleteKeywordRequest,
            ByteString.CopyFrom(ProtoHelper.Serialize(req)));

        var response = await _handler.DeleteKeywordAsync(envelope, default);

        response.StatusCode.Should().Be(0);
        response.MessageType.Should().Be(MessageTypeCode.DeleteKeywordResponse);

        // Verify deletion
        using var verifyScope = _serviceProvider.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var deleted = await verifyDb.Keywords.FindAsync(new object[] { kw.Id });
        deleted.Should().BeNull();
    }

    [Fact]
    public async Task DeleteKeywordAsync_NotFound_ReturnsError()
    {
        var req = new DeleteKeywordRequest { Id = Guid.NewGuid().ToString() };
        var envelope = Request(_editorToken, MessageTypeCode.DeleteKeywordRequest,
            ByteString.CopyFrom(ProtoHelper.Serialize(req)));

        var response = await _handler.DeleteKeywordAsync(envelope, default);

        response.StatusCode.Should().Be(5);
        response.ErrorMessage.Should().Be("Keyword not found");
    }

    [Fact]
    public async Task DeleteKeywordAsync_Viewer_Forbidden()
    {
        var req = new DeleteKeywordRequest { Id = Guid.NewGuid().ToString() };
        var envelope = Request(_viewerToken, MessageTypeCode.DeleteKeywordRequest,
            ByteString.CopyFrom(ProtoHelper.Serialize(req)));

        var response = await _handler.DeleteKeywordAsync(envelope, default);

        response.StatusCode.Should().Be(7);
    }

    // ──────────────────────────────────────────────
    //  CreateCategoryAsync
    // ──────────────────────────────────────────────

    [Fact]
    public async Task CreateCategoryAsync_Editor_Success()
    {
        var req = new CreateCategoryRequest { Name = "TestCategory" };
        var envelope = Request(_editorToken, MessageTypeCode.CreateCategoryRequest,
            ByteString.CopyFrom(ProtoHelper.Serialize(req)));

        var response = await _handler.CreateCategoryAsync(envelope, default);

        response.StatusCode.Should().Be(0);
        response.MessageType.Should().Be(MessageTypeCode.CreateCategoryResponse);

        var payload = ProtoHelper.Deserialize<CreateCategoryResponse>(response.Payload.ToByteArray());
        Guid.TryParse(payload.Id, out var catId).Should().BeTrue();
        catId.Should().NotBeEmpty();

        // Verify it persisted
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var category = await db.Categories.FindAsync(new object[] { catId });
        category.Should().NotBeNull();
        category!.Name.Should().Be("TestCategory");
        category.NormalizedName.Should().Be("TESTCATEGORY");
    }

    [Fact]
    public async Task CreateCategoryAsync_Viewer_Forbidden()
    {
        var req = new CreateCategoryRequest { Name = "TestCategory" };
        var envelope = Request(_viewerToken, MessageTypeCode.CreateCategoryRequest,
            ByteString.CopyFrom(ProtoHelper.Serialize(req)));

        var response = await _handler.CreateCategoryAsync(envelope, default);

        response.StatusCode.Should().Be(7);
    }

    // ──────────────────────────────────────────────
    //  UpdateCategoryAsync
    // ──────────────────────────────────────────────

    [Fact]
    public async Task UpdateCategoryAsync_Editor_Success()
    {
        // Arrange: create a category first
        using var setupScope = _serviceProvider.CreateScope();
        var setupDb = setupScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cat = new Category { Id = Guid.NewGuid(), Name = "Original", NormalizedName = "ORIGINAL" };
        setupDb.Categories.Add(cat);
        await setupDb.SaveChangesAsync();

        var req = new UpdateCategoryRequest { Id = cat.Id.ToString(), Name = "Renamed" };
        var envelope = Request(_editorToken, MessageTypeCode.UpdateCategoryRequest,
            ByteString.CopyFrom(ProtoHelper.Serialize(req)));

        var response = await _handler.UpdateCategoryAsync(envelope, default);

        response.StatusCode.Should().Be(0);

        // Verify it updated
        using var verifyScope = _serviceProvider.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var updated = await verifyDb.Categories.FindAsync(new object[] { cat.Id });
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("Renamed");
        updated.NormalizedName.Should().Be("RENAMED");
    }

    [Fact]
    public async Task UpdateCategoryAsync_NotFound_ReturnsError()
    {
        var req = new UpdateCategoryRequest { Id = Guid.NewGuid().ToString(), Name = "Ghost" };
        var envelope = Request(_editorToken, MessageTypeCode.UpdateCategoryRequest,
            ByteString.CopyFrom(ProtoHelper.Serialize(req)));

        var response = await _handler.UpdateCategoryAsync(envelope, default);

        response.StatusCode.Should().Be(5);
        response.ErrorMessage.Should().Be("Category not found");
    }

    [Fact]
    public async Task UpdateCategoryAsync_Viewer_Forbidden()
    {
        var req = new UpdateCategoryRequest { Id = Guid.NewGuid().ToString(), Name = "Nope" };
        var envelope = Request(_viewerToken, MessageTypeCode.UpdateCategoryRequest,
            ByteString.CopyFrom(ProtoHelper.Serialize(req)));

        var response = await _handler.UpdateCategoryAsync(envelope, default);

        response.StatusCode.Should().Be(7);
    }

    // ──────────────────────────────────────────────
    //  DeleteCategoryAsync
    // ──────────────────────────────────────────────

    [Fact]
    public async Task DeleteCategoryAsync_Editor_Success()
    {
        // Arrange: create a category first
        using var setupScope = _serviceProvider.CreateScope();
        var setupDb = setupScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cat = new Category { Id = Guid.NewGuid(), Name = "ToDelete", NormalizedName = "TODELETE" };
        setupDb.Categories.Add(cat);
        await setupDb.SaveChangesAsync();

        var req = new DeleteCategoryRequest { Id = cat.Id.ToString() };
        var envelope = Request(_editorToken, MessageTypeCode.DeleteCategoryRequest,
            ByteString.CopyFrom(ProtoHelper.Serialize(req)));

        var response = await _handler.DeleteCategoryAsync(envelope, default);

        response.StatusCode.Should().Be(0);
        response.MessageType.Should().Be(MessageTypeCode.DeleteCategoryResponse);

        // Verify deletion
        using var verifyScope = _serviceProvider.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var deleted = await verifyDb.Categories.FindAsync(new object[] { cat.Id });
        deleted.Should().BeNull();
    }

    [Fact]
    public async Task DeleteCategoryAsync_NotFound_ReturnsError()
    {
        var req = new DeleteCategoryRequest { Id = Guid.NewGuid().ToString() };
        var envelope = Request(_editorToken, MessageTypeCode.DeleteCategoryRequest,
            ByteString.CopyFrom(ProtoHelper.Serialize(req)));

        var response = await _handler.DeleteCategoryAsync(envelope, default);

        response.StatusCode.Should().Be(5);
        response.ErrorMessage.Should().Be("Category not found");
    }

    [Fact]
    public async Task DeleteCategoryAsync_Viewer_Forbidden()
    {
        var req = new DeleteCategoryRequest { Id = Guid.NewGuid().ToString() };
        var envelope = Request(_viewerToken, MessageTypeCode.DeleteCategoryRequest,
            ByteString.CopyFrom(ProtoHelper.Serialize(req)));

        var response = await _handler.DeleteCategoryAsync(envelope, default);

        response.StatusCode.Should().Be(7);
    }
}
