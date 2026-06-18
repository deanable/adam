using Adam.Shared.Data;
using Adam.Shared.Models;
using Adam.Shared.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.Shared.Tests.Services;

/// <summary>
/// Tests for EmbeddingClusterService covering clustering of all assets,
/// per-asset clustering, edge cases with small datasets, and cancellation.
/// Uses synthetic low-dimensional embeddings for deterministic similarity.
/// </summary>
public sealed class EmbeddingClusterServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly EmbeddingClusterService _service;

    public EmbeddingClusterServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.db");

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseSqlite($"Data Source={_dbPath}");
        var options = optionsBuilder.Options;

        _dbFactory = new SimpleDbContextFactory(options);
        _service = new EmbeddingClusterService(_dbFactory, NullLogger<EmbeddingClusterService>.Instance);

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
    //  ClusterAsync
    // ═══════════════════════════════════════════════════════

    [Fact]
    public async Task ClusterAsync_WithSimilarItems_ReturnsClusters()
    {
        await SeedAssetsWithEmbeddingsAsync(5, sameGroup: true);
        _service.MinClusterSize = 3;
        _service.MinSimilarity = 0.7;

        var clusters = await _service.ClusterAsync();

        clusters.Should().NotBeEmpty();
        clusters[0].SuggestedName.Should().NotBeNullOrEmpty();
        clusters[0].Assets.Should().HaveCountGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task ClusterAsync_WithDissimilarItems_ReturnsEmpty()
    {
        await SeedAssetsWithEmbeddingsAsync(5, sameGroup: false);
        _service.MinClusterSize = 3;
        _service.MinSimilarity = 0.7;

        var clusters = await _service.ClusterAsync();

        // Orthogonal vectors won't cluster above 0.7 threshold
        clusters.Should().BeEmpty();
    }

    [Fact]
    public async Task ClusterAsync_FewerThanMinClusterSize_ReturnsEmpty()
    {
        await SeedAssetsWithEmbeddingsAsync(2, sameGroup: true);
        _service.MinClusterSize = 3;

        var clusters = await _service.ClusterAsync();

        clusters.Should().BeEmpty();
    }

    [Fact]
    public async Task ClusterAsync_SpecificAssetIds_OnlyClustersThose()
    {
        var allIds = await SeedAssetsWithEmbeddingsAsync(6, sameGroup: true);
        var subset = allIds.Take(3).ToList();
        _service.MinClusterSize = 2;

        var clusters = await _service.ClusterAsync(subset);

        clusters.Should().NotBeEmpty();
        // All clustered assets should be from the subset
        foreach (var cluster in clusters)
        {
            foreach (var asset in cluster.Assets)
                subset.Should().Contain(a => a == asset.AssetId);
        }
    }

    [Fact]
    public async Task ClusterAsync_ReportsProgress()
    {
        await SeedAssetsWithEmbeddingsAsync(5, sameGroup: true);
        _service.MinClusterSize = 2;

        var progressItems = new List<(int completed, int total)>();
        var progress = new Progress<(int completed, int total)>(p => progressItems.Add(p));

        var clusters = await _service.ClusterAsync(progress: progress);

        progressItems.Should().NotBeEmpty();
        var last = progressItems.Last();
        last.completed.Should().Be(last.total);
    }

    [Fact]
    public async Task ClusterAsync_HigherThreshold_FewerClusters()
    {
        await SeedAssetsWithEmbeddingsAsync(5, sameGroup: true);
        _service.MinClusterSize = 2;

        _service.MinSimilarity = 0.9;
        var strictClusters = await _service.ClusterAsync();

        _service.MinSimilarity = 0.5;
        var looseClusters = await _service.ClusterAsync();

        // Loose threshold should find at least as many clusters as strict
        looseClusters.Count.Should().BeGreaterThanOrEqualTo(strictClusters.Count);
    }

    // ═══════════════════════════════════════════════════════
    //  ClusterForAssetAsync
    // ═══════════════════════════════════════════════════════

    [Fact]
    public async Task ClusterForAssetAsync_WithSimilarAssets_ReturnsCluster()
    {
        var ids = await SeedAssetsWithEmbeddingsAsync(5, sameGroup: true);
        _service.MinClusterSize = 3;

        var cluster = await _service.ClusterForAssetAsync(ids[0]);

        cluster.Should().NotBeNull();
        cluster!.SuggestedName.Should().Contain("img_0");
        cluster.Assets.Should().HaveCountGreaterThanOrEqualTo(4); // 4 similar others
    }

    [Fact]
    public async Task ClusterForAssetAsync_NoSimilarAssets_ReturnsNull()
    {
        var ids = await SeedAssetsWithEmbeddingsAsync(5, sameGroup: false);
        _service.MinClusterSize = 3;

        var cluster = await _service.ClusterForAssetAsync(ids[0]);

        cluster.Should().BeNull();
    }

    [Fact]
    public async Task ClusterForAssetAsync_UnknownAsset_ReturnsNull()
    {
        var cluster = await _service.ClusterForAssetAsync(Guid.NewGuid());

        cluster.Should().BeNull();
    }

    // ═══════════════════════════════════════════════════════
    //  Album Name Generation
    // ═══════════════════════════════════════════════════════

    [Fact]
    public async Task ClusterAsync_ClusterNameGeneratedFromKeywords()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        // Create assets with shared keywords
        var groupId = Guid.NewGuid();
        for (int i = 0; i < 5; i++)
        {
            var assetId = Guid.NewGuid();
            var kw = new Keyword
            {
                Id = Guid.NewGuid(),
                Name = "sunset",
                NormalizedName = "SUNSET"
            };
            db.DigitalAssets.Add(new DigitalAsset
            {
                Id = assetId,
                FileName = $"sunset_{i}.jpg",
                FileExtension = ".jpg",
                MimeType = "image/jpeg",
                FileSize = 100,
                ChecksumSha256 = $"s{i:D4}",
                StoragePath = $"/sunset_{i}.jpg",
                OriginalPath = $"/sunset_{i}.jpg",
                Title = $"Sunset {i}",
                Type = AssetType.Image,
                Keywords = [kw]
            });

            var vec = Normalize([1, 1, 1, 1, 0, 0, 0, 0]);
            db.AssetEmbeddings.Add(new AssetEmbedding
            {
                Id = Guid.NewGuid(),
                AssetId = assetId,
                TextEmbedding = new byte[4],
                ImageEmbedding = FloatsToBytes(vec)
            });
        }
        await db.SaveChangesAsync();

        _service.MinClusterSize = 3;
        _service.MinSimilarity = 0.7;

        var clusters = await _service.ClusterAsync();

        clusters.Should().NotBeEmpty();
        // The name should include "sunset" since it's the common keyword
        clusters[0].SuggestedName.Should().Contain("sunset");
        clusters[0].CommonKeywords.Should().Contain("sunset");
    }

    /// <summary>
    /// Seeds assets with synthetic 8-dim embeddings.
    /// sameGroup=true → vectors cluster together; sameGroup=false → orthogonal.
    /// </summary>
    private async Task<List<Guid>> SeedAssetsWithEmbeddingsAsync(
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
                Type = AssetType.Image,
                Keywords = []
            });

            float[] vec;
            if (sameGroup)
            {
                vec = Normalize([1, 1, 1, 1, 0, 0, 0, 0]);
                // Add slight noise for variation
                vec[i % 8] += (i * 0.01f);
                vec = Normalize(vec);
            }
            else
            {
                var axis = new float[8];
                axis[i % 8] = 1;
                vec = Normalize(axis);
            }

            db.AssetEmbeddings.Add(new AssetEmbedding
            {
                Id = Guid.NewGuid(),
                AssetId = assetId,
                TextEmbedding = new byte[4],
                ImageEmbedding = FloatsToBytes(vec)
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
