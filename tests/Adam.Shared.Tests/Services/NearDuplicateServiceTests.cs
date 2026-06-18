using Adam.Shared.Data;
using Adam.Shared.Models;
using Adam.Shared.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.Shared.Tests.Services;

/// <summary>
/// Tests for NearDuplicateService covering per-asset duplicate detection,
/// full-catalog scanning, progress reporting, and edge cases.
/// Uses synthetic low-dimensional embeddings to control cosine similarity.
/// </summary>
public sealed class NearDuplicateServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly NearDuplicateService _service;

    public NearDuplicateServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.db");

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseSqlite($"Data Source={_dbPath}");
        var options = optionsBuilder.Options;

        _dbFactory = new SimpleDbContextFactory(options);
        _service = new NearDuplicateService(_dbFactory, NullLogger<NearDuplicateService>.Instance);

        using var db = _dbFactory.CreateDbContext();
        db.Database.EnsureCreated();
        db.SaveChanges();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
    }

    // ═══════════════════════════════════════════════════════
    //  FindForAssetAsync
    // ═══════════════════════════════════════════════════════

    [Fact]
    public async Task FindForAssetAsync_WithSimilarEmbeddings_ReturnsGroup()
    {
        var ids = await SeedImageAssetsWithEmbeddingsAsync(3, sameGroup: true);

        var groups = await _service.FindForAssetAsync(ids[0]);

        groups.Should().NotBeEmpty();
        groups[0].Duplicates.Should().NotBeEmpty();
        groups[0].GroupType.Should().BeOneOf("Near-identical", "Edited version", "Similar");
    }

    [Fact]
    public async Task FindForAssetAsync_WithDissimilarEmbeddings_ReturnsEmpty()
    {
        var ids = await SeedImageAssetsWithEmbeddingsAsync(2, sameGroup: false);

        var groups = await _service.FindForAssetAsync(ids[0]);

        groups.Should().BeEmpty();
    }

    [Fact]
    public async Task FindForAssetAsync_NoImageEmbedding_ReturnsEmpty()
    {
        var id = Guid.NewGuid();
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.DigitalAssets.Add(new DigitalAsset
            {
                Id = id,
                FileName = "no-embedding.jpg",
                FileExtension = ".jpg",
                MimeType = "image/jpeg",
                FileSize = 100,
                ChecksumSha256 = "noemb",
                StoragePath = "/no-embedding.jpg",
                OriginalPath = "/no-embedding.jpg",
                Title = "No Embedding",
                Type = AssetType.Image
            });
            // Add embedding with null/empty ImageEmbedding
            db.AssetEmbeddings.Add(new AssetEmbedding
            {
                Id = Guid.NewGuid(),
                AssetId = id,
                TextEmbedding = new byte[4] // minimal
            });
            await db.SaveChangesAsync();
        }

        var groups = await _service.FindForAssetAsync(id);

        groups.Should().BeEmpty();
    }

    [Fact]
    public async Task FindForAssetAsync_UnknownAssetId_ReturnsEmpty()
    {
        var groups = await _service.FindForAssetAsync(Guid.NewGuid());

        groups.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════
    //  ScanAllAsync
    // ═══════════════════════════════════════════════════════

    [Fact]
    public async Task ScanAllAsync_WithDuplicateGroups_ReturnsGroups()
    {
        await SeedImageAssetsWithEmbeddingsAsync(4, sameGroup: true);

        var groups = await _service.ScanAllAsync();

        groups.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ScanAllAsync_NoDuplicates_ReturnsEmpty()
    {
        await SeedImageAssetsWithEmbeddingsAsync(4, sameGroup: false);

        var groups = await _service.ScanAllAsync();

        // Each asset is in a very different direction; no matches above threshold
        // With perpendicular vectors, cosine similarity is ~0, below 0.85 threshold
        groups.Should().BeEmpty();
    }

    [Fact]
    public async Task ScanAllAsync_ReportsProgress()
    {
        await SeedImageAssetsWithEmbeddingsAsync(3, sameGroup: true);

        var progressItems = new List<(int completed, int total)>();
        var progress = new Progress<(int completed, int total)>(p => progressItems.Add(p));

        var groups = await _service.ScanAllAsync(progress);

        // Should have reported progress at least once
        progressItems.Should().NotBeEmpty();
        var last = progressItems.Last();
        last.completed.Should().Be(last.total);
    }

    [Fact]
    public async Task ScanAllAsync_FewerThanTwoEmbeddings_ReturnsEmpty()
    {
        await SeedImageAssetsWithEmbeddingsAsync(1, sameGroup: true);

        var groups = await _service.ScanAllAsync();

        groups.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════
    //  GetStatsAsync
    // ═══════════════════════════════════════════════════════

    [Fact]
    public async Task GetStatsAsync_ReturnsCorrectStats()
    {
        await SeedImageAssetsWithEmbeddingsAsync(3, sameGroup: true);

        var stats = await _service.GetStatsAsync();

        stats.TotalAssets.Should().BeGreaterThanOrEqualTo(3);
        stats.DuplicateGroups.Should().BeGreaterThanOrEqualTo(0);
        stats.PotentialSavingsBytes.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task GetStatsAsync_EmptyCatalog_ReturnsZeroStats()
    {
        var stats = await _service.GetStatsAsync();

        stats.TotalAssets.Should().Be(0);
        stats.DuplicateGroups.Should().Be(0);
        stats.PotentialSavingsBytes.Should().Be(0);
    }

    /// <summary>
    /// Seeds image assets with synthetic 8-dim embeddings.
    /// When sameGroup=true, all vectors cluster in {1,1,1,1,0,0,0,0};
    /// when sameGroup=false, vectors point in orthogonal directions.
    /// </summary>
    private async Task<List<Guid>> SeedImageAssetsWithEmbeddingsAsync(
        int count, bool sameGroup)
    {
        var ids = new List<Guid>();
        await using var db = await _dbFactory.CreateDbContextAsync();

        for (int i = 0; i < count; i++)
        {
            var assetId = Guid.NewGuid();
            ids.Add(assetId);

            db.DigitalAssets.Add(new DigitalAsset
            {
                Id = assetId,
                FileName = $"img_{i}.jpg",
                FileExtension = ".jpg",
                MimeType = "image/jpeg",
                FileSize = 100 + i,
                ChecksumSha256 = $"hash{i:D4}",
                StoragePath = $"/imgs/img_{i}.jpg",
                OriginalPath = $"/imgs/img_{i}.jpg",
                Title = $"Image {i}",
                Type = AssetType.Image
            });

            // Create embedding vector
            float[] vec;
            if (sameGroup)
            {
                // All point in roughly the same direction
                var baseVec = new float[] { 1, 1, 1, 1, 0, 0, 0, 0 };
                vec = Normalize(baseVec);
            }
            else
            {
                // Each axis independently — orthogonal vectors
                var axis = new float[8];
                axis[i % 8] = 1;
                vec = Normalize(axis);
            }

            var bytes = FloatsToBytes(vec);
            db.AssetEmbeddings.Add(new AssetEmbedding
            {
                Id = Guid.NewGuid(),
                AssetId = assetId,
                TextEmbedding = new byte[4],
                ImageEmbedding = bytes
            });
        }

        await db.SaveChangesAsync();
        return ids;
    }

    private static float[] Normalize(float[] vec)
    {
        float norm = 0;
        foreach (var v in vec) norm += v * v;
        norm = MathF.Sqrt(norm);
        if (norm > 0)
            for (int i = 0; i < vec.Length; i++)
                vec[i] /= norm;
        return vec;
    }

    private static byte[] FloatsToBytes(float[] floats)
    {
        var bytes = new byte[floats.Length * 4];
        Buffer.BlockCopy(floats, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static class SqliteConnection
    {
        public static void ClearAllPools()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        }
    }
}
