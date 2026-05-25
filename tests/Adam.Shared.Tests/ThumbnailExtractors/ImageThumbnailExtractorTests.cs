using System;
using System.IO;
using System.Threading.Tasks;
using Adam.Shared.ThumbnailExtractors;
using FluentAssertions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Jpeg;
using Xunit;

namespace Adam.Shared.Tests.ThumbnailExtractors;

public class ImageThumbnailExtractorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ImageThumbnailExtractor _sut = new();

    public ImageThumbnailExtractorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string CreateTestImage(int width, int height, string fileName)
    {
        var path = Path.Combine(_tempDir, fileName);
        using var image = new Image<Rgba32>(width, height, new Rgba32(255, 0, 0));
        image.Save(path, new JpegEncoder());
        return path;
    }

    [Fact]
    public void CanExtract_Jpg_ReturnsTrue()
    {
        _sut.CanExtract("/tmp/photo.jpg", "image/jpeg").Should().BeTrue();
    }

    [Fact]
    public void CanExtract_Png_ReturnsTrue()
    {
        _sut.CanExtract("/tmp/photo.png", "image/png").Should().BeTrue();
    }

    [Fact]
    public void CanExtract_Mp4_ReturnsFalse()
    {
        _sut.CanExtract("/tmp/video.mp4", "video/mp4").Should().BeFalse();
    }

    [Fact]
    public async Task ExtractAsync_LandscapeImage_ResizesToMaxWidth()
    {
        var source = CreateTestImage(1024, 512, "landscape.jpg");
        var dest = Path.Combine(_tempDir, "thumb.jpg");

        var result = await _sut.ExtractAsync(source, dest, 256, default);

        result.Should().BeTrue();
        File.Exists(dest).Should().BeTrue();
        using var thumb = Image.Load(dest);
        thumb.Width.Should().Be(256);
        thumb.Height.Should().BeLessThan(256);
    }

    [Fact]
    public async Task ExtractAsync_PortraitImage_ResizesToMaxHeight()
    {
        var source = CreateTestImage(512, 1024, "portrait.jpg");
        var dest = Path.Combine(_tempDir, "thumb.jpg");

        var result = await _sut.ExtractAsync(source, dest, 256, default);

        result.Should().BeTrue();
        File.Exists(dest).Should().BeTrue();
        using var thumb = Image.Load(dest);
        thumb.Height.Should().Be(256);
        thumb.Width.Should().BeLessThan(256);
    }
}
