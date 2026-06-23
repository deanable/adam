using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Adam.CatalogBrowser.Models;
using Adam.CatalogBrowser.Services;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Adam.Shared.Services;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp.Formats.Tiff.Constants;

namespace Adam.CatalogBrowser.ViewModels;

public class ExportDialogViewModel : INotifyPropertyChanged
{
    private readonly ModeManager? _modeManager;
    private string _destinationFolder = string.Empty;
    private int _quality = 85;
    private int _selectedFormatIndex;
    private int _selectedCompressionIndex = 1; // LZW default
    private int _selectedResolutionIndex;
    private string _customWidthText = "1920";
    private string _customHeightText = "1080";
    private int _selectedAspectRatioIndex;
    private int _videoCrf = 23;
    private bool _isExporting;
    private double _progressValue;
    private string _progressText = string.Empty;

    // CSV field toggles
    private bool _csvExportTitle = true;
    private bool _csvExportDescription = true;
    private bool _csvExportKeywords = true;
    private bool _csvExportCategories = true;
    private bool _csvExportRating = true;
    private bool _csvExportLabel = true;
    private bool _csvExportFlag = true;
    private bool _csvExportCopyright = true;
    private bool _csvExportGps = true;
    private bool _csvExportCamera = true;

    // Resolution presets: (Width, Height, Label)
    private static readonly (int Width, int Height, string Label)[] ResolutionPresets =
    [
        (0, 0, "Original"),          // No resize
        (854, 480, "480p"),           // 480p widescreen
        (1280, 720, "720p"),          // 720p HD
        (1920, 1080, "1080p"),        // 1080p Full HD
        (2560, 1440, "2K"),           // 2K QHD
        (3840, 2160, "4K"),           // 4K UHD
        (0, 0, "Custom")              // Custom
    ];

    private const int CustomResolutionIndex = 6;

    // Aspect ratio presets: (W, H, Label)
    private static readonly (int W, int H, string Label)[] AspectRatioPresets =
    [
        (0, 0, "Original"),          // No constraint
        (1, 1, "1:1 Square"),
        (4, 3, "4:3"),
        (3, 2, "3:2"),
        (16, 10, "16:10"),
        (16, 9, "16:9"),
        (21, 9, "21:9 Ultrawide"),
        (9, 16, "9:16 Portrait")
    ];

    public ExportDialogViewModel(ModeManager? modeManager = null)
    {
        _modeManager = modeManager;
        ExportCommand = new RelayCommand(async _ => await ExportAsync(), _ => CanExport);
        BrowseCommand = new RelayCommand(_ => BrowseFolder());
    }

