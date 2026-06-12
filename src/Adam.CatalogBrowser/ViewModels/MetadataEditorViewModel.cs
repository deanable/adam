using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Adam.CatalogBrowser.Services;
using Adam.Shared.Models;
using Adam.Shared.Services;
using LiquidVision.Core;
using Microsoft.EntityFrameworkCore;

namespace Adam.CatalogBrowser.ViewModels;

public class MetadataEditorViewModel : INotifyPropertyChanged
{
    private readonly ModeManager _modeManager;
    private readonly IUiDispatcher _dispatcher;
    private readonly AiTaggingService? _aiTaggingService;
    private DigitalAsset? _asset;
    private MetadataProfile? _profile;
    private string _title = string.Empty;
    private string? _description;
    private ObservableCollection<string> _tags = [];
    private IEnumerable<string>? _autoCompleteSource;
    private int _rating;
    private bool _isDirty;
    private bool _hasAsset;
    private bool _isLoading;
    private bool _isAiTagging;
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
    private bool _canEdit = true;
    private string _editDisabledReason = string.Empty;

    public MetadataEditorViewModel(ModeManager modeManager, IUiDispatcher? dispatcher = null, AiTaggingService? aiTaggingService = null)
    {
        _modeManager = modeManager;
        _dispatcher = dispatcher ?? new AvaloniaUiDispatcher();
        _aiTaggingService = aiTaggingService;
        SaveCommand = new RelayCommand(async _ => await SaveAsync(), _ => CanEdit && IsDirty && HasAsset);
        SetRatingCommand = new RelayCommand(param =>
        {
            if (param is string s && int.TryParse(s, out var r))
            {
                Rating = r;
            }
        });
        AutoTagCommand = new RelayCommand(async _ => await AutoTagAsync(), _ => HasAsset && !IsLoading && !IsAiTagging);
    }

