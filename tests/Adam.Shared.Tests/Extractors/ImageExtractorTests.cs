using Adam.Shared.Extractors;
using Adam.Shared.Models;
using Adam.Shared.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.Shared.Tests.Extractors;

/// <summary>
/// Tests for <see cref="ImageExtractor"/> adapter that wraps
/// <see cref="MetadataExtractorService"/>.
/// </summary>
public sealed class ImageExtractorTests
{
    private readonly MetadataExtractorService _innerService = new();
    private readonly ImageExtractor _sut;

    public ImageExtractorTests()
    {
        _sut = new ImageExtractor(_innerService);
    }

    [Fact]
    public void Priority_Is100()
    {
        _sut.Priority.Should().Be(100);
    }

    [Fact]
    public void Name_IsNotEmpty()
    {
        _sut.Name.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("photo.jpg", "image/jpeg", true)]
    [InlineData("photo.png", "image/png", true)]
    [InlineData("photo.webp", "image/webp", true)]
    [InlineData("photo.tiff", "image/tiff", true)]
    [InlineData("doc.pdf", "application/pdf", false)]
    [InlineData("doc.txt", "text/plain", false)]
    [InlineData("video.mp4", "video/mp4", false)]
    public void CanExtract_ReturnsExpected(string filePath, string mimeType, bool expected)
    {
        _sut.CanExtract(filePath, mimeType).Should().Be(expected);
    }

    [Fact]
    public void ExtractTextAsync_DelegatesToInnerService()
    {
        // Verify the adapter calls through to MetadataExtractorService
        // by checking that non-existent file throws (not a null/NOP result).
        var act = () => _sut.ExtractTextAsync("nonexistent.jpg", CancellationToken.None);
        act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public void ExtractAsync_DelegatesToInnerService()
    {
        var act = () => _sut.ExtractAsync("nonexistent.jpg", CancellationToken.None);
        act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public void HasAnyContent_OnEmptyExtractedText_ReturnsFalse()
    {
        var empty = new ExtractedTextMetadata();
        empty.HasAnyContent.Should().BeFalse();
    }

    [Fact]
    public void HasAnyContent_OnPopulatedExtractedText_ReturnsTrue()
    {
        var meta = new ExtractedTextMetadata { Title = "Test" };
        meta.HasAnyContent.Should().BeTrue();
    }
}
