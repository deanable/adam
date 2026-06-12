using System.Text;
using System.Xml.Linq;
using Adam.Shared.Models;
using Adam.Shared.Services;
using FluentAssertions;

namespace Adam.Shared.Tests.Services;

/// <summary>
/// Unit tests for <see cref="MetadataWritebackService"/> that verify XMP packet
/// construction, sidecar writing, file-type detection, and read-only guard —
/// all without needing a real camera file or MetadataExtractor parsing.
/// </summary>
public class MetadataWritebackServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly MetadataWritebackService _sut = new();

    public MetadataWritebackServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"adam_writeback_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, recursive: true); }
        catch { }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private string TempFile(string name, byte[]? content = null)
    {
        var path = Path.Combine(_testDir, name);
        File.WriteAllBytes(path, content ?? []);
        return path;
    }

    private static XDocument ParseXmp(string xmpText)
    {
        // Strip the xpacket processing instructions
        var inner = xmpText
            .Replace("<?xpacket begin=\"\uFEFF\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>", "")
            .Replace("<?xpacket end=\"w\"?>", "")
            .Trim();
        return XDocument.Parse(inner);
    }

    private static string? FindDcElement(XDocument doc, string localName)
    {
        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        return doc.Descendants(dc + localName).FirstOrDefault()?.Value;
    }

    // ── SupportsEmbeddedMetadata ──────────────────────────────────────────────

    [Theory]
    [InlineData("photo.jpg",  true)]
    [InlineData("photo.jpeg", true)]
    [InlineData("image.png",  true)]
    [InlineData("scan.tiff",  true)]
    [InlineData("scan.tif",   true)]
    [InlineData("clip.webp",  true)]
    [InlineData("raw.cr2",    false)]
    [InlineData("raw.nef",    false)]
    [InlineData("raw.arw",    false)]
    [InlineData("doc.pdf",    false)]
    public void SupportsEmbeddedMetadata_ReturnsExpected(string file, bool expected)
    {
        _sut.SupportsEmbeddedMetadata(file).Should().Be(expected);
    }

    // ── IsRawFile ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("shot.cr2",  true)]
    [InlineData("shot.nef",  true)]
    [InlineData("shot.arw",  true)]
    [InlineData("shot.dng",  true)]
    [InlineData("shot.raf",  true)]
    [InlineData("shot.orf",  true)]
    [InlineData("shot.pef",  true)]
    [InlineData("shot.rw2",  true)]
    [InlineData("photo.jpg", false)]
    [InlineData("image.png", false)]
    public void IsRawFile_ReturnsExpected(string file, bool expected)
    {
        _sut.IsRawFile(file).Should().Be(expected);
    }

    // ── WriteSidecarXmpAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task WriteSidecarXmpAsync_CreatesXmpFileNextToRaw()
    {
        var rawPath = TempFile("photo.cr2");
        var asset = new DigitalAsset
        {
            Id = Guid.NewGuid(),
            Title = "Mountain Sunrise",
            Description = "A beautiful dawn",
            Copyright = "© 2025 Test",
            Rating = 4,
            FileName = "photo.cr2",
            FileExtension = ".cr2",
            MimeType = "image/x-canon-cr2",
            FileSize = 1,
            ChecksumSha256 = new string('a', 64),
            StoragePath = rawPath,
            OriginalPath = rawPath
        };

        await _sut.WriteSidecarXmpAsync(rawPath, asset);

        var sidecarPath = Path.ChangeExtension(rawPath, ".xmp");
        File.Exists(sidecarPath).Should().BeTrue();

        var content = await File.ReadAllTextAsync(sidecarPath);
        content.Should().Contain("xpacket");
        content.Should().Contain("Mountain Sunrise");
        content.Should().Contain("© 2025 Test");
    }

    [Fact]
    public async Task WriteSidecarXmpAsync_ThrowsReadOnlyFileException_WhenSidecarIsReadOnly()
    {
        var rawPath = TempFile("locked.cr2");
        var sidecarPath = Path.ChangeExtension(rawPath, ".xmp");
        File.WriteAllText(sidecarPath, "existing");
        File.SetAttributes(sidecarPath, FileAttributes.ReadOnly);

        var asset = new DigitalAsset
        {
            Id = Guid.NewGuid(),
            Title = "Test",
            FileName = "locked.cr2",
            FileExtension = ".cr2",
            MimeType = "image/x-canon-cr2",
            FileSize = 1,
            ChecksumSha256 = new string('b', 64),
            StoragePath = rawPath,
            OriginalPath = rawPath
        };

        Func<Task> act = () => _sut.WriteSidecarXmpAsync(rawPath, asset);

        await act.Should().ThrowAsync<MetadataWritebackService.ReadOnlyFileException>()
            .WithMessage("*locked*");

        // cleanup
        File.SetAttributes(sidecarPath, FileAttributes.Normal);
    }

    // ── WriteMetadataAsync (DigitalAsset overload) — XMP content ─────────────

    [Fact]
    public async Task WriteMetadataAsync_Asset_EmbedsXmpInJpeg()
    {
        // Minimal valid JPEG: SOI + APP0 marker + EOI
        var jpegBytes = new byte[]
        {
            0xFF, 0xD8,                                     // SOI
            0xFF, 0xE0, 0x00, 0x10,                         // APP0 marker + length=16
            0x4A, 0x46, 0x49, 0x46, 0x00,                   // JFIF\0
            0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, // version + density
            0xFF, 0xD9                                       // EOI
        };
        var path = TempFile("embed.jpg", jpegBytes);

        var asset = new DigitalAsset
        {
            Id = Guid.NewGuid(),
            Title = "Test Title",
            Copyright = "© Tester",
            Rating = 5,
            Label = AssetLabel.Green,
            FileName = "embed.jpg",
            FileExtension = ".jpg",
            MimeType = "image/jpeg",
            FileSize = jpegBytes.Length,
            ChecksumSha256 = new string('c', 64),
            StoragePath = path,
            OriginalPath = path
        };

        await _sut.WriteMetadataAsync(path, asset);

        var result = await File.ReadAllBytesAsync(path);
        var resultText = Encoding.UTF8.GetString(result);
        resultText.Should().Contain("xpacket");
        resultText.Should().Contain("Test Title");
        resultText.Should().Contain("© Tester");
    }

    [Fact]
    public async Task WriteMetadataAsync_Asset_ThrowsReadOnlyFileException_WhenFileIsReadOnly()
    {
        var path = TempFile("readonly.jpg", [0xFF, 0xD8, 0xFF, 0xD9]);
        File.SetAttributes(path, FileAttributes.ReadOnly);

        var asset = new DigitalAsset
        {
            Id = Guid.NewGuid(),
            Title = "X",
            FileName = "readonly.jpg",
            FileExtension = ".jpg",
            MimeType = "image/jpeg",
            FileSize = 4,
            ChecksumSha256 = new string('d', 64),
            StoragePath = path,
            OriginalPath = path
        };

        Func<Task> act = () => _sut.WriteMetadataAsync(path, asset);

        await act.Should().ThrowAsync<MetadataWritebackService.ReadOnlyFileException>();

        File.SetAttributes(path, FileAttributes.Normal);
    }

    // ── XMP packet structure (via sidecar — no file format parsing needed) ────

    [Fact]
    public async Task WriteSidecarXmpAsync_XmpContainsKeywords_AsSubjectBag()
    {
        var rawPath = TempFile("keywords.cr2");
        var asset = new DigitalAsset
        {
            Id = Guid.NewGuid(),
            Title = "Tagged",
            FileName = "keywords.cr2",
            FileExtension = ".cr2",
            MimeType = "image/x-canon-cr2",
            FileSize = 1,
            ChecksumSha256 = new string('e', 64),
            StoragePath = rawPath,
            OriginalPath = rawPath,
            Keywords =
            [
                new Keyword { Id = Guid.NewGuid(), Name = "Nature", NormalizedName = "nature" },
                new Keyword { Id = Guid.NewGuid(), Name = "Landscape", NormalizedName = "landscape" }
            ]
        };

        await _sut.WriteSidecarXmpAsync(rawPath, asset);

        var content = await File.ReadAllTextAsync(Path.ChangeExtension(rawPath, ".xmp"));
        content.Should().Contain("Nature");
        content.Should().Contain("Landscape");
        content.Should().Contain("dc:subject");
    }

    [Fact]
    public async Task WriteSidecarXmpAsync_XmpContainsGpsCoordinates_WhenPresent()
    {
        var rawPath = TempFile("gps.cr2");
        var asset = new DigitalAsset
        {
            Id = Guid.NewGuid(),
            Title = "GPS Shot",
            GpsLatitude = 48.8566,
            GpsLongitude = 2.3522,
            FileName = "gps.cr2",
            FileExtension = ".cr2",
            MimeType = "image/x-canon-cr2",
            FileSize = 1,
            ChecksumSha256 = new string('f', 64),
            StoragePath = rawPath,
            OriginalPath = rawPath
        };

        await _sut.WriteSidecarXmpAsync(rawPath, asset);

        var content = await File.ReadAllTextAsync(Path.ChangeExtension(rawPath, ".xmp"));
        content.Should().Contain("GPSLatitude");
        content.Should().Contain("48.856600");
        content.Should().Contain("GPSLongitude");
    }

    [Fact]
    public async Task WriteSidecarXmpAsync_NoGpsElements_WhenCoordinatesAbsent()
    {
        var rawPath = TempFile("nogps.cr2");
        var asset = new DigitalAsset
        {
            Id = Guid.NewGuid(),
            Title = "No GPS",
            FileName = "nogps.cr2",
            FileExtension = ".cr2",
            MimeType = "image/x-canon-cr2",
            FileSize = 1,
            ChecksumSha256 = new string('g', 64),
            StoragePath = rawPath,
            OriginalPath = rawPath
        };

        await _sut.WriteSidecarXmpAsync(rawPath, asset);

        var content = await File.ReadAllTextAsync(Path.ChangeExtension(rawPath, ".xmp"));
        content.Should().NotContain("GPSLatitude");
    }

    // ── WriteMetadataAsync (MetadataProfile overload) — PNG round-trip ──────

    [Fact]
    public async Task WriteMetadataAsync_Png_EmbeddedXmpRoundTrip()
    {
        // Minimal valid PNG: signature + IHDR + IEND
        var pngBytes = CreateMinimalPng();
        var path = TempFile("roundtrip.png", pngBytes);

        var profile = new MetadataProfile
        {
            Title = "PNG Title",
            Description = "PNG Description",
            Copyright = "© 2024 Test",
            Rating = 4
        };

        await _sut.WriteMetadataAsync(path, profile);

        // Verify XMP was embedded by reading the raw bytes and checking for xpacket
        var result = await File.ReadAllBytesAsync(path);
        var resultText = Encoding.UTF8.GetString(result);
        resultText.Should().Contain("xpacket");
        resultText.Should().Contain("PNG Title");
        resultText.Should().Contain("PNG Description");
        resultText.Should().Contain("© 2024 Test");
    }

    // ── WriteMetadataAsync — unsupported extension ───────────────────────────

    [Fact]
    public async Task WriteMetadataAsync_UnsupportedExtension_DoesNothing()
    {
        var originalContent = Encoding.UTF8.GetBytes("original mp4 content");
        var path = TempFile("video.mp4", originalContent);

        var profile = new MetadataProfile
        {
            Title = "Should Not Be Written",
            Rating = 3
        };

        // Should not throw
        await _sut.WriteMetadataAsync(path, profile);

        // File content should be unchanged
        var result = await File.ReadAllBytesAsync(path);
        result.Should().Equal(originalContent);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static byte[] CreateMinimalPng()
    {
        // Minimal valid 1x1 PNG: signature + IHDR + IEND
        using var ms = new MemoryStream();
        // PNG signature
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], 0, 8);
        // IHDR chunk: 1x1 pixel, 8-bit RGB
        var ihdr = new byte[13];
        WriteBigEndian(ihdr, 0, 1); // width
        WriteBigEndian(ihdr, 4, 1); // height
        ihdr[8] = 8; // bit depth
        ihdr[9] = 2; // color type (RGB)
        WriteChunk(ms, "IHDR", ihdr);
        // IEND chunk
        WriteChunk(ms, "IEND", []);
        return ms.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);
        var length = (uint)data.Length;
        var buffer = new byte[4];
        WriteBigEndian(buffer, 0, length);
        stream.Write(buffer, 0, 4);
        stream.Write(typeBytes, 0, 4);
        stream.Write(data, 0, data.Length);
        // CRC over type + data
        var crcBytes = new byte[typeBytes.Length + data.Length];
        Array.Copy(typeBytes, 0, crcBytes, 0, typeBytes.Length);
        Array.Copy(data, 0, crcBytes, typeBytes.Length, data.Length);
        var crc = Crc32(crcBytes);
        WriteBigEndian(buffer, 0, crc);
        stream.Write(buffer, 0, 4);
    }

    private static void WriteBigEndian(byte[] buffer, int offset, uint value)
    {
        buffer[offset]     = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static uint Crc32(byte[] data)
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
                c = (c & 1) == 1 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
            crc = table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }
}
