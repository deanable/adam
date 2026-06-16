using Avalonia;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Adam.CatalogBrowser.Models;
using Adam.Shared.Models;
using Adam.Shared.Services;
using Microsoft.Extensions.Logging;

namespace Adam.CatalogBrowser.ViewModels;

/// <summary>
/// Mode for the compare view layout.
/// </summary>
public enum CompareViewMode
{
    SideBySide,
    Overlay
}

/// <summary>
/// A single entry in the metadata diff table.
/// </summary>
public sealed class MetadataDiffItem
{
    public string Field { get; init; } = string.Empty;
    public string? LeftValue { get; init; }
    public string? RightValue { get; init; }
    public bool IsDifferent { get; init; }
}

/// <summary>
/// Shared state for synchronized zoom/pan between two ZoomBorder instances.
/// </summary>
public sealed class ZoomSyncState : INotifyPropertyChanged
{
    private double _zoomLevel = 1.0;
    private Vector _panOffset;

    public double ZoomLevel
    {
        get => _zoomLevel;
        set { _zoomLevel = value; OnPropertyChanged(); }
    }

    public Vector PanOffset
    {
        get => _panOffset;
        set { _panOffset = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>
/// ViewModel for the compare view that shows two assets side-by-side
/// with synchronized zoom/pan and a metadata diff table.
/// </summary>
public sealed class CompareViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ModeManager _modeManager;
    private readonly ILogger<CompareViewModel>? _logger;
    private Bitmap? _leftImage;
    private Bitmap? _rightImage;
    private bool _isSyncEnabled = true;
    private CompareViewMode _viewMode = CompareViewMode.SideBySide;
    private double _overlayOpacity = 0.5;
    private bool _isLoadingLeft;
    private bool _isLoadingRight;

    public CompareViewModel(
        ModeManager modeManager,
        ILogger<CompareViewModel>? logger = null)
    {
        _modeManager = modeManager;
        _logger = logger;

        SwapCommand = new RelayCommand(_ => Swap());
        ToggleSyncCommand = new RelayCommand(_ => IsSyncEnabled = !IsSyncEnabled);
        ToggleViewModeCommand = new RelayCommand(_ =>
            ViewMode = ViewMode == CompareViewMode.SideBySide
                ? CompareViewMode.Overlay
                : CompareViewMode.SideBySide);
        CloseCommand = new RelayCommand(_ => Close());

        SyncState = new ZoomSyncState();
    }

    /// <summary>
    /// The left asset for comparison.
    /// </summary>
    public AssetListItem? LeftAsset { get; private set; }

    /// <summary>
    /// The right asset for comparison.
    /// </summary>
    public AssetListItem? RightAsset { get; private set; }

    /// <summary>
    /// Full-resolution image for the left panel.
    /// </summary>
    public Bitmap? LeftImage
    {
        get => _leftImage;
        private set { _leftImage = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Full-resolution image for the right panel.
    /// </summary>
    public Bitmap? RightImage
    {
        get => _rightImage;
        private set { _rightImage = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Whether sync between left/right zoom/pan is enabled.
    /// </summary>
    public bool IsSyncEnabled
    {
        get => _isSyncEnabled;
        set { _isSyncEnabled = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Current view mode: side-by-side or overlay/swipe.
    /// </summary>
    public CompareViewMode ViewMode
    {
        get => _viewMode;
        set
        {
            _viewMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsOverlayMode));
            OnPropertyChanged(nameof(IsSideBySideMode));
        }
    }

    public bool IsSideBySideMode => _viewMode == CompareViewMode.SideBySide;
    public bool IsOverlayMode => _viewMode == CompareViewMode.Overlay;

    /// <summary>
    /// Opacity of the overlay image in overlay mode (0.0-1.0).
    /// </summary>
    public double OverlayOpacity
    {
        get => _overlayOpacity;
        set { _overlayOpacity = Math.Clamp(value, 0.0, 1.0); OnPropertyChanged(); }
    }

    /// <summary>
    /// Loading states for each panel.
    /// </summary>
    public bool IsLoadingLeft
    {
        get => _isLoadingLeft;
        set { _isLoadingLeft = value; OnPropertyChanged(); }
    }

    public bool IsLoadingRight
    {
        get => _isLoadingRight;
        set { _isLoadingRight = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Metadata diff items comparing left vs right asset.
    /// </summary>
    public ObservableCollection<MetadataDiffItem> DiffItems { get; } = [];

    /// <summary>
    /// Shared synchronization state for linked zoom/pan.
    /// </summary>
    public ZoomSyncState SyncState { get; }

    // Commands
    public ICommand SwapCommand { get; }
    public ICommand ToggleSyncCommand { get; }
    public ICommand ToggleViewModeCommand { get; }
    public ICommand CloseCommand { get; }

    /// <summary>
    /// Event raised when the user closes the compare view.
    /// </summary>
    public event Action? CloseRequested;

    /// <summary>
    /// Sets the left asset and loads its image.
    /// </summary>
    public async Task SetLeftAssetAsync(AssetListItem asset)
    {
        LeftAsset = asset;
        OnPropertyChanged(nameof(LeftAsset));
        await LoadImageAsync(asset, isLeft: true);
        RebuildDiff();
    }

    /// <summary>
    /// Sets the right asset and loads its image.
    /// </summary>
    public async Task SetRightAssetAsync(AssetListItem asset)
    {
        RightAsset = asset;
        OnPropertyChanged(nameof(RightAsset));
        await LoadImageAsync(asset, isLeft: false);
        RebuildDiff();
    }

    private async Task LoadImageAsync(AssetListItem asset, bool isLeft)
    {
        if (isLeft)
            IsLoadingLeft = true;
        else
            IsLoadingRight = true;

        try
        {
            var storagePath = asset.StoragePath;
            if (!string.IsNullOrEmpty(storagePath) && File.Exists(storagePath))
            {
                var bitmap = await Task.Run(() => new Bitmap(storagePath)).ConfigureAwait(false);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (isLeft)
                        LeftImage?.Dispose();
                    else
                        RightImage?.Dispose();

                    if (isLeft)
                        LeftImage = bitmap;
                    else
                        RightImage = bitmap;
                });
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load compare image for {AssetId}", asset.Id);
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (isLeft)
                    IsLoadingLeft = false;
                else
                    IsLoadingRight = false;
            });
        }
    }

    /// <summary>
    /// Swaps the left and right assets.
    /// </summary>
    private async void Swap()
    {
        try
        {
            var tempAsset = LeftAsset;
            var tempImage = LeftImage;

            LeftAsset = null;
            if (LeftImage != null)
            {
                LeftImage.Dispose();
                LeftImage = null;
            }

            if (RightAsset != null)
            {
                await SetLeftAssetAsync(RightAsset);
            }

            RightAsset = null;
            if (RightImage != null)
            {
                RightImage.Dispose();
                RightImage = null;
            }

            if (tempAsset != null)
            {
                await SetRightAssetAsync(tempAsset);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Swap failed");
        }
    }

    /// <summary>
    /// Rebuilds the metadata diff table from the two assets.
    /// </summary>
    private void RebuildDiff()
    {
        DiffItems.Clear();

        if (LeftAsset == null || RightAsset == null) return;

        DiffItems.Add(new MetadataDiffItem
        {
            Field = "File Name",
            LeftValue = LeftAsset.FileName ?? LeftAsset.Title,
            RightValue = RightAsset.FileName ?? RightAsset.Title,
            IsDifferent = !string.Equals(LeftAsset.FileName, RightAsset.FileName, StringComparison.OrdinalIgnoreCase)
        });

        DiffItems.Add(new MetadataDiffItem
        {
            Field = "Title",
            LeftValue = LeftAsset.Title,
            RightValue = RightAsset.Title,
            IsDifferent = !string.Equals(LeftAsset.Title, RightAsset.Title, StringComparison.OrdinalIgnoreCase)
        });

        DiffItems.Add(new MetadataDiffItem
        {
            Field = "Dimensions",
            LeftValue = LeftAsset.Width.HasValue && LeftAsset.Height.HasValue
                ? $"{LeftAsset.Width} × {LeftAsset.Height}" : null,
            RightValue = RightAsset.Width.HasValue && RightAsset.Height.HasValue
                ? $"{RightAsset.Width} × {RightAsset.Height}" : null,
            IsDifferent = LeftAsset.Width != RightAsset.Width || LeftAsset.Height != RightAsset.Height
        });

        DiffItems.Add(new MetadataDiffItem
        {
            Field = "File Size",
            LeftValue = FormatFileSize(LeftAsset.FileSize),
            RightValue = FormatFileSize(RightAsset.FileSize),
            IsDifferent = LeftAsset.FileSize != RightAsset.FileSize
        });

        DiffItems.Add(new MetadataDiffItem
        {
            Field = "File Type",
            LeftValue = LeftAsset.FileType,
            RightValue = RightAsset.FileType,
            IsDifferent = !string.Equals(LeftAsset.FileType, RightAsset.FileType, StringComparison.OrdinalIgnoreCase)
        });

        DiffItems.Add(new MetadataDiffItem
        {
            Field = "Rating",
            LeftValue = LeftAsset.Rating > 0 ? $"{LeftAsset.Rating}/5" : "None",
            RightValue = RightAsset.Rating > 0 ? $"{RightAsset.Rating}/5" : "None",
            IsDifferent = LeftAsset.Rating != RightAsset.Rating
        });
    }

    /// <summary>
    /// Closes the compare view and releases resources.
    /// </summary>
    public void Close()
    {
        Dispose();
        CloseRequested?.Invoke();
    }

    public void Dispose()
    {
        LeftImage?.Dispose();
        RightImage?.Dispose();
        LeftImage = null;
        RightImage = null;
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
