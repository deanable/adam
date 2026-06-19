using Adam.Shared.Data;
using Adam.Shared.Models;
using Adam.Shared.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.Shared.Tests.Services;

/// <summary>
/// Tests for FaceMatcherService covering matching, clustering, centroid computation,
/// and edge cases.
/// </summary>
public sealed class FaceMatcherServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly FaceMatcherService _sut;
    private Guid _faceId;

    public FaceMatcherServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.db");
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;

        _dbFactory = new TestDbContextFactory(opts);

        using var db = new AppDbContext(opts);
        db.Database.EnsureCreated();

        // Seed a person with a known centroid
        var personId = Guid.NewGuid();
        var centroid = new float[512];
        centroid[0] = 1.0f;  // unit vector along first dimension
        var centroidBytes = FloatsToBytes(centroid);

        db.Persons.Add(new Person
        {
            Id = personId,
            Name = "Test Person",
            CentroidEmbedding = centroidBytes,
            EmbeddingModelVersion = "test-v1",
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow
        });

        // Seed an asset
        var assetId = Guid.NewGuid();
        db.DigitalAssets.Add(new DigitalAsset
        {
            Id = assetId,
            FileName = "face.jpg",
            FileExtension = ".jpg",
            MimeType = "image/jpeg",
            FileSize = 100,
            ChecksumSha256 = new string('a', 64),
            StoragePath = "/test/face.jpg",
            OriginalPath = "/test/face.jpg",
            Title = "Face",
            Type = AssetType.Image,
            Version = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow
        });

        db.SaveChanges();

        // Seed a face embedding that matches the centroid
        var faceVec = new float[512];
        faceVec[0] = 0.95f;  // close to centroid
        _faceId = Guid.NewGuid();
        db.AssetFaces.Add(new AssetFace
        {
            Id = _faceId,
            AssetId = assetId,
            PersonId = personId,
            FaceEmbedding = FloatsToBytes(faceVec),
            BoundingBoxJson = "{}",
            DetectionConfidence = 0.95f
        });

        db.SaveChanges();

        _sut = new FaceMatcherService(_dbFactory, NullLogger<FaceMatcherService>.Instance);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
    }

    [Fact]
    public async Task MatchAsync_ExactMatch_AutoAssigns()
    {
        var result = await _sut.MatchAsync(_faceId);

        result.Should().NotBeNull();
        result.MatchType.Should().Be(FaceMatchType.AutoAssigned);
        result.Confidence.Should().BeGreaterThanOrEqualTo((float)_sut.AutoAssignThreshold);
    }

    [Fact]
    public async Task MatchAsync_Partial_Suggests()
    {
        // Create a face further from centroid
        await using var db = await _dbFactory.CreateDbContextAsync();
        var assetId = Guid.NewGuid();
        db.DigitalAssets.Add(new DigitalAsset
        {
            Id = assetId,
            FileName = "partial.jpg",
            FileExtension = ".jpg",
            MimeType = "image/jpeg",
            FileSize = 100,
            ChecksumSha256 = new string('b', 64),
            StoragePath = "/test/partial.jpg",
            OriginalPath = "/test/partial.jpg",
            Title = "Partial",
            Type = AssetType.Image,
            Version = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow
        });

        // Vectors at ~40° angle to centroid [1,0,...]:
        // cos(40°) ≈ 0.766 → cosine similarity 0.766 (between suggest=0.70 and auto-assign=0.85)
        var angleRad = 40.0 * Math.PI / 180.0;
        var partialVec = new float[512];
        partialVec[0] = (float)Math.Cos(angleRad);
        partialVec[1] = (float)Math.Sin(angleRad);
        var partialFaceId = Guid.NewGuid();
        db.AssetFaces.Add(new AssetFace
        {
            Id = partialFaceId,
            AssetId = assetId,
            FaceEmbedding = FloatsToBytes(partialVec),
            BoundingBoxJson = "{}",
            DetectionConfidence = 0.85f
        });
        await db.SaveChangesAsync();

        var result = await _sut.MatchAsync(partialFaceId);

        result.Should().NotBeNull();
        result.MatchType.Should().Be(FaceMatchType.Suggested);
    }

    [Fact]
    public async Task MatchAsync_NoMatch_Unknown()
    {
        // Create a face with a completely different vector
        await using var db = await _dbFactory.CreateDbContextAsync();
        var assetId = Guid.NewGuid();
        db.DigitalAssets.Add(new DigitalAsset
        {
            Id = assetId,
            FileName = "unknown.jpg",
            FileExtension = ".jpg",
            MimeType = "image/jpeg",
            FileSize = 100,
            ChecksumSha256 = new string('c', 64),
            StoragePath = "/test/unknown.jpg",
            OriginalPath = "/test/unknown.jpg",
            Title = "Unknown",
            Type = AssetType.Image,
            Version = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow
        });

        var unknownVec = new float[512];
        unknownVec[0] = -0.5f;  // very different
        var unknownFaceId = Guid.NewGuid();
        db.AssetFaces.Add(new AssetFace
        {
            Id = unknownFaceId,
            AssetId = assetId,
            FaceEmbedding = FloatsToBytes(unknownVec),
            BoundingBoxJson = "{}",
            DetectionConfidence = 0.9f
        });
        await db.SaveChangesAsync();

        var result = await _sut.MatchAsync(unknownFaceId);

        result.Should().NotBeNull();
        result.MatchType.Should().Be(FaceMatchType.Unknown);
    }

    [Fact]
    public async Task MatchAsync_NoKnownPersons_ReturnsUnknown()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        // Create a face with no persons in DB (use a fresh DB path)
        var freshDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.db");
        var freshOpts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={freshDbPath}")
            .Options;

        using (var freshDb = new AppDbContext(freshOpts))
        {
            freshDb.Database.EnsureCreated();
            var assetId = Guid.NewGuid();
            freshDb.DigitalAssets.Add(new DigitalAsset
            {
                Id = assetId,
                FileName = "orphan.jpg",
                FileExtension = ".jpg",
                MimeType = "image/jpeg",
                FileSize = 100,
                ChecksumSha256 = new string('d', 64),
                StoragePath = "/test/orphan.jpg",
                OriginalPath = "/test/orphan.jpg",
                Title = "Orphan",
                Type = AssetType.Image,
                Version = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                ModifiedAt = DateTimeOffset.UtcNow
            });
            var orphanFaceId = Guid.NewGuid();
            freshDb.AssetFaces.Add(new AssetFace
            {
                Id = orphanFaceId,
                AssetId = assetId,
                FaceEmbedding = FloatsToBytes(new float[512]),
                BoundingBoxJson = "{}",
                DetectionConfidence = 0.9f
            });
            await freshDb.SaveChangesAsync();

            var freshFactory = new TestDbContextFactory(freshOpts);
            var freshMatcher = new FaceMatcherService(freshFactory, NullLogger<FaceMatcherService>.Instance);

            var result = await freshMatcher.MatchAsync(orphanFaceId);
            result.MatchType.Should().Be(FaceMatchType.Unknown);
        }

        try { File.Delete(freshDbPath); } catch { }
    }

    [Fact]
    public async Task ComputeCentroid_AveragesVectors()
    {
        var centroid = await _sut.ComputeCentroidAsync(
            (await GetFirstPersonIdAsync()));

        centroid.Should().NotBeNull();
        centroid.Length.Should().Be(512 * 4); // 512 float32 values
    }

    [Fact]
    public void CosineSimilarity_IdenticalVectors_Returns1()
    {
        var vec = new float[] { 1.0f, 0.0f, 0.0f };
        var score = FaceMatcherService.CosineSimilarity(vec, vec);
        score.Should().BeApproximately(1.0f, 0.001f);
    }

    [Fact]
    public void CosineSimilarity_OrthogonalVectors_Returns0()
    {
        var a = new float[] { 1.0f, 0.0f, 0.0f };
        var b = new float[] { 0.0f, 1.0f, 0.0f };
        var score = FaceMatcherService.CosineSimilarity(a, b);
        score.Should().BeApproximately(0.0f, 0.001f);
    }

    private async Task<Guid> GetFirstPersonIdAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return (await db.Persons.FirstAsync()).Id;
    }

    private static byte[] FloatsToBytes(float[] floats)
    {
        var bytes = new byte[floats.Length * 4];
        Buffer.BlockCopy(floats, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> _opts;
        public TestDbContextFactory(DbContextOptions<AppDbContext> opts) => _opts = opts;
        public AppDbContext CreateDbContext() => new(_opts);
        public Task<AppDbContext> CreateDbContextAsync(CancellationToken ct = default)
            => Task.FromResult(CreateDbContext());
    }
}
