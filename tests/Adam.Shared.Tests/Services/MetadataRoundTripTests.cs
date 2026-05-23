using Adam.Shared.Models;
using Adam.Shared.Services;
using FluentAssertions;

namespace Adam.Shared.Tests.Services;

public class MetadataRoundTripTests : IDisposable
{
    private readonly string _testDir;
    private readonly MetadataWritebackService _writeback = new();
    private readonly MetadataExtractorService _extractor = new();

    public MetadataRoundTripTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"adam_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
                Directory.Delete(_testDir, recursive: true);
        }
        catch { }
    }

    private string CreateTestJpeg()
    {
        var path = Path.Combine(_testDir, "test.jpg");
        // Create a minimal valid 1x1 JPEG
        var jpegBytes = new byte[]
        {
            0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01,
            0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0xFF, 0xDB, 0x00, 0x43,
            0x00, 0x08, 0x06, 0x06, 0x07, 0x06, 0x05, 0x08, 0x07, 0x07, 0x07, 0x09,
            0x09, 0x08, 0x0A, 0x0C, 0x14, 0x0D, 0x0C, 0x0B, 0x0B, 0x0C, 0x19, 0x12,
            0x13, 0x0F, 0x14, 0x1D, 0x1A, 0x1F, 0x1E, 0x1D, 0x1A, 0x1C, 0x1C, 0x20,
            0x24, 0x2E, 0x27, 0x20, 0x22, 0x2C, 0x23, 0x1C, 0x1C, 0x28, 0x37, 0x29,
            0x2C, 0x30, 0x31, 0x34, 0x34, 0x34, 0x1F, 0x27, 0x39, 0x3D, 0x38, 0x32,
            0x3C, 0x2E, 0x33, 0x34, 0x32, 0xFF, 0xC0, 0x00, 0x0B, 0x08, 0x00, 0x01,
            0x00, 0x01, 0x01, 0x01, 0x11, 0x00, 0xFF, 0xC4, 0x00, 0x1F, 0x00, 0x00,
            0x01, 0x05, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07,
            0x08, 0x09, 0x0A, 0x0B, 0xFF, 0xC4, 0x00, 0xB5, 0x10, 0x00, 0x02, 0x01,
            0x03, 0x03, 0x02, 0x04, 0x03, 0x05, 0x05, 0x04, 0x04, 0x00, 0x00, 0x01,
            0x7D, 0x01, 0x02, 0x03, 0x00, 0x04, 0x11, 0x05, 0x12, 0x21, 0x31, 0x41,
            0x06, 0x13, 0x51, 0x61, 0x07, 0x22, 0x71, 0x14, 0x32, 0x81, 0x91, 0xA1,
            0x08, 0x23, 0x42, 0xB1, 0xC1, 0x15, 0x52, 0xD1, 0xF0, 0x24, 0x33, 0x62,
            0x72, 0x82, 0x09, 0x0A, 0x16, 0x17, 0x18, 0x19, 0x1A, 0x25, 0x26, 0x27,
            0x28, 0x29, 0x2A, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3A, 0x43, 0x44,
            0x45, 0x46, 0x47, 0x48, 0x49, 0x4A, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58,
            0x59, 0x5A, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69, 0x6A, 0x73, 0x74,
            0x75, 0x76, 0x77, 0x78, 0x79, 0x7A, 0x83, 0x84, 0x85, 0x86, 0x87, 0x88,
            0x89, 0x8A, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98, 0x99, 0x9A, 0xA2,
            0xA3, 0xA4, 0xA5, 0xA6, 0xA7, 0xA8, 0xA9, 0xAA, 0xB2, 0xB3, 0xB4, 0xB5,
            0xB6, 0xB7, 0xB8, 0xB9, 0xBA, 0xC2, 0xC3, 0xC4, 0xC5, 0xC6, 0xC7, 0xC8,
            0xC9, 0xCA, 0xD2, 0xD3, 0xD4, 0xD5, 0xD6, 0xD7, 0xD8, 0xD9, 0xDA, 0xE1,
            0xE2, 0xE3, 0xE4, 0xE5, 0xE6, 0xE7, 0xE8, 0xE9, 0xEA, 0xF1, 0xF2, 0xF3,
            0xF4, 0xF5, 0xF6, 0xF7, 0xF8, 0xF9, 0xFA, 0xFF, 0xDA, 0x00, 0x08, 0x01,
            0x01, 0x00, 0x00, 0x3F, 0x00, 0xFB, 0xD5, 0xDB, 0x20, 0xF9, 0x4E, 0xFF,
            0xD9
        };
        File.WriteAllBytes(path, jpegBytes);
        return path;
    }

    [Fact]
    public async Task WriteEmbeddedXmp_ToJpeg_CanBeReadBack()
    {
        // Arrange
        var jpegPath = CreateTestJpeg();
        var asset = new DigitalAsset
        {
            Title = "Test Title",
            Description = "Test Description",
            Copyright = "© 2026 Test",
            Rating = 4,
            GpsLatitude = 51.5074,
            GpsLongitude = -0.1278
        };

        // Act
        await _writeback.WriteMetadataAsync(jpegPath, asset);

        // Assert - read back with MetadataExtractor
        var textMeta = _extractor.ExtractTextMetadata(jpegPath);
        textMeta.Title.Should().Be("Test Title");
        textMeta.Description.Should().Be("Test Description");
        textMeta.Rating.Should().Be(4);

        // Also verify the raw XMP packet contains the expected fields
        // (MetadataExtractor's flat property map doesn't always capture structured XMP like dc:rights/rdf:Alt)
        var directories = MetadataExtractor.ImageMetadataReader.ReadMetadata(jpegPath);
        var xmpDir = directories.OfType<MetadataExtractor.Formats.Xmp.XmpDirectory>().FirstOrDefault();
        xmpDir.Should().NotBeNull();
        var props = xmpDir!.GetXmpProperties();
        props.Values.Should().Contain("Test Title");
        props.Values.Should().Contain("Test Description");
        props.Values.Should().Contain("© 2026 Test");

        // GPS is written as exif:GPSLatitude/GPSLongitude in XMP;
        // MetadataExtractor reads GPS from EXIF GpsDirectory, not XMP,
        // so we verify the XMP content directly (culture-aware formatting).
        props.Values.Should().Contain(v => v.Contains("51") && v.Contains("5074"));
        props.Values.Should().Contain(v => v.Contains("-0") && v.Contains("1278"));
    }

    [Fact]
    public async Task WriteSidecarXmp_ForRawFile_CreatesXmpFile()
    {
        // Arrange
        var rawPath = Path.Combine(_testDir, "test.nef");
        File.WriteAllText(rawPath, "dummy raw");
        var asset = new DigitalAsset
        {
            Title = "Raw Title",
            Description = "Raw Description",
            Rating = 3
        };

        // Act
        await _writeback.WriteSidecarXmpAsync(rawPath, asset);

        // Assert
        var sidecarPath = Path.ChangeExtension(rawPath, ".xmp");
        File.Exists(sidecarPath).Should().BeTrue();
        var content = File.ReadAllText(sidecarPath);
        content.Should().Contain("Raw Title");
        content.Should().Contain("Raw Description");
        content.Should().Contain("xmp:Rating");
    }

    [Fact]
    public void WriteMetadataAsync_ReadOnlyFile_ThrowsReadOnlyFileException()
    {
        // Arrange
        var jpegPath = CreateTestJpeg();
        var fi = new FileInfo(jpegPath);
        fi.IsReadOnly = true;
        var asset = new DigitalAsset { Title = "Test" };

        // Act & Assert
        var act = () => _writeback.WriteMetadataAsync(jpegPath, asset).GetAwaiter().GetResult();
        act.Should().Throw<MetadataWritebackService.ReadOnlyFileException>();
    }

    [Fact]
    public async Task ImageExportService_ExportJpeg_ProducesValidFile()
    {
        // Arrange
        var sourcePath = CreateTestJpeg();
        var destPath = Path.Combine(_testDir, "exported.jpg");
        var service = new ImageExportService();

        // Act
        await service.ExportAsync(sourcePath, destPath, ImageExportService.ExportFormat.Jpeg, quality: 90);

        // Assert
        File.Exists(destPath).Should().BeTrue();
        new FileInfo(destPath).Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ImageExportService_ExportTiff_ProducesValidFile()
    {
        // Arrange
        var sourcePath = CreateTestJpeg();
        var destPath = Path.Combine(_testDir, "exported.tiff");
        var service = new ImageExportService();

        // Act
        await service.ExportAsync(sourcePath, destPath, ImageExportService.ExportFormat.Tiff);

        // Assert
        File.Exists(destPath).Should().BeTrue();
        new FileInfo(destPath).Length.Should().BeGreaterThan(0);
    }
}
