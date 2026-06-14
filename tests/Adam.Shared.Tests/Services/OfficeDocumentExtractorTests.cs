using Adam.Shared.Services;

namespace Adam.Shared.Tests.Services;

public sealed class OfficeDocumentExtractorTests
{
    private readonly OfficeDocumentExtractor _extractor = new();

    [Fact]
    public void Extract_NonOfficeFile_ReturnsEmpty()
    {
        var result = _extractor.Extract("photo.jpg");
        Assert.NotNull(result);
        Assert.Null(result.Title);
        Assert.Empty(result.Keywords);
    }

    [Fact]
    public void Extract_TextFile_ReturnsEmpty()
    {
        var result = _extractor.Extract("readme.txt");
        Assert.NotNull(result);
        Assert.Null(result.Title);
    }

    [Fact]
    public void SupportedExtensions_ContainsOfficeFormats()
    {
        Assert.Contains(".docx", OfficeDocumentExtractor.SupportedExtensions);
        Assert.Contains(".xlsx", OfficeDocumentExtractor.SupportedExtensions);
        Assert.Contains(".pptx", OfficeDocumentExtractor.SupportedExtensions);
        Assert.Equal(3, OfficeDocumentExtractor.SupportedExtensions.Count);
    }

    [Fact]
    public void SupportedExtensions_DoesNotContainImages()
    {
        Assert.DoesNotContain(".jpg", OfficeDocumentExtractor.SupportedExtensions);
        Assert.DoesNotContain(".pdf", OfficeDocumentExtractor.SupportedExtensions);
    }
}
