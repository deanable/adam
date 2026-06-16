using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Adam.CatalogBrowser.Controls;
using Adam.Shared.Models;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Adam.CatalogBrowser.Models;

public class AssetListItem : INotifyPropertyChanged, IDisposable
{
    private static ILogger<AssetListItem>? _logger;
    private static ILogger<AssetListItem>? Logger => _logger ??= App.ServiceProvider?.GetService<ILogger<AssetListItem>>();

    private string _title = string.Empty;
    private string _fileName = string.Empty;
    private string _thumbnailPath = string.Empty;
    private string _fileType = string.Empty;
    private bool _isSelected;
    private Bitmap? _thumbnail;
    private int _rating;
    private string _colorLabel = string.Empty;
    private IBrush? _colorBrush;
    private bool _isFlagged;
    private string? _highlightText;
    private IReadOnlyList<string> _matchedFields = [];

    public Guid Id { get; set; }
    public string StoragePath { get; set; } = string.Empty;

    public string Title
    {
        get => _title;
        set { _title = value; OnPropertyChanged(); }
    }

    public string FileName
    {
        get => _fileName;
        set { _fileName = value; OnPropertyChanged(); }
    }

    public string ThumbnailPath
    {
        get => _thumbnailPath;
        set
        {
            var oldPath = _thumbnailPath;
            var oldThumbnail = _thumbnail;

            _thumbnailPath = value;
            _thumbnail = null;

            // Marshal property-changed notifications and bitmap disposal to the
            // UI thread. The setter can be called from background threads (e.g.
            // BackfillMissingThumbnailsAsync runs on Parallel.ForEachAsync).
            if (Dispatcher.UIThread.CheckAccess())
            {
                // Already on UI thread — fire directly
                if (!string.IsNullOrEmpty(oldPath) && SharedThumbnailCache != null)
                    SharedThumbnailCache.Remove(oldPath);
                OnPropertyChanged();
                OnPropertyChanged(nameof(Thumbnail));
                oldThumbnail?.Dispose();
            }
            else
            {
                // On background thread — marshal to UI thread
                var capturedPath = oldPath;
                var capturedBitmap = oldThumbnail;
                _ = Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!string.IsNullOrEmpty(capturedPath) && SharedThumbnailCache != null)
                        SharedThumbnailCache.Remove(capturedPath);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Thumbnail));
                    capturedBitmap?.Dispose();
                });
            }
        }
    }

    public Bitmap? Thumbnail
    {
        get => _thumbnail;
        private set
        {
            // T12.7: Dispose the old bitmap to avoid memory leak when the
            // tile is updated (search results, scroll-back with fresh decode).
            // Use ReferenceEquals to avoid double-dispose when the new value
            // is the same object as the old (e.g. cache hit returning the
            // same reference that was already removed from cache).
            if (!ReferenceEquals(_thumbnail, value))
            {
                _thumbnail?.Dispose();
            }
            _thumbnail = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Shared memory cache for decoded thumbnails (T12.1). Set by AssetGalleryViewModel.
    /// </summary>
    internal static Shared.Services.ThumbnailCache? SharedThumbnailCache { get; set; }

    // T12.6: Cancellation support for pending thumbnail loads.
    private CancellationTokenSource? _loadCts;

    /// <summary>
    /// Cancels any pending thumbnail load for this item.
    /// Called on scroll-out and disposal to avoid wasted work.
    /// </summary>
    public void CancelPendingLoad()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
    }

    /// <summary>
    /// T21.3: Loads a thumbnail asynchronously with cancellation and memory cache.
    /// Uses async file I/O to avoid blocking thread pool threads on disk reads.
    /// </summary>
    public async Task LoadThumbnailAsync(int decodeWidth = 256)
    {
        if (_thumbnail != null || string.IsNullOrEmpty(_thumbnailPath))
            return;

        // T12.6: Cancel any previous pending load and start fresh
        CancelPendingLoad();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        await Task.Run(async () =>
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                // T12.1: Check memory cache first
                if (SharedThumbnailCache != null &&
                    SharedThumbnailCache.TryGet(_thumbnailPath, out var cached) &&
                    cached is Bitmap cachedBmp)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        Thumbnail = cachedBmp;
                    });
                    return;
                }

                // T21.3: Async file existence check
                var exists = await Task.Run(() => File.Exists(_thumbnailPath), ct);
                if (!exists)
                    return;

                ct.ThrowIfCancellationRequested();

                // T21.3: Async file read — open stream asynchronously
                await using var stream = new FileStream(
                    _thumbnailPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 4096,
                    useAsync: true);

                var bitmap = Bitmap.DecodeToWidth(stream, decodeWidth, BitmapInterpolationMode.LowQuality);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Thumbnail = bitmap;
                });

                // T12.1: Store in memory cache
                SharedThumbnailCache?.Add(
                    _thumbnailPath,
                    bitmap,
                    Shared.Services.ThumbnailCache.EstimateBitmapSize(bitmap.PixelSize.Width, bitmap.PixelSize.Height));
            }
            catch (Exception ex)
            {
                Logger?.LogDebug(ex, "[Thumbnail] FAILED to load {ThumbnailPath}: {ExceptionType} - {Message}",
                    _thumbnailPath, ex.GetType().Name, ex.Message);
            }
        }, ct);
    }

    public string FileType
    {
        get => _fileType;
        set { _fileType = value; OnPropertyChanged(); }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    // ── Tile affordances (T8.20) ──

    /// <summary>
    /// Asset rating (0-5). Bound to the rating slot on the tile.
    /// </summary>
    public int Rating
    {
        get => _rating;
        set { _rating = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Color label text (e.g. "Red", "Blue"). Shown as a swatch on the tile.
    /// </summary>
    public string ColorLabel
    {
        get => _colorLabel;
        set { _colorLabel = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Brush for the colour swatch indicator on the tile.
    /// </summary>
    public IBrush? ColorBrush
    {
        get => _colorBrush;
        set { _colorBrush = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Whether the asset is flagged (Pick/Reject).
    /// </summary>
    public bool IsFlagged
    {
        get => _isFlagged;
        set { _isFlagged = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Search query used to highlight matching text in the tile (T11.10).
    /// </summary>
    public string? HighlightText
    {
        get => _highlightText;
        set { _highlightText = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsSearchHighlighted)); }
    }

    /// <summary>
    /// Which fields matched the FTS query (e.g., "Title", "Keywords").
    /// Shown as a small badge below the tile title when search is active.
    /// </summary>
    public IReadOnlyList<string> MatchedFields
    {
        get => _matchedFields;
        set { _matchedFields = value; OnPropertyChanged(); OnPropertyChanged(nameof(MatchedFieldsText)); }
    }

    /// <summary>
    /// Comma-separated matched fields text for display (e.g. "Title, Keywords").
    /// </summary>
    public string MatchedFieldsText => MatchedFields.Count > 0 ? string.Join(", ", MatchedFields) : string.Empty;

    /// <summary>
    /// True when this tile is part of FTS search results and should show highlighting.
    /// </summary>
    public bool IsSearchHighlighted => !string.IsNullOrEmpty(HighlightText);

    /// <summary>
    /// Semantic search similarity score (0.0 to 1.0). Shown on result tiles
    /// when the gallery is displaying semantic search results.
    /// </summary>
    public float SearchScore { get; set; }

    /// <summary>
    /// Formatted similarity score for display (e.g. "92% match").
    /// </summary>
    public string SearchScoreText => SearchScore > 0 ? $"{SearchScore * 100:F0}% match" : string.Empty;

    /// <summary>
    /// True when this tile has a semantic search score to display.
    /// </summary>
    public bool HasSearchScore => SearchScore > 0;

    /// <summary>
    /// Toolbar action buttons for the tile (e.g., quick rate, label, flag).
    /// </summary>
    public ObservableCollection<ToolbarAction> ToolbarActions { get; set; } = [];

    public long FileSize { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public string? Dimensions => Width.HasValue && Height.HasValue ? $"{Width.Value}x{Height.Value}" : null;

    public string FileSizeFormatted
    {
        get
        {
            if (FileSize < 1024) return $"{FileSize} B";
            if (FileSize < 1024 * 1024) return $"{FileSize / 1024.0:F1} KB";
            if (FileSize < 1024L * 1024 * 1024) return $"{FileSize / (1024.0 * 1024):F1} MB";
            return $"{FileSize / (1024.0 * 1024 * 1024):F1} GB";
        }
    }

    public string CreatedAtFormatted => CreatedAt.ToString("g");

    public void Dispose()
    {
        // Cancel pending load first
        CancelPendingLoad();

        // Dispose the bitmap (remove from shared cache first to avoid
        // returning a disposed reference on next scroll-back)
        if (_thumbnail != null)
        {
            if (SharedThumbnailCache != null && !string.IsNullOrEmpty(_thumbnailPath))
                SharedThumbnailCache.Remove(_thumbnailPath);
            _thumbnail.Dispose();
            _thumbnail = null;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// Maps an <see cref="AssetLabel"/> to its display text and brush.
    /// Shared between <see cref="AssetGalleryViewModel.LoadPageAsync"/> and
    /// <see cref="MainWindowViewModel.SetLabelSelectedAsync"/> to keep them in sync.
    /// </summary>
    public static (string label, IBrush? brush) MapLabelToDisplay(AssetLabel label) => label switch
    {
        AssetLabel.Red => ("Red", Brushes.Red),
        AssetLabel.Green => ("Green", Brushes.Green),
        AssetLabel.Blue => ("Blue", Brushes.Blue),
        AssetLabel.Yellow => ("Yellow", Brushes.Goldenrod),
        AssetLabel.Purple => ("Purple", Brushes.Purple),
        _ => (string.Empty, null)
    };
}
