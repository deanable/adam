using Adam.Shared.Data;
using Adam.Shared.Models;
using Adam.Shared.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.Shared.Tests.Services;

/// <summary>
/// Tests for SearchRankingService covering click logging, re-ranking with
/// click affinity, purge, query normalization, and edge cases.
/// Uses in-memory SQLite (via DbContextFactory) for isolated per-test databases.
/// </summary>
public sealed class SearchRankingServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly SearchRankingService _service;
    private Guid _assetId;

    public SearchRankingServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.db");

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseSqlite($"Data Source={_dbPath}");
        var options = optionsBuilder.Options;

        _dbFactory = new SimpleDbContextFactory(options);
        _service = new SearchRankingService(_dbFactory, NullLogger<SearchRankingService>.Instance);

        // Seed a digital asset for FK references
        using var db = _dbFactory.CreateDbContext();
        db.Database.EnsureCreated();
        _assetId = Guid.NewGuid();
        db.DigitalAssets.Add(new DigitalAsset
        {
            Id = _assetId,
            FileName = "test.jpg",
            FileExtension = ".jpg",
            MimeType = "image/jpeg",
            FileSize = 100,
            ChecksumSha256 = "abc123",
            StoragePath = "/test.jpg",
            OriginalPath = "/test.jpg",
            Title = "Test",
            Type = AssetType.Image
        });
        db.SaveChanges();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
    }

    // ═══════════════════════════════════════════════════════
    //  LogClickAsync
    // ═══════════════════════════════════════════════════════

    [Fact]
    public async Task LogClickAsync_ValidParameters_PersistsToDatabase()
    {
        var logId = await _service.LogClickAsync(_assetId, "sunset photo", 3, 2500);

        logId.Should().NotBeEmpty();

        await using var db = await _dbFactory.CreateDbContextAsync();
        var log = await db.SearchClickLogs.FindAsync(logId);
        log.Should().NotBeNull();
        log!.AssetId.Should().Be(_assetId);
        log.QueryText.Should().Be("sunset photo");
        log.NormalizedQuery.Should().Be("sunset photo"); // no normalization needed
        log.RankPosition.Should().Be(3);
        log.DwellTimeMs.Should().Be(2500);
        log.ClickedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task LogClickAsync_QueryWithWhitespace_NormalizesQuery()
    {
        var logId = await _service.LogClickAsync(_assetId, "  Sunset   PHOTO  ", 1, 1000);

        await using var db = await _dbFactory.CreateDbContextAsync();
        var log = await db.SearchClickLogs.FindAsync(logId);
        log.Should().NotBeNull();
        log!.NormalizedQuery.Should().Be("sunset photo");
    }

    [Fact]
    public async Task LogClickAsync_EmptyQuery_StoresEmptyNormalized()
    {
        var logId = await _service.LogClickAsync(_assetId, "", 0, 0);

        await using var db = await _dbFactory.CreateDbContextAsync();
        var log = await db.SearchClickLogs.FindAsync(logId);
        log.Should().NotBeNull();
        log!.NormalizedQuery.Should().Be("");
    }

    [Fact]
    public async Task LogClickAsync_ZeroDwellTime_StoresCorrectly()
    {
        var logId = await _service.LogClickAsync(_assetId, "test", 2, 0);

        await using var db = await _dbFactory.CreateDbContextAsync();
        var log = await db.SearchClickLogs.FindAsync(logId);
        log!.DwellTimeMs.Should().Be(0);
    }

    // ═══════════════════════════════════════════════════════
    //  ReRankAsync
    // ═══════════════════════════════════════════════════════

    [Fact]
    public async Task ReRankAsync_NoClickData_ReturnsOriginalOrder()
    {
        var results = CreateSampleResults(3, ["A", "B", "C"]);

        var ranked = await _service.ReRankAsync(results, "test");

        ranked.Should().HaveCount(3);
        ranked[0].CombinedScore.Should().BeApproximately(0.7f * 0.9f, 0.01f); // 0.7 * 0.9
        ranked[1].CombinedScore.Should().BeApproximately(0.7f * 0.8f, 0.01f);
        ranked[2].CombinedScore.Should().BeApproximately(0.7f * 0.7f, 0.01f);
        // Rankings should be in descending score order
        ranked[0].Rank.Should().Be(1);
        ranked[1].Rank.Should().Be(2);
        ranked[2].Rank.Should().Be(3);
    }

    [Fact]
    public async Task ReRankAsync_WithClickData_BoostsClickedResults()
    {
        // Seed click data: asset "B" has the most clicks
        var query = "test query";
        var assetIds = CreateSeedAssets(3);
        await SeedClickDataAsync(assetIds, query, clicksPerAsset: [0, 5, 1]);

        var results = assetIds.Select((id, i) => new SemanticSearchResult
        {
            Asset = new DigitalAsset { Id = id },
            Score = new[] { 0.9f, 0.8f, 0.7f }[i],
            Rank = i + 1
        }).ToList();

        var ranked = await _service.ReRankAsync(results, query);

        // Asset B (index 1) has the most clicks, should be boosted above A
        ranked.Should().HaveCount(3);
        var bResult = ranked.FirstOrDefault(r => r.AssetId == assetIds[1]);
        bResult.Should().NotBeNull();
        bResult!.ClickBoost.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ReRankAsync_EmptyResults_ReturnsEmpty()
    {
        var ranked = await _service.ReRankAsync([], "anything");

        ranked.Should().BeEmpty();
    }

    [Fact]
    public async Task ReRankAsync_RecentClicks_AddsRecencyBonus()
    {
        var query = "recent";
        var assetIds = CreateSeedAssets(2);

        // Add a click from within the last 7 days for asset 0
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.SearchClickLogs.AddRange(
                new SearchClickLog
                {
                    Id = Guid.NewGuid(),
                    AssetId = assetIds[0],
                    QueryText = query,
                    NormalizedQuery = query,
                    ClickedAt = DateTimeOffset.UtcNow.AddDays(-1),
                    DwellTimeMs = 1000,
                    RankPosition = 1
                },
                new SearchClickLog
                {
                    Id = Guid.NewGuid(),
                    AssetId = assetIds[1],
                    QueryText = query,
                    NormalizedQuery = query,
                    ClickedAt = DateTimeOffset.UtcNow.AddDays(-30),
                    DwellTimeMs = 1000,
                    RankPosition = 2
                });
            await db.SaveChangesAsync();
        }

        var results = assetIds.Select((id, i) => new SemanticSearchResult
        {
            Asset = new DigitalAsset { Id = id },
            Score = 0.5f,
            Rank = i + 1
        }).ToList();

        var ranked = await _service.ReRankAsync(results, query);

        // Asset 0 has a recent click — should have higher click boost than asset 1
        ranked[0].ClickBoost.Should().BeGreaterThan(ranked[1].ClickBoost);
    }

    [Fact]
    public async Task ReRankAsync_LongDwell_BoostsAboveShortDwell()
    {
        var query = "dwell";
        var assetIds = CreateSeedAssets(2);

        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.SearchClickLogs.AddRange(
                new SearchClickLog
                {
                    Id = Guid.NewGuid(),
                    AssetId = assetIds[0],
                    QueryText = query,
                    NormalizedQuery = query,
                    ClickedAt = DateTimeOffset.UtcNow,
                    DwellTimeMs = 8000, // long dwell (>5s threshold)
                    RankPosition = 1
                },
                new SearchClickLog
                {
                    Id = Guid.NewGuid(),
                    AssetId = assetIds[1],
                    QueryText = query,
                    NormalizedQuery = query,
                    ClickedAt = DateTimeOffset.UtcNow.AddDays(-30), // old click, no recency bonus
                    DwellTimeMs = 500, // short dwell (<5s threshold)
                    RankPosition = 2
                });
            await db.SaveChangesAsync();
        }

        var results = assetIds.Select((id, i) => new SemanticSearchResult
        {
            Asset = new DigitalAsset { Id = id },
            Score = 0.5f,
            Rank = i + 1
        }).ToList();

        var ranked = await _service.ReRankAsync(results, query);

        // Long-dwell item gets recency + dwell bonuses; short-dwell old item gets neither
        ranked[0].ClickBoost.Should().BeGreaterThan(ranked[1].ClickBoost);
    }

    // ═══════════════════════════════════════════════════════
    //  GetClickCountAsync
    // ═══════════════════════════════════════════════════════

    [Fact]
    public async Task GetClickCountAsync_WithClicks_ReturnsCorrectCount()
    {
        var query = "count me";
        await _service.LogClickAsync(_assetId, query, 1, 100);
        await _service.LogClickAsync(_assetId, query, 2, 200);
        await _service.LogClickAsync(_assetId, query, 3, 300);

        var count = await _service.GetClickCountAsync(_assetId, query);

        count.Should().Be(3);
    }

    [Fact]
    public async Task GetClickCountAsync_NoClicks_ReturnsZero()
    {
        var count = await _service.GetClickCountAsync(Guid.NewGuid(), "nonexistent");

        count.Should().Be(0);
    }

    // ═══════════════════════════════════════════════════════
    //  PurgeOldLogsAsync
    // ═══════════════════════════════════════════════════════

    [Fact]
    public async Task PurgeOldLogsAsync_OldLogsExist_RemovesThem()
    {
        // Create a log that's older than 90 days
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.SearchClickLogs.Add(new SearchClickLog
            {
                Id = Guid.NewGuid(),
                AssetId = _assetId,
                QueryText = "old",
                NormalizedQuery = "old",
                ClickedAt = DateTimeOffset.UtcNow.AddDays(-100),
                DwellTimeMs = 0,
                RankPosition = 1
            });
            await db.SaveChangesAsync();
        }

        var purged = await _service.PurgeOldLogsAsync();

        purged.Should().Be(1);

        await using var db2 = await _dbFactory.CreateDbContextAsync();
        var remaining = await db2.SearchClickLogs.CountAsync();
        remaining.Should().Be(0);
    }

    [Fact]
    public async Task PurgeOldLogsAsync_NoOldLogs_ReturnsZero()
    {
        // Add a recent log
        await _service.LogClickAsync(_assetId, "recent", 1, 100);

        var purged = await _service.PurgeOldLogsAsync();

        purged.Should().Be(0);
    }

    // ═══════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════

    private static IReadOnlyList<SemanticSearchResult> CreateSampleResults(
        int count, string[] names)
    {
        var results = new List<SemanticSearchResult>();
        for (int i = 0; i < count; i++)
        {
            results.Add(new SemanticSearchResult
            {
                Asset = new DigitalAsset
                {
                    Id = Guid.NewGuid(),
                    FileName = $"{names[i]}.jpg"
                },
                Score = new[] { 0.9f, 0.8f, 0.7f }[Math.Min(i, 2)],
                Rank = i + 1
            });
        }
        return results;
    }

    private List<Guid> CreateSeedAssets(int count)
    {
        var ids = new List<Guid>();
        using var db = _dbFactory.CreateDbContext();
        for (int i = 0; i < count; i++)
        {
            var id = Guid.NewGuid();
            ids.Add(id);
            db.DigitalAssets.Add(new DigitalAsset
            {
                Id = id,
                FileName = $"asset_{i}.jpg",
                FileExtension = ".jpg",
                MimeType = "image/jpeg",
                FileSize = 100 + i,
                ChecksumSha256 = $"hash{i:D3}",
                StoragePath = $"/path/asset_{i}.jpg",
                OriginalPath = $"/path/asset_{i}.jpg",
                Title = $"Asset {i}",
                Type = AssetType.Image
            });
        }
        db.SaveChanges();
        return ids;
    }

    private async Task SeedClickDataAsync(
        List<Guid> assetIds, string query, int[] clicksPerAsset)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        for (int i = 0; i < assetIds.Count; i++)
        {
            for (int c = 0; c < clicksPerAsset[i]; c++)
            {
                db.SearchClickLogs.Add(new SearchClickLog
                {
                    Id = Guid.NewGuid(),
                    AssetId = assetIds[i],
                    QueryText = query,
                    NormalizedQuery = query,
                    ClickedAt = DateTimeOffset.UtcNow,
                    DwellTimeMs = 1000,
                    RankPosition = c + 1
                });
            }
        }
        await db.SaveChangesAsync();
    }
}
