using System;
using System.IO;
using System.Threading.Tasks;
using Adam.Shared.ThumbnailExtractors;
using FluentAssertions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Png;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace Adam.Shared.Tests.ThumbnailExtractors;

public class PdfPreviewExtractorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly PdfPreviewExtractor _sut = new();

    public PdfPreviewExtractorTests()
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
    public void CanExtract_Pdf_ReturnsTrue()
    {
        _sut.CanExtract("/tmp/doc.pdf", "application/pdf").Should().BeTrue();
    }

    [Fact]
    public void CanExtract_Jpg_ReturnsFalse()
    {
        _sut.CanExtract("/tmp/photo.jpg", "image/jpeg").Should().BeFalse();
    }

    [Fact]
    public async Task ExtractAsync_PdfWithEmbeddedImage_ReturnsTrue()
    {
        var source = Path.Combine(_tempDir, "with_image.pdf");
        var dest = Path.Combine(_tempDir, "thumb.jpg");

        CreatePdfWithImage(source);

        var result = await _sut.ExtractAsync(source, dest, 256, default);

        result.Should().BeTrue();
        File.Exists(dest).Should().BeTrue();
    }

    [Fact]
    public async Task ExtractAsync_PdfWithoutImages_ReturnsFalse()
    {
        var source = Path.Combine(_tempDir, "no_images.pdf");
        var dest = Path.Combine(_tempDir, "thumb.jpg");

        CreateMinimalPdf(source);

        var result = await _sut.ExtractAsync(source, dest, 256, default);

        result.Should().BeFalse();
    }

    private static void CreatePdfWithImage(string path)
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(612, 792);

        // Create a simple 100x100 red PNG image bytes
        using var ms = new MemoryStream();
        using (var image = new Image<Rgba32>(100, 100, new Rgba32(255, 0, 0)))
        {
            image.Save(ms, new PngEncoder());
        }
        var imageBytes = ms.ToArray();

        page.AddPng(imageBytes, new PdfRectangle(0, 0, 100, 100));

        var doc = builder.Build();
        File.WriteAllBytes(path, doc);
    }

    private static void CreateMinimalPdf(string path)
    {
        var builder = new PdfDocumentBuilder();
        builder.AddPage(612, 792);
        var doc = builder.Build();
        File.WriteAllBytes(path, doc);
    }
}
