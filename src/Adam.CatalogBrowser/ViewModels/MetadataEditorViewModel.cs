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
    private readonly MetadataWritebackService? _writeback;
    private readonly IUserPreferenceService? _prefs;
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
    private string _usageTerms = string.Empty;
    private string _contactInfo = string.Empty;
    private string _city = string.Empty;
    private string _state = string.Empty;
    private string _country = string.Empty;
    private string _orientation = string.Empty;
    private string _gpsAltitude = string.Empty;
    private string _fileName = string.Empty;

    // ── §25-B: Collapsible panel visibility ──
    private bool _isPanelADescriptionExpanded = true;
    private bool _isPanelBCreatorExpanded = true;
    private bool _isPanelCRightsExpanded = true;
    private bool _isPanelDLocationExpanded;
    private bool _isPanelEDatesExpanded;
    private bool _isPanelFCameraExpanded;
    private bool _isPanelGGpsExpanded;
    private bool _isPanelHRawExpanded;
    private bool _isRestoringMetadataPanels;
    private ObservableCollection<MetadataRawItem> _rawMetadataItems = [];
    private bool _canEdit = true;
    private string _editDisabledReason = string.Empty;
    private HashSet<string> _aiGeneratedTags = new(StringComparer.OrdinalIgnoreCase);

    public MetadataEditorViewModel(ModeManager modeManager, IUiDispatcher? dispatcher = null, AiTaggingService? aiTaggingService = null, MetadataWritebackService? writeback = null, IUserPreferenceService? prefs = null)
    {
        _modeManager = modeManager;
        _dispatcher = dispatcher ?? new AvaloniaUiDispatcher();
        _aiTaggingService = aiTaggingService;
        _writeback = writeback;
        _prefs = prefs;
        SaveCommand = new RelayCommand(async _ => await SaveAsync(), _ => CanEdit && IsDirty && HasAsset);
        SetRatingCommand = new RelayCommand(param =>
        {
            if (param is string s && int.TryParse(s, out var r))
            {
                Rating = r;
            }
        });
        AutoTagCommand = new RelayCommand(async _ => await AutoTagAsync(), _ => HasAsset && !IsLoading && !IsAiTagging);
        TogglePanelCommand = new RelayCommand(param =>
        {
            if (param is string panelName)
            {
                switch (panelName)
                {
                    case "A": IsPanelADescriptionExpanded = !IsPanelADescriptionExpanded; break;
                    case "B": IsPanelBCreatorExpanded = !IsPanelBCreatorExpanded; break;
                    case "C": IsPanelCRightsExpanded = !IsPanelCRightsExpanded; break;
                    case "D": IsPanelDLocationExpanded = !IsPanelDLocationExpanded; break;
                    case "E": IsPanelEDatesExpanded = !IsPanelEDatesExpanded; break;
                    case "F": IsPanelFCameraExpanded = !IsPanelFCameraExpanded; break;
                    case "G": IsPanelGGpsExpanded = !IsPanelGGpsExpanded; break;
                    case "H": IsPanelHRawExpanded = !IsPanelHRawExpanded; break;
                }
            }
        });
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

    /// <summary>
    /// Tag names that were AI-generated, used for the AI provenance badge (T16.6).
    /// </summary>
    public IReadOnlySet<string> AiGeneratedTagNames => _aiGeneratedTags;

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
    public string Creator { get => _creator; set { _creator = value; _isDirty = true; OnPropertyChanged(); OnPropertyChanged(nameof(SaveCommand)); } }
    public string Copyright { get => _copyright; set { _copyright = value; _isDirty = true; OnPropertyChanged(); OnPropertyChanged(nameof(SaveCommand)); } }
    public string Headline { get => _headline; set { _headline = value; _isDirty = true; OnPropertyChanged(); OnPropertyChanged(nameof(SaveCommand)); } }

    // Surface hidden metadata profile fields (§25-A) — read-only display
    public string UsageTerms { get => _usageTerms; set { _usageTerms = value; OnPropertyChanged(); } }
    public string ContactInfo { get => _contactInfo; set { _contactInfo = value; OnPropertyChanged(); } }
    public string City { get => _city; set { _city = value; OnPropertyChanged(); } }
    public string State { get => _state; set { _state = value; OnPropertyChanged(); } }
    public string Country { get => _country; set { _country = value; OnPropertyChanged(); } }
    public string ReadOnlyOrientation { get => _orientation; set { _orientation = value; OnPropertyChanged(); } }
    public string GpsAltitude { get => _gpsAltitude; set { _gpsAltitude = value; OnPropertyChanged(); } }
    public string FileName { get => _fileName; set { _fileName = value; OnPropertyChanged(); } }

    // ── §25-B: Collapsible panel visibility properties ──
    public ICommand TogglePanelCommand { get; }

    public bool IsPanelADescriptionExpanded
    {
        get => _isPanelADescriptionExpanded;
        set { _isPanelADescriptionExpanded = value; OnPropertyChanged(); if (!_isRestoringMetadataPanels) _ = PersistMetadataPanelStatesAsync(); }
    }

    public bool IsPanelBCreatorExpanded
    {
        get => _isPanelBCreatorExpanded;
        set { _isPanelBCreatorExpanded = value; OnPropertyChanged(); if (!_isRestoringMetadataPanels) _ = PersistMetadataPanelStatesAsync(); }
    }

    public bool IsPanelCRightsExpanded
    {
        get => _isPanelCRightsExpanded;
        set { _isPanelCRightsExpanded = value; OnPropertyChanged(); if (!_isRestoringMetadataPanels) _ = PersistMetadataPanelStatesAsync(); }
    }

    public bool IsPanelDLocationExpanded
    {
        get => _isPanelDLocationExpanded;
        set { _isPanelDLocationExpanded = value; OnPropertyChanged(); if (!_isRestoringMetadataPanels) _ = PersistMetadataPanelStatesAsync(); }
    }

    public bool IsPanelEDatesExpanded
    {
        get => _isPanelEDatesExpanded;
        set { _isPanelEDatesExpanded = value; OnPropertyChanged(); if (!_isRestoringMetadataPanels) _ = PersistMetadataPanelStatesAsync(); }
    }

    public bool IsPanelFCameraExpanded
    {
        get => _isPanelFCameraExpanded;
        set { _isPanelFCameraExpanded = value; OnPropertyChanged(); if (!_isRestoringMetadataPanels) _ = PersistMetadataPanelStatesAsync(); }
    }

    public bool IsPanelGGpsExpanded
    {
        get => _isPanelGGpsExpanded;
        set { _isPanelGGpsExpanded = value; OnPropertyChanged(); if (!_isRestoringMetadataPanels) _ = PersistMetadataPanelStatesAsync(); }
    }

    public bool IsPanelHRawExpanded
    {
        get => _isPanelHRawExpanded;
        set { _isPanelHRawExpanded = value; OnPropertyChanged(); if (!_isRestoringMetadataPanels) _ = PersistMetadataPanelStatesAsync(); }
    }

    /// <summary>
    /// Raw metadata key-value pairs for the All Metadata viewer (Panel H).
    /// </summary>
    public ObservableCollection<MetadataRawItem> RawMetadataItems
    {
        get => _rawMetadataItems;
        set { _rawMetadataItems = value; OnPropertyChanged(); }
    }

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

    /// <summary>
    /// Optional callback set by the host (MainWindow) to show the AI tag review dialog.
    /// Accepts the scored <see cref="AiTagResult"/> and returns accepted keywords/categories
    /// (or null if cancelled). If not set, tags are auto-applied (legacy behavior).
    /// </summary>
    public Func<AiTagResult, Task<AiTagResult?>>? ShowAiReviewDialogAsync;

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
                _aiGeneratedTags = new HashSet<string>(
                    _asset.Keywords.Where(k => k.IsAiGenerated).Select(k => k.Name),
                    StringComparer.OrdinalIgnoreCase);
                Rating = _profile?.Rating ?? 0;

                // Restore saved metadata panel expand states
                RestoreMetadataPanelStates();

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
                UsageTerms = _profile?.UsageTerms ?? "";
                ContactInfo = _profile?.ContactInfo ?? "";
                City = _profile?.City ?? "";
                State = _profile?.State ?? "";
                Country = _profile?.Country ?? "";
                ReadOnlyOrientation = _profile?.Orientation ?? "";
                GpsAltitude = _profile?.GpsAltitude?.ToString("F1") ?? "";

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
            await new KeywordService(db).AssociateKeywordsAsync(asset, tagNames, isAiGenerated: false, ct);

            // Re-apply provenance for AI-generated keywords (T16.5)
            if (_aiGeneratedTags.Count > 0)
            {
                foreach (var keyword in asset.Keywords)
                {
                    if (_aiGeneratedTags.Contains(keyword.Name))
                        keyword.IsAiGenerated = true;
                }
            }
        }

        if (_profile != null)
        {
            var profile = await db.MetadataProfiles.FirstOrDefaultAsync(m => m.DigitalAssetId == asset.Id, ct).ConfigureAwait(false);
            if (profile != null)
            {
                profile.Rating = Rating;
                profile.Creator = Creator;
                profile.Copyright = Copyright;
                profile.Headline = Headline;
                profile.UsageTerms = UsageTerms;
                profile.ContactInfo = ContactInfo;
                profile.City = City;
                profile.State = State;
                profile.Country = Country;
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // Write metadata back to the source file (best-effort; failures don't fail the save)
        if (_writeback != null && !string.IsNullOrEmpty(asset.StoragePath))
        {
            try
            {
                var filePath = asset.StoragePath;
                if (_writeback.IsRawFile(filePath) || _writeback.IsOfficeDocument(filePath))
                    await _writeback.WriteSidecarXmpAsync(filePath, asset, ct).ConfigureAwait(false);
                else if (_writeback.SupportsEmbeddedMetadata(filePath))
                    await _writeback.WriteMetadataAsync(filePath, asset, ct).ConfigureAwait(false);
            }
            catch (MetadataWritebackService.ReadOnlyFileException)
            {
                // File is read-only — catalog was saved, but file writeback skipped
                await RunOnUiThreadAsync(() =>
                {
                    StatusText = "Saved (file is read-only — metadata not written to disk)";
                });
                // Continue to clear dirty state — catalog data is persisted
            }
            catch (Exception ex)
            {
                // Log but don't surface the error to the user for writeback failures
                System.Diagnostics.Debug.WriteLine($"Metadata write-back failed: {ex.Message}");
            }
        }

        await RunOnUiThreadAsync(() =>
        {
            IsDirty = false;
            StatusText = "Saved.";
            SaveCompleted?.Invoke();
        });
    }

    /// <summary>
    /// Trigger B: Analyzes the loaded image with AI and either shows a review dialog
    /// (if <see cref="ShowAiReviewDialogAsync"/> is set) or auto-applies the results.
    /// Categories are applied directly to the database (D-12a: categories not surfaced in editor UI).
    /// </summary>
    private async Task AutoTagAsync()
    {
        if (_asset is null || _aiTaggingService is null || _asset.Type != AssetType.Image) return;

        IsAiTagging = true;
        StatusText = "AI tagging…";
        try
        {
            var scoredResult = await _aiTaggingService.AnalyzeAssetWithScoresAsync(_asset.Id);

            // If a review dialog callback is registered, let the user review before applying
            if (ShowAiReviewDialogAsync != null)
            {
                var accepted = await ShowAiReviewDialogAsync(scoredResult);
                if (accepted == null)
                {
                    StatusText = "AI tagging cancelled.";
                    return;
                }

                // Track AI-generated tag names for provenance preservation (T16.5)
                foreach (var kw in accepted.Keywords)
                {
                    if (!Tags.Contains(kw.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        Tags.Add(kw.Name);
                        _aiGeneratedTags.Add(kw.Name);
                    }
                }

                // Fill description only when empty (D-06)
                if (string.IsNullOrWhiteSpace(Description) && !string.IsNullOrWhiteSpace(accepted.Description))
                    Description = accepted.Description;

                // Apply accepted categories directly to the DB
                if (accepted.Categories.Count > 0)
                {
                    await using var db = await _modeManager.CreateDbContextAsync();
                    var asset = await db.DigitalAssets
                        .Include(a => a.Categories)
                        .FirstOrDefaultAsync(a => a.Id == _asset.Id);
                    if (asset != null)
                    {
                        await new CategoryService(db).AssociateCategoriesAsync(
                            asset, accepted.Categories.Select(c => c.Name).ToList());
                        await db.SaveChangesAsync();
                    }
                }

                StatusText = $"AI tags applied ({accepted.Keywords.Count} keywords, {accepted.Categories.Count} categories)";
            }
            else
            {
                // Legacy auto-apply mode (no review dialog available)
                var raw = await _aiTaggingService.AnalyzeAssetAsync(_asset.Id);

                foreach (var kw in raw.Keywords)
                {
                    if (!Tags.Contains(kw, StringComparer.OrdinalIgnoreCase))
                    {
                        Tags.Add(kw);
                        _aiGeneratedTags.Add(kw);
                    }
                }

                if (string.IsNullOrWhiteSpace(Description) && !string.IsNullOrWhiteSpace(raw.Description))
                    Description = raw.Description;

                if (raw.Categories.Count > 0)
                {
                    await using var db = await _modeManager.CreateDbContextAsync();
                    var asset = await db.DigitalAssets
                        .Include(a => a.Categories)
                        .FirstOrDefaultAsync(a => a.Id == _asset.Id);
                    if (asset != null)
                    {
                        await new CategoryService(db).AssociateCategoriesAsync(asset, raw.Categories);
                        await db.SaveChangesAsync();
                    }
                }

                StatusText = $"AI tags applied ({raw.Keywords.Count} keywords, {raw.Categories.Count} categories)";
            }
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

    // ── Metadata panel expand persistence ───────────────────────

    /// <summary>
    /// Persists the current expand/collapse state of all 8 metadata editor panels
    /// to UserPreferenceService as a JSON set of panel IDs under "metadata.expandedPanels".
    /// </summary>
    private async Task PersistMetadataPanelStatesAsync()
    {
        if (_prefs == null) return;
        try
        {
            var expanded = new System.Collections.Generic.HashSet<string>();
            if (_isPanelADescriptionExpanded) expanded.Add("A");
            if (_isPanelBCreatorExpanded) expanded.Add("B");
            if (_isPanelCRightsExpanded) expanded.Add("C");
            if (_isPanelDLocationExpanded) expanded.Add("D");
            if (_isPanelEDatesExpanded) expanded.Add("E");
            if (_isPanelFCameraExpanded) expanded.Add("F");
            if (_isPanelGGpsExpanded) expanded.Add("G");
            if (_isPanelHRawExpanded) expanded.Add("H");

            // Merge with existing saved state (sidebar panels stored by MainWindowViewModel)
            var existing = await _prefs.GetAsync<System.Collections.Generic.HashSet<string>>("metadata.expandedPanels");
            if (existing != null)
            {
                // Keep non-metadata-editor entries (sidebar/right-panel states)
                foreach (var p in existing)
                {
                    if (p.Length != 1 || p[0] < 'A' || p[0] > 'H')
                        expanded.Add(p);
                }
            }

            await _prefs.SetAsync("metadata.expandedPanels", expanded);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MetadataEditor] Failed to persist panel states: {ex.Message}");
        }
    }

    /// <summary>
    /// Restores saved metadata panel expand states from UserPreferenceService.
    /// Called during LoadAssetAsync after asset data is populated.
    /// </summary>
    private void RestoreMetadataPanelStates()
    {
        if (_prefs == null) return;
        _ = RestoreMetadataPanelStatesAsync();
    }

    private async Task RestoreMetadataPanelStatesAsync()
    {
        try
        {
            var expanded = await _prefs!.GetAsync<System.Collections.Generic.HashSet<string>>("metadata.expandedPanels");
            if (expanded == null) return;

            // Suppress individual saves during batch restore to avoid race conditions
            _isRestoringMetadataPanels = true;
            await RunOnUiThreadAsync(() =>
            {
                IsPanelADescriptionExpanded = expanded.Contains("A");
                IsPanelBCreatorExpanded = expanded.Contains("B");
                IsPanelCRightsExpanded = expanded.Contains("C");
                IsPanelDLocationExpanded = expanded.Contains("D");
                IsPanelEDatesExpanded = expanded.Contains("E");
                IsPanelFCameraExpanded = expanded.Contains("F");
                IsPanelGGpsExpanded = expanded.Contains("G");
                IsPanelHRawExpanded = expanded.Contains("H");
            });
            _isRestoringMetadataPanels = false;

            // Single coalesced save after all panels are restored
            _ = PersistMetadataPanelStatesAsync();
        }
        catch (Exception ex)
        {
            _isRestoringMetadataPanels = false;
            System.Diagnostics.Debug.WriteLine($"[MetadataEditor] Failed to restore panel states: {ex.Message}");
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
