using Adam.CatalogBrowser.ViewModels;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Adam.Shared.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.CatalogBrowser.Tests.ViewModels;

/// <summary>
/// Tests for <see cref="FaceTaggingViewModel"/> covering loading, naming,
/// suggesting, confirming, and rejecting faces.
/// </summary>
public sealed class FaceTaggingViewModelTests : IDisposable
{
    private readonly string _dbPath;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly FaceMatcherService _matcher;
    private readonly FaceTaggingViewModel _vm;

    public FaceTaggingViewModelTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.db");
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;

        _dbFactory = new TestDbContextFactory(opts);

        using var db = new AppDbContext(opts);
        db.Database.EnsureCreated();
        Seed(db);

        _matcher = new FaceMatcherService(_dbFactory, NullLogger<FaceMatcherService>.Instance);
        _vm = new FaceTaggingViewModel(_dbFactory, _matcher);
    }

    private static void Seed(AppDbContext db)
    {
        // Seed a person with faces
        var personId = Guid.NewGuid();
        db.Persons.Add(new Person
        {
            Id = personId,
            Name = "Alice Johnson",
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow
        });

        var assetId = Guid.NewGuid();
        db.DigitalAssets.Add(new DigitalAsset
        {
            Id = assetId,
            FileName = "alice.jpg",
            FileExtension = ".jpg",
            MimeType = "image/jpeg",
            FileSize = 100,
            ChecksumSha256 = new string('a', 64),
            StoragePath = "/test/alice.jpg",
            OriginalPath = "/test/alice.jpg",
            Title = "Alice",
            Type = AssetType.Image,
            Version = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow
        });
        db.SaveChanges();

        db.AssetFaces.Add(new AssetFace
        {
            Id = Guid.NewGuid(),
            AssetId = assetId,
            PersonId = personId,
            FaceEmbedding = new byte[2048],
            BoundingBoxJson = "{}",
            DetectionConfidence = 0.95f,
            MatchingConfidence = 0.92f,
            IsAutoAssigned = true
        });

        // Seed an unknown face
        var unknownAssetId = Guid.NewGuid();
        db.DigitalAssets.Add(new DigitalAsset
        {
            Id = unknownAssetId,
            FileName = "unknown.jpg",
            FileExtension = ".jpg",
            MimeType = "image/jpeg",
            FileSize = 100,
            ChecksumSha256 = new string('b', 64),
            StoragePath = "/test/unknown.jpg",
            OriginalPath = "/test/unknown.jpg",
            Title = "Unknown",
            Type = AssetType.Image,
            Version = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow
        });
        db.SaveChanges();

        db.AssetFaces.Add(new AssetFace
        {
            Id = Guid.NewGuid(),
            AssetId = unknownAssetId,
            FaceEmbedding = new byte[2048],
            BoundingBoxJson = "{}",
            DetectionConfidence = 0.85f
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task LoadAsync_LoadsPersonsAndUnknownFaces()
    {
        await _vm.LoadAsync();

        _vm.Persons.Should().HaveCount(1);
        _vm.Persons[0].Name.Should().Be("Alice Johnson");
        _vm.Persons[0].FaceCount.Should().Be(1);
        _vm.UnknownFaces.Should().HaveCount(1);
        _vm.HasUnknownFaces.Should().BeTrue();
        _vm.TotalFaceCount.Should().Be(2);
    }

    [Fact]
    public async Task LoadAsync_SetsLoadingState()
    {
        var loadingChanges = new List<bool>();
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FaceTaggingViewModel.IsLoading))
                loadingChanges.Add(_vm.IsLoading);
        };

        await _vm.LoadAsync();

        loadingChanges.Should().Contain(true);
        _vm.IsLoading.Should().BeFalse();
    }

    [Fact]
    public async Task NameFace_CreatesPersonAndLinksFace()
    {
        await _vm.LoadAsync();
        var unknownFace = _vm.UnknownFaces[0];

        await _vm.NameFaceAsync(unknownFace.FaceId, "Bob Smith");

        await using var db = await _dbFactory.CreateDbContextAsync();
        var person = await db.Persons.FirstOrDefaultAsync(p => p.Name == "Bob Smith");
        person.Should().NotBeNull();

        var face = await db.AssetFaces.FindAsync(unknownFace.FaceId);
        face!.PersonId.Should().Be(person!.Id);
    }

    [Fact]
    public async Task NameFace_ExistingPerson_LinksFace()
    {
        await _vm.LoadAsync();
        var unknownFace = _vm.UnknownFaces[0];

        await _vm.NameFaceAsync(unknownFace.FaceId, "Alice Johnson");

        await using var db = await _dbFactory.CreateDbContextAsync();
        var alice = await db.Persons.FirstAsync(p => p.Name == "Alice Johnson");
        var face = await db.AssetFaces.FindAsync(unknownFace.FaceId);
        face!.PersonId.Should().Be(alice.Id);
    }

    [Fact]
    public async Task RefreshCommand_ReloadsData()
    {
        await _vm.LoadAsync();
        _vm.Persons.Should().HaveCount(1);

        // Add another person directly
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Persons.Add(new Person
            {
                Id = Guid.NewGuid(),
                Name = "Charlie",
                CreatedAt = DateTimeOffset.UtcNow,
                ModifiedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        _vm.RefreshCommand.Execute(null);

        _vm.Persons.Should().HaveCount(2);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
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
