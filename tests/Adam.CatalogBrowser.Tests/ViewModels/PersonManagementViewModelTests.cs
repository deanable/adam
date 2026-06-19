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
/// Tests for <see cref="PersonManagementViewModel"/> covering rename, merge,
/// delete, and property binding.
/// </summary>
public sealed class PersonManagementViewModelTests : IDisposable
{
    private readonly string _dbPath;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly FaceMatcherService _matcher;
    private readonly PersonManagementViewModel _vm;

    public PersonManagementViewModelTests()
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
        _vm = new PersonManagementViewModel(_dbFactory, _matcher);
    }

    private static void Seed(AppDbContext db)
    {
        db.Persons.Add(new Person
        {
            Id = Guid.NewGuid(),
            Name = "Alice Johnson",
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow
        });
        db.Persons.Add(new Person
        {
            Id = Guid.NewGuid(),
            Name = "Bob Smith",
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task LoadAsync_LoadsAllPersons()
    {
        await _vm.LoadAsync();

        _vm.Persons.Should().HaveCount(2);
        _vm.Persons.Select(p => p.Name).Should().BeEquivalentTo(["Alice Johnson", "Bob Smith"]);
    }

    [Fact]
    public async Task LoadAsync_SetsLoadingState()
    {
        var loadingChanges = new List<bool>();
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PersonManagementViewModel.IsLoading))
                loadingChanges.Add(_vm.IsLoading);
        };

        await _vm.LoadAsync();

        loadingChanges.Should().Contain(true);
        _vm.IsLoading.Should().BeFalse();
    }

    [Fact]
    public async Task SelectedPerson_SetsEditName()
    {
        await _vm.LoadAsync();
        var alice = _vm.Persons[0];

        _vm.SelectedPerson = alice;

        _vm.EditName.Should().Be("Alice Johnson");
    }

    [Fact]
    public async Task RenameCommand_UpdatesName()
    {
        await _vm.LoadAsync();
        _vm.SelectedPerson = _vm.Persons[0];
        _vm.EditName = "Alice Johnson-Smith";

        _vm.RenameCommand.Execute(null);

        await using var db = await _dbFactory.CreateDbContextAsync();
        var person = await db.Persons.FindAsync(_vm.SelectedPerson.Id);
        person!.Name.Should().Be("Alice Johnson-Smith");
    }

    [Fact]
    public async Task MergeCommand_MovesFaces()
    {
        await _vm.LoadAsync();
        var alice = _vm.Persons[0];
        var bob = _vm.Persons[1];

        // Give Alice a face
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
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

            db.AssetFaces.Add(new AssetFace
            {
                Id = Guid.NewGuid(),
                AssetId = assetId,
                PersonId = alice.Id,
                FaceEmbedding = new byte[2048],
                BoundingBoxJson = "{}",
                DetectionConfidence = 0.95f
            });
            await db.SaveChangesAsync();
        }

        _vm.SelectedPerson = alice;
        _vm.MergeTarget = bob;

        _vm.MergeCommand.Execute(null);

        await using (var db2 = await _dbFactory.CreateDbContextAsync())
        {
            var faces = await db2.AssetFaces.Where(f => f.PersonId == bob.Id).ToListAsync();
            faces.Should().HaveCount(1, "Alice's faces should move to Bob");

            var aliceDeleted = await db2.Persons.FindAsync(alice.Id);
            aliceDeleted.Should().BeNull("Alice should be deleted after merge");
        }
    }

    [Fact]
    public async Task DeleteCommand_RemovesPersonAndUnlinksFaces()
    {
        await _vm.LoadAsync();
        var alice = _vm.Persons[0];

        // Give Alice a face
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
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

            db.AssetFaces.Add(new AssetFace
            {
                Id = Guid.NewGuid(),
                AssetId = assetId,
                PersonId = alice.Id,
                FaceEmbedding = new byte[2048],
                BoundingBoxJson = "{}",
                DetectionConfidence = 0.95f
            });
            await db.SaveChangesAsync();
        }

        _vm.SelectedPerson = alice;

        _vm.DeleteCommand.Execute(null);

        await using (var db2 = await _dbFactory.CreateDbContextAsync())
        {
            var deleted = await db2.Persons.FindAsync(alice.Id);
            deleted.Should().BeNull("person should be deleted");

            var orphanFaces = await db2.AssetFaces.Where(f => f.PersonId == alice.Id).ToListAsync();
            orphanFaces.Should().BeEmpty("faces should be unlinked");
        }
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
