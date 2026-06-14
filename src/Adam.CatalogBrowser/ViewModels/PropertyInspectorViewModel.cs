using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Adam.CatalogBrowser.Models;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Adam.Shared.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Adam.CatalogBrowser.Services;
using Adam.CatalogBrowser.Controls;

namespace Adam.CatalogBrowser.ViewModels;

public class MetadataEntry
{
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class PropertyInspectorViewModel : INotifyPropertyChanged
{
    private readonly ILogger<PropertyInspectorViewModel> _logger;
    private readonly ModeManager _modeManager;
    private readonly MetadataWritebackService _writeback;
    private readonly IUiDispatcher _dispatcher;
    private AssetListItem? _selectedAsset;
    private readonly List<AssetListItem> _selectedAssets = [];
    private bool _isPropertyInspectorLoading;
    private ObservableCollection<MetadataEntry> _selectedAssetMetadata = [];
    private ObservableCollection<TagOccurrence>? _aggregatedTags;
    private ObservableCollection<TagOccurrence>? _aggregatedCategories;
    private List<string>? _tagAutoCompleteSource;
    private ObservableCollection<string> _selectedAssetTags = [];
    private bool _tagsDirty;

    private string? _selectedAssetDescription;
    private bool _descriptionDirty;
    private ObservableCollection<string> _selectedAssetCategories = [];
    private bool _categoriesDirty;
    private IEnumerable<string>? _categoryAutoCompleteSource;
    private DateTimeOffset? _selectedAssetDateTaken;
    private bool _dateTakenDirty;
    private int _selectedAssetRating;
    private bool _ratingDirty;
    private int _selectedAssetLabel;
    private bool _labelDirty;
    private int _selectedAssetFlag;
    private bool _flagDirty;
    private string? _selectedAssetCopyright;
    private bool _copyrightDirty;
    private string? _selectedAssetGpsLatitude;
    private string? _selectedAssetGpsLongitude;
    private bool _gpsDirty;
    private bool _canEdit = true;

    // Batch mode mixed-value indicators
    private bool _isDescriptionMixed;
    private bool _isRatingMixed;
    private bool _isLabelMixed;
    private bool _isFlagMixed;
    private bool _isCopyrightMixed;
    private bool _isGpsMixed;
    private bool _isDateTakenMixed;
    private bool _isBatchMode;
    private bool _isApplyInProgress;

    private CancellationTokenSource? _metadataCts;

    public PropertyInspectorViewModel(ILogger<PropertyInspectorViewModel> logger, ModeManager modeManager, MetadataWritebackService writeback, IUiDispatcher? dispatcher = null)
    {
        _logger = logger;
        _modeManager = modeManager;
        _writeback = writeback;
        _dispatcher = dispatcher ?? new AvaloniaUiDispatcher();

        SaveTagsCommand = new RelayCommand(
            async _ => await AutoSaveTagsAsync(),
            _ => _selectedAsset != null && CanEdit && AnyDirty);

        ApplyBatchEditCommand = new RelayCommand(
            async _ => await ApplyBatchEditAsync(),
            _ => CanEdit && AnyDirty);

        SelectedAssetTags = _selectedAssetTags;
        SelectedAssetCategories = _selectedAssetCategories;
    }

    private bool AnyDirty => _tagsDirty || _descriptionDirty || _categoriesDirty || _dateTakenDirty
        || _ratingDirty || _labelDirty || _flagDirty || _copyrightDirty || _gpsDirty;

    /// <summary>
    /// Whether the current user has permission to edit metadata.
    /// Set by MainWindowViewModel.RefreshPermissionsAsync().
    /// When false, all editing controls in the right panel are disabled (T7.2).
    /// </summary>
    public bool CanEdit
    {
        get => _canEdit;
        set { _canEdit = value; OnPropertyChanged(); }
    }

    public RelayCommand SaveTagsCommand { get; }

    // ──────────────────────────────────────────────
    //  T14.1: Batch editing
    // ──────────────────────────────────────────────

    /// <summary>
    /// Command to apply batch metadata changes to all selected assets.
    /// </summary>
    public RelayCommand ApplyBatchEditCommand { get; }

    /// <summary>
    /// True when multiple assets are selected (batch mode).
    /// </summary>
    public bool IsBatchMode
    {
        get => _isBatchMode;
        set
        {
            if (_isBatchMode == value) return;
            _isBatchMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSingleMode));
        }
    }

    /// <summary>
    /// True in single-asset editing mode (not batch).
    /// </summary>
    public bool IsSingleMode => !IsBatchMode && HasSelectedAsset;

    /// <summary>
    /// True while a batch apply operation is in progress.
    /// </summary>
    public bool IsApplyInProgress
    {
        get => _isApplyInProgress;
        set { _isApplyInProgress = value; OnPropertyChanged(); }
    }

    // Mixed-value indicators (T14.1 — detect when selected assets have different values)

    public bool IsDescriptionMixed
    {
        get => _isDescriptionMixed;
        set { _isDescriptionMixed = value; OnPropertyChanged(); }
    }

    public bool IsRatingMixed
    {
        get => _isRatingMixed;
        set { _isRatingMixed = value; OnPropertyChanged(); }
    }

    public bool IsLabelMixed
    {
        get => _isLabelMixed;
        set { _isLabelMixed = value; OnPropertyChanged(); }
    }

    public bool IsFlagMixed
    {
        get => _isFlagMixed;
        set { _isFlagMixed = value; OnPropertyChanged(); }
    }

    public bool IsCopyrightMixed
    {
        get => _isCopyrightMixed;
        set { _isCopyrightMixed = value; OnPropertyChanged(); }
    }

    public bool IsGpsMixed
    {
        get => _isGpsMixed;
        set { _isGpsMixed = value; OnPropertyChanged(); }
    }

    public bool IsDateTakenMixed
    {
        get => _isDateTakenMixed;
        set { _isDateTakenMixed = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Selection count text shown in the batch UI header.
    /// </summary>
    public string BatchSelectionCountText => _selectedAssets.Count > 0
        ? $"Editing {_selectedAssets.Count} assets"
        : string.Empty;

    /// <summary>
    /// Apply button text showing the affected asset count.
    /// </summary>
    public string ApplyBatchButtonText => _selectedAssets.Count > 0
        ? $"Apply to {_selectedAssets.Count} assets"
        : "Apply";

    public AssetListItem? SelectedAsset
    {
        get => _selectedAsset;
        set
        {
            if (_selectedAsset == value) return;
            _ = OnSelectionChangingAsync(value);
        }
    }

    private async Task OnSelectionChangingAsync(AssetListItem? newAsset)
    {
        // Save any pending tag edits before switching assets
        await AutoSaveTagsAsync();

        await _dispatcher.InvokeAsync(() =>
        {
            _selectedAsset = newAsset;
            OnPropertyChanged(nameof(SelectedAsset));
            OnPropertyChanged(nameof(HasSelectedAsset));
            OnPropertyChanged(nameof(HasSingleSelection));
            SaveTagsCommand.RaiseCanExecuteChanged();
        });

        await LoadSelectedAssetMetadataAsync();
    }

    public void SetMultiSelection(IEnumerable<AssetListItem> assets)
    {
        _selectedAssets.Clear();
        _selectedAssets.AddRange(assets);
        OnPropertyChanged(nameof(HasMultiSelection));
        OnPropertyChanged(nameof(HasSingleSelection));
        OnPropertyChanged(nameof(BatchSelectionCountText));
        OnPropertyChanged(nameof(ApplyBatchButtonText));

        IsBatchMode = _selectedAssets.Count > 1;

        if (HasMultiSelection)
            _ = DetectBatchMixedValuesAsync();
        else
            ClearBatchMixedValues();

        _ = ComputeAggregatedTagsAsync();
        _ = ComputeAggregatedCategoriesAsync();
    }

    /// <summary>
    /// Resets all mixed-value indicators to false.
    /// </summary>
    private void ClearBatchMixedValues()
    {
        IsDescriptionMixed = false;
        IsRatingMixed = false;
        IsLabelMixed = false;
        IsFlagMixed = false;
        IsCopyrightMixed = false;
        IsGpsMixed = false;
        IsDateTakenMixed = false;
    }

    /// <summary>
    /// Queries the database for all selected assets and detects which fields
    /// have differing values (mixed). Sets the primary asset's values as defaults
    /// for the batch edit fields, then clears dirty flags so the user starts fresh.
    /// </summary>
    private async Task DetectBatchMixedValuesAsync()
    {
        if (_selectedAssets.Count <= 1 || !_modeManager.IsStandalone)
        {
            ClearBatchMixedValues();
            return;
        }

        try
        {
            await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);
            var ids = _selectedAssets.Select(a => a.Id).ToList();
            var assets = await db.DigitalAssets
                .Include(a => a.MetadataProfile)
                .Where(a => ids.Contains(a.Id))
                .ToListAsync().ConfigureAwait(false);

            if (assets.Count == 0) return;

            await _dispatcher.InvokeAsync(() =>
            {
                // Detect mixed values by comparing all assets
                IsDescriptionMixed = assets.Select(a => a.Description ?? "").Distinct().Count() > 1;
                IsRatingMixed = assets.Select(a => a.Rating).Distinct().Count() > 1;
                IsLabelMixed = assets.Select(a => (int)a.Label).Distinct().Count() > 1;
                IsFlagMixed = assets.Select(a => (int)a.Flag).Distinct().Count() > 1;
                IsCopyrightMixed = assets.Select(a => a.Copyright ?? "").Distinct().Count() > 1;
                IsDateTakenMixed = assets
                    .Select(a => a.MetadataProfile?.DateTaken?.Ticks ?? 0L)
                    .Distinct().Count() > 1;

                var gpsMixed = assets
                    .Select(a => (a.GpsLatitude ?? 0.0, a.GpsLongitude ?? 0.0))
                    .Distinct().Count() > 1;
                IsGpsMixed = gpsMixed;

                // Populate edit fields with the primary asset's values (preserves existing behavior)
                var primary = assets.FirstOrDefault(a => a.Id == _selectedAsset?.Id) ?? assets[0];

                // Set values without marking dirty
                _selectedAssetDescription = primary.Description;
                _descriptionDirty = false;
                OnPropertyChanged(nameof(SelectedAssetDescription));

                _selectedAssetRating = primary.Rating;
                _ratingDirty = false;
                OnPropertyChanged(nameof(SelectedAssetRating));

                _selectedAssetLabel = (int)primary.Label;
                _labelDirty = false;
                OnPropertyChanged(nameof(SelectedAssetLabel));

                _selectedAssetFlag = (int)primary.Flag;
                _flagDirty = false;
                OnPropertyChanged(nameof(SelectedAssetFlag));

                _selectedAssetCopyright = primary.Copyright;
                _copyrightDirty = false;
                OnPropertyChanged(nameof(SelectedAssetCopyright));

                _selectedAssetGpsLatitude = primary.GpsLatitude?.ToString();
                _selectedAssetGpsLongitude = primary.GpsLongitude?.ToString();
                _gpsDirty = false;
                OnPropertyChanged(nameof(SelectedAssetGpsLatitude));
                OnPropertyChanged(nameof(SelectedAssetGpsLongitude));

                _selectedAssetDateTaken = primary.MetadataProfile?.DateTaken.HasValue == true
                    ? new DateTimeOffset(primary.MetadataProfile.DateTaken.Value)
                    : null;
                _dateTakenDirty = false;
                OnPropertyChanged(nameof(SelectedAssetDateTaken));

                SaveTagsCommand.RaiseCanExecuteChanged();
                ApplyBatchEditCommand.RaiseCanExecuteChanged();
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to detect batch mixed values");
        }
    }

    /// <summary>
    /// Applies the current batch edits to ALL selected assets (T14.1).
    /// Only applies fields that have been modified (dirty flags).
    /// </summary>
    private async Task ApplyBatchEditAsync()
    {
        if (!AnyDirty || _selectedAssets.Count == 0 || !_modeManager.IsStandalone)
            return;

        IsApplyInProgress = true;

        try
        {
            await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);
            var ids = _selectedAssets.Select(a => a.Id).ToList();
            var assets = await db.DigitalAssets
                .Include(a => a.Keywords)
                .Include(a => a.Categories)
                .Include(a => a.MetadataProfile)
                .Where(a => ids.Contains(a.Id))
                .ToListAsync().ConfigureAwait(false);

            foreach (var asset in assets)
            {
                if (_descriptionDirty) asset.Description = SelectedAssetDescription;
                if (_tagsDirty)
                {
                    asset.Keywords.Clear();
                    var tagNames = _selectedAssetTags.ToArray();
                    if (tagNames.Length > 0) await new KeywordService(db).AssociateKeywordsAsync(asset, tagNames).ConfigureAwait(false);
                }
                if (_categoriesDirty)
                {
                    asset.Categories.Clear();
                    var categoryNames = _selectedAssetCategories.ToArray();
                    if (categoryNames.Length > 0) await new CategoryService(db).AssociateCategoriesAsync(asset, categoryNames).ConfigureAwait(false);
                }
                if (_dateTakenDirty)
                {
                    if (asset.MetadataProfile == null) asset.MetadataProfile = new MetadataProfile { Id = Guid.NewGuid(), DigitalAssetId = asset.Id };
                    asset.MetadataProfile.DateTaken = SelectedAssetDateTaken?.DateTime;
                }
                if (_ratingDirty) asset.Rating = SelectedAssetRating;
                if (_labelDirty) asset.Label = (AssetLabel)SelectedAssetLabel;
                if (_flagDirty) asset.Flag = (AssetFlag)SelectedAssetFlag;
                if (_copyrightDirty) asset.Copyright = SelectedAssetCopyright;
                if (_gpsDirty)
                {
                    asset.GpsLatitude = double.TryParse(SelectedAssetGpsLatitude, out var lat) ? lat : null;
                    asset.GpsLongitude = double.TryParse(SelectedAssetGpsLongitude, out var lon) ? lon : null;
                }
            }

            await db.SaveChangesAsync().ConfigureAwait(false);

            await _dispatcher.InvokeAsync(() =>
            {
                _tagsDirty = false;
                _descriptionDirty = false;
                _categoriesDirty = false;
                _dateTakenDirty = false;
                _ratingDirty = false;
                _labelDirty = false;
                _flagDirty = false;
                _copyrightDirty = false;
                _gpsDirty = false;
                SaveTagsCommand.RaiseCanExecuteChanged();
                ApplyBatchEditCommand.RaiseCanExecuteChanged();
            });

            // Re-detect mixed values after apply to refresh indicators
            _ = DetectBatchMixedValuesAsync();

            _logger.LogInformation("Batch-applied metadata to {Count} asset(s)", assets.Count);

            // Write metadata back to source files (best-effort)
            foreach (var asset in assets)
            {
                try
                {
                    var filePath = asset.StoragePath;
                    if (_writeback.IsRawFile(filePath) || _writeback.IsOfficeDocument(filePath))
                        await _writeback.WriteSidecarXmpAsync(filePath, asset).ConfigureAwait(false);
                    else if (_writeback.SupportsEmbeddedMetadata(filePath))
                        await _writeback.WriteMetadataAsync(filePath, asset).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Metadata write-back failed for asset {AssetId}", asset.Id);
                }
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to batch-apply metadata"); }
        finally
        {
            IsApplyInProgress = false;
        }
    }

    public bool HasSelectedAsset => _selectedAsset != null;
    public bool HasSingleSelection => !HasMultiSelection && HasSelectedAsset;
    public bool HasMultiSelection => _selectedAssets.Count > 1;

    public ObservableCollection<MetadataEntry> SelectedAssetMetadata
    {
        get => _selectedAssetMetadata;
        set { _selectedAssetMetadata = value; OnPropertyChanged(); }
    }

    public ObservableCollection<TagOccurrence>? AggregatedTags
    {
        get => _aggregatedTags;
        set { _aggregatedTags = value; OnPropertyChanged(); }
    }

    public ObservableCollection<TagOccurrence>? AggregatedCategories
    {
        get => _aggregatedCategories;
        set { _aggregatedCategories = value; OnPropertyChanged(); }
    }

    public IEnumerable<string>? TagAutoCompleteSource
    {
        get => _tagAutoCompleteSource;
        set { _tagAutoCompleteSource = value?.ToList(); OnPropertyChanged(); }
    }

    public ObservableCollection<string> SelectedAssetTags
    {
        get => _selectedAssetTags;
        set
        {
            if (_selectedAssetTags != null)
                _selectedAssetTags.CollectionChanged -= OnCollectionDirty;
            _selectedAssetTags = value ?? [];
            _selectedAssetTags.CollectionChanged += OnCollectionDirty;
            OnPropertyChanged();
        }
    }

    private void OnCollectionDirty(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        _tagsDirty = true;
        SaveTagsCommand.RaiseCanExecuteChanged();
        ApplyBatchEditCommand.RaiseCanExecuteChanged();
    }

    private void OnCategoriesDirty(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        _categoriesDirty = true;
        SaveTagsCommand.RaiseCanExecuteChanged();
        ApplyBatchEditCommand.RaiseCanExecuteChanged();
    }

    public string? SelectedAssetDescription
    {
        get => _selectedAssetDescription;
        set
        {
            if (_selectedAssetDescription != value)
            {
                _selectedAssetDescription = value;
                _descriptionDirty = true;
                IsDescriptionMixed = false;
                SaveTagsCommand.RaiseCanExecuteChanged();
                ApplyBatchEditCommand.RaiseCanExecuteChanged();
                OnPropertyChanged();
            }
        }
    }

    public DateTimeOffset? SelectedAssetDateTaken
    {
        get => _selectedAssetDateTaken;
        set
        {
            if (_selectedAssetDateTaken != value)
            {
                _selectedAssetDateTaken = value;
                _dateTakenDirty = true;
                IsDateTakenMixed = false;
                SaveTagsCommand.RaiseCanExecuteChanged();
                ApplyBatchEditCommand.RaiseCanExecuteChanged();
                OnPropertyChanged();
            }
        }
    }

    public int SelectedAssetRating
    {
        get => _selectedAssetRating;
        set
        {
            if (_selectedAssetRating != value)
            {
                _selectedAssetRating = value;
                _ratingDirty = true;
                IsRatingMixed = false;
                SaveTagsCommand.RaiseCanExecuteChanged();
                ApplyBatchEditCommand.RaiseCanExecuteChanged();
                OnPropertyChanged();
            }
        }
    }

    public int SelectedAssetLabel
    {
        get => _selectedAssetLabel;
        set
        {
            if (_selectedAssetLabel != value)
            {
                _selectedAssetLabel = value;
                _labelDirty = true;
                IsLabelMixed = false;
                SaveTagsCommand.RaiseCanExecuteChanged();
                ApplyBatchEditCommand.RaiseCanExecuteChanged();
                OnPropertyChanged();
            }
        }
    }

    public int SelectedAssetFlag
    {
        get => _selectedAssetFlag;
        set
        {
            if (_selectedAssetFlag != value)
            {
                _selectedAssetFlag = value;
                _flagDirty = true;
                IsFlagMixed = false;
                SaveTagsCommand.RaiseCanExecuteChanged();
                ApplyBatchEditCommand.RaiseCanExecuteChanged();
                OnPropertyChanged();
            }
        }
    }

    public string? SelectedAssetCopyright
    {
        get => _selectedAssetCopyright;
        set
        {
            if (_selectedAssetCopyright != value)
            {
                _selectedAssetCopyright = value;
                _copyrightDirty = true;
                IsCopyrightMixed = false;
                SaveTagsCommand.RaiseCanExecuteChanged();
                ApplyBatchEditCommand.RaiseCanExecuteChanged();
                OnPropertyChanged();
            }
        }
    }

    public string? SelectedAssetGpsLatitude
    {
        get => _selectedAssetGpsLatitude;
        set
        {
            if (_selectedAssetGpsLatitude != value)
            {
                _selectedAssetGpsLatitude = value;
                _gpsDirty = true;
                IsGpsMixed = false;
                SaveTagsCommand.RaiseCanExecuteChanged();
                ApplyBatchEditCommand.RaiseCanExecuteChanged();
                OnPropertyChanged();
            }
        }
    }

    public string? SelectedAssetGpsLongitude
    {
        get => _selectedAssetGpsLongitude;
        set
        {
            if (_selectedAssetGpsLongitude != value)
            {
                _selectedAssetGpsLongitude = value;
                _gpsDirty = true;
                IsGpsMixed = false;
                SaveTagsCommand.RaiseCanExecuteChanged();
                ApplyBatchEditCommand.RaiseCanExecuteChanged();
                OnPropertyChanged();
            }
        }
    }

    public ObservableCollection<string> SelectedAssetCategories
    {
        get => _selectedAssetCategories;
        set
        {
            if (_selectedAssetCategories != null)
                _selectedAssetCategories.CollectionChanged -= OnCategoriesDirty;
            _selectedAssetCategories = value ?? [];
            _selectedAssetCategories.CollectionChanged += OnCategoriesDirty;
            OnPropertyChanged();
        }
    }

    public IEnumerable<string>? CategoryAutoCompleteSource
    {
        get => _categoryAutoCompleteSource;
        set { _categoryAutoCompleteSource = value; OnPropertyChanged(); }
    }

    public bool IsPropertyInspectorLoading
    {
        get => _isPropertyInspectorLoading;
        set { _isPropertyInspectorLoading = value; OnPropertyChanged(); }
    }

    public async Task LoadSelectedAssetMetadataAsync()
    {
        CancellationToken ct;
        var thisCts = new CancellationTokenSource();
        var oldCts = Interlocked.Exchange(ref _metadataCts, thisCts);
        oldCts?.Cancel();
        oldCts?.Dispose();
        ct = thisCts.Token;

        var currentSelectedAsset = _selectedAsset;

        await _dispatcher.InvokeAsync(() => IsPropertyInspectorLoading = true);
        ct.ThrowIfCancellationRequested();

        try
        {
            if (currentSelectedAsset == null)
            {
                await _dispatcher.InvokeAsync(() =>
                {
                    SelectedAssetMetadata = [];
                });
                return;
            }

            var entries = new List<MetadataEntry>();
            entries.Add(new MetadataEntry { Label = "File name:", Value = currentSelectedAsset.FileName });
            entries.Add(new MetadataEntry { Label = "Title:", Value = currentSelectedAsset.Title });

            if (_modeManager.IsStandalone)
            {
                try
                {
                    await using var db = await _modeManager.CreateDbContextAsync(ct).ConfigureAwait(false);
                    ct.ThrowIfCancellationRequested();

                    var asset = await db.DigitalAssets
                        .Include(a => a.Keywords)
                        .Include(a => a.Categories)
                        .Include(a => a.MetadataProfile)
                        .FirstOrDefaultAsync(a => a.Id == currentSelectedAsset.Id, ct).ConfigureAwait(false);
                    ct.ThrowIfCancellationRequested();

                    if (asset != null)
                    {
                        AddIfValue(entries, "Type:", asset.MimeType);
                        AddIfValue(entries, "Dimensions:", asset.Width.HasValue && asset.Height.HasValue
                            ? $"{asset.Width} x {asset.Height}" : null);
                        AddIfValue(entries, "Size:", FormatFileSize(asset.FileSize));
                        AddIfValue(entries, "Duration:", asset.Duration.HasValue
                            ? $"{asset.Duration.Value:F2} s" : null);
                        AddIfValue(entries, "Date added:", asset.CreatedAt.ToLocalTime().ToString("g"));
                        AddIfValue(entries, "Date modified:", asset.ModifiedAt.ToLocalTime().ToString("g"));
                        AddIfValue(entries, "Version:", asset.Version.ToString());
                        AddIfValue(entries, "Checksum (SHA-256):", asset.ChecksumSha256);
                        AddIfValue(entries, "Storage path:", asset.StoragePath);

                        var tagNames = asset.Keywords.Select(k => k.Name).ToList();
                        var categoryNames = asset.Categories.Select(c => c.Name).ToList();
                        await _dispatcher.InvokeAsync(() =>
                        {
                            SelectedAssetTags = new ObservableCollection<string>(tagNames);
                            _tagsDirty = false;

                            SelectedAssetCategories = new ObservableCollection<string>(categoryNames);
                            _categoriesDirty = false;

                            SelectedAssetDescription = asset.Description;
                            _descriptionDirty = false;

                            SelectedAssetDateTaken = asset.MetadataProfile?.DateTaken.HasValue == true
                                ? new DateTimeOffset(asset.MetadataProfile.DateTaken.Value)
                                : null;
                            _dateTakenDirty = false;

                            SelectedAssetRating = asset.Rating;
                            _ratingDirty = false;
                            SelectedAssetLabel = (int)asset.Label;
                            _labelDirty = false;
                            SelectedAssetFlag = (int)asset.Flag;
                            _flagDirty = false;
                            SelectedAssetCopyright = asset.Copyright;
                            _copyrightDirty = false;
                            SelectedAssetGpsLatitude = asset.GpsLatitude?.ToString();
                            SelectedAssetGpsLongitude = asset.GpsLongitude?.ToString();
                            _gpsDirty = false;

                            SaveTagsCommand.RaiseCanExecuteChanged();
                            ApplyBatchEditCommand.RaiseCanExecuteChanged();
                        });

                        try
                        {
                            var catNames = await db.Categories
                                .Select(c => c.Name)
                                .Distinct()
                                .OrderBy(n => n)
                                .ToListAsync(ct).ConfigureAwait(false);
                            ct.ThrowIfCancellationRequested();
                            await _dispatcher.InvokeAsync(() => CategoryAutoCompleteSource = catNames);
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex) { _logger.LogWarning(ex, "Failed to load category autocomplete source"); }

                        if (asset.MetadataProfile != null)
                        {
                            var p = asset.MetadataProfile;
                            AddIfValue(entries, "Camera make:", p.CameraMake);
                            AddIfValue(entries, "Camera model:", p.CameraModel);
                            AddIfValue(entries, "Lens model:", p.LensModel);
                            AddIfValue(entries, "Focal length:", p.FocalLength.HasValue ? $"{p.FocalLength.Value:F1} mm" : null);
                            AddIfValue(entries, "Aperture:", p.Aperture.HasValue ? $"f/{p.Aperture.Value:F1}" : null);
                            AddIfValue(entries, "Exposure time:", p.ExposureTime);
                            AddIfValue(entries, "ISO:", p.Iso?.ToString());
                            AddIfValue(entries, "Flash:", p.Flash.HasValue ? (p.Flash.Value ? "Yes" : "No") : null);
                            AddIfValue(entries, "Orientation:", p.Orientation);
                            AddIfValue(entries, "Date taken:", p.DateTaken?.ToString("g"));
                            AddIfValue(entries, "GPS latitude:", p.GpsLatitude?.ToString("F6"));
                            AddIfValue(entries, "GPS longitude:", p.GpsLongitude?.ToString("F6"));
                            AddIfValue(entries, "GPS altitude:", p.GpsAltitude?.ToString("F1"));
                            AddIfValue(entries, "Rating:", p.Rating?.ToString());
                            AddIfValue(entries, "Creator:", p.Creator);
                            AddIfValue(entries, "Copyright:", p.Copyright);
                            AddIfValue(entries, "Usage terms:", p.UsageTerms);
                            AddIfValue(entries, "Contact info:", p.ContactInfo);
                            AddIfValue(entries, "City:", p.City);
                            AddIfValue(entries, "State:", p.State);
                            AddIfValue(entries, "Country:", p.Country);
                            AddIfValue(entries, "Headline:", p.Headline);
                        }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to load metadata for selected asset"); }
            }

            ct.ThrowIfCancellationRequested();
            await _dispatcher.InvokeAsync(() =>
            {
                ct.ThrowIfCancellationRequested();
                SelectedAssetMetadata = new ObservableCollection<MetadataEntry>(entries);
            });
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (Interlocked.CompareExchange(ref _metadataCts, null, thisCts) == thisCts)
            {
                thisCts.Dispose();
                await _dispatcher.InvokeAsync(() => IsPropertyInspectorLoading = false);
            }
        }
    }

    private static void AddIfValue(List<MetadataEntry> entries, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            entries.Add(new MetadataEntry { Label = label, Value = value });
    }

    private static string FormatFileSize(long bytes)
    {
        const long kb = 1024;
        const long mb = kb * 1024;
        const long gb = mb * 1024;
        return bytes >= gb ? $"{bytes / (double)gb:F2} GB"
            : bytes >= mb ? $"{bytes / (double)mb:F2} MB"
            : bytes >= kb ? $"{bytes / (double)kb:F2} KB"
            : $"{bytes} B";
    }

    public async Task LoadTagAutoCompleteSourceAsync()
    {
        try
        {
            if (_modeManager.IsStandalone)
            {
                await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);
                var names = await db.Keywords
                    .Select(k => k.Name)
                    .Distinct()
                    .OrderBy(n => n)
                    .ToListAsync().ConfigureAwait(false);
                await _dispatcher.InvokeAsync(() => TagAutoCompleteSource = names);
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to load tag autocomplete source"); }
    }

    private async Task ComputeAggregatedTagsAsync()
    {
        if (_selectedAssets.Count <= 1 || !_modeManager.IsStandalone)
        {
            await _dispatcher.InvokeAsync(() => AggregatedTags = null);
            return;
        }

        try
        {
            List<(string name, OccurrenceLevel level)> aggregated;
            await using (var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false))
            {
                var assetIds = _selectedAssets.Select(a => a.Id).ToList();
                var total = assetIds.Count;
                var assets = await db.DigitalAssets.Include(a => a.Keywords).Where(a => assetIds.Contains(a.Id)).ToListAsync().ConfigureAwait(false);

                var occurrenceCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var asset in assets)
                    foreach (var kw in asset.Keywords)
                        occurrenceCounts[kw.Name] = occurrenceCounts.GetValueOrDefault(kw.Name) + 1;

                aggregated = occurrenceCounts.OrderBy(kv => kv.Key).Select(kvp =>
                {
                    var level = kvp.Value == total ? OccurrenceLevel.All : kvp.Value == 1 ? OccurrenceLevel.One : OccurrenceLevel.Some;
                    return (kvp.Key, level);
                }).ToList();
            }

            var tags = new ObservableCollection<TagOccurrence>();
            foreach (var (name, level) in aggregated)
                tags.Add(new TagOccurrence { Name = name, Level = level });

            await _dispatcher.InvokeAsync(() => AggregatedTags = tags);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to compute aggregated tags"); }
    }

    private async Task ComputeAggregatedCategoriesAsync()
    {
        if (_selectedAssets.Count <= 1 || !_modeManager.IsStandalone)
        {
            await _dispatcher.InvokeAsync(() => AggregatedCategories = null);
            return;
        }

        try
        {
            List<(string name, OccurrenceLevel level)> aggregated;
            await using (var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false))
            {
                var assetIds = _selectedAssets.Select(a => a.Id).ToList();
                var total = assetIds.Count;
                var assets = await db.DigitalAssets.Include(a => a.Categories).Where(a => assetIds.Contains(a.Id)).ToListAsync().ConfigureAwait(false);

                var occurrenceCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var asset in assets)
                    foreach (var cat in asset.Categories)
                        occurrenceCounts[cat.Name] = occurrenceCounts.GetValueOrDefault(cat.Name) + 1;

                aggregated = occurrenceCounts.OrderBy(kv => kv.Key).Select(kvp =>
                {
                    var level = kvp.Value == total ? OccurrenceLevel.All : kvp.Value == 1 ? OccurrenceLevel.One : OccurrenceLevel.Some;
                    return (kvp.Key, level);
                }).ToList();
            }

            var cats = new ObservableCollection<TagOccurrence>();
            foreach (var (name, level) in aggregated)
                cats.Add(new TagOccurrence { Name = name, Level = level });

            await _dispatcher.InvokeAsync(() => AggregatedCategories = cats);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to compute aggregated categories"); }
    }

    public async Task AutoSaveTagsAsync()
    {
        if (!AnyDirty || _selectedAsset == null || !_modeManager.IsStandalone)
            return;

        try
        {
            await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);
            var asset = await db.DigitalAssets
                .Include(a => a.Keywords)
                .Include(a => a.Categories)
                .Include(a => a.MetadataProfile)
                .FirstOrDefaultAsync(a => a.Id == _selectedAsset.Id).ConfigureAwait(false);

            if (asset == null) return;

            if (_descriptionDirty) asset.Description = SelectedAssetDescription;
            if (_tagsDirty)
            {
                asset.Keywords.Clear();
                var tagNames = _selectedAssetTags.ToArray();
                if (tagNames.Length > 0) await new KeywordService(db).AssociateKeywordsAsync(asset, tagNames).ConfigureAwait(false);
            }
            if (_categoriesDirty)
            {
                asset.Categories.Clear();
                var categoryNames = _selectedAssetCategories.ToArray();
                if (categoryNames.Length > 0) await new CategoryService(db).AssociateCategoriesAsync(asset, categoryNames).ConfigureAwait(false);
            }
            if (_dateTakenDirty)
            {
                if (asset.MetadataProfile == null) asset.MetadataProfile = new MetadataProfile { Id = Guid.NewGuid(), DigitalAssetId = asset.Id };
                asset.MetadataProfile.DateTaken = SelectedAssetDateTaken?.DateTime;
            }
            if (_ratingDirty) asset.Rating = SelectedAssetRating;
            if (_labelDirty) asset.Label = (AssetLabel)SelectedAssetLabel;
            if (_flagDirty) asset.Flag = (AssetFlag)SelectedAssetFlag;
            if (_copyrightDirty) asset.Copyright = SelectedAssetCopyright;
            if (_gpsDirty)
            {
                asset.GpsLatitude = double.TryParse(SelectedAssetGpsLatitude, out var lat) ? lat : null;
                asset.GpsLongitude = double.TryParse(SelectedAssetGpsLongitude, out var lon) ? lon : null;
            }

            await db.SaveChangesAsync().ConfigureAwait(false);

            await _dispatcher.InvokeAsync(() =>
            {
                _tagsDirty = false;
                _descriptionDirty = false;
                _categoriesDirty = false;
                _dateTakenDirty = false;
                _ratingDirty = false;
                _labelDirty = false;
                _flagDirty = false;
                _copyrightDirty = false;
                _gpsDirty = false;
                SaveTagsCommand.RaiseCanExecuteChanged();
                ApplyBatchEditCommand.RaiseCanExecuteChanged();
            });

            _logger.LogInformation("Auto-saved tags/metadata for asset {AssetId}", asset.Id);

            // Write metadata back to source file (best-effort; failures don't fail the save)
            try
            {
                var writePath = asset.StoragePath;
                if (_writeback.IsRawFile(writePath) || _writeback.IsOfficeDocument(writePath))
                    await _writeback.WriteSidecarXmpAsync(writePath, asset).ConfigureAwait(false);
                else if (_writeback.SupportsEmbeddedMetadata(writePath))
                    await _writeback.WriteMetadataAsync(writePath, asset).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Metadata write-back failed for asset {AssetId}", asset.Id);
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to auto-save metadata"); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
