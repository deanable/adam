using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Adam.CatalogBrowser.Controls;
using Adam.Shared.Models;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Adam.CatalogBrowser.Models;

public class AssetListItem : INotifyPropertyChanged
{
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
            // T12.1: Remove stale cache entry before disposing the bitmap
            // so the cache doesn't return a disposed bitmap on next scroll-back.
            if (!string.IsNullOrEmpty(_thumbnailPath) && SharedThumbnailCache != null)
                SharedThumbnailCache.Remove(_thumbnailPath);

            _thumbnailPath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Thumbnail));
            _thumbnail?.Dispose();
            _thumbnail = null;
        }
    }

    public Bitmap? Thumbnail
    {
        get => _thumbnail;
        private set
        {
            _thumbnail = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Shared memory cache for decoded thumbnails (T12.1). Set by AssetGalleryViewModel.
    /// </summary>
    internal static Shared.Services.ThumbnailCache? SharedThumbnailCache { get; set; }

    public async Task LoadThumbnailAsync(int decodeWidth = 256)
    {
        if (_thumbnail != null || string.IsNullOrEmpty(_thumbnailPath))
            return;

        await Task.Run(() =>
        {
            try
            {
                // T12.1: Check memory cache first
                if (SharedThumbnailCache != null &&
                    SharedThumbnailCache.TryGet(_thumbnailPath, out var cached) &&
                    cached is Bitmap cachedBmp)
                {
                    Thumbnail = cachedBmp;
                    return;
                }

                var exists = File.Exists(_thumbnailPath);
                if (!exists)
                    return;

                using var stream = File.OpenRead(_thumbnailPath);
                var bitmap = Bitmap.DecodeToWidth(stream, decodeWidth, BitmapInterpolationMode.LowQuality);
                Thumbnail = bitmap;

                // T12.1: Store in memory cache
                SharedThumbnailCache?.Add(
                    _thumbnailPath,
                    bitmap,
                    Shared.Services.ThumbnailCache.EstimateBitmapSize(bitmap.PixelSize.Width, bitmap.PixelSize.Height));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Thumbnail] FAILED to load {_thumbnailPath}: {ex.GetType().Name} - {ex.Message}");
            }
        });
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
        AssetLabel.Red => ("Red", new SolidColorBrush(Colors.Red)),
        AssetLabel.Green => ("Green", new SolidColorBrush(Colors.Green)),
        AssetLabel.Blue => ("Blue", new SolidColorBrush(Colors.Blue)),
        AssetLabel.Yellow => ("Yellow", new SolidColorBrush(Color.FromArgb(255, 218, 165, 32))),
        AssetLabel.Purple => ("Purple", new SolidColorBrush(Colors.Purple)),
        _ => (string.Empty, null)
    };
}
