using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Adam.CatalogBrowser.ViewModels;

public class AssetDetailViewModel : INotifyPropertyChanged
{
    private string _fileName = string.Empty;
    private string _fileType = string.Empty;
    private string _fileSizeText = string.Empty;
    private string _dimensions = string.Empty;
    private string _createdAtText = string.Empty;
    private string _cameraInfo = string.Empty;
    private string _lensInfo = string.Empty;
    private string _exposureInfo = string.Empty;
    private string _isoText = string.Empty;
    private string _dateTakenText = string.Empty;

    public string FileName
    {
        get => _fileName;
        set { _fileName = value; OnPropertyChanged(); }
    }

    public string FileType
    {
        get => _fileType;
        set { _fileType = value; OnPropertyChanged(); }
    }

    public string FileSizeText
    {
        get => _fileSizeText;
        set { _fileSizeText = value; OnPropertyChanged(); }
    }

    public string Dimensions
    {
        get => _dimensions;
        set { _dimensions = value; OnPropertyChanged(); }
    }

    public string CreatedAtText
    {
        get => _createdAtText;
        set { _createdAtText = value; OnPropertyChanged(); }
    }

    public string CameraInfo
    {
        get => _cameraInfo;
        set { _cameraInfo = value; OnPropertyChanged(); }
    }

    public string LensInfo
    {
        get => _lensInfo;
        set { _lensInfo = value; OnPropertyChanged(); }
    }

    public string ExposureInfo
    {
        get => _exposureInfo;
        set { _exposureInfo = value; OnPropertyChanged(); }
    }

    public string IsoText
    {
        get => _isoText;
        set { _isoText = value; OnPropertyChanged(); }
    }

    public string DateTakenText
    {
        get => _dateTakenText;
        set { _dateTakenText = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
