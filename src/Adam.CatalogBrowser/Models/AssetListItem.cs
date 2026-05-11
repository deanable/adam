using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Adam.CatalogBrowser.Models;

public class AssetListItem : INotifyPropertyChanged
{
    private string _title = string.Empty;
    private string _fileName = string.Empty;
    private string _thumbnailPath = string.Empty;
    private string _fileType = string.Empty;
    private bool _isSelected;

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
        set { _thumbnailPath = value; OnPropertyChanged(); }
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

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
