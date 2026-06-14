using System.Text;
using Adam.CatalogBrowser.Services;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Adam.Shared.Services;
using Microsoft.EntityFrameworkCore;

namespace Adam.CatalogBrowser.Tests.Services;

public sealed class CsvMetadataServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CsvMetadataService _sut = new();

    public CsvMetadataServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ──────────────────────────────────────────────
    //  Export tests
    // ──────────────────────────────────────────────

    [Fact]
    public async Task ExportToCsvAsync_CreatesValidCsvFile()
    {
        // Arrange
        var assets = new List<DigitalAsset>
        {
            new()
            {
                FileName = "photo1.jpg",
                Title = "Sunset",
                Description = "Beautiful sunset",
                Rating = 4,
                Label = AssetLabel.Red,
                Flag = AssetFlag.Pick,
                Copyright = "© 2024",
                GpsLatitude = 48.8566,
                GpsLongitude = 2.3522,
                Keywords = [new Keyword { Name = "sunset" }, new Keyword { Name = "landscape" }],
                Categories = [new Category { Name = "Nature" }]
            }
        };

        var outputPath = Path.Combine(_tempDir, "export.csv");

        // Act
        await _sut.ExportToCsvAsync(assets, outputPath);

        // Assert
        Assert.True(File.Exists(outputPath));
        var content = await File.ReadAllTextAsync(outputPath, Encoding.UTF8);
        Assert.Contains("FileName", content);
        Assert.Contains("photo1.jpg", content);
        Assert.Contains("Sunset", content);
        Assert.Contains("sunset|landscape", content);
        Assert.Contains("Nature", content);
        Assert.Contains("4", content); // rating
        Assert.Contains("Red", content); // label
        Assert.Contains("Pick", content); // flag
        Assert.Contains("48.8566", content); // gps
        Assert.Contains("2.3522", content); // gps
    }

    [Fact]
    public async Task ExportToCsvAsync_NullFieldsHandledGracefully()
    {
        // Arrange
        var assets = new List<DigitalAsset>
        {
            new()
            {
                FileName = "test.jpg",
                Title = string.Empty,
                Description = null,
                Rating = 0,
                Label = AssetLabel.None,
                Flag = AssetFlag.Unflagged,
                Keywords = [],
                Categories = []
            }
        };

        var outputPath = Path.Combine(_tempDir, "null_test.csv");

        // Act
        await _sut.ExportToCsvAsync(assets, outputPath);

        // Assert
        var content = await File.ReadAllTextAsync(outputPath, Encoding.UTF8);
        var rows = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, rows.Length); // header + 1 row
    }

    [Fact]
    public async Task ExportToCsvAsync_UsesUtf8Bom()
    {
        // Arrange
        var assets = new List<DigitalAsset> { new() { FileName = "a.jpg" } };
        var outputPath = Path.Combine(_tempDir, "bom_test.csv");

        // Act
        await _sut.ExportToCsvAsync(assets, outputPath);

        // Assert
        var bytes = await File.ReadAllBytesAsync(outputPath);
        Assert.True(bytes.Length >= 3);
        Assert.Equal(0xEF, bytes[0]); // BOM first byte
        Assert.Equal(0xBB, bytes[1]);
        Assert.Equal(0xBF, bytes[2]);
    }

    [Fact]
    public async Task ExportToCsvAsync_HandlesCommasInFields()
    {
        // Arrange
        var assets = new List<DigitalAsset>
        {
            new()
            {
                FileName = "photo, with, commas.jpg",
                Title = "Title, with, commas",
                Description = "Desc with \"quotes\"",
                Keywords = [],
                Categories = []
            }
        };

        var outputPath = Path.Combine(_tempDir, "commas.csv");

        // Act
        await _sut.ExportToCsvAsync(assets, outputPath);

        // Assert
        var content = await File.ReadAllTextAsync(outputPath, Encoding.UTF8);
        Assert.Contains("\"photo, with, commas.jpg\"", content);
        Assert.Contains("\"Title, with, commas\"", content);
        Assert.Contains("\"Desc with \"\"quotes\"\"\"", content);
    }

    // ──────────────────────────────────────────────
    //  Round-trip tests
    // ──────────────────────────────────────────────

    [Fact]
    public async Task ExportThenRead_RoundTrip_PreservesValues()
    {
        // Arrange
        var assets = new List<DigitalAsset>
        {
            new()
            {
                FileName = "roundtrip.jpg",
                Title = "Round Trip Test",
                Description = "Testing round-trip",
                Rating = 3,
                Label = AssetLabel.Blue,
                Flag = AssetFlag.Reject,
                Copyright = "© Test",
                GpsLatitude = 51.5074,
                GpsLongitude = -0.1278,
                Keywords = [new Keyword { Name = "test" }],
                Categories = [new Category { Name = "Testing" }]
            }
        };

        var outputPath = Path.Combine(_tempDir, "roundtrip.csv");

        // Act
        await _sut.ExportToCsvAsync(assets, outputPath);
        var rows = await _sut.ReadCsvAsync(outputPath);

        // Assert
        Assert.Single(rows);
        var row = rows[0];
        Assert.Equal("roundtrip.jpg", row.FileName);
        Assert.Equal("Round Trip Test", row.Title);
        Assert.Equal("Testing round-trip", row.Description);
        Assert.Equal(3, row.Rating);
        Assert.Equal("Blue", row.Label);
        Assert.Equal("Reject", row.Flag);
        Assert.Equal("© Test", row.Copyright);
        Assert.Equal(51.5074, row.GpsLatitude);
        Assert.Equal(-0.1278, row.GpsLongitude);
    }

    // ──────────────────────────────────────────────
    //  Read/Parse tests
    // ──────────────────────────────────────────────

    [Fact]
    public async Task ReadCsvAsync_MissingFileNameColumn_Throws()
    {
        // Arrange
        var path = Path.Combine(_tempDir, "bad.csv");
        await File.WriteAllTextAsync(path, "Title,Rating\nTest,3\n");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidDataException>(() => _sut.ReadCsvAsync(path));
    }

    [Fact]
    public async Task ReadCsvAsync_ExtraColumns_IgnoresUnknown()
    {
        // Arrange
        var path = Path.Combine(_tempDir, "extra.csv");
        await File.WriteAllTextAsync(path, "FileName,Title,Rating,UnknownColumn\nphoto.jpg,Test,3,ignored\n");

        // Act
        var rows = await _sut.ReadCsvAsync(path);

        // Assert
        Assert.Single(rows);
        Assert.Equal("photo.jpg", rows[0].FileName);
        Assert.Equal("Test", rows[0].Title);
        Assert.Equal(3, rows[0].Rating);
    }

    [Fact]
    public async Task ReadCsvAsync_PipeSeparatedKeywords_ParsedCorrectly()
    {
        // Arrange
        var path = Path.Combine(_tempDir, "keywords.csv");
        await File.WriteAllTextAsync(path, "FileName,Keywords\nphoto.jpg,sunset|landscape|ocean\n");

        // Act
        var rows = await _sut.ReadCsvAsync(path);

        // Assert
        Assert.Single(rows);
        Assert.Equal("sunset|landscape|ocean", rows[0].Keywords);
    }

    [Fact]
    public async Task ReadCsvAsync_EscapedCommas_ParsedCorrectly()
    {
        // Arrange
        var path = Path.Combine(_tempDir, "escaped.csv");
        await File.WriteAllTextAsync(path, "FileName,Title\n\"photo, with, commas.jpg\",\"Title, with, commas\"\n");

        // Act
        var rows = await _sut.ReadCsvAsync(path);

        // Assert
        Assert.Single(rows);
        Assert.Equal("photo, with, commas.jpg", rows[0].FileName);
        Assert.Equal("Title, with, commas", rows[0].Title);
    }

    // ──────────────────────────────────────────────
    //  ApplyRowToAsset tests
    // ──────────────────────────────────────────────

    [Fact]
    public void ApplyRowToAsset_OverwriteMode_ReplacesValues()
    {
        // Arrange
        var asset = new DigitalAsset
        {
            FileName = "test.jpg",
            Title = "Old Title",
            Rating = 1,
            Label = AssetLabel.None,
            Flag = AssetFlag.Unflagged,
            Keywords = [],
            Categories = []
        };

        var row = new CsvMetadataRow
        {
            FileName = "test.jpg",
            Title = "New Title",
            Rating = 5,
            Label = "Green",
            Flag = "Pick"
        };

        // Act
        _sut.ApplyRowToAsset(row, asset, ConflictMode.Overwrite);

        // Assert
        Assert.Equal("New Title", asset.Title);
        Assert.Equal(5, asset.Rating);
        Assert.Equal(AssetLabel.Green, asset.Label);
        Assert.Equal(AssetFlag.Pick, asset.Flag);
    }

    [Fact]
    public void ApplyRowToAsset_AppendMode_AddsKeywordsAlongsideExisting()
    {
        // Arrange
        var asset = new DigitalAsset
        {
            FileName = "test.jpg",
            Keywords = [new Keyword { Name = "existing" }],
            Categories = []
        };

        var row = new CsvMetadataRow
        {
            FileName = "test.jpg",
            Keywords = "new|another"
        };

        // Act
        _sut.ApplyRowToAsset(row, asset, ConflictMode.AppendKeywords);

        // Assert
        Assert.Contains(asset.Keywords, k => k.Name == "existing");
        Assert.Contains(asset.Keywords, k => k.Name == "new");
        Assert.Contains(asset.Keywords, k => k.Name == "another");
        Assert.Equal(3, asset.Keywords.Count);
    }

    [Fact]
    public void ApplyRowToAsset_OverwriteMode_ReplacesKeywords()
    {
        // Arrange
        var asset = new DigitalAsset
        {
            FileName = "test.jpg",
            Keywords = [new Keyword { Name = "old" }],
            Categories = []
        };

        var row = new CsvMetadataRow
        {
            FileName = "test.jpg",
            Keywords = "new"
        };

        // Act
        _sut.ApplyRowToAsset(row, asset, ConflictMode.Overwrite);

        // Assert
        Assert.DoesNotContain(asset.Keywords, k => k.Name == "old");
        Assert.Contains(asset.Keywords, k => k.Name == "new");
        Assert.Single(asset.Keywords);
    }

    // ──────────────────────────────────────────────
    //  PreviewImport tests
    // ──────────────────────────────────────────────

    [Fact]
    public async Task PreviewImport_NonExistentAsset_ShowsSkipped()
    {
        // Arrange
        var rows = new List<CsvMetadataRow>
        {
            new() { FileName = "nonexistent.jpg", Title = "Test" }
        };

        await using var db = CreateInMemoryDb();
        var preview = await _sut.PreviewImportAsync(rows, db);

        // Assert
        var skipped = preview.FirstOrDefault(l => l.StartsWith("✕"));
        Assert.NotNull(skipped);
        Assert.Contains("nonexistent.jpg", skipped);
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
