using Adam.Shared.Extractors;
using Adam.Shared.Services;
using FluentAssertions;

namespace Adam.Shared.Tests.Extractors;

/// <summary>
/// Tests for <see cref="OfficeExtractor"/> adapter that wraps
/// <see cref="OfficeDocumentExtractor"/>.
/// </summary>
public sealed class OfficeExtractorTests
{
    private readonly OfficeDocumentExtractor _innerService = new();
    private readonly OfficeExtractor _sut;

    public OfficeExtractorTests()
    {
        _sut = new OfficeExtractor(_innerService);
    }

    [Fact]
    public void Priority_Is200()
    {
        _sut.Priority.Should().Be(200);
    }

    [Fact]
    public void Name_IsNotEmpty()
    {
        _sut.Name.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("doc.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", true)]
    [InlineData("sheet.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", true)]
    [InlineData("pres.pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation", true)]
    [InlineData("photo.jpg", "image/jpeg", false)]
    [InlineData("doc.pdf", "application/pdf", false)]
    [InlineData("doc.txt", "text/plain", false)]
    public void CanExtract_ReturnsExpected(string filePath, string mimeType, bool expected)
    {
        _sut.CanExtract(filePath, mimeType).Should().Be(expected);
    }

    [Fact]
    public async Task ExtractAsync_ReturnsNull()
    {
        // Office docs don't have rich metadata profiles
        var result = await _sut.ExtractAsync("test.docx", CancellationToken.None);
        result.Should().BeNull();
    }

    [Fact]
    public async Task ExtractTextAsync_ForNonExistentFile_ReturnsEmpty()
    {
        // Office docs that don't exist will return empty metadata
        var result = await _sut.ExtractTextAsync("nonexistent.docx", CancellationToken.None);
        result.Should().BeNull();
    }
}
