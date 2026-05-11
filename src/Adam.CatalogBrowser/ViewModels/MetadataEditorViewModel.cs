using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Adam.CatalogBrowser.Services;
using Adam.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace Adam.CatalogBrowser.ViewModels;

public class MetadataEditorViewModel : INotifyPropertyChanged
{
    private readonly ModeManager _modeManager;
    private DigitalAsset? _asset;
    private MetadataProfile? _profile;
    private string _title = string.Empty;
    private string? _description;
    private string _tagsText = string.Empty;
    private int _rating;
    private bool _isDirty;
    private bool _hasAsset;
    private string _statusText = string.Empty;

    private string _cameraMake = string.Empty;
    private string _cameraModel = string.Empty;
    private string _lensModel = string.Empty;
    private string _focalLength = string.Empty;
    private string _aperture = string.Empty;
    private string _exposureTime = string.Empty;
    private string _iso = string.Empty;
    private string _dateTaken = string.Empty;
    private string _flash = string.Empty;
    private string _gps = string.Empty;
    private string _creator = string.Empty;
    private string _copyright = string.Empty;
    private string _headline = string.Empty;
    private string _fileName = string.Empty;

    public MetadataEditorViewModel(ModeManager modeManager)
    {
        _modeManager = modeManager;
        SaveCommand = new RelayCommand(async _ => await SaveAsync(), _ => IsDirty && HasAsset);
        SetRatingCommand = new RelayCommand(param =>
        {
            if (param is string s && int.TryParse(s, out var r))
            {
                Rating = r;
            }
        });
    }

    public ICommand SaveCommand { get; }
    public ICommand SetRatingCommand { get; }

    public bool HasAsset
    {
        get => _hasAsset;
        set { _hasAsset = value; OnPropertyChanged(); }
    }

    public string Title { get => _title; set { _title = value; _isDirty = true; OnPropertyChanged(); OnPropertyChanged(nameof(SaveCommand)); } }
    public string? Description { get => _description; set { _description = value; _isDirty = true; OnPropertyChanged(); OnPropertyChanged(nameof(SaveCommand)); } }
    public string TagsText { get => _tagsText; set { _tagsText = value; _isDirty = true; OnPropertyChanged(); OnPropertyChanged(nameof(SaveCommand)); } }
    public int Rating { get => _rating; set { _rating = value; _isDirty = true; OnPropertyChanged(); OnPropertyChanged(nameof(SaveCommand)); } }

    public string CameraMake { get => _cameraMake; set { _cameraMake = value; OnPropertyChanged(); } }
    public string CameraModel { get => _cameraModel; set { _cameraModel = value; OnPropertyChanged(); } }
    public string LensModel { get => _lensModel; set { _lensModel = value; OnPropertyChanged(); } }
    public string FocalLength { get => _focalLength; set { _focalLength = value; OnPropertyChanged(); } }
    public string Aperture { get => _aperture; set { _aperture = value; OnPropertyChanged(); } }
    public string ExposureTime { get => _exposureTime; set { _exposureTime = value; OnPropertyChanged(); } }
    public string Iso { get => _iso; set { _iso = value; OnPropertyChanged(); } }
    public string DateTaken { get => _dateTaken; set { _dateTaken = value; OnPropertyChanged(); } }
    public string Flash { get => _flash; set { _flash = value; OnPropertyChanged(); } }
    public string Gps { get => _gps; set { _gps = value; OnPropertyChanged(); } }
    public string Creator { get => _creator; set { _creator = value; OnPropertyChanged(); } }
    public string Copyright { get => _copyright; set { _copyright = value; OnPropertyChanged(); } }
    public string Headline { get => _headline; set { _headline = value; OnPropertyChanged(); } }
    public string FileName { get => _fileName; set { _fileName = value; OnPropertyChanged(); } }
    public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }
    public bool IsDirty { get => _isDirty; set { _isDirty = value; OnPropertyChanged(); OnPropertyChanged(nameof(SaveCommand)); } }

    public async Task LoadAssetAsync(Guid assetId, CancellationToken ct = default)
    {
        await using var db = _modeManager.CreateDbContext();
        _asset = await db.DigitalAssets
            .Include(a => a.MetadataProfile)
            .FirstOrDefaultAsync(a => a.Id == assetId, ct);

        if (_asset == null)
        {
            HasAsset = false;
            return;
        }

        _profile = _asset.MetadataProfile;
        HasAsset = true;
        FileName = _asset.FileName;
        Title = _asset.Title;
        Description = _asset.Description;
        TagsText = string.Join(", ", _asset.Tags);
        Rating = _profile?.Rating ?? 0;

        CameraMake = _profile?.CameraMake ?? "";
        CameraModel = _profile?.CameraModel ?? "";
        LensModel = _profile?.LensModel ?? "";
        FocalLength = _profile?.FocalLength?.ToString("F1") ?? "";
        Aperture = _profile?.Aperture is double a ? $"f/{a:F1}" : "";
        ExposureTime = _profile?.ExposureTime ?? "";
        Iso = _profile?.Iso?.ToString() ?? "";
        DateTaken = _profile?.DateTaken?.ToString("g") ?? "";
        Flash = _profile?.Flash == true ? "Yes" : _profile?.Flash == false ? "No" : "";
        Gps = _profile?.GpsLatitude is double lat && _profile?.GpsLongitude is double lng
            ? $"{lat:F5}, {lng:F5}" : "";
        Creator = _profile?.Creator ?? "";
        Copyright = _profile?.Copyright ?? "";
        Headline = _profile?.Headline ?? "";

        IsDirty = false;
    }

    public async Task SaveAsync()
    {
        if (_asset == null) return;
        await using var db = _modeManager.CreateDbContext();

        _asset.Title = Title;
        _asset.Description = Description;
        _asset.Tags = (TagsText ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (_profile != null)
            _profile.Rating = Rating;

        db.DigitalAssets.Update(_asset);
        await db.SaveChangesAsync();

        IsDirty = false;
        StatusText = "Saved.";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
