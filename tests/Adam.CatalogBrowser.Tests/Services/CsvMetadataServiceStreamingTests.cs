using System.Text;
using Adam.CatalogBrowser.Services;
using Adam.Shared.Data;
using Adam.Shared.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Adam.CatalogBrowser.Tests.Services;

/// <summary>
/// Tests for the streaming CSV methods added in T15.2.
/// Separate file so we don't disturb the existing export/read tests.
/// </summary>
public sealed class CsvMetadataServiceStreamingTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CsvMetadataService _sut = new();

    public CsvMetadataServiceStreamingTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ──────────────────────────────────────────────
    //  ReadCsvStreamAsync tests
    // ──────────────────────────────────────────────

    [Fact]
    public async Task ReadCsvStreamAsync_ReadsAllRows()
    {
        // Arrange
        var path = Path.Combine(_tempDir, "stream_test.csv");
        await File.WriteAllTextAsync(path,
            "FileName,Title,Rating\n" +
            "a.jpg,Alpha,3\n" +
            "b.jpg,Beta,4\n" +
            "c.jpg,Gamma,5\n",
            Encoding.UTF8);

        var rows = new List<CsvMetadataRow>();

        // Act
        await foreach (var row in _sut.ReadCsvStreamAsync(path))
            rows.Add(row);

        // Assert
        rows.Should().HaveCount(3);
        rows[0].FileName.Should().Be("a.jpg");
        rows[0].Title.Should().Be("Alpha");
        rows[0].Rating.Should().Be(3);
        rows[2].FileName.Should().Be("c.jpg");
        rows[2].Rating.Should().Be(5);
    }

    [Fact]
    public async Task ReadCsvStreamAsync_MissingFileName_Throws()
    {
        // Arrange
        var path = Path.Combine(_tempDir, "bad.csv");
        await File.WriteAllTextAsync(path, "Title,Rating\nTest,3\n", Encoding.UTF8);

        // Act
        var act = async () =>
        {
            await foreach (var _ in _sut.ReadCsvStreamAsync(path)) { }
        };

        // Assert
        await act.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*missing the required 'FileName'*");
    }

    [Fact]
    public async Task ReadCsvStreamAsync_EmptyFile_YieldsNothing()
    {
        // Arrange
        var path = Path.Combine(_tempDir, "empty.csv");
        await File.WriteAllTextAsync(path, "FileName,Title\n", Encoding.UTF8); // header only

        var rows = new List<CsvMetadataRow>();

        // Act
        await foreach (var row in _sut.ReadCsvStreamAsync(path))
            rows.Add(row);

        // Assert
        rows.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadCsvStreamAsync_PipeSeparatedKeywords_ParsedCorrectly()
    {
        // Arrange
        var path = Path.Combine(_tempDir, "kw.csv");
        await File.WriteAllTextAsync(path,
            "FileName,Keywords\nphoto.jpg,sunset|landscape|ocean\n",
            Encoding.UTF8);

        var rows = new List<CsvMetadataRow>();

        // Act
        await foreach (var row in _sut.ReadCsvStreamAsync(path))
            rows.Add(row);

        // Assert
        rows.Should().ContainSingle();
        rows[0].Keywords.Should().Be("sunset|landscape|ocean");
    }

    [Fact]
    public async Task ReadCsvStreamAsync_SkipsBlankLines()
    {
        // Arrange
        var path = Path.Combine(_tempDir, "blanks.csv");
        await File.WriteAllTextAsync(path,
            "FileName\n" +
            "a.jpg\n" +
            "\n" +
            "b.jpg\n" +
            "  \n",
            Encoding.UTF8);

        var rows = new List<CsvMetadataRow>();

        // Act
        await foreach (var row in _sut.ReadCsvStreamAsync(path))
            rows.Add(row);

        // Assert
        rows.Should().HaveCount(2);
        rows.Select(r => r.FileName).Should().BeEquivalentTo(["a.jpg", "b.jpg"]);
    }

    // ──────────────────────────────────────────────
    //  ImportFromCsvStreamAsync tests
    // ──────────────────────────────────────────────

    [Fact]
    public async Task ImportFromCsvStreamAsync_MatchesAssetsByFileName()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var assetId = Guid.NewGuid();
        db.DigitalAssets.Add(new DigitalAsset
        {
            Id = assetId,
            FileName = "target.jpg",
            Title = "Original",
            MimeType = "image/jpeg",
            FileExtension = ".jpg",
            FileSize = 100,
            ChecksumSha256 = new string('a', 64),
            StoragePath = "target.jpg",
            OriginalPath = "target.jpg",
            Type = AssetType.Image,
            Keywords = [],
            Categories = []
        });
        await db.SaveChangesAsync();

        var rows = new List<CsvMetadataRow>
        {
            new() { FileName = "target.jpg", Title = "Updated Title", Rating = 5 }
        }.ToAsyncEnumerable();

        // Act
        var updated = await _sut.ImportFromCsvStreamAsync(rows, db);

        // Assert
        updated.Should().Be(1);
        var asset = await db.DigitalAssets.FindAsync(assetId);
        asset!.Title.Should().Be("Updated Title");
        asset.Rating.Should().Be(5);
    }

    [Fact]
    public async Task ImportFromCsvStreamAsync_UnknownFileName_Skips()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var assetId = Guid.NewGuid();
        db.DigitalAssets.Add(new DigitalAsset
        {
            Id = assetId,
            FileName = "existing.jpg",
            Title = "Existing",
            MimeType = "image/jpeg",
            FileExtension = ".jpg",
            FileSize = 100,
            ChecksumSha256 = new string('b', 64),
            StoragePath = "existing.jpg",
            OriginalPath = "existing.jpg",
            Type = AssetType.Image,
            Keywords = [],
            Categories = []
        });
        await db.SaveChangesAsync();

        var rows = new List<CsvMetadataRow>
        {
            new() { FileName = "nonexistent.jpg", Title = "WontApply" }
        }.ToAsyncEnumerable();

        // Act
        var updated = await _sut.ImportFromCsvStreamAsync(rows, db);

        // Assert
        updated.Should().Be(0);
        var asset = await db.DigitalAssets.FindAsync(assetId);
        asset!.Title.Should().Be("Existing"); // unchanged
    }

    [Fact]
    public async Task ImportFromCsvStreamAsync_ReportsProgress()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        for (var i = 0; i < 3; i++)
        {
            db.DigitalAssets.Add(new DigitalAsset
            {
                Id = Guid.NewGuid(),
                FileName = $"file{i}.jpg",
                Title = $"Original {i}",
                MimeType = "image/jpeg",
                FileExtension = ".jpg",
                FileSize = 100,
                ChecksumSha256 = new string((char)('c' + i), 64),
                StoragePath = $"file{i}.jpg",
                OriginalPath = $"file{i}.jpg",
                Type = AssetType.Image,
                Keywords = [],
                Categories = []
            });
        }
        await db.SaveChangesAsync();

        var rows = Enumerable.Range(0, 3)
            .Select(i => new CsvMetadataRow { FileName = $"file{i}.jpg", Title = $"Updated {i}" })
            .ToAsyncEnumerable();

        var progressValues = new List<int>();

        // Act
        await _sut.ImportFromCsvStreamAsync(rows, db,
            progress: new Progress<int>(v => progressValues.Add(v)));

        // Assert
        progressValues.Should().HaveCount(3);
        progressValues.Last().Should().Be(3);
    }

    // ──────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────

    private static AppDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        var db = new AppDbContext(options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return db;
    }
}

/// <summary>
/// Helper to convert IEnumerable to IAsyncEnumerable for test compatibility.
/// </summary>
internal static class EnumerableExtensions
{
    public static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(this IEnumerable<T> source)
    {
        foreach (var item in source)
        {
            await Task.CompletedTask;
            yield return item;
        }
    }
}
