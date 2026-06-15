using Adam.Shared.Configuration;
using Adam.Shared.Extractors;
using Adam.Shared.Services;
using Adam.Shared.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Adam.Shared.Tests.Services;

/// <summary>
/// Tests for <see cref="PluginLoaderService"/> — plugin discovery, priority ordering,
/// graceful degradation, and extraction pipeline.
/// </summary>
public sealed class PluginLoaderServiceTests
{
    [Fact]
    public void Constructor_RegistersBuiltInExtractors()
    {
        var sut = CreateSut();

        sut.Extractors.Should().HaveCount(2);
        sut.Extractors[0].Should().BeOfType<ImageExtractor>();
        sut.Extractors[1].Should().BeOfType<OfficeExtractor>();
    }

    [Fact]
    public void Extractors_AreSortedByPriority()
    {
        var sut = CreateSut();

        sut.Extractors[0].Priority.Should().BeLessThan(sut.Extractors[1].Priority);
    }

    [Fact]
    public void LoadedPlugins_IncludesBuiltIn()
    {
        var sut = CreateSut();

        sut.LoadedPlugins.Should().HaveCount(2);
        sut.LoadedPlugins.Should().AllSatisfy(p => p.IsBuiltIn.Should().BeTrue());
        sut.LoadedPlugins.Should().Contain(p => p.Name.Contains("Image"));
        sut.LoadedPlugins.Should().Contain(p => p.Name.Contains("Office"));
    }

    [Fact]
    public void GetExtractor_ForImage_ReturnsImageExtractor()
    {
        var sut = CreateSut();

        var extractor = sut.GetExtractor("photo.jpg", "image/jpeg");
        extractor.Should().BeOfType<ImageExtractor>();
    }

    [Fact]
    public void GetExtractor_ForOffice_ReturnsOfficeExtractor()
    {
        var sut = CreateSut();

        var extractor = sut.GetExtractor("doc.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        extractor.Should().BeOfType<OfficeExtractor>();
    }

    [Fact]
    public void GetExtractor_ForUnsupportedFile_ReturnsNull()
    {
        var sut = CreateSut();

        var extractor = sut.GetExtractor("data.bin", "application/octet-stream");
        extractor.Should().BeNull();
    }

    [Fact]
    public void ExtractAllAsync_ForUnsupported_RunsWithoutError()
    {
        var sut = CreateSut();

        var act = () => sut.ExtractAllAsync("data.bin", "application/octet-stream", CancellationToken.None);
        act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ReloadAsync_ReRegistersBuiltIn()
    {
        var sut = CreateSut();
        await sut.ReloadAsync();

        // After reload, built-in extractors should still be registered
        sut.Extractors.Should().HaveCount(2);
        sut.Extractors.Should().AllSatisfy(e => e.Should().NotBeNull());
    }

    [Fact]
    public void HasAnyContent_OnEmptyExtractedText_Metadata_ReturnsFalse()
    {
        var meta = new ExtractedTextMetadata();
        meta.HasAnyContent.Should().BeFalse();
    }

    [Fact]
    public void HasAnyContent_OnPopulatedExtractedText_Metadata_ReturnsTrue()
    {
        var meta = new ExtractedTextMetadata { Title = "Test" };
        meta.HasAnyContent.Should().BeTrue();
    }

    private static PluginLoaderService CreateSut()
    {
        return new PluginLoaderService(
            Options.Create(new PluginConfig()),
            new NullLogger<PluginLoaderService>());
    }
}
