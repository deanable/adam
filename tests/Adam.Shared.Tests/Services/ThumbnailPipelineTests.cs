using System.Threading;
using System.Threading.Tasks;
using Adam.Shared.Services;
using Adam.Shared.ThumbnailExtractors;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Adam.Shared.Tests.Services;

public class ThumbnailPipelineTests
{
    [Fact]
    public async Task TryExtractAsync_FirstExtractorSucceeds_ReturnsTrue()
    {
        // Arrange
        var extractor = Substitute.For<IThumbnailExtractor>();
        extractor.Priority.Returns(100);
        extractor.CanExtract(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        extractor.ExtractAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var pipeline = new ThumbnailPipeline([extractor]);

        // Act
        var result = await pipeline.TryExtractAsync("/tmp/test.mp4", "/tmp/out.jpg", "video/mp4", 256, default);

        // Assert
        result.Should().BeTrue();
        await extractor.Received(1).ExtractAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TryExtractAsync_FirstExtractorFails_SecondSucceeds_ReturnsTrue()
    {
        // Arrange
        var first = Substitute.For<IThumbnailExtractor>();
        first.Priority.Returns(100);
        first.CanExtract(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        first.ExtractAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        var second = Substitute.For<IThumbnailExtractor>();
        second.Priority.Returns(200);
        second.CanExtract(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        second.ExtractAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var pipeline = new ThumbnailPipeline([first, second]);

        // Act
        var result = await pipeline.TryExtractAsync("/tmp/test.mp4", "/tmp/out.jpg", "video/mp4", 256, default);

        // Assert
        result.Should().BeTrue();
        await first.Received(1).ExtractAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await second.Received(1).ExtractAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TryExtractAsync_ExtractorThrowsException_PipelineContinues()
    {
        // Arrange
        var first = Substitute.For<IThumbnailExtractor>();
        first.Priority.Returns(100);
        first.CanExtract(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        first.ExtractAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(new InvalidOperationException("boom")));

        var second = Substitute.For<IThumbnailExtractor>();
        second.Priority.Returns(200);
        second.CanExtract(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        second.ExtractAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var pipeline = new ThumbnailPipeline([first, second]);

        // Act
        var result = await pipeline.TryExtractAsync("/tmp/test.mp4", "/tmp/out.jpg", "video/mp4", 256, default);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task TryExtractAsync_CanExtractReturnsFalse_SkipsExtractor()
    {
        // Arrange
        var first = Substitute.For<IThumbnailExtractor>();
        first.Priority.Returns(100);
        first.CanExtract(Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        var second = Substitute.For<IThumbnailExtractor>();
        second.Priority.Returns(200);
        second.CanExtract(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        second.ExtractAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var pipeline = new ThumbnailPipeline([first, second]);

        // Act
        var result = await pipeline.TryExtractAsync("/tmp/test.mp4", "/tmp/out.jpg", "video/mp4", 256, default);

        // Assert
        result.Should().BeTrue();
        await first.Received(0).ExtractAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TryExtractAsync_AllExtractorsFail_ReturnsFalse()
    {
        // Arrange
        var extractor = Substitute.For<IThumbnailExtractor>();
        extractor.Priority.Returns(100);
        extractor.CanExtract(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        extractor.ExtractAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        var pipeline = new ThumbnailPipeline([extractor]);

        // Act
        var result = await pipeline.TryExtractAsync("/tmp/test.mp4", "/tmp/out.jpg", "video/mp4", 256, default);

        // Assert
        result.Should().BeFalse();
    }
}
