using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Adam.CatalogBrowser.Models;
using Adam.Shared.Models;
using Adam.Shared.Services;
using SixLabors.ImageSharp.Formats.Tiff.Constants;

namespace Adam.CatalogBrowser.ViewModels;

public class ExportDialogViewModel : INotifyPropertyChanged
{
    private string _destinationFolder = string.Empty;
    private int _quality = 85;
    private string _maxDimensionText = string.Empty;
    private int _selectedFormatIndex;
    private int _selectedCompressionIndex = 1; // LZW default
    private bool _isExporting;
    private double _progressValue;
    private string _progressText = string.Empty;

    public ExportDialogViewModel()
    {
        ExportCommand = new RelayCommand(async _ => await ExportAsync(), _ => CanExport);
        BrowseCommand = new RelayCommand(_ => BrowseFolder());
    }

    public string DestinationFolder
    {
        get => _destinationFolder;
        set { _destinationFolder = value; OnPropertyChanged(); }
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
        set { _selectedFormatIndex = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsJpegSelected)); }
    }

    public int SelectedCompressionIndex
    {
        get => _selectedCompressionIndex;
        set { _selectedCompressionIndex = value; OnPropertyChanged(); }
    }

    public bool IsJpegSelected => SelectedFormatIndex == 0;

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
            DigitalAsset? asset = new DigitalAsset { Orientation = ImageOrientation.Normal }; // TODO: load real asset from DB
            items.Add((a.StoragePath, destPath, asset));
        }

        var progress = new Progress<(int Completed, int Total, string CurrentFile)>(p =>
        {
            ProgressValue = (double)p.Completed / p.Total * 100;
            ProgressText = $"{p.Completed}/{p.Total} — {p.CurrentFile}";
        });

        try
        {
            await exportService.ExportBatchAsync(items, format, Quality, maxDim, compression, progress);
            ProgressText = "Export complete!";
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

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