    /// <summary>
    /// Whether editing is permitted for the current session.
    /// Set externally by <c>MainWindowViewModel.RefreshPermissionsAsync</c>.
    /// When <c>false</c>, all editing controls are disabled with a tooltip explanation.
    /// </summary>
    public bool CanEdit
    {
        get => _canEdit;
        set
        {
            _canEdit = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(EditPermissionTooltip));
            (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (AutoTagCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// Human-readable explanation shown as tooltip when editing is disabled.
    /// Empty when editing is permitted.
    /// </summary>
    public string EditPermissionTooltip
    {
        get => _canEdit ? string.Empty : _editDisabledReason;
        set => _editDisabledReason = value ?? string.Empty;
    }

    public ICommand SaveCommand { get; }
    public ICommand SetRatingCommand { get; }
    public ICommand AutoTagCommand { get; }

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            _isLoading = value;
            OnPropertyChanged();
            (AutoTagCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public bool HasAsset
    {
        get => _hasAsset;
        set { _hasAsset = value; OnPropertyChanged(); }
    }

    public string Title { get => _title; set { _title = value; _isDirty = true; OnPropertyChanged(); OnPropertyChanged(nameof(SaveCommand)); } }
    public string? Description { get => _description; set { _description = value; _isDirty = true; OnPropertyChanged(); OnPropertyChanged(nameof(SaveCommand)); } }

    /// <summary>
    /// The collection of tags displayed and edited in the TagEditorControl.
    /// Setting this replaces the collection and subscribes to change notifications.
    /// </summary>
    public ObservableCollection<string> Tags
    {
        get => _tags;
        set
        {
            if (_tags != null)
                _tags.CollectionChanged -= OnTagsCollectionChanged;
            _tags = value ?? [];
            _tags.CollectionChanged += OnTagsCollectionChanged;
            OnPropertyChanged();
            IsDirty = true;
            OnPropertyChanged(nameof(SaveCommand));
        }
    }

    /// <summary>
    /// All keyword names in the system, used as autocomplete suggestions
    /// in the TagEditorControl.
    /// </summary>
    public IEnumerable<string>? AutoCompleteSource
    {
        get => _autoCompleteSource;
        set { _autoCompleteSource = value; OnPropertyChanged(); }
    }

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
    public bool IsAiTagging
    {
        get => _isAiTagging;
        set
        {
            _isAiTagging = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanAutoTag));
            (AutoTagCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public bool CanAutoTag => HasAsset && !IsLoading && !IsAiTagging;

    public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }
    public bool IsDirty { get => _isDirty; set { _isDirty = value; OnPropertyChanged(); OnPropertyChanged(nameof(SaveCommand)); } }

    /// <summary>
    /// Raised after a successful save so the host (MainWindowViewModel)
    /// can refresh the catalog and property inspector tags.
    /// </summary>
    public event Action? SaveCompleted;

    private void OnTagsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _isDirty = true;
        OnPropertyChanged(nameof(SaveCommand));
    }

    private async Task RunOnUiThreadAsync(Action action)
    {
        if (_dispatcher.CheckAccess())
            action();
        else
            await _dispatcher.InvokeAsync(action);
    }

    /// <summary>
    /// Loads a single asset's metadata and populates the tag collection.
    /// Also loads the system-wide keyword list for autocomplete.
    /// </summary>
    public async Task LoadAssetAsync(Guid assetId, CancellationToken ct = default)
    {
        await RunOnUiThreadAsync(() => IsLoading = true);
        try
        {
            await using var db = await _modeManager.CreateDbContextAsync(ct).ConfigureAwait(false);
            _asset = await db.DigitalAssets
                .Include(a => a.MetadataProfile)
                .Include(a => a.Keywords)
                .FirstOrDefaultAsync(a => a.Id == assetId, ct).ConfigureAwait(false);

            if (_asset == null)
            {
                await RunOnUiThreadAsync(() => HasAsset = false);
                return;
            }

            _profile = _asset.MetadataProfile;

            // Load keyword suggestions for autocomplete
            await LoadAutoCompleteSourceAsync(db);

            await RunOnUiThreadAsync(() =>
            {
                HasAsset = true;
                FileName = _asset.FileName;
                Title = _asset.Title;
                Description = _asset.Description;
                Tags = new ObservableCollection<string>(_asset.Keywords.Select(k => k.Name));
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
            });
        }
        finally
        {
            await RunOnUiThreadAsync(() => IsLoading = false);
        }
    }

    /// <summary>
    /// Loads all known keyword names from the database for autocomplete suggestions.
    /// </summary>
    private async Task LoadAutoCompleteSourceAsync(Adam.Shared.Data.AppDbContext db, CancellationToken ct = default)
    {
        try
        {
            var names = await db.Keywords
                .Select(k => k.Name)
                .Distinct()
                .OrderBy(n => n)
                .ToListAsync(ct);
            await RunOnUiThreadAsync(() => AutoCompleteSource = names);
        }
        catch
        {
            await RunOnUiThreadAsync(() => AutoCompleteSource = null);
        }
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        if (_asset == null) return;
        await using var db = await _modeManager.CreateDbContextAsync(ct).ConfigureAwait(false);

        // Reload asset with keywords tracking
        var asset = await db.DigitalAssets
            .Include(a => a.Keywords)
            .FirstOrDefaultAsync(a => a.Id == _asset.Id, ct).ConfigureAwait(false);
        if (asset == null) return;

        asset.Title = Title;
        asset.Description = Description;
        asset.Keywords.Clear();

        var tagNames = Tags.ToArray();
        if (tagNames.Length > 0)
        {
            await new KeywordService(db).AssociateKeywordsAsync(asset, tagNames, ct);
        }

        if (_profile != null)
        {
            var profile = await db.MetadataProfiles.FirstOrDefaultAsync(m => m.DigitalAssetId == asset.Id, ct).ConfigureAwait(false);
            if (profile != null)
                profile.Rating = Rating;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await RunOnUiThreadAsync(() =>
        {
            IsDirty = false;
            StatusText = "Saved.";
            SaveCompleted?.Invoke();
        });
    }

    /// <summary>
    /// Trigger B: Analyzes the loaded image with AI and unions results into the editable tags.
    /// Categories are applied directly to the database (D-12a: categories not surfaced in editor UI).
    /// </summary>
    private async Task AutoTagAsync()
    {
        if (_asset is null || _aiTaggingService is null || _asset.Type != AssetType.Image) return;

        IsAiTagging = true;
        StatusText = "AI tagging…";
        try
        {
            var result = await _aiTaggingService.AnalyzeAssetAsync(_asset.Id);

            // Union keywords into the editable Tags collection (rides existing dirty/Save flow)
            foreach (var kw in result.Keywords)
            {
                if (!Tags.Contains(kw, StringComparer.OrdinalIgnoreCase))
                    Tags.Add(kw);
            }

            // Fill description only when empty (D-06)
            if (string.IsNullOrWhiteSpace(Description) && !string.IsNullOrWhiteSpace(result.Description))
                Description = result.Description;

            // Apply categories directly to the DB (D-12a: no category UI in editor)
            await using var db = await _modeManager.CreateDbContextAsync();
            var asset = await db.DigitalAssets
                .Include(a => a.Categories)
                .FirstOrDefaultAsync(a => a.Id == _asset.Id);
            if (asset != null && result.Categories.Count > 0)
            {
                await new CategoryService(db).AssociateCategoriesAsync(asset, result.Categories);
                await db.SaveChangesAsync();
            }

            StatusText = $"AI tags applied ({result.Keywords.Count} keywords, {result.Categories.Count} categories)";
        }
        catch (Exception ex)
        {
            StatusText = $"AI tagging failed: {ex.Message}";
        }
        finally
        {
            IsAiTagging = false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
