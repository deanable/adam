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
    private string _maxDimensionText = string.Empty;
    private int _selectedFormatIndex;
    private int _selectedCompressionIndex = 1; // LZW default
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

    public string MaxDimensionText
    {
        get => _maxDimensionText;
        set { _maxDimensionText = value; OnPropertyChanged(); }
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
                await ExportImageAsync();
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

    private async Task ExportImageAsync()
    {
        var format = SelectedFormatIndex == 0 ? ImageExportService.ExportFormat.Jpeg : ImageExportService.ExportFormat.Tiff;
        var compression = SelectedCompressionIndex == 0
            ? TiffCompression.None
            : TiffCompression.Lzw;
        int? maxDim = int.TryParse(MaxDimensionText, out var md) && md > 0 ? md : null;

        var exportService = new ImageExportService();
        var items = new List<(string SourcePath, string DestinationPath, DigitalAsset? Asset)>();
        foreach (var a in SelectedAssets)
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

        await exportService.ExportBatchAsync(items, format, Quality, maxDim, compression, progress);
        ProgressText = "Export complete!";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
