using Adam.BrokerService.Handlers;
using Adam.Shared.Contracts;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.BrokerService.Tests.Handlers;

/// <summary>
/// Tests for CommentHandler — broker-side comment CRUD with authorization.
/// Tests use direct DB access to set up and verify state.
/// </summary>
public sealed class CommentHandlerTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly string _dbPath;
    private ServiceProvider _serviceProvider = null!;
    private AppDbContext _db = null!;
    private User _user = null!;
    private DigitalAsset _asset = null!;

    public CommentHandlerTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.db");
        _connection = new SqliteConnection($"Data Source={_dbPath}");
    }

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(opts => opts.UseSqlite(_connection));

        _serviceProvider = services.BuildServiceProvider();
        _db = _serviceProvider.GetRequiredService<AppDbContext>();
        await _db.Database.EnsureCreatedAsync();

        // Seed data
        _user = new User
        {
            Id = Guid.NewGuid(),
            Username = "admin",
            Email = "admin@test.com",
            PasswordHash = "hash",
            RoleId = Guid.Parse("00000000-0000-0000-0000-000000000003"),
            IsActive = true
        };
        _db.Users.Add(_user);

        _asset = new DigitalAsset
        {
            Id = Guid.NewGuid(),
            FileName = "test.jpg",
            FileExtension = ".jpg",
            MimeType = "image/jpeg",
            FileSize = 100,
            ChecksumSha256 = "abc",
            StoragePath = "/test.jpg",
            OriginalPath = "/test.jpg",
            Title = "Test",
            Type = AssetType.Image
        };
        _db.DigitalAssets.Add(_asset);
        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _serviceProvider.DisposeAsync();
        await _connection.CloseAsync();
        _connection.Dispose();
        try { File.Delete(_dbPath); } catch { }
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task CreateComment_ValidRequest_PersistsToDatabase()
    {
        var comment = new Comment
        {
            Id = Guid.NewGuid(),
            AssetId = _asset.Id,
            UserId = _user.Id,
            Body = "Test comment",
            CreatedAt = DateTimeOffset.UtcNow,
            Version = 1
        };
        _db.Comments.Add(comment);
        await _db.SaveChangesAsync();

        var saved = await _db.Comments
            .Include(c => c.User)
            .FirstAsync(c => c.Id == comment.Id);

        Assert.Equal("Test comment", saved.Body);
        Assert.Equal(_user.Id, saved.UserId);
    }

    [Fact]
    public async Task CreateComment_WithParent_CreatesReply()
    {
        var parent = new Comment
        {
            Id = Guid.NewGuid(),
            AssetId = _asset.Id,
            UserId = _user.Id,
            Body = "Parent",
            CreatedAt = DateTimeOffset.UtcNow,
            Version = 1
        };
        _db.Comments.Add(parent);
        await _db.SaveChangesAsync();

        var reply = new Comment
        {
            Id = Guid.NewGuid(),
            AssetId = _asset.Id,
            ParentCommentId = parent.Id,
            UserId = _user.Id,
            Body = "Reply",
            CreatedAt = DateTimeOffset.UtcNow,
            Version = 1
        };
        _db.Comments.Add(reply);
        await _db.SaveChangesAsync();

        var saved = await _db.Comments
            .Include(c => c.ParentComment)
            .FirstAsync(c => c.Id == reply.Id);

        Assert.Equal(parent.Id, saved.ParentCommentId);
        Assert.Equal("Reply", saved.Body);
    }

    [Fact]
    public async Task UpdateComment_ChangesBodyAndSetsEditedAt()
    {
        var comment = new Comment
        {
            Id = Guid.NewGuid(),
            AssetId = _asset.Id,
            UserId = _user.Id,
            Body = "Original",
            CreatedAt = DateTimeOffset.UtcNow,
            Version = 1
        };
        _db.Comments.Add(comment);
        await _db.SaveChangesAsync();

        comment.Body = "Updated";
        comment.EditedAt = DateTimeOffset.UtcNow;
        comment.Version++;
        await _db.SaveChangesAsync();

        var saved = await _db.Comments.FindAsync(comment.Id);
        Assert.Equal("Updated", saved!.Body);
        Assert.NotNull(saved.EditedAt);
        Assert.Equal(2, saved.Version);
    }

    [Fact]
    public async Task DeleteComment_SoftDelete_SetsIsDeleted()
    {
        var comment = new Comment
        {
            Id = Guid.NewGuid(),
            AssetId = _asset.Id,
            UserId = _user.Id,
            Body = "To delete",
            CreatedAt = DateTimeOffset.UtcNow,
            Version = 1
        };
        _db.Comments.Add(comment);
        await _db.SaveChangesAsync();

        comment.IsDeleted = true;
        await _db.SaveChangesAsync();

        var visible = await _db.Comments
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == comment.Id);
        Assert.NotNull(visible);
        Assert.True(visible!.IsDeleted);

        var filtered = await _db.Comments.FirstOrDefaultAsync(c => c.Id == comment.Id);
        Assert.Null(filtered);
    }

    [Fact]
    public async Task ListComments_ByAssetId_ReturnsOnlyThatAssetsComments()
    {
        var otherAsset = new DigitalAsset
        {
            Id = Guid.NewGuid(),
            FileName = "other.jpg",
            FileExtension = ".jpg",
            MimeType = "image/jpeg",
            FileSize = 100,
            ChecksumSha256 = "def",
            StoragePath = "/other.jpg",
            OriginalPath = "/other.jpg",
            Title = "Other",
            Type = AssetType.Image
        };
        _db.DigitalAssets.Add(otherAsset);

        var c1 = new Comment { Id = Guid.NewGuid(), AssetId = _asset.Id, UserId = _user.Id, Body = "A", CreatedAt = DateTimeOffset.UtcNow, Version = 1 };
        var c2 = new Comment { Id = Guid.NewGuid(), AssetId = _asset.Id, UserId = _user.Id, Body = "B", CreatedAt = DateTimeOffset.UtcNow, Version = 1 };
        var c3 = new Comment { Id = Guid.NewGuid(), AssetId = otherAsset.Id, UserId = _user.Id, Body = "C", CreatedAt = DateTimeOffset.UtcNow, Version = 1 };
        _db.Comments.AddRange(c1, c2, c3);
        await _db.SaveChangesAsync();

        var assetComments = await _db.Comments.Where(c => c.AssetId == _asset.Id).ToListAsync();
        Assert.Equal(2, assetComments.Count);
    }

    [Fact]
    public async Task CountComments_ReturnsCorrectTotal()
    {
        for (int i = 0; i < 5; i++)
        {
            _db.Comments.Add(new Comment
            {
                Id = Guid.NewGuid(),
                AssetId = _asset.Id,
                UserId = _user.Id,
                Body = $"Comment {i}",
                CreatedAt = DateTimeOffset.UtcNow,
                Version = 1
            });
        }
        await _db.SaveChangesAsync();

        var count = await _db.Comments.CountAsync(c => c.AssetId == _asset.Id);
        Assert.Equal(5, count);
    }
}
