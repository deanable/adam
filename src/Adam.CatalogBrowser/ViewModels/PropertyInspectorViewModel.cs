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

    private CancellationTokenSource? _metadataCts;

    public PropertyInspectorViewModel(ILogger<PropertyInspectorViewModel> logger, ModeManager modeManager, MetadataWritebackService writeback, IUiDispatcher? dispatcher = null)
    {
        _logger = logger;
        _modeManager = modeManager;
        _writeback = writeback;
        _dispatcher = dispatcher ?? new AvaloniaUiDispatcher();

        SaveTagsCommand = new RelayCommand(
            async _ => await AutoSaveTagsAsync(),
            _ => _selectedAsset != null && CanEdit && (_tagsDirty || _descriptionDirty || _categoriesDirty || _dateTakenDirty || _ratingDirty || _labelDirty || _flagDirty || _copyrightDirty || _gpsDirty));

        SelectedAssetTags = _selectedAssetTags;
        SelectedAssetCategories = _selectedAssetCategories;
    }

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
        _ = ComputeAggregatedTagsAsync();
        _ = ComputeAggregatedCategoriesAsync();
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
    }

    private void OnCategoriesDirty(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        _categoriesDirty = true;
        SaveTagsCommand.RaiseCanExecuteChanged();
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
                SaveTagsCommand.RaiseCanExecuteChanged();
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
                SaveTagsCommand.RaiseCanExecuteChanged();
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
                SaveTagsCommand.RaiseCanExecuteChanged();
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
                SaveTagsCommand.RaiseCanExecuteChanged();
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
                SaveTagsCommand.RaiseCanExecuteChanged();
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
                SaveTagsCommand.RaiseCanExecuteChanged();
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
                SaveTagsCommand.RaiseCanExecuteChanged();
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
                SaveTagsCommand.RaiseCanExecuteChanged();
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
        if ((!_tagsDirty && !_categoriesDirty && !_descriptionDirty && !_dateTakenDirty && !_ratingDirty && !_labelDirty && !_flagDirty && !_copyrightDirty && !_gpsDirty) || _selectedAsset == null || !_modeManager.IsStandalone)
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
            });

            _logger.LogInformation("Auto-saved tags/metadata for asset {AssetId}", asset.Id);

            // Write metadata back to source file (best-effort; failures don't fail the save)
            try
            {
                var filePath = asset.StoragePath;
                if (_writeback.IsRawFile(filePath))
                    await _writeback.WriteSidecarXmpAsync(filePath, asset).ConfigureAwait(false);
                else if (_writeback.SupportsEmbeddedMetadata(filePath))
                    await _writeback.WriteMetadataAsync(filePath, asset).ConfigureAwait(false);
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
