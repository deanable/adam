using Adam.Shared.Data;
using Adam.Shared.Models;
using Adam.Shared.Services;
using FluentAssertions;
using LiquidVision.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.ComponentModel;

namespace Adam.Shared.Tests.Services;

public sealed class AiTaggingServiceTests : IDisposable
{
    private readonly string _basePath;
    private readonly ModeManager _modeManager;
    private readonly ILiquidVisionAnalyzer _fakeAnalyzer;
    private readonly AiTaggingService _service;

    public AiTaggingServiceTests()
    {
        _basePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _modeManager = new ModeManager(_basePath);
        _modeManager.InitializeAsync().GetAwaiter().GetResult();

        _fakeAnalyzer = Substitute.For<ILiquidVisionAnalyzer>();
        _fakeAnalyzer.IsInitialized.Returns(true);
        _fakeAnalyzer.InitializeAsync(Arg.Any<IProgress<double>?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _fakeAnalyzer.WhenForAnyArgs(x => x.PropertyChanged += Arg.Any<PropertyChangedEventHandler>())
            .Do(_ => { }); // suppress NSubstitute warning for event subscription

        _service = new AiTaggingService(
            _fakeAnalyzer,
            _modeManager,
            NullLogger<AiTaggingService>.Instance);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_basePath, recursive: true); } catch { }
    }

    private async Task<Guid> SeedImageAssetAsync()
    {
        await using var db = await _modeManager.CreateDbContextAsync();
        var id = Guid.NewGuid();
        var tempFile = Path.Combine(_basePath, $"{id}.jpg");
        await File.WriteAllBytesAsync(tempFile, [0xFF, 0xD8, 0xFF, 0xE0]); // tiny JPEG header
        db.DigitalAssets.Add(new DigitalAsset
        {
            Id = id,
            FileName = $"{id}.jpg",
            FileExtension = ".jpg",
            MimeType = "image/jpeg",
            FileSize = 42,
            ChecksumSha256 = "abcdef",
            StoragePath = tempFile.Replace('\\', '/'),
            OriginalPath = tempFile.Replace('\\', '/'),
            Title = "Test Image",
            Type = AssetType.Image,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> SeedNonImageAssetAsync(AssetType type)
    {
        await using var db = await _modeManager.CreateDbContextAsync();
        var id = Guid.NewGuid();
        db.DigitalAssets.Add(new DigitalAsset
        {
            Id = id,
            FileName = $"{id}.txt",
            FileExtension = ".txt",
            MimeType = "text/plain",
            FileSize = 10,
            ChecksumSha256 = "123456",
            StoragePath = $"C:/test/{id}.txt",
            OriginalPath = $"C:/test/{id}.txt",
            Title = "Test Document",
            Type = type,
        });
        await db.SaveChangesAsync();
        return id;
    }

    [Fact]
    public async Task TagAssetAsync_ImageAsset_AnalyzerCalledAndKeywordsMerged()
    {
        // Arrange
        var assetId = await SeedImageAssetAsync();
        _fakeAnalyzer.AnalyzeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ImageTagResult("a cat on a mat", ["cat", "mat"], ["animals"], "{}", 10.0, "1.0"));

        // Act
        await _service.TagAssetAsync(assetId);

        // Assert
        await using var db = await _modeManager.CreateDbContextAsync();
        var asset = await db.DigitalAssets
            .Include(a => a.Keywords)
            .Include(a => a.Categories)
            .FirstAsync(a => a.Id == assetId);

        asset.Keywords.Should().Contain(k => k.Name == "cat");
        asset.Keywords.Should().Contain(k => k.Name == "mat");
        asset.Categories.Should().Contain(c => c.Name == "animals");
    }

    [Fact]
    public async Task TagAssetAsync_NonImageAsset_AnalyzerNotCalled()
    {
        // Arrange
        var assetId = await SeedNonImageAssetAsync(AssetType.Document);

        // Act
        await _service.TagAssetAsync(assetId);

        // Assert
        await _fakeAnalyzer.Received(0).AnalyzeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _fakeAnalyzer.Received(0).AnalyzeAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TagAssetAsync_DescriptionFilledOnlyWhenEmpty()
    {
        // Arrange
        var assetId = await SeedImageAssetAsync();
        _fakeAnalyzer.AnalyzeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ImageTagResult("AI generated description", ["tag1"], ["cat"], "{}", 10.0, "1.0"));

        // Act — description is null from seed
        await _service.TagAssetAsync(assetId);

        // Assert — description was filled
        await using var db = await _modeManager.CreateDbContextAsync();
        var asset1 = await db.DigitalAssets.FirstAsync(a => a.Id == assetId);
        asset1.Description.Should().Be("AI generated description");

        // Re-seed with existing description and re-run
        asset1.Description = "Human written description";
        await db.SaveChangesAsync();

        await _service.TagAssetAsync(assetId);

        // Assert — description unchanged (D-06: never overwrite human text)
        var asset2 = await db.DigitalAssets.FirstAsync(a => a.Id == assetId);
        asset2.Description.Should().Be("Human written description");
    }

    [Fact]
    public async Task TagAssetAsync_CancellationThrows()
    {
        // Arrange
        var assetId = await SeedImageAssetAsync();
        _fakeAnalyzer.AnalyzeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var ct = callInfo.ArgAt<CancellationToken>(1);
                ct.ThrowIfCancellationRequested();
                return new ImageTagResult("desc", ["tag"], ["cat"], "{}", 10.0, "1.0");
            });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        Func<Task> act = () => _service.TagAssetAsync(assetId, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task TagAssetsAsync_ImageOnlyFilteringAndProgress()
    {
        // Arrange
        var imageId = await SeedImageAssetAsync();
        var docId = await SeedNonImageAssetAsync(AssetType.Document);
        _fakeAnalyzer.AnalyzeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ImageTagResult("desc", ["tag"], ["cat"], "{}", 10.0, "1.0"));

        var progressItems = new List<(int, int)>();

        var progress = new Progress<(int completed, int total)>(p => progressItems.Add(p));

        // Act
        await _service.TagAssetsAsync([imageId, docId], progress);

        // Assert — only image asset was analyzed
        await _fakeAnalyzer.Received(1).AnalyzeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

        // Progress should be reported for both attempts
        progressItems.Should().HaveCount(2);
        progressItems[0].Should().Be((1, 2));
        progressItems[1].Should().Be((2, 2));
    }

    [Fact]
    public async Task AnalyzeAssetAsync_ReturnsResultWithoutDbWrite()
    {
        // Arrange
        var assetId = await SeedImageAssetAsync();
        _fakeAnalyzer.AnalyzeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ImageTagResult("analyzed", ["kw1"], ["cat1"], "{}", 10.0, "1.0"));

        // Act
        var result = await _service.AnalyzeAssetAsync(assetId);

        // Assert
        result.Description.Should().Be("analyzed");
        result.Keywords.Should().BeEquivalentTo(["kw1"]);
        result.Categories.Should().BeEquivalentTo(["cat1"]);

        // Verify no DB write occurred
        await using var db = await _modeManager.CreateDbContextAsync();
        var asset = await db.DigitalAssets
            .Include(a => a.Keywords)
            .FirstAsync(a => a.Id == assetId);
        asset.Keywords.Should().BeEmpty();
    }

    [Fact]
    public async Task AnalyzeAssetAsync_NonImageAsset_Throws()
    {
        // Arrange
        var assetId = await SeedNonImageAssetAsync(AssetType.Video);

        // Act
        Func<Task> act = () => _service.AnalyzeAssetAsync(assetId);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        await _fakeAnalyzer.Received(0).AnalyzeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
