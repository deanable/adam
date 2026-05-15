using System.ComponentModel;
using System.Runtime.CompilerServices;
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

    public Guid Id { get; set; }

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
            _thumbnailPath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Thumbnail));
            _thumbnail?.Dispose();
            _thumbnail = null;
        }
    }

    public Bitmap? Thumbnail
    {
        get
        {
            if (_thumbnail == null && !string.IsNullOrEmpty(_thumbnailPath) && File.Exists(_thumbnailPath))
            {
                try { _thumbnail = new Bitmap(_thumbnailPath); }
                catch { }
            }
            return _thumbnail;
        }
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
}
