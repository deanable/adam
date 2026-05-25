using System;
using System.IO;
using System.Threading.Tasks;
using Adam.Shared.ThumbnailExtractors;
using FluentAssertions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Adam.Shared.Tests.ThumbnailExtractors;

public class GenericIconExtractorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly GenericIconExtractor _sut = new();

    public GenericIconExtractorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void CanExtract_AnyFile_ReturnsTrue()
    {
        _sut.CanExtract("/tmp/anything.txt", "text/plain").Should().BeTrue();
        _sut.CanExtract("/tmp/noextension", "application/octet-stream").Should().BeTrue();
        _sut.CanExtract("/tmp/archive.zip", "application/zip").Should().BeTrue();
    }

    [Fact]
    public void Priority_Returns999()
    {
        _sut.Priority.Should().Be(999);
    }

    [Fact]
    public async Task ExtractAsync_ValidPath_CreatesJpegFile()
    {
        var source = Path.Combine(_tempDir, "testfile.zip");
        var dest = Path.Combine(_tempDir, "icon.jpg");
        
        File.WriteAllText(source, "dummy content");

        var result = await _sut.ExtractAsync(source, dest, 256, default);

        result.Should().BeTrue();
        File.Exists(dest).Should().BeTrue();
        
        using var image = Image.Load<Rgba32>(dest);
        image.Width.Should().Be(256);
        image.Height.Should().Be(256);
    }

    [Fact]
    public async Task ExtractAsync_SourceFileDoesNotExist_Succeeds()
    {
        var source = Path.Combine(_tempDir, "nonexistent.xyz");
        var dest = Path.Combine(_tempDir, "icon.jpg");

        var result = await _sut.ExtractAsync(source, dest, 256, default);

        result.Should().BeTrue();
        File.Exists(dest).Should().BeTrue();
    }

    [Fact]
    public async Task ExtractAsync_FileWithoutExtension_UsesFILE()
    {
        var source = Path.Combine(_tempDir, "noextension");
        var dest = Path.Combine(_tempDir, "icon.jpg");
        
        File.WriteAllText(source, "dummy");

        var result = await _sut.ExtractAsync(source, dest, 128, default);

        result.Should().BeTrue();
        File.Exists(dest).Should().BeTrue();
        
        using var image = Image.Load<Rgba32>(dest);
        image.Width.Should().Be(128);
        image.Height.Should().Be(128);
    }

    [Fact]
    public async Task ExtractAsync_DifferentExtensions_ProduceDeterministicColors()
    {
        var dest1 = Path.Combine(_tempDir, "icon1.jpg");
        var dest2 = Path.Combine(_tempDir, "icon2.jpg");
        var dest3 = Path.Combine(_tempDir, "icon3.jpg");

        await _sut.ExtractAsync("/tmp/file.pdf", dest1, 64, default);
        await _sut.ExtractAsync("/tmp/other.pdf", dest2, 64, default);
        await _sut.ExtractAsync("/tmp/file.docx", dest3, 64, default);

        using var img1 = Image.Load<Rgba32>(dest1);
        using var img2 = Image.Load<Rgba32>(dest2);
        using var img3 = Image.Load<Rgba32>(dest3);

        var pixel1 = img1[0, 0];
        var pixel2 = img2[0, 0];
        var pixel3 = img3[0, 0];

        pixel1.Should().Be(pixel2);
        
        pixel3.Should().NotBe(pixel1);
    }
}
