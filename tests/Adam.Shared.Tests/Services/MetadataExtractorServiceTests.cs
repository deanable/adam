using Adam.Shared.Models;
using Adam.Shared.Services;
using FluentAssertions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace Adam.Shared.Tests.Services;

/// <summary>
/// Unit tests for <see cref="MetadataExtractorService"/> that verify EXIF, GPS,
/// IPTC, and XMP extraction from programmatically generated image files.
/// </summary>
public class MetadataExtractorServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly MetadataExtractorService _sut = new();
    private readonly MetadataWritebackService _writeback = new();

    public MetadataExtractorServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"adam_extractor_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, recursive: true); }
        catch { }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a minimal 1×1 JPEG with the specified EXIF tags embedded.
    /// </summary>
    private string CreateJpegWithExif(
        string? cameraMake = null,
        string? cameraModel = null,
        string? lensModel = null,
        double? gpsLatitude = null,
        double? gpsLongitude = null)
    {
        var path = Path.Combine(_testDir, $"exif_{Guid.NewGuid():N}.jpg");
        using var image = new Image<Rgb24>(1, 1, new Rgb24(128, 128, 128));

        var exif = new ExifProfile();

        if (cameraMake is not null)
            exif.SetValue(ExifTag.Make, cameraMake);
        if (cameraModel is not null)
            exif.SetValue(ExifTag.Model, cameraModel);
        if (lensModel is not null)
            exif.SetValue(ExifTag.LensModel, lensModel);

        if (gpsLatitude.HasValue && gpsLongitude.HasValue)
        {
            var absLat = Math.Abs(gpsLatitude.Value);
            var absLon = Math.Abs(gpsLongitude.Value);

            var latD = (uint)absLat;
            var latM = (uint)((absLat - latD) * 60);
            var latS = (absLat - latD - latM / 60.0) * 3600;

            var lonD = (uint)absLon;
            var lonM = (uint)((absLon - lonD) * 60);
            var lonS = (absLon - lonD - lonM / 60.0) * 3600;

            exif.SetValue(ExifTag.GPSLatitude, new[]
            {
                new Rational(latD, 1u),
                new Rational(latM, 1u),
                new Rational((uint)(latS * 1000), 1000u)
            });
            exif.SetValue(ExifTag.GPSLatitudeRef, gpsLatitude >= 0 ? "N" : "S");
            exif.SetValue(ExifTag.GPSLongitude, new[]
            {
                new Rational(lonD, 1u),
                new Rational(lonM, 1u),
                new Rational((uint)(lonS * 1000), 1000u)
            });
            exif.SetValue(ExifTag.GPSLongitudeRef, gpsLongitude >= 0 ? "E" : "W");
        }

        image.Metadata.ExifProfile = exif;
        image.SaveAsJpeg(path, new JpegEncoder { Quality = 95 });
        return path;
    }

    /// <summary>
    /// Creates a minimal 1×1 PNG with no metadata at all.
    /// </summary>
    private string CreatePlainPng()
    {
        var path = Path.Combine(_testDir, $"plain_{Guid.NewGuid():N}.png");
        using var image = new Image<Rgb24>(1, 1, new Rgb24(255, 0, 0));
        image.SaveAsPng(path, new PngEncoder { CompressionLevel = PngCompressionLevel.NoCompression });
        return path;
    }

    /// <summary>
    /// Creates a minimal JPEG (no EXIF), writes XMP via MetadataWritebackService,
    /// then returns the path. Useful for testing XMP-based extraction via ExtractTextMetadata.
    /// </summary>
    private async Task<string> CreateJpegWithXmp(
        string? title = null,
        string? description = null,
        string? copyright = null,
        int? rating = null,
        IReadOnlyList<string>? keywords = null)
    {
        // Minimal valid JPEG: SOI + APP0 marker + EOI
        var jpegBytes = new byte[]
        {
            0xFF, 0xD8,
            0xFF, 0xE0, 0x00, 0x10,
            0x4A, 0x46, 0x49, 0x46, 0x00,
            0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
            0xFF, 0xD9
        };
        var path = Path.Combine(_testDir, $"xmp_{Guid.NewGuid():N}.jpg");
        await File.WriteAllBytesAsync(path, jpegBytes);

        var asset = new DigitalAsset
        {
            Id = Guid.NewGuid(),
            Title = title ?? string.Empty,
            Description = description,
            Copyright = copyright,
            Rating = rating ?? 0,
            FileName = Path.GetFileName(path),
            FileExtension = ".jpg",
            MimeType = "image/jpeg",
            FileSize = jpegBytes.Length,
            ChecksumSha256 = new string('x', 64),
            StoragePath = path,
            OriginalPath = path
        };

        if (keywords is not null)
        {
            foreach (var kw in keywords)
                asset.Keywords.Add(new Keyword
                {
                    Id = Guid.NewGuid(),
                    Name = kw,
                    NormalizedName = kw.ToLowerInvariant()
                });
        }

        await _writeback.WriteMetadataAsync(path, asset);
        return path;
    }

    // ── Extract: EXIF ───────────────────────────────────────────────────────

    [Fact]
    public void Extract_JpegWithExif_ParsesCameraMakeAndModel()
    {
        var path = CreateJpegWithExif(cameraMake: "Canon", cameraModel: "EOS R5");

        var profile = _sut.Extract(path);

        profile.CameraMake.Should().Be("Canon");
        profile.CameraModel.Should().Be("EOS R5");
    }

    [Fact]
    public void Extract_JpegWithExif_ParsesLensModel()
    {
        var path = CreateJpegWithExif(lensModel: "RF 24-70mm F2.8L IS USM");

        var profile = _sut.Extract(path);

        profile.LensModel.Should().Be("RF 24-70mm F2.8L IS USM");
    }

    // ── Extract: GPS ────────────────────────────────────────────────────────

    [Fact]
    public void Extract_JpegWithGps_ParsesLatLon()
    {
        // 48° 51′ 23.6″ N, 2° 21′ 7.8″ E  ≈  Paris
        var path = CreateJpegWithExif(gpsLatitude: 48.85656, gpsLongitude: 2.35217);

        var profile = _sut.Extract(path);

        profile.GpsLatitude.Should().BeApproximately(48.85656, 0.001);
        profile.GpsLongitude.Should().BeApproximately(2.35217, 0.001);
    }

    [Fact]
    public void Extract_JpegWithGps_SouthernHemisphere()
    {
        // 33° 51′ 54″ S, 151° 12′ 34″ E  ≈  Sydney
        var path = CreateJpegWithExif(gpsLatitude: -33.865, gpsLongitude: 151.2094);

        var profile = _sut.Extract(path);

        profile.GpsLatitude.Should().BeApproximately(-33.865, 0.001);
        profile.GpsLongitude.Should().BeApproximately(151.2094, 0.001);
    }

    [Fact]
    public void Extract_JpegWithNoGps_GPSFieldsNull()
    {
        var path = CreateJpegWithExif(cameraMake: "Nikon");

        var profile = _sut.Extract(path);

        profile.GpsLatitude.Should().BeNull();
        profile.GpsLongitude.Should().BeNull();
        profile.GpsAltitude.Should().BeNull();
    }

    // ── Extract: no metadata ────────────────────────────────────────────────

    [Fact]
    public void Extract_FileWithNoExif_ReturnsEmptyProfile()
    {
        var path = CreatePlainPng();

        var profile = _sut.Extract(path);

        profile.CameraMake.Should().BeNull();
        profile.CameraModel.Should().BeNull();
        profile.LensModel.Should().BeNull();
        profile.GpsLatitude.Should().BeNull();
        profile.GpsLongitude.Should().BeNull();
        profile.DateTaken.Should().BeNull();
        profile.Iso.Should().BeNull();
        profile.FocalLength.Should().BeNull();
        profile.Aperture.Should().BeNull();
        profile.ExposureTime.Should().BeNull();
        profile.Flash.Should().BeNull();
        profile.Orientation.Should().BeNull();
        profile.Creator.Should().BeNull();
        profile.Copyright.Should().BeNull();
    }

    // ── Extract: XMP round-trip (via MetadataWritebackService) ───────────────
    // Note: Extract() only maps dc:creator and dc:rights from XMP.
    // Title, Description, Rating, Keywords are only available via ExtractTextMetadata().

    [Fact]
    public async Task Extract_JpegWithXmpCopyright_ParsesCopyrightFromXmp()
    {
        var path = await CreateJpegWithXmp(copyright: "© 2026 Test Corp");

        var profile = _sut.Extract(path);

        profile.Copyright.Should().Be("© 2026 Test Corp");
    }

    // ── Extract: error handling ─────────────────────────────────────────────

    [Fact]
    public void Extract_NonExistentFile_ThrowsFileNotFoundException()
    {
        var fakePath = Path.Combine(_testDir, "does_not_exist.jpg");

        var act = () => _sut.Extract(fakePath);

        act.Should().Throw<FileNotFoundException>();
    }

    // ── ExtractTextMetadata ──────────────────────────────────────────────────

    [Fact]
    public async Task ExtractTextMetadata_JpegWithXmpKeywords_ReturnsKeywords()
    {
        var path = await CreateJpegWithXmp(title: "Tagged", keywords: ["Nature", "Landscape", "Golden Hour"]);

        var result = _sut.ExtractTextMetadata(path);

        result.Keywords.Should().Contain("Nature");
        result.Keywords.Should().Contain("Landscape");
        result.Keywords.Should().Contain("Golden Hour");
    }

    [Fact]
    public async Task ExtractTextMetadata_JpegWithXmpTitle_ReturnsTitle()
    {
        var path = await CreateJpegWithXmp(title: "Mountain Vista");

        var result = _sut.ExtractTextMetadata(path);

        result.Title.Should().Be("Mountain Vista");
    }

    [Fact]
    public async Task ExtractTextMetadata_JpegWithXmpDescription_ReturnsDescription()
    {
        var path = await CreateJpegWithXmp(description: "Fog rolling through the valley at dawn");

        var result = _sut.ExtractTextMetadata(path);

        result.Description.Should().Be("Fog rolling through the valley at dawn");
    }

    [Fact]
    public async Task ExtractTextMetadata_JpegWithXmpRating_ReturnsRating()
    {
        var path = await CreateJpegWithXmp(rating: 5);

        var result = _sut.ExtractTextMetadata(path);

        result.Rating.Should().Be(5);
    }

    [Fact]
    public void ExtractTextMetadata_PlainPng_ReturnsEmptyResult()
    {
        var path = CreatePlainPng();

        var result = _sut.ExtractTextMetadata(path);

        result.Title.Should().BeNull();
        result.Description.Should().BeNull();
        result.Keywords.Should().BeEmpty();
        result.Categories.Should().BeEmpty();
        result.Rating.Should().BeNull();
        result.HasHierarchicalKeywords.Should().BeFalse();
    }

    [Fact]
    public void ExtractTextMetadata_NonExistentFile_ThrowsFileNotFoundException()
    {
        var fakePath = Path.Combine(_testDir, "missing.jpg");

        var act = () => _sut.ExtractTextMetadata(fakePath);

        act.Should().Throw<FileNotFoundException>();
    }
}
