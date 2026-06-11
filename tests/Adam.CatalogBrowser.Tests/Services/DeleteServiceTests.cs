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

    [Fact]
    public async Task BulkSoftDeleteAsync_DeletesMultipleAssets()
    {
        // Arrange: seed two more assets
        var id2 = Guid.NewGuid();
        var id3 = Guid.NewGuid();
        _db.DigitalAssets.Add(new DigitalAsset
        {
            Id = id2,
            FileName = "test2.jpg",
            FileExtension = ".jpg",
            MimeType = "image/jpeg",
            FileSize = 2048,
            ChecksumSha256 = new string('b', 64),
            StoragePath = "test2.jpg",
            Title = "Test Asset 2",
            Type = AssetType.Image,
            CollectionId = _db.Collections.First().Id,
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow,
        });
        _db.DigitalAssets.Add(new DigitalAsset
        {
            Id = id3,
            FileName = "test3.jpg",
            FileExtension = ".jpg",
            MimeType = "image/jpeg",
            FileSize = 3072,
            ChecksumSha256 = new string('c', 64),
            StoragePath = "test3.jpg",
            Title = "Test Asset 3",
            Type = AssetType.Image,
            CollectionId = _db.Collections.First().Id,
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();

        // Act
        var count = await _sut.BulkSoftDeleteAsync([_assetId, id2, id3]);

        // Assert
        count.Should().Be(3);
        var deleted = await _sut.GetDeletedAssetsAsync();
        deleted.Should().HaveCount(3);
        deleted.Select(a => a.Id).Should().Contain([_assetId, id2, id3]);
    }

    [Fact]
    public async Task BulkSoftDeleteAsync_EmptyList_ReturnsZero()
    {
        var count = await _sut.BulkSoftDeleteAsync([]);
        count.Should().Be(0);
    }

    [Fact]
    public async Task BulkSoftDeleteAsync_MixedExistence_ReturnsOnlyExisting()
    {
        // Act — _assetId exists, bogus does not
        var bogusId = Guid.NewGuid();
        var count = await _sut.BulkSoftDeleteAsync([_assetId, bogusId]);

        // Assert
        count.Should().Be(1);
        var deleted = await _sut.GetDeletedAssetsAsync();
        deleted.Should().ContainSingle(a => a.Id == _assetId);
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
