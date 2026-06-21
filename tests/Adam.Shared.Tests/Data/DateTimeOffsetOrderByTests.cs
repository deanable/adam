using Adam.Shared.Data;
using Adam.Shared.Models;
using Adam.Shared.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Adam.Shared.Tests.Data;

/// <summary>
/// Verifies that DateTimeOffset ORDER BY works in SQLite mode.
///
/// ## Key Finding
///
/// The SQLite EF Core provider throws NotSupportedException when any
/// DateTimeOffset member is used in ORDER BY — including .UtcDateTime,
/// .UtcTicks, and EF.Property. The ONLY approach that works is
/// **client-side ordering**: load records to memory first, then sort
/// with LINQ-to-Objects.
///
/// ## Fix Pattern (Client-Side Ordering)
///
/// ```csharp
/// // ❌ These all throw NotSupportedException on SQLite:
/// .OrderBy(x => x.DateTimeOffsetProperty)
/// .OrderBy(x => x.DateTimeOffsetProperty.UtcDateTime)
/// .OrderBy(x => EF.Property<object>(x, "Property"))
/// .OrderBy(x => EF.Property<string>(x, "Property"))
///
/// ✅ Load to memory, then sort with LINQ-to-Objects:
/// var results = await query.ToListAsync(ct);
/// return [.. results.OrderByDescending(x => x.DateTimeOffsetProperty)];
/// ```
///
/// For queries with Skip/Take pagination (e.g. SearchService, AssetGalleryViewModel),
/// use a two-query approach:
/// 1. Load ALL matching IDs + sort columns, sort in memory, apply Skip/Take
/// 2. Load full entities by the paginated IDs
///
/// ## Entity Types Covered
///   - SearchHistoryEntry.ExecutedAt
///   - AccessLog.Timestamp
///   - Comment.CreatedAt
///   - DigitalAsset.CreatedAt
///   - DigitalAsset.ModifiedAt
/// </summary>
public sealed class DateTimeOffsetOrderByTests : IDisposable
{
    private readonly SqliteConnection _sqlite;
    private readonly DbContextOptions<AppDbContext> _options;

    public DateTimeOffsetOrderByTests()
    {
        _sqlite = new SqliteConnection("DataSource=:memory:");
        _sqlite.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_sqlite).Options;
        using var db = new AppDbContext(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _sqlite.Close();
        _sqlite.Dispose();
    }

    private AppDbContext CreateContext() => new(_options);

    // ══════════════════════════════════════════════
    //  SearchHistoryEntry.ExecutedAt
    // ══════════════════════════════════════════════

    [Fact]
    public async Task OrderBySearchHistoryExecutedAtDesc_ClientSide_Works()
    {
        using var db = CreateContext();
        await SeedSearchHistoryAsync(db);

        // Load without ORDER BY, then sort in memory
        var items = await db.SearchHistoryEntries
            .AsNoTracking()
            .ToListAsync();

        var results = items
            .OrderByDescending(s => s.ExecutedAt)
            .ToList();

        results.Should().HaveCount(2);
        results[0].QueryText.Should().Be("newer");
        results[1].QueryText.Should().Be("older");
    }

    // ══════════════════════════════════════════════
    //  AccessLog.Timestamp
    // ══════════════════════════════════════════════

    [Fact]
    public async Task OrderByAccessLogTimestampDesc_ClientSide_Works()
    {
        using var db = CreateContext();
        await SeedAccessLogsAsync(db);

        // Load without ORDER BY, then sort in memory
        var items = await db.AccessLogs
            .Include(l => l.User)
            .ToListAsync();

        var results = items
            .OrderByDescending(l => l.Timestamp)
            .ToList();

        results.Should().HaveCount(2);
        results[0].Action.Should().Be("deleted");
        results[1].Action.Should().Be("created");
    }

    // ══════════════════════════════════════════════
    //  Comment.CreatedAt
    // ══════════════════════════════════════════════

    [Fact]
    public async Task OrderByCommentCreatedAt_ClientSide_Works()
    {
        using var db = CreateContext();
        await SeedCommentsAsync(db);

        // Load without ORDER BY, then sort in memory
        var items = await db.Comments
            .Include(c => c.User)
            .ToListAsync();

        var results = items
            .OrderBy(c => c.CreatedAt)
            .ToList();

        results.Should().HaveCount(2);
        results[0].Body.Should().Be("Older comment");
        results[1].Body.Should().Be("Newer comment");
    }

    // ══════════════════════════════════════════════
    //  DigitalAsset.CreatedAt / ModifiedAt
    // ══════════════════════════════════════════════

    [Fact]
    public async Task OrderByDigitalAssetCreatedAtDesc_ClientSide_Works()
    {
        using var db = CreateContext();
        await SeedDigitalAssetsAsync(db);

        // Load without ORDER BY, then sort in memory
        var items = await db.DigitalAssets
            .ToListAsync();

        var results = items
            .OrderByDescending(a => a.CreatedAt)
            .ToList();

        results.Should().HaveCount(2);
        results[0].FileName.Should().Be("newer.jpg");
        results[1].FileName.Should().Be("older.jpg");
    }

    [Fact]
    public async Task OrderByDigitalAssetModifiedAtDesc_ClientSide_Works()
    {
        using var db = CreateContext();
        await SeedDigitalAssetsAsync(db);

        var items = await db.DigitalAssets.ToListAsync();
        var results = items.OrderByDescending(a => a.ModifiedAt).ToList();

        results.Should().HaveCount(2);
        results[0].FileName.Should().Be("newer.jpg");
    }

    // ══════════════════════════════════════════════
    //  SearchService integration — DateAdded sort
    // ══════════════════════════════════════════════

