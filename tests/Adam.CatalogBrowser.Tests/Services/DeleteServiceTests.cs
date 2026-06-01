using Adam.CatalogBrowser.Services;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Adam.Shared.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Adam.CatalogBrowser.Tests.Services;

public class DeleteServiceTests : IDisposable
{
    private readonly string _basePath;
    private readonly ModeManager _modeManager;
    private readonly DeleteService _sut;
    private readonly AppDbContext _db;
    private readonly Guid _assetId;

    public DeleteServiceTests()
    {
        _basePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _modeManager = new ModeManager(_basePath);
        _modeManager.InitializeAsync().GetAwaiter().GetResult();

        _db = _modeManager.CreateDbContext();
        _db.Database.EnsureCreated();

        var collection = new Collection
        {
            Id = Guid.NewGuid(),
            Name = "Test Collection",
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow
        };
        _db.Collections.Add(collection);

        _assetId = Guid.NewGuid();
        _db.DigitalAssets.Add(new DigitalAsset
        {
            Id = _assetId,
            FileName = "test.jpg",
            FileExtension = ".jpg",
            MimeType = "image/jpeg",
            FileSize = 1024,
            ChecksumSha256 = new string('a', 64),
            StoragePath = "test.jpg",
            Title = "Test Asset",
            Type = AssetType.Image,
            CollectionId = collection.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();

        _sut = new DeleteService(_modeManager);
    }

    [Fact]
    public async Task SoftDeleteAsync_SetsIsDeletedFlag()
    {
        var result = await _sut.SoftDeleteAsync(_assetId);
        result.Should().BeTrue();

        var inDb = await _sut.GetDeletedAssetsAsync();
        inDb.Should().ContainSingle(a => a.Id == _assetId);
    }

    [Fact]
    public async Task SoftDeleteAsync_NonExistentAsset_ReturnsFalse()
    {
        var result = await _sut.SoftDeleteAsync(Guid.NewGuid());
        result.Should().BeFalse();
    }

    [Fact]
    public async Task RestoreAsync_RestoresSoftDeletedAsset()
    {
        await _sut.SoftDeleteAsync(_assetId);
        var result = await _sut.RestoreAsync(_assetId);
        result.Should().BeTrue();

        var deleted = await _sut.GetDeletedAssetsAsync();
        deleted.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDeletedAssetsAsync_ReturnsOnlyDeleted()
    {
        await _sut.SoftDeleteAsync(_assetId);

        var deleted = await _sut.GetDeletedAssetsAsync();
        deleted.Should().ContainSingle(a => a.Id == _assetId);
    }

    [Fact]
    public async Task PermanentlyDeleteAsync_RemovesAsset()
    {
        await _sut.SoftDeleteAsync(_assetId);
        var result = await _sut.PermanentlyDeleteAsync(_assetId);
        result.Should().BeTrue();

        var deleted = await _sut.GetDeletedAssetsAsync();
        deleted.Should().BeEmpty();
    }

    [Fact]
    public async Task PermanentlyDeleteAsync_NonDeletedAsset_ReturnsFalse()
    {
        var result = await _sut.PermanentlyDeleteAsync(_assetId);
        result.Should().BeFalse();
    }

    public void Dispose()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_basePath))
                Directory.Delete(_basePath, recursive: true);
        }
        catch (IOException)
        {
            // SQLite may still hold file lock during cleanup
        }
    }
}
