using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Adam.CatalogBrowser.Models;
using Adam.Shared.Models;
using Adam.Shared.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Adam.CatalogBrowser.ViewModels;

/// <summary>
/// Represents a single filmstrip thumbnail item in the loupe view.
/// </summary>
public sealed class FilmstripItem : INotifyPropertyChanged
{
    private Bitmap? _thumbnail;
    private bool _isCurrent;

    public Guid AssetId { get; init; }
    public string? FileName { get; init; }
    public string? ThumbnailPath { get; init; }

    public Bitmap? Thumbnail
    {
        get => _thumbnail;
        set { _thumbnail = value; OnPropertyChanged(); }
    }

    public bool IsCurrent
    {
        get => _isCurrent;
        set { _isCurrent = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>
/// ViewModel for the full-resolution loupe view.
/// Loads full-res images asynchronously, provides filmstrip navigation,
/// info overlay, and keyboard-controlled zoom/pan.
/// </summary>
public sealed class LoupeViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ModeManager _modeManager;
    private readonly ILogger<LoupeViewModel>? _logger;
    private readonly ThumbnailService _thumbnailService = new();
    private CancellationTokenSource? _loadCts;
    private Bitmap? _fullResImage;
    private bool _showInfoOverlay;
    private bool _isLoading;
    private bool _isFitMode = true;
    private int _currentIndex;
    private string? _statusText;

    /// <summary>
    /// All assets available for navigation (from the current gallery view).
    /// </summary>
    public IReadOnlyList<AssetListItem> AllAssets { get; private set; } = [];

    /// <summary>
    /// The currently displayed asset.
    /// </summary>
    public DigitalAsset? CurrentAsset { get; private set; }

    /// <summary>
    /// Full-resolution bitmap of the current asset.
    /// </summary>
    public Bitmap? FullResImage
    {
        get => _fullResImage;
        private set { _fullResImage = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Filmstrip thumbnails for surrounding assets.
    /// </summary>
    public ObservableCollection<FilmstripItem> FilmstripItems { get; } = [];

    /// <summary>
    /// True while the full-res image is loading.
    /// </summary>
    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Whether the info overlay is visible (toggled with I key).
    /// </summary>
    public bool ShowInfoOverlay
    {
        get => _showInfoOverlay;
        set { _showInfoOverlay = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Whether fit-to-window mode is active (vs. fill mode).
    /// </summary>
    public bool IsFitMode
    {
        get => _isFitMode;
        set { _isFitMode = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Status text shown in the loupe toolbar.
    /// </summary>
    public string? StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    public bool CanGoPrev => _currentIndex > 0;
    public bool CanGoNext => _currentIndex < AllAssets.Count - 1;

    // Info overlay properties
    public string? FileName => CurrentAsset?.FileName;
    public string? Dimensions => CurrentAsset is { Width: not null, Height: not null }
        ? $"{CurrentAsset.Width} × {CurrentAsset.Height}"
        : null;
    public string? FileSizeFormatted => CurrentAsset != null
        ? FormatFileSize(CurrentAsset.FileSize)
        : null;
    public string? RatingStars => CurrentAsset != null
        ? new string('★', CurrentAsset.Rating) + new string('☆', 5 - CurrentAsset.Rating)
        : null;
    public string? CameraModel => CurrentAsset?.MetadataProfile?.CameraModel;
    public string? LensModel => CurrentAsset?.MetadataProfile?.LensModel;
    public string? Iso => CurrentAsset?.MetadataProfile?.Iso?.ToString();
    public string? Aperture => CurrentAsset?.MetadataProfile?.Aperture?.ToString("F1");
    public string? ShutterSpeed => CurrentAsset?.MetadataProfile?.ExposureTime;

    // Commands
    public ICommand NextCommand { get; }
    public ICommand PrevCommand { get; }
    public ICommand FitToWindowCommand { get; }
    public ICommand ZoomInCommand { get; }
    public ICommand ZoomOutCommand { get; }
    public ICommand CloseCommand { get; }
    public ICommand ToggleInfoOverlayCommand { get; }
    public ICommand ToggleFitFillCommand { get; }

    /// <summary>
    /// Event raised when the user wants to close the loupe and return to the gallery.
    /// </summary>
    public event Action? CloseRequested;

    /// <summary>
    /// Event raised when new info is available (on asset change).
    /// </summary>
    public event Action? InfoChanged;

    public LoupeViewModel(
        ModeManager modeManager,
        ILogger<LoupeViewModel>? logger = null)
    {
        _modeManager = modeManager;
        _logger = logger;

        NextCommand = new RelayCommand(_ => Navigate(1), _ => CanGoNext);
        PrevCommand = new RelayCommand(_ => Navigate(-1), _ => CanGoPrev);
        FitToWindowCommand = new RelayCommand(_ => { IsFitMode = true; });
        ZoomInCommand = new RelayCommand(_ => RequestZoomIn?.Invoke());
        ZoomOutCommand = new RelayCommand(_ => RequestZoomOut?.Invoke());
        CloseCommand = new RelayCommand(_ => Close());
        ToggleInfoOverlayCommand = new RelayCommand(_ => ShowInfoOverlay = !ShowInfoOverlay);
        ToggleFitFillCommand = new RelayCommand(_ => IsFitMode = !IsFitMode);
    }

    /// <summary>
    /// Event raised when the ZoomBorder should zoom in/out programmatically.
    /// </summary>
    public event Action? RequestZoomIn;
    public event Action? RequestZoomOut;

    /// <summary>
    /// Opens an asset in the loupe view. Optionally provides the full gallery asset list
    /// for filmstrip navigation.
    /// </summary>
    public async Task OpenAsync(AssetListItem assetItem, IReadOnlyList<AssetListItem>? allAssets = null)
    {
        AllAssets = allAssets ?? [assetItem];
        _currentIndex = -1;
        for (int i = 0; i < AllAssets.Count; i++)
        {
            if (AllAssets[i].Id == assetItem.Id)
            {
                _currentIndex = i;
                break;
            }
        }
        if (_currentIndex < 0) _currentIndex = 0;

        await LoadCurrentAssetAsync();
        BuildFilmstrip();
        OnPropertyChanged(nameof(CanGoPrev));
        OnPropertyChanged(nameof(CanGoNext));
    }

    /// <summary>
    /// Navigates to an adjacent asset in the filmstrip.
    /// </summary>
    private async void Navigate(int direction)
    {
        var newIndex = _currentIndex + direction;
        if (newIndex < 0 || newIndex >= AllAssets.Count) return;

        _currentIndex = newIndex;
        await LoadCurrentAssetAsync();
        BuildFilmstrip();
        OnPropertyChanged(nameof(CanGoPrev));
        OnPropertyChanged(nameof(CanGoNext));
    }

    /// <summary>
    /// Navigates to a specific asset from the filmstrip.
    /// </summary>
    public async Task NavigateToAsync(Guid assetId)
    {
        var index = -1;
        for (int i = 0; i < AllAssets.Count; i++)
        {
            if (AllAssets[i].Id == assetId)
            {
                index = i;
                break;
            }
        }
        if (index < 0) return;

        _currentIndex = index;
        await LoadCurrentAssetAsync();
        BuildFilmstrip();
        OnPropertyChanged(nameof(CanGoPrev));
        OnPropertyChanged(nameof(CanGoNext));
    }

    /// <summary>
    /// Starts a compare session with the current asset.
    /// </summary>
    public event Action<AssetListItem>? CompareRequested;

    public void RequestCompare(AssetListItem asset)
    {
        CompareRequested?.Invoke(asset);
    }

    /// <summary>
    /// Loads the full-resolution image for the current asset.
    /// </summary>
    private async Task LoadCurrentAssetAsync()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        IsLoading = true;
        FullResImage?.Dispose();
        FullResImage = null;

        try
        {
            if (_currentIndex < 0 || _currentIndex >= AllAssets.Count)
                return;

            var item = AllAssets[_currentIndex];
            CurrentAsset = new DigitalAsset
            {
                Id = item.Id,
                FileName = item.FileName ?? item.Title ?? "Unknown",
                StoragePath = item.StoragePath ?? "",
                FileSize = item.FileSize,
                Width = item.Width,
                Height = item.Height,
                Rating = item.Rating,
                MimeType = item.FileType ?? ""
            };

            // Load metadata profile if available
            if (_modeManager.IsStandalone)
            {
                await using var db = await _modeManager.CreateDbContextAsync(ct).ConfigureAwait(false);
                var profile = await db.MetadataProfiles
                    .FirstOrDefaultAsync(m => m.DigitalAssetId == item.Id, ct)
                    .ConfigureAwait(false);
                if (profile != null)
                {
                    CurrentAsset.MetadataProfile = profile;
                }
            }

            // Load the full-resolution image asynchronously
            var storagePath = item.StoragePath;
            if (!string.IsNullOrEmpty(storagePath) && File.Exists(storagePath))
            {
                var bitmap = await Task.Run(() =>
                {
                    ct.ThrowIfCancellationRequested();
                    return new Bitmap(storagePath);
                }, ct).ConfigureAwait(false);

                ct.ThrowIfCancellationRequested();
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    FullResImage = bitmap;
                    StatusText = $"{item.Title ?? item.FileName}  ({FormatFileSize(item.FileSize)})";
                    IsFitMode = true;
                });
            }
            else
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StatusText = "File not found";
                });
            }

            InfoChanged?.Invoke();
            OnPropertyChanged(nameof(FileName));
            OnPropertyChanged(nameof(Dimensions));
            OnPropertyChanged(nameof(FileSizeFormatted));
            OnPropertyChanged(nameof(RatingStars));
            OnPropertyChanged(nameof(CameraModel));
            OnPropertyChanged(nameof(LensModel));
            OnPropertyChanged(nameof(Iso));
            OnPropertyChanged(nameof(Aperture));
            OnPropertyChanged(nameof(ShutterSpeed));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            var assetId = (_currentIndex >= 0 && _currentIndex < AllAssets.Count) ? AllAssets[_currentIndex].Id : (Guid?)null;
            _logger?.LogError(ex, "Failed to load full-res image for {AssetId}", assetId);
            StatusText = "Failed to load image";
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsLoading = false);
        }
    }

    /// <summary>
    /// Builds the filmstrip items (up to 20 before/after current position).
    /// </summary>
    private void BuildFilmstrip()
    {
        var items = new List<FilmstripItem>();
        var startIdx = Math.Max(0, _currentIndex - 10);
        var endIdx = Math.Min(AllAssets.Count, _currentIndex + 11);

        for (int i = startIdx; i < endIdx; i++)
        {
            var asset = AllAssets[i];
            var dbDir = _modeManager.IsStandalone
                ? Path.GetDirectoryName(_modeManager.DbPath) ?? "."
                : ".";
            var thumbnailDir = Path.Combine(dbDir, "thumbnails");
            var thumbPath = _thumbnailService.GetThumbnailPath(asset.StoragePath ?? "", thumbnailDir);

            items.Add(new FilmstripItem
            {
                AssetId = asset.Id,
                FileName = asset.FileName ?? asset.Title,
                ThumbnailPath = thumbPath,
                IsCurrent = i == _currentIndex
            });
        }

        FilmstripItems.Clear();
        foreach (var item in items)
            FilmstripItems.Add(item);

        // Load thumbnails asynchronously
        foreach (var item in items)
        {
            if (!string.IsNullOrEmpty(item.ThumbnailPath) && File.Exists(item.ThumbnailPath))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var bmp = new Bitmap(item.ThumbnailPath);
                        await Dispatcher.UIThread.InvokeAsync(() => item.Thumbnail = bmp);
                    }
                    catch { }
                });
            }
        }
    }

    /// <summary>
    /// Closes the loupe view and disposes resources.
    /// </summary>
    public void Close()
    {
        _loadCts?.Cancel();
        Dispose();
        CloseRequested?.Invoke();
    }

    public void Dispose()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
        FullResImage?.Dispose();
        FullResImage = null;

        foreach (var item in FilmstripItems)
            item.Thumbnail?.Dispose();
        FilmstripItems.Clear();
    }

    private static string FormatFileSize(long bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
            _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
