using Adam.CatalogBrowser.Services;
using Adam.Shared.Data;
using Adam.Shared.Models;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.CatalogBrowser.Tests.Services;

/// <summary>
/// Tests for the <see cref="BulkOperationQueue"/> background processing queue.
/// Each test creates a fresh ModeManager with a temporary SQLite database,
/// seeds test data, enqueues operations, and waits for completion via the
/// <see cref="BulkOperationQueue.AllCompleted"/> event.
/// </summary>
public sealed class BulkOperationQueueTests : IAsyncLifetime
{
    private readonly string _basePath;
    private readonly ModeManager _modeManager;
    private readonly NullLogger<BulkOperationQueue> _logger;
    private BulkOperationQueue _queue = null!;

    public BulkOperationQueueTests()
    {
        _basePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _modeManager = new ModeManager(_basePath);
        _logger = new NullLogger<BulkOperationQueue>();
    }

    public async Task InitializeAsync()
    {
        await _modeManager.InitializeAsync();
        _queue = new BulkOperationQueue(_modeManager, _logger);
    }

    public async Task DisposeAsync()
    {
        await _queue.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_basePath))
                Directory.Delete(_basePath, recursive: true);
        }
        catch (IOException) { }
    }

    // ──────────────────────────────────────────────
    //  Enqueue + progress
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Enqueue_KeywordOperation_AssignsKeywordToAsset()
    {
        // Arrange: seed an asset
        var assetId = await SeedAssetAsync();
        var completedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _queue.AllCompleted += (_, _) => completedTcs.TrySetResult();

        // Act
        _queue.Enqueue(new BulkOperation
        {
            AssetIds = [assetId],
            Name = "Summer",
            IsKeyword = true
        });

        await completedTcs.Task.WaitAsync(TimeSpan.FromSeconds(15));

        // Assert
        await using var db = _modeManager.CreateDbContext();
        var asset = await db.DigitalAssets
            .Include(a => a.Keywords)
            .FirstAsync(a => a.Id == assetId);

        asset.Keywords.Should().ContainSingle(k => k.Name == "Summer");
    }

    [Fact]
    public async Task Enqueue_CategoryOperation_AssignsCategoryToAsset()
    {
        // Arrange: seed an asset
        var assetId = await SeedAssetAsync();
        var completedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _queue.AllCompleted += (_, _) => completedTcs.TrySetResult();

        // Act
        _queue.Enqueue(new BulkOperation
        {
            AssetIds = [assetId],
            Name = "Nature",
            IsKeyword = false
        });

        await completedTcs.Task.WaitAsync(TimeSpan.FromSeconds(15));

        // Assert
        await using var db = _modeManager.CreateDbContext();
        var asset = await db.DigitalAssets
            .Include(a => a.Categories)
            .FirstAsync(a => a.Id == assetId);

        asset.Categories.Should().ContainSingle(c => c.Name == "Nature");
    }

    [Fact]
    public async Task Enqueue_MultipleAssets_AssignsToAll()
    {
        // Arrange: seed two assets
        var id1 = await SeedAssetAsync();
        var id2 = await SeedAssetAsync();
        var completedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _queue.AllCompleted += (_, _) => completedTcs.TrySetResult();

        // Act
        _queue.Enqueue(new BulkOperation
        {
            AssetIds = [id1, id2],
            Name = "SharedKeyword",
            IsKeyword = true
        });

        await completedTcs.Task.WaitAsync(TimeSpan.FromSeconds(15));

        // Assert
        await using var db = _modeManager.CreateDbContext();
        var asset1 = await db.DigitalAssets
            .Include(a => a.Keywords)
            .FirstAsync(a => a.Id == id1);
        var asset2 = await db.DigitalAssets
            .Include(a => a.Keywords)
            .FirstAsync(a => a.Id == id2);

        asset1.Keywords.Should().ContainSingle(k => k.Name == "SharedKeyword");
        asset2.Keywords.Should().ContainSingle(k => k.Name == "SharedKeyword");
    }

    [Fact]
    public async Task Enqueue_NonExistentAsset_SkipsGracefully()
    {
        // Arrange: use a random non-existent ID
        var missingId = Guid.NewGuid();
        var completedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _queue.AllCompleted += (_, _) => completedTcs.TrySetResult();

        // Act — should not throw
        _queue.Enqueue(new BulkOperation
        {
            AssetIds = [missingId],
            Name = "Ghost",
            IsKeyword = true
        });

        await completedTcs.Task.WaitAsync(TimeSpan.FromSeconds(15));

        // Assert: no exception means the skip was handled
        Assert.True(true);
    }

    // ──────────────────────────────────────────────
    //  Progress event
    // ──────────────────────────────────────────────

    [Fact]
    public async Task ProgressChanged_FiresDuringProcessing()
    {
        // Arrange
        var assetId = await SeedAssetAsync();
        var progressSnapshot = new List<BulkOperationProgress>();
        var allCompletedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _queue.ProgressChanged += (_, progress) =>
        {
            lock (progressSnapshot)
                progressSnapshot.Add(progress);
        };
        _queue.AllCompleted += (_, _) => allCompletedTcs.TrySetResult();

        // Act
        _queue.Enqueue(new BulkOperation
        {
            AssetIds = [assetId],
            Name = "TagMe",
            IsKeyword = true
        });

        await allCompletedTcs.Task.WaitAsync(TimeSpan.FromSeconds(15));

        // Assert
        lock (progressSnapshot)
        {
            progressSnapshot.Should().NotBeEmpty("progress should fire at least once");
            var last = progressSnapshot.Last();
            last.Completed.Should().Be(1);
            last.Total.Should().Be(1);
            last.Percentage.Should().Be(100);
            last.IsActive.Should().BeFalse("all work done, should be inactive");
        }
    }

    [Fact]
    public async Task CurrentProgress_ReflectsQueueAfterCompletion()
    {
        // Arrange
        var assetId = await SeedAssetAsync();
        var completedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _queue.AllCompleted += (_, _) => completedTcs.TrySetResult();

        _queue.Enqueue(new BulkOperation
        {
            AssetIds = [assetId],
            Name = "ProgressCheck",
            IsKeyword = true
        });

        await completedTcs.Task.WaitAsync(TimeSpan.FromSeconds(15));

        // Assert
        _queue.CurrentProgress.Completed.Should().Be(1);
        _queue.CurrentProgress.Total.Should().Be(1);
        _queue.CurrentProgress.IsActive.Should().BeFalse();
    }

    // ──────────────────────────────────────────────
    //  Multiple batch operations
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Enqueue_MultipleBatches_ProcessesSequentially()
    {
        // Arrange: seed two assets
        var id1 = await SeedAssetAsync();
        var id2 = await SeedAssetAsync();
        var firstBatchTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondBatchTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Wire up a stateful handler to track batches
        bool firstBatchCompleted = false;
        _queue.AllCompleted += (_, _) =>
        {
            if (!firstBatchCompleted)
            {
                firstBatchCompleted = true;
                firstBatchTcs.TrySetResult();
            }
            else
            {
                secondBatchTcs.TrySetResult();
            }
        };

        // Act: enqueue first batch
        _queue.Enqueue(new BulkOperation
        {
            AssetIds = [id1],
            Name = "KeywordA",
            IsKeyword = true
        });

        // Wait for first batch to complete before enqueueing second
        await firstBatchTcs.Task.WaitAsync(TimeSpan.FromSeconds(15));

        // Enqueue second batch
        _queue.Enqueue(new BulkOperation
        {
            AssetIds = [id2],
            Name = "KeywordB",
            IsKeyword = true
        });

        await secondBatchTcs.Task.WaitAsync(TimeSpan.FromSeconds(15));

        // Assert
        await using var db = _modeManager.CreateDbContext();
        var asset1 = await db.DigitalAssets
            .Include(a => a.Keywords)
            .FirstAsync(a => a.Id == id1);
        var asset2 = await db.DigitalAssets
            .Include(a => a.Keywords)
            .FirstAsync(a => a.Id == id2);

        asset1.Keywords.Should().ContainSingle(k => k.Name == "KeywordA");
        asset2.Keywords.Should().ContainSingle(k => k.Name == "KeywordB");
    }

    // ──────────────────────────────────────────────
    //  Disposal
    // ──────────────────────────────────────────────

    [Fact]
    public async Task DisposeAsync_CancelsProcessingAndCompletes()
    {
        // Act & Assert: DisposeAsync should complete without throwing
        await _queue.DisposeAsync();

        // After disposal, enqueue should still work (TryWrite) but won't be processed
        _queue.Enqueue(new BulkOperation
        {
            AssetIds = [Guid.NewGuid()],
            Name = "AfterDispose",
            IsKeyword = true
        });

        // No exception means success
        Assert.True(true);
    }

    // ──────────────────────────────────────────────
    //  Empty / edge cases
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Enqueue_EmptyAssetIds_DoesNotThrow()
    {
        var completedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.AllCompleted += (_, _) => completedTcs.TrySetResult();

        _queue.Enqueue(new BulkOperation
        {
            AssetIds = [],
            Name = "Empty",
            IsKeyword = true
        });

        await completedTcs.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.True(true);
    }

    [Fact]
    public async Task Enqueue_EmptyName_DoesNotAssign()
    {
        // Arrange: seed an asset
        var assetId = await SeedAssetAsync();
        var completedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _queue.AllCompleted += (_, _) => completedTcs.TrySetResult();

        // Act: enqueue with empty name — AssociateKeywordsAsync handles this
        _queue.Enqueue(new BulkOperation
        {
            AssetIds = [assetId],
            Name = string.Empty,
            IsKeyword = true
        });

        await completedTcs.Task.WaitAsync(TimeSpan.FromSeconds(15));

        // Assert: no keyword should be assigned (empty name filtered out by AssociateKeywordsAsync)
        await using var db = _modeManager.CreateDbContext();
        var asset = await db.DigitalAssets
            .Include(a => a.Keywords)
            .FirstAsync(a => a.Id == assetId);

        asset.Keywords.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────

    private async Task<Guid> SeedAssetAsync()
    {
        var assetId = Guid.NewGuid();
        await using var db = await _modeManager.CreateDbContextAsync();
        db.DigitalAssets.Add(new DigitalAsset
        {
            Id = assetId,
            FileName = "test.jpg",
            FileExtension = ".jpg",
            MimeType = "image/jpeg",
            FileSize = 1024,
            ChecksumSha256 = new string('a', 64),
            StoragePath = "test.jpg",
            Title = "Test Asset",
            Type = AssetType.Image,
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return assetId;
    }
}
