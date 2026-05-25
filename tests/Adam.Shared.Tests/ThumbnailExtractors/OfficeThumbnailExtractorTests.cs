using System;
using System.IO;
using System.IO.Packaging;
using System.Threading.Tasks;
using Adam.Shared.ThumbnailExtractors;
using FluentAssertions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Adam.Shared.Tests.ThumbnailExtractors;

public class OfficeThumbnailExtractorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly OfficeThumbnailExtractor _sut = new();

    public OfficeThumbnailExtractorTests()
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
    public void CanExtract_Docx_ReturnsTrue()
    {
        _sut.CanExtract("/tmp/doc.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document").Should().BeTrue();
    }

    [Fact]
    public void CanExtract_Xlsx_ReturnsTrue()
    {
        _sut.CanExtract("/tmp/sheet.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet").Should().BeTrue();
    }

    [Fact]
    public void CanExtract_Pdf_ReturnsFalse()
    {
        _sut.CanExtract("/tmp/doc.pdf", "application/pdf").Should().BeFalse();
    }

    [Fact]
    public async Task ExtractAsync_DocxWithThumbnail_ReturnsTrue()
    {
        var source = Path.Combine(_tempDir, "with_thumb.docx");
        var dest = Path.Combine(_tempDir, "thumb.jpg");

        CreateDocxWithThumbnail(source);

        var result = await _sut.ExtractAsync(source, dest, 256, default);

        result.Should().BeTrue();
        File.Exists(dest).Should().BeTrue();
    }

    [Fact]
    public async Task ExtractAsync_DocxWithoutThumbnail_ReturnsFalse()
    {
        var source = Path.Combine(_tempDir, "no_thumb.docx");
        var dest = Path.Combine(_tempDir, "thumb.jpg");

        CreateMinimalDocx(source);

        var result = await _sut.ExtractAsync(source, dest, 256, default);

        result.Should().BeFalse();
    }

    private static void CreateDocxWithThumbnail(string path)
    {
        using var package = Package.Open(path, FileMode.Create);
        package.CreatePart(new Uri("/[Content_Types].xml", UriKind.Relative), "application/xml");
        
        var thumbUri = new Uri("/docProps/thumbnail.jpeg", UriKind.Relative);
        var thumbPart = package.CreatePart(thumbUri, "image/jpeg");
        using (var thumbStream = thumbPart.GetStream(FileMode.Create))
        {
            using var image = new Image<Rgba32>(100, 100, new Rgba32(0, 128, 255));
            image.Save(thumbStream, new JpegEncoder());
        }
    }

    private static void CreateMinimalDocx(string path)
    {
        using var package = Package.Open(path, FileMode.Create);
        package.CreatePart(new Uri("/[Content_Types].xml", UriKind.Relative), "application/xml");
    }
}