    [Fact]
    public async Task SearchService_DateAddedDesc_EndToEnd_Works()
    {
        // Use in-memory SQLite to test SearchService's actual two-query implementation
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_sqlite)
            .Options;
        using var db = new AppDbContext(options);
        db.Database.EnsureCreated();

        // Seed assets with known CreatedAt values
        db.DigitalAssets.AddRange(
            new DigitalAsset
            {
                Id = Guid.NewGuid(), FileName = "old.jpg", Title = "Old",
                MimeType = "image/jpeg", FileExtension = ".jpg", FileSize = 100,
                ChecksumSha256 = new string('a', 64), StoragePath = "/a", OriginalPath = "/a",
                Type = AssetType.Image,
                CreatedAt = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
                ModifiedAt = DateTimeOffset.UtcNow
            },
            new DigitalAsset
            {
                Id = Guid.NewGuid(), FileName = "new.jpg", Title = "New",
                MimeType = "image/jpeg", FileExtension = ".jpg", FileSize = 200,
                ChecksumSha256 = new string('b', 64), StoragePath = "/b", OriginalPath = "/b",
                Type = AssetType.Image,
                CreatedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
                ModifiedAt = DateTimeOffset.UtcNow
            });
        await db.SaveChangesAsync();

        // Act: call the actual SearchService with DateAdded sort
        var searchService = new SearchService(db);
        var results = await searchService.SearchAsync(sortBy: "DateAdded", sortDir: "desc");

        // Assert
        results.Should().HaveCount(2);
        results[0].FileName.Should().Be("new.jpg");
        results[1].FileName.Should().Be("old.jpg");
    }

    // ══════════════════════════════════════════════
    //  Seed helpers
    // ══════════════════════════════════════════════

    private static async Task SeedDigitalAssetsAsync(AppDbContext db)
    {
        var collection = new Collection
        {
            Id = Guid.NewGuid(), Name = "Test",
            CreatedAt = DateTimeOffset.UtcNow, ModifiedAt = DateTimeOffset.UtcNow
        };
        db.Collections.Add(collection);

        db.DigitalAssets.AddRange(
            new DigitalAsset
            {
                Id = Guid.NewGuid(), FileName = "older.jpg", Title = "Older",
                MimeType = "image/jpeg", FileExtension = ".jpg", FileSize = 100,
                ChecksumSha256 = new string('a', 64), StoragePath = "/a", OriginalPath = "/a",
                Type = AssetType.Image, CollectionId = collection.Id,
                CreatedAt = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
                ModifiedAt = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
                MetadataProfile = new MetadataProfile { Id = Guid.NewGuid() }
            },
            new DigitalAsset
            {
                Id = Guid.NewGuid(), FileName = "newer.jpg", Title = "Newer",
                MimeType = "image/jpeg", FileExtension = ".jpg", FileSize = 200,
                ChecksumSha256 = new string('b', 64), StoragePath = "/b", OriginalPath = "/b",
                Type = AssetType.Image, CollectionId = collection.Id,
                CreatedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
                ModifiedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
                MetadataProfile = new MetadataProfile { Id = Guid.NewGuid() }
            });
        await db.SaveChangesAsync();
    }

    private static async Task SeedAccessLogsAsync(AppDbContext db)
    {
        var user = new User
        {
            Id = Guid.NewGuid(), Username = "testuser", Email = "t@t.com",
            PasswordHash = "hash", RoleId = Guid.Parse("00000000-0000-0000-0000-000000000003"),
            IsActive = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.AccessLogs.AddRange(
            new AccessLog { Id = Guid.NewGuid(), UserId = user.Id, Action = "created", EntityType = "Asset", Timestamp = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero) },
            new AccessLog { Id = Guid.NewGuid(), UserId = user.Id, Action = "deleted", EntityType = "Asset", Timestamp = new DateTimeOffset(2025, 1, 15, 8, 30, 0, TimeSpan.Zero) });
        await db.SaveChangesAsync();
    }

    private static async Task SeedCommentsAsync(AppDbContext db)
    {
        var user = new User
        {
            Id = Guid.NewGuid(), Username = "commenter", Email = "c@t.com",
            PasswordHash = "hash", RoleId = Guid.Parse("00000000-0000-0000-0000-000000000003"),
            IsActive = true
        };
        db.Users.Add(user);

        var asset = new DigitalAsset
        {
            Id = Guid.NewGuid(), FileName = "a.jpg", Title = "A",
            MimeType = "image/jpeg", FileExtension = ".jpg", FileSize = 100,
            ChecksumSha256 = new string('c', 64), StoragePath = "/c", OriginalPath = "/c",
            Type = AssetType.Image, CreatedAt = DateTimeOffset.UtcNow, ModifiedAt = DateTimeOffset.UtcNow
        };
        db.DigitalAssets.Add(asset);
        await db.SaveChangesAsync();

        db.Comments.AddRange(
            new Comment { Id = Guid.NewGuid(), AssetId = asset.Id, UserId = user.Id, Body = "Older comment", CreatedAt = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), Version = 1 },
            new Comment { Id = Guid.NewGuid(), AssetId = asset.Id, UserId = user.Id, Body = "Newer comment", CreatedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero), Version = 1 });
        await db.SaveChangesAsync();
    }

    private static async Task SeedSearchHistoryAsync(AppDbContext db)
    {
        db.SearchHistoryEntries.AddRange(
            new SearchHistoryEntry { Id = Guid.NewGuid(), QueryText = "older", ExecutedAt = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero) },
            new SearchHistoryEntry { Id = Guid.NewGuid(), QueryText = "newer", ExecutedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero) });
        await db.SaveChangesAsync();
    }
}
