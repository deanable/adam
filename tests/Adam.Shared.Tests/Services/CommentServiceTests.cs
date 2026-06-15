using Adam.Shared.Data;
using Adam.Shared.Models;
using Adam.Shared.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.Shared.Tests.Services;

/// <summary>
/// Tests for CommentService — standalone mode (direct AppDbContext access).
/// </summary>
public sealed class CommentServiceTests : IDisposable
{
    private readonly string _basePath;
    private readonly ModeManager _modeManager;
    private readonly CommentService _sut;
    private readonly AppDbContext _db;

    // Seeded user and asset used across tests
    private User _user = null!;
    private DigitalAsset _asset = null!;

    public CommentServiceTests()
    {
        _basePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _modeManager = new ModeManager(_basePath);
        // Initialize synchronously for test setup
        _modeManager.InitializeAsync().GetAwaiter().GetResult();

        // Create a direct context for seeding test data
        _db = _modeManager.CreateDbContextAsync().GetAwaiter().GetResult();
        SeedTestData();

        _sut = new CommentService(_modeManager, NullLogger<CommentService>.Instance);
    }

    private void SeedTestData()
    {
        _user = new User
        {
            Id = Guid.NewGuid(),
            Username = "testuser",
            Email = "test@example.com",
            PasswordHash = "hash",
            RoleId = Guid.Parse("00000000-0000-0000-0000-000000000003"), // Administrator
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
            ChecksumSha256 = "abc123",
            StoragePath = "/test/test.jpg",
            OriginalPath = "/test/test.jpg",
            Title = "Test Asset",
            Type = AssetType.Image
        };
        _db.DigitalAssets.Add(_asset);
        _db.SaveChanges();
    }

    [Fact]
    public async Task CreateCommentAsync_TopLevel_CreatesComment()
    {
        var dto = await _sut.CreateCommentAsync(_asset.Id, null, "Great photo!", _user.Id);

        Assert.NotNull(dto);
        Assert.Equal("Great photo!", dto.Body);
        Assert.Equal(_user.Username, dto.Username);
        Assert.Null(dto.ParentCommentId);
    }

    [Fact]
    public async Task CreateCommentAsync_Reply_CreatesReply()
    {
        var parent = await _sut.CreateCommentAsync(_asset.Id, null, "Parent", _user.Id);
        var parentId = Guid.Parse(parent.Id);

        var reply = await _sut.CreateCommentAsync(_asset.Id, parentId, "Reply", _user.Id);

        Assert.NotNull(reply);
        Assert.Equal("Reply", reply.Body);
        Assert.Equal(parentId.ToString(), reply.ParentCommentId);
    }

    [Fact]
    public async Task ListCommentsAsync_ReturnsAllComments()
    {
        await _sut.CreateCommentAsync(_asset.Id, null, "First", _user.Id);
        await _sut.CreateCommentAsync(_asset.Id, null, "Second", _user.Id);

        var comments = await _sut.ListCommentsAsync(_asset.Id);

        Assert.Equal(2, comments.Count);
    }

    [Fact]
    public async Task ListCommentsAsync_EmptyAsset_ReturnsEmpty()
    {
        var orphanAssetId = Guid.NewGuid();
        var comments = await _sut.ListCommentsAsync(orphanAssetId);

        Assert.Empty(comments);
    }

    [Fact]
    public async Task UpdateCommentAsync_OwnComment_UpdatesBody()
    {
        var dto = await _sut.CreateCommentAsync(_asset.Id, null, "Original", _user.Id);
        var commentId = Guid.Parse(dto.Id);

        var updated = await _sut.UpdateCommentAsync(commentId, "Updated body", _user.Id);

        Assert.NotNull(updated);
        Assert.Equal("Updated body", updated!.Body);
        Assert.NotNull(updated.EditedAtUnix);
    }

    [Fact]
    public async Task UpdateCommentAsync_NotOwner_ReturnsNull()
    {
        var dto = await _sut.CreateCommentAsync(_asset.Id, null, "Original", _user.Id);
        var commentId = Guid.Parse(dto.Id);
        var otherUserId = Guid.NewGuid();

        var updated = await _sut.UpdateCommentAsync(commentId, "Hacked!", otherUserId);

        Assert.Null(updated);
    }

    [Fact]
    public async Task DeleteCommentAsync_OwnComment_SoftDeletes()
    {
        var dto = await _sut.CreateCommentAsync(_asset.Id, null, "To delete", _user.Id);
        var commentId = Guid.Parse(dto.Id);

        var deleted = await _sut.DeleteCommentAsync(commentId, _user.Id);

        Assert.True(deleted);

        // Verify soft-delete — comment should no longer appear in the list
        // (filtered by the global query filter in AppDbContext)
        var comments = await _sut.ListCommentsAsync(_asset.Id);
        Assert.DoesNotContain(comments, c => c.Id == dto.Id);

        // Verify the comment still exists in the database with IsDeleted = true
        await using var db = await _modeManager.CreateDbContextAsync();
        var dbComment = await db.Comments
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == commentId);
        Assert.NotNull(dbComment);
        Assert.True(dbComment!.IsDeleted);
    }

    [Fact]
    public async Task DeleteCommentAsync_NotOwner_ReturnsFalse()
    {
        var dto = await _sut.CreateCommentAsync(_asset.Id, null, "Mine", _user.Id);
        var commentId = Guid.Parse(dto.Id);
        var otherUserId = Guid.NewGuid();

        var deleted = await _sut.DeleteCommentAsync(commentId, otherUserId);

        Assert.False(deleted);
    }

    [Fact]
    public async Task CountCommentsAsync_ReturnsCorrectCount()
    {
        await _sut.CreateCommentAsync(_asset.Id, null, "A", _user.Id);
        await _sut.CreateCommentAsync(_asset.Id, null, "B", _user.Id);

        var count = await _sut.CountCommentsAsync(_asset.Id);

        Assert.Equal(2, count);
    }

    public void Dispose()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_basePath, recursive: true); } catch { }
    }
}