    public string DestinationFolder
    {
        get => _destinationFolder;
        set { _destinationFolder = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanExport)); }
    }

    public int Quality
    {
        get => _quality;
        set { _quality = value; OnPropertyChanged(); }
    }

    public int SelectedFormatIndex
    {
        get => _selectedFormatIndex;
        set
        {
            _selectedFormatIndex = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsJpegSelected));
            OnPropertyChanged(nameof(IsCsvSelected));
        }
    }

    public int SelectedCompressionIndex
    {
        get => _selectedCompressionIndex;
        set { _selectedCompressionIndex = value; OnPropertyChanged(); }
    }

    public int SelectedResolutionIndex
    {
        get => _selectedResolutionIndex;
        set
        {
            _selectedResolutionIndex = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCustomResolution));
            OnPropertyChanged(nameof(ResolutionTargetWidth));
            OnPropertyChanged(nameof(ResolutionTargetHeight));
            OnPropertyChanged(nameof(EffectiveTargetWidth));
            OnPropertyChanged(nameof(EffectiveTargetHeight));
            OnPropertyChanged(nameof(EffectiveSizeText));
        }
    }

    /// <summary>
    /// True when the user has selected the "Custom" resolution option.
    /// </summary>
    public bool IsCustomResolution => SelectedResolutionIndex == CustomResolutionIndex;

    /// <summary>
    /// Width text for custom resolution. Parsed to int in ResolutionTargetWidth.
    /// </summary>
    public string CustomWidthText
    {
        get => _customWidthText;
        set { _customWidthText = value; OnPropertyChanged(); OnPropertyChanged(nameof(ResolutionTargetWidth)); OnPropertyChanged(nameof(EffectiveTargetWidth)); OnPropertyChanged(nameof(EffectiveSizeText)); }
    }

    /// <summary>
    /// Height text for custom resolution. Parsed to int in ResolutionTargetHeight.
    /// </summary>
    public string CustomHeightText
    {
        get => _customHeightText;
        set { _customHeightText = value; OnPropertyChanged(); OnPropertyChanged(nameof(ResolutionTargetHeight)); OnPropertyChanged(nameof(EffectiveTargetHeight)); OnPropertyChanged(nameof(EffectiveSizeText)); }
    }

    /// <summary>
    /// Parsed custom width. Falls back to 0 if the text is not a valid positive integer.
    /// </summary>
    private int CustomWidthParsed => int.TryParse(_customWidthText, out var w) && w > 0 ? w : 0;

    /// <summary>
    /// Parsed custom height. Falls back to 0 if the text is not a valid positive integer.
    /// </summary>
    private int CustomHeightParsed => int.TryParse(_customHeightText, out var h) && h > 0 ? h : 0;

    public int ResolutionTargetWidth
    {
        get
        {
            if (IsCustomResolution)
                return CustomWidthParsed;
            return SelectedResolutionIndex >= 0 && SelectedResolutionIndex < ResolutionPresets.Length
                ? ResolutionPresets[SelectedResolutionIndex].Width
                : 0;
        }
    }

    public int ResolutionTargetHeight
    {
        get
        {
            if (IsCustomResolution)
                return CustomHeightParsed;
            return SelectedResolutionIndex >= 0 && SelectedResolutionIndex < ResolutionPresets.Length
                ? ResolutionPresets[SelectedResolutionIndex].Height
                : 0;
        }
    }

    public int SelectedAspectRatioIndex
    {
        get => _selectedAspectRatioIndex;
        set
        {
            _selectedAspectRatioIndex = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsAspectRatioActive));
            OnPropertyChanged(nameof(EffectiveTargetWidth));
            OnPropertyChanged(nameof(EffectiveTargetHeight));
        }
    }

    /// <summary>
    /// True when the user has selected a non-Original aspect ratio.
    /// </summary>
    public bool IsAspectRatioActive => _selectedAspectRatioIndex > 0;

    /// <summary>
    /// Computes the export width combining resolution bounds and aspect ratio constraint.
    /// When an aspect ratio is active, this finds the largest dimensions within the
    /// resolution bounds that match the selected ratio.
    /// </summary>
    public int EffectiveTargetWidth
    {
        get
        {
            var maxW = ResolutionTargetWidth;
            var maxH = ResolutionTargetHeight;
            if (!IsAspectRatioActive || maxW <= 0 || maxH <= 0)
                return maxW;

            var preset = AspectRatioPresets[_selectedAspectRatioIndex];
            var hFromW = maxW * preset.H / preset.W;
            return hFromW <= maxH ? maxW : maxH * preset.W / preset.H;
        }
    }

    /// <summary>
    /// Computes the export height combining resolution bounds and aspect ratio constraint.
    /// </summary>
    public int EffectiveTargetHeight
    {
        get
        {
            var maxW = ResolutionTargetWidth;
            var maxH = ResolutionTargetHeight;
            if (!IsAspectRatioActive || maxW <= 0 || maxH <= 0)
                return maxH;

            var preset = AspectRatioPresets[_selectedAspectRatioIndex];
            var hFromW = maxW * preset.H / preset.W;
            return hFromW <= maxH ? hFromW : maxH;
        }
    }

    /// <summary>
    /// Text showing the effective export size when aspect ratio is active.
    /// </summary>
    public string EffectiveSizeText
    {
        get
        {
            var w = EffectiveTargetWidth;
            var h = EffectiveTargetHeight;
            if (w <= 0 || h <= 0)
                return string.Empty;

            var label = AspectRatioPresets[_selectedAspectRatioIndex].Label;
            var resolutionLabel = ResolutionLabel;
            return IsAspectRatioActive
                ? $"→ {w}×{h} ({label}, within {resolutionLabel})"
                : w > 0 && h > 0 ? $"→ {w}×{h}" : string.Empty;
        }
    }

    private string ResolutionLabel
    {
        get
        {
            if (IsCustomResolution)
                return $"{CustomWidthParsed}×{CustomHeightParsed}";
            return SelectedResolutionIndex >= 0 && SelectedResolutionIndex < ResolutionPresets.Length
                ? ResolutionPresets[SelectedResolutionIndex].Label
                : "Original";
        }
    }

    public int VideoCrf
    {
        get => _videoCrf;
        set { _videoCrf = value; OnPropertyChanged(); OnPropertyChanged(nameof(VideoCrfLabel)); }
    }

    public string VideoCrfLabel => $"CRF {_videoCrf}";

    /// <summary>
    /// Whether any selected asset is a video file.
    /// </summary>
    public bool HasVideos => SelectedAssets.Any(IsVideoAsset);

    /// <summary>
    /// Returns true if the asset is a video file based on its extension.
    /// </summary>
    private static bool IsVideoAsset(AssetListItem a)
    {
        var ext = Path.GetExtension(a.FileName ?? a.StoragePath).ToLowerInvariant();
        return ext is ".mp4" or ".avi" or ".mov" or ".mkv" or ".wmv" or ".webm" or ".flv" or ".m4v";
    }

    public bool IsJpegSelected => SelectedFormatIndex == 0;
    public bool IsCsvSelected => SelectedFormatIndex == 2;

    public bool IsExporting
    {
        get => _isExporting;
        set { _isExporting = value; OnPropertyChanged(); }
    }

    public double ProgressValue
    {
        get => _progressValue;
        set { _progressValue = value; OnPropertyChanged(); }
    }

    public string ProgressText
    {
        get => _progressText;
        set { _progressText = value; OnPropertyChanged(); }
    }

    public bool CanExport => !string.IsNullOrWhiteSpace(DestinationFolder) && Directory.Exists(DestinationFolder) && !IsExporting;

    // CSV field toggles
    public bool CsvExportTitle { get => _csvExportTitle; set { _csvExportTitle = value; OnPropertyChanged(); } }
    public bool CsvExportDescription { get => _csvExportDescription; set { _csvExportDescription = value; OnPropertyChanged(); } }
    public bool CsvExportKeywords { get => _csvExportKeywords; set { _csvExportKeywords = value; OnPropertyChanged(); } }
    public bool CsvExportCategories { get => _csvExportCategories; set { _csvExportCategories = value; OnPropertyChanged(); } }
    public bool CsvExportRating { get => _csvExportRating; set { _csvExportRating = value; OnPropertyChanged(); } }
    public bool CsvExportLabel { get => _csvExportLabel; set { _csvExportLabel = value; OnPropertyChanged(); } }
    public bool CsvExportFlag { get => _csvExportFlag; set { _csvExportFlag = value; OnPropertyChanged(); } }
    public bool CsvExportCopyright { get => _csvExportCopyright; set { _csvExportCopyright = value; OnPropertyChanged(); } }
    public bool CsvExportGps { get => _csvExportGps; set { _csvExportGps = value; OnPropertyChanged(); } }
    public bool CsvExportCamera { get => _csvExportCamera; set { _csvExportCamera = value; OnPropertyChanged(); } }

    public string AssetCountText => $"Exporting {SelectedAssets.Count} asset(s)";

    public ICommand ExportCommand { get; }
    public ICommand BrowseCommand { get; }

    public List<AssetListItem> SelectedAssets { get; set; } = [];

    /// <summary>
    /// Callback to invalidate properties when SelectedAssets changes.
    /// </summary>
    public void OnSelectedAssetsChanged()
    {
        OnPropertyChanged(nameof(HasVideos));
    }
    public Func<Task<string>>? BrowseFolderFunc { get; set; }

    private async void BrowseFolder()
    {
        if (BrowseFolderFunc == null)
            return;
        var path = await BrowseFolderFunc.Invoke();
        if (!string.IsNullOrEmpty(path))
            DestinationFolder = path;
    }

    private async Task ExportAsync()
    {
        if (!CanExport || SelectedAssets.Count == 0)
            return;

        IsExporting = true;
        ProgressValue = 0;
        ProgressText = "Preparing...";

        try
        {
            if (IsCsvSelected)
            {
                await ExportCsvAsync();
            }
            else
            {
                // Split selection into images and videos, run both pipelines
                var images = SelectedAssets.Where(a => !IsVideoAsset(a)).ToList();
                var videos = SelectedAssets.Where(a => IsVideoAsset(a)).ToList();

                if (images.Count > 0)
                {
                    ProgressText = $"Exporting {images.Count} image(s)...";
                    await ExportImageAsync(images);
                }

                if (videos.Count > 0)
                {
                    ProgressText = $"Exporting {videos.Count} video(s)...";
                    await ExportVideoAsync(videos);
                }

                if (images.Count == 0 && videos.Count == 0)
                {
                    ProgressText = "No supported files to export";
                }
                else
                {
                    ProgressText = "Export complete!";
                }
            }
        }
        catch (Exception ex)
        {
            ProgressText = $"Export failed: {ex.Message}";
        }
        finally
        {
            IsExporting = false;
        }
    }

    private async Task ExportCsvAsync()
    {
        var csvService = new CsvMetadataService();
        List<DigitalAsset> assets;

        ProgressText = $"Loading {SelectedAssets.Count} asset(s) from database...";

        if (_modeManager != null && _modeManager.IsStandalone)
        {
            await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);
            var ids = SelectedAssets.Select(a => a.Id).ToList();
            assets = await db.DigitalAssets
                .Include(a => a.Keywords)
                .Include(a => a.Categories)
                .Include(a => a.MetadataProfile)
                .Where(a => ids.Contains(a.Id))
                .ToListAsync().ConfigureAwait(false);
        }
        else
        {
            // Fallback: build minimal metadata from list items
            assets = SelectedAssets.Select(item => new DigitalAsset
            {
                FileName = item.FileName,
                Title = item.Title,
                Rating = item.Rating,
                Keywords = [],
                Categories = []
            }).ToList();
        }

        var fileName = $"metadata_export_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        var outputPath = Path.Combine(DestinationFolder, fileName);

        ProgressText = $"Writing {assets.Count} rows...";

        // Build field filter from selected toggles
        var fieldFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (CsvExportTitle) fieldFilter.Add("title");
        if (CsvExportDescription) fieldFilter.Add("description");
        if (CsvExportKeywords) fieldFilter.Add("keywords");
        if (CsvExportCategories) fieldFilter.Add("categories");
        if (CsvExportRating) fieldFilter.Add("rating");
        if (CsvExportLabel) fieldFilter.Add("label");
        if (CsvExportFlag) fieldFilter.Add("flag");
        if (CsvExportCopyright) fieldFilter.Add("copyright");
        if (CsvExportGps) fieldFilter.Add("gps");
        if (CsvExportCamera) fieldFilter.Add("camera");

        await csvService.ExportToCsvAsync(assets, outputPath, fieldFilter);

        ProgressText = $"Exported to {fileName}";
        ProgressValue = 100;
    }

    private async Task ExportImageAsync(IReadOnlyList<AssetListItem> assets)
    {
        var format = SelectedFormatIndex == 0 ? ImageExportService.ExportFormat.Jpeg : ImageExportService.ExportFormat.Tiff;
        var compression = SelectedCompressionIndex == 0
            ? TiffCompression.None
            : TiffCompression.Lzw;

        var exportService = new ImageExportService();
        var items = new List<(string SourcePath, string DestinationPath, DigitalAsset? Asset)>();
        foreach (var a in assets)
        {
            var ext = format == ImageExportService.ExportFormat.Jpeg ? ".jpg" : ".tiff";
            var destName = Path.GetFileNameWithoutExtension(a.FileName) + "_export" + ext;
            var destPath = Path.Combine(DestinationFolder, destName);
            DigitalAsset? asset = new DigitalAsset { Orientation = ImageOrientation.Normal };
            items.Add((a.StoragePath, destPath, asset));
        }

        var progress = new Progress<(int Completed, int Total, string CurrentFile)>(p =>
        {
            ProgressValue = (double)p.Completed / p.Total * 100;
            ProgressText = $"{p.Completed}/{p.Total} — {p.CurrentFile}";
        });

        int? tw = EffectiveTargetWidth > 0 ? EffectiveTargetWidth : null;
        int? th = EffectiveTargetHeight > 0 ? EffectiveTargetHeight : null;
        int? cw = IsAspectRatioActive && EffectiveTargetWidth > 0 && EffectiveTargetHeight > 0 ? (int?)AspectRatioPresets[_selectedAspectRatioIndex].W : null;
        int? ch = IsAspectRatioActive && EffectiveTargetWidth > 0 && EffectiveTargetHeight > 0 ? (int?)AspectRatioPresets[_selectedAspectRatioIndex].H : null;
        await exportService.ExportBatchAsync(items, format, Quality, null, tw, th, compression, progress, cropAspectW: cw, cropAspectH: ch);
    }

    private async Task ExportVideoAsync(IReadOnlyList<AssetListItem> assets)
    {
        var targetWidth = EffectiveTargetWidth;
        var targetHeight = EffectiveTargetHeight;

        var exportService = new VideoExportService();
        var items = new List<(string SourcePath, string DestinationPath)>();
        foreach (var a in assets)
        {
            var destName = Path.GetFileNameWithoutExtension(a.FileName) + "_export.mp4";
            var destPath = Path.Combine(DestinationFolder, destName);
            items.Add((a.StoragePath, destPath));
        }

        var progress = new Progress<(int Completed, int Total, string CurrentFile)>(p =>
        {
            ProgressValue = (double)p.Completed / p.Total * 100;
            ProgressText = $"{p.Completed}/{p.Total} — {p.CurrentFile}";
        });

        await exportService.ExportBatchAsync(items, targetWidth, targetHeight, VideoCrf, progress);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
