using Adam.Shared.ThumbnailExtractors;
using FluentAssertions;
using Xunit;

namespace Adam.Shared.Tests.ThumbnailExtractors;

public class VideoThumbnailExtractorTests
{
    private readonly VideoThumbnailExtractor _sut = new();

    [Fact]
    public void CanExtract_Mp4_ReturnsTrue()
    {
        _sut.CanExtract("/tmp/video.mp4", "video/mp4").Should().BeTrue();
    }

    [Fact]
    public void CanExtract_Avi_ReturnsTrue()
    {
        _sut.CanExtract("/tmp/video.avi", "video/x-msvideo").Should().BeTrue();
    }

    [Fact]
    public void CanExtract_Mov_ReturnsTrue()
    {
        _sut.CanExtract("/tmp/video.mov", "video/quicktime").Should().BeTrue();
    }

    [Fact]
    public void CanExtract_Jpg_ReturnsFalse()
    {
        _sut.CanExtract("/tmp/photo.jpg", "image/jpeg").Should().BeFalse();
    }
}
