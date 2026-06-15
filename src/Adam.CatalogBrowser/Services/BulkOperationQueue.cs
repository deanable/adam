using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Adam.Shared.Data;
using Adam.Shared.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Adam.CatalogBrowser.Services;

/// <summary>
/// Represents a bulk operation that assigns a keyword or category to a set of assets.
/// </summary>
public sealed class BulkOperation
{
    public List<Guid> AssetIds { get; init; } = [];
    public string Name { get; init; } = string.Empty;
    public bool IsKeyword { get; init; }
}

/// <summary>
/// Progress snapshot for a bulk operation batch.
/// </summary>
public class BulkOperationProgress : INotifyPropertyChanged
{
    private int _completed;
    private int _total;
    private int _failed;
    private double _percentage;
    private string _currentOperation = string.Empty;
    private bool _isActive;

    public int Completed
    {
        get => _completed;
        set { _completed = value; OnPropertyChanged(); }
    }

    public int Total
    {
        get => _total;
        set { _total = value; OnPropertyChanged(); }
    }

    public int Pending => Total - Completed - Failed;

    public int Failed
    {
        get => _failed;
        set { _failed = value; OnPropertyChanged(); OnPropertyChanged(nameof(Pending)); }
    }

    public double Percentage
    {
        get => _percentage;
        set { _percentage = value; OnPropertyChanged(); }
    }

    public string CurrentOperation
    {
        get => _currentOperation;
        set { _currentOperation = value; OnPropertyChanged(); }
    }

    public bool IsActive
    {
        get => _isActive;
        set { _isActive = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>
/// Processes bulk catalog/metadata updates sequentially on a background thread.
/// Enqueue operations (e.g. assign a keyword to many assets) and they are
/// applied one-by-one, with progress reported back to subscribers.
/// </summary>
public sealed class BulkOperationQueue : IAsyncDisposable
{
    private readonly ModeManager _modeManager;
    private readonly ILogger<BulkOperationQueue> _logger;
    private readonly Channel<BulkOperation> _channel;
    private readonly CancellationTokenSource _cts;
    private readonly Task _processorTask;
    private int _totalQueued;
    private int _completedCount;
    private int _failedCount;
    private bool _disposed;

    /// <summary>
    /// Fires after each operation is processed so the UI can update progress.
    /// </summary>
    public event EventHandler<BulkOperationProgress>? ProgressChanged;

    /// <summary>
    /// Fires when all queued operations have been processed.
    /// </summary>
    public event EventHandler? AllCompleted;

    public BulkOperationProgress CurrentProgress { get; } = new();

    public BulkOperationQueue(ModeManager modeManager, ILogger<BulkOperationQueue> logger)
    {
        _modeManager = modeManager;
        _logger = logger;
        _channel = Channel.CreateBounded<BulkOperation>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true
        });
        _cts = new CancellationTokenSource();
        _processorTask = ProcessLoopAsync(_cts.Token);
    }

    /// <summary>
    /// Enqueue a bulk operation for background processing.
    /// </summary>
    public void Enqueue(BulkOperation operation)
    {
        // If starting a new batch after a completed one, reset counters
        var completed = Volatile.Read(ref _completedCount);
        var failed = Volatile.Read(ref _failedCount);
        var total = Volatile.Read(ref _totalQueued);
        if (total > 0 && completed + failed >= total)
        {
            Interlocked.Exchange(ref _totalQueued, 0);
            Interlocked.Exchange(ref _completedCount, 0);
            Interlocked.Exchange(ref _failedCount, 0);
        }

        Interlocked.Increment(ref _totalQueued);
        _channel.Writer.TryWrite(operation);
        _logger.LogInformation("[BulkQueue] Enqueued: {Type} '{Name}' for {Count} asset(s)",
            operation.IsKeyword ? "Keyword" : "Category", operation.Name, operation.AssetIds.Count);
        NotifyProgress("Queued");
    }

    private async Task ProcessLoopAsync(CancellationToken ct)
    {
        while (await _channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
        {
            while (_channel.Reader.TryRead(out var operation))
            {
                try
                {
                    await using var db = await _modeManager.CreateDbContextAsync(ct).ConfigureAwait(false);

                    _logger.LogInformation("[BulkQueue] Processing: {Type} '{Name}' for {Count} asset(s)",
                        operation.IsKeyword ? "Keyword" : "Category", operation.Name, operation.AssetIds.Count);

                    foreach (var assetId in operation.AssetIds)
                    {
                        ct.ThrowIfCancellationRequested();

                        var asset = await db.DigitalAssets
                            .Include(a => a.Keywords)
                            .Include(a => a.Categories)
                            .FirstOrDefaultAsync(a => a.Id == assetId, ct).ConfigureAwait(false);

                        if (asset == null)
                        {
                            _logger.LogWarning("[BulkQueue] Asset {Id} not found, skipping", assetId);
                            continue;
                        }

                        if (operation.IsKeyword && !string.IsNullOrEmpty(operation.Name))
                        {
                            await new KeywordService(db).AssociateKeywordsAsync(asset, [operation.Name], isAiGenerated: false, ct).ConfigureAwait(false);
                        }
                        else if (!operation.IsKeyword && !string.IsNullOrEmpty(operation.Name))
                        {
                            await new CategoryService(db).AssociateCategoriesAsync(asset, [operation.Name], isAiGenerated: false, ct).ConfigureAwait(false);
                        }
                    }

                    await db.SaveChangesAsync(ct).ConfigureAwait(false);
                    Interlocked.Increment(ref _completedCount);

                    _logger.LogInformation("[BulkQueue] Completed: '{Name}'", operation.Name);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref _failedCount);
                    _logger.LogError(ex, "[BulkQueue] Failed operation '{Name}'", operation.Name);
                }

                NotifyProgress(operation.Name ?? "");
            }

            // All operations in the channel have been drained
            if (Volatile.Read(ref _totalQueued) == Interlocked.Add(ref _completedCount, 0) + Interlocked.Add(ref _failedCount, 0))
            {
                AllCompleted?.Invoke(this, EventArgs.Empty);
                NotifyProgress("Idle");
                CurrentProgress.IsActive = false;
            }
        }
    }

    private void NotifyProgress(string currentOp)
    {
        var total = Volatile.Read(ref _totalQueued);
        var completed = Volatile.Read(ref _completedCount);
        var failed = Volatile.Read(ref _failedCount);

        CurrentProgress.Completed = completed;
        CurrentProgress.Total = total;
        CurrentProgress.Failed = failed;
        CurrentProgress.Percentage = total > 0 ? (double)completed / total * 100 : 0;
        CurrentProgress.CurrentOperation = currentOp;
        CurrentProgress.IsActive = total > 0 && completed + failed < total;

        ProgressChanged?.Invoke(this, CurrentProgress);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await _cts.CancelAsync();
        _channel.Writer.TryComplete();

        try { await _processorTask.ConfigureAwait(false); }
        catch (OperationCanceledException) { }

        _cts.Dispose();
    }
}
