using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Adam.CatalogBrowser.Controls;
using Adam.CatalogBrowser.Models;
using Adam.CatalogBrowser.Services;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;

namespace Adam.CatalogBrowser.ViewModels;

public class MetadataEntry
{
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly ModeManager _modeManager;
    private object? _currentView;
    private string _statusText = "Ready";
    private bool _isBusy;
    private bool _isInitialLoading;
    private AssetListItem? _selectedAsset;
    private readonly List<AssetListItem> _selectedAssets = [];
    private bool _isPropertyInspectorLoading;
    private ObservableCollection<MetadataEntry> _selectedAssetMetadata = [];
    private ObservableCollection<TagOccurrence>? _aggregatedTags;
    private ObservableCollection<TagOccurrence>? _aggregatedCategories;
    private List<string>? _tagAutoCompleteSource;
    private readonly ObservableCollection<string> _selectedAssetTags = [];
    private bool _tagsDirty;

    // Editable fields in the property inspector panel
    private string? _selectedAssetDescription;
    private bool _descriptionDirty;
    private ObservableCollection<string> _selectedAssetCategories = [];
    private bool _categoriesDirty;
    private IEnumerable<string>? _categoryAutoCompleteSource;
    private bool _showSaveToast;
    private string _saveToastText = "Changes saved";

    public MainWindowViewModel(ILogger<MainWindowViewModel> logger, ModeManager modeManager, SidebarViewModel sidebar, AssetGalleryViewModel assetGallery, AdminPanelViewModel adminPanel, IngestionViewModel ingestion, MetadataEditorViewModel metadataEditor, UserManagementViewModel userManagement, AuditLogViewModel auditLog, MigrationWizardViewModel migrationWizard)
    {
        _logger = logger;
        _modeManager = modeManager;
        ModeManager = modeManager;
        Sidebar = sidebar;
        AssetGallery = assetGallery;
        AdminPanel = adminPanel;
        Ingestion = ingestion;
        MetadataEditor = metadataEditor;
        UserManagement = userManagement;
        AuditLog = auditLog;
        MigrationWizard = migrationWizard;
        _currentView = assetGallery;

        SaveTagsCommand = new RelayCommand(
            async _ => await AutoSaveTagsAsync(),
            _ => _selectedAsset != null && (_tagsDirty || _descriptionDirty || _categoriesDirty));

        ShowGalleryCommand = new RelayCommand(async _ =>
        {
            try
            {
                await assetGallery.LoadAssetsAsync();
                CurrentView = assetGallery;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load gallery");
            }
        });
        ShowAdminCommand = new RelayCommand(_ => CurrentView = adminPanel);
        ShowIngestionCommand = new RelayCommand(async _ =>
        {
            CurrentView = ingestion;
            await ingestion.LoadIngestedFoldersAsync();
        });
        ShowMetadataEditorCommand = new RelayCommand(async _ =>
        {
            if (_selectedAsset != null)
                await metadataEditor.LoadAssetAsync(_selectedAsset.Id);
            CurrentView = metadataEditor;
        });
        ShowUserManagementCommand = new RelayCommand(_ => CurrentView = userManagement);
        ShowAuditLogCommand = new RelayCommand(_ => CurrentView = auditLog);

        adminPanel.NavigateToMigrationWizard += () => CurrentView = migrationWizard;

        sidebar.FilterChanged += () =>
        {
            var mediaFormat = sidebar.SelectedMediaFormat?.Name ?? "All";
            var folderPath = sidebar.SelectedFolder?.Path;
            var keywordIds = GetDescendantKeywordIds(sidebar.SelectedKeyword);
            var categoryIds = GetDescendantCategoryIds(sidebar.SelectedMetadataCategory);

            // Date taken filter from sidebar tree
            DateTime? dateFrom = null;
            DateTime? dateTo = null;
            var selectedDate = sidebar.SelectedDateTaken;
            if (selectedDate != null && selectedDate.Year.HasValue)
            {
                if (selectedDate.Month.HasValue)
                {
                    // Month-level: filter to that specific month
                    dateFrom = new DateTime(selectedDate.Year.Value, selectedDate.Month.Value, 1);
                    dateTo = dateFrom.Value.AddMonths(1);
                }
                else
                {
                    // Year-level: filter to that full year
                    dateFrom = new DateTime(selectedDate.Year.Value, 1, 1);
                    dateTo = dateFrom.Value.AddYears(1);
                }
            }

            assetGallery.ApplyFilter(mediaFormat, folderPath, keywordIds, categoryIds, dateFrom, dateTo);
        };

        ingestion.IngestionCompleted += () =>
        {
            _ = Task.Run(async () =>
            {
                await Sidebar.LoadAsync();
                await AssetGallery.LoadAssetsAsync();
            });
        };

        // When metadata editor saves changes, refresh the catalog
        metadataEditor.SaveCompleted += () =>
        {
            _ = Task.Run(async () =>
            {
                await Sidebar.LoadAsync();
                await AssetGallery.LoadAssetsAsync();
                // Reload the selected asset's tags in the property inspector
                if (_selectedAsset != null)
                    await LoadSelectedAssetMetadataAsync();
            });
        };

        // Track edits in the property inspector for auto-save + manual save
        _selectedAssetTags.CollectionChanged += (_, _) =>
        {
            _tagsDirty = true;
            SaveTagsCommand.RaiseCanExecuteChanged();
        };
        _selectedAssetCategories.CollectionChanged += (_, _) =>
        {
            _categoriesDirty = true;
            SaveTagsCommand.RaiseCanExecuteChanged();
        };

        assetGallery.SelectionChanged += asset =>
        {
            _ = Task.Run(async () =>
            {
                // Save any pending tag edits before switching assets
                await AutoSaveTagsAsync();

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _selectedAsset = asset;
                    SaveTagsCommand.RaiseCanExecuteChanged();
                });
                await LoadSelectedAssetMetadataAsync();
                await Dispatcher.UIThread.InvokeAsync(() => OnPropertyChanged(nameof(HasSelectedAsset)));
            });
        };

        assetGallery.MultiSelectionChanged += assets =>
        {
            _ = Task.Run(async () =>
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _selectedAssets.Clear();
                    _selectedAssets.AddRange(assets);
                });
                await Dispatcher.UIThread.InvokeAsync(() => OnPropertyChanged(nameof(HasMultiSelection)));
                await ComputeAggregatedTagsAsync();
                await ComputeAggregatedCategoriesAsync();
            });
        };

        IsInitialLoading = true;

        // Run the entire startup pipeline on a background thread so the UI
        // thread stays free to paint the window, handle input, and show the
        // loading overlay.  Each sub-method already dispatches UI updates
        // (e.g., collection changes, IsLoading flags) back to the UI thread.
        _ = Task.Run(async () =>
        {
            try
            {
                _logger.LogInformation("[Startup] Initializing database...");
                await modeManager.InitializeAsync();
                _logger.LogInformation("[Startup] Database initialized");

                _logger.LogInformation("[Startup] Beginning Sidebar.LoadAsync()...");
                await Sidebar.LoadAsync();
                _logger.LogInformation("[Startup] Sidebar.LoadAsync() completed");

                _logger.LogInformation("[Startup] Beginning AssetGallery.LoadAssetsAsync()...");
                await AssetGallery.LoadAssetsAsync();
                _logger.LogInformation("[Startup] AssetGallery.LoadAssetsAsync() completed");

                // Pre-load keyword names for tag autocomplete
                await LoadTagAutoCompleteSourceAsync();
                _logger.LogInformation("[Startup] TagAutoCompleteSource loaded with {Count} entries", _tagAutoCompleteSource?.Count ?? 0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Startup] FAILED to load sidebar and gallery on startup. Exception type={ExType}, Message={Message}", ex.GetType().Name, ex.Message);
            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(() => IsInitialLoading = false);
            }
        });
    }

    private static List<Guid> GetDescendantKeywordIds(KeywordNode? node)
    {
        if (node == null || node.KeywordId == Guid.Empty) return [];
        var result = new List<Guid> { node.KeywordId };
        foreach (var child in node.Children)
            result.AddRange(GetDescendantKeywordIds(child));
        return result;
    }

    private static List<Guid> GetDescendantCategoryIds(CategoryNode? node)
    {
        if (node == null || node.CategoryId == Guid.Empty) return [];
        var result = new List<Guid> { node.CategoryId };
        foreach (var child in node.Children)
            result.AddRange(GetDescendantCategoryIds(child));
        return result;
    }

    public ModeManager ModeManager { get; }
    public SidebarViewModel Sidebar { get; }
    public AssetGalleryViewModel AssetGallery { get; }
    public AdminPanelViewModel AdminPanel { get; }
    public IngestionViewModel Ingestion { get; }
    public MetadataEditorViewModel MetadataEditor { get; }
    public UserManagementViewModel UserManagement { get; }
    public AuditLogViewModel AuditLog { get; }
    public MigrationWizardViewModel MigrationWizard { get; }

    public AssetListItem? SelectedAsset => _selectedAsset;

    public ObservableCollection<MetadataEntry> SelectedAssetMetadata
    {
        get => _selectedAssetMetadata;
        set { _selectedAssetMetadata = value; OnPropertyChanged(); }
    }

    public bool HasSelectedAsset => _selectedAsset != null;

    /// <summary>
    /// True when exactly one asset is selected (not multi-selection).
    /// </summary>
    public bool HasSingleSelection => !HasMultiSelection && HasSelectedAsset;

    /// <summary>
    /// True when multiple assets are selected in the gallery.
    /// </summary>
    public bool HasMultiSelection => _selectedAssets.Count > 1;

    /// <summary>
    /// Aggregated tag occurrences across all selected assets.
    /// Each <see cref="TagOccurrence"/> carries a level indicating whether the
    /// tag is present in all, some, or exactly one of the selected assets.
    /// </summary>
    public ObservableCollection<TagOccurrence>? AggregatedTags
    {
        get => _aggregatedTags;
        set { _aggregatedTags = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Aggregated category occurrences across all selected assets.
    /// Each <see cref="TagOccurrence"/> carries a level indicating whether the
    /// category is present in all, some, or exactly one of the selected assets.
    /// </summary>
    public ObservableCollection<TagOccurrence>? AggregatedCategories
    {
        get => _aggregatedCategories;
        set { _aggregatedCategories = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// All keywords in the system, used as autocomplete suggestions.
    /// </summary>
    public IEnumerable<string>? TagAutoCompleteSource
    {
        get => _tagAutoCompleteSource;
        set { _tagAutoCompleteSource = value?.ToList(); OnPropertyChanged(); }
    }

    /// <summary>
    /// Tags for the currently selected asset, shown in the property
    /// inspector&#39;s Tags section.  Changes are tracked and auto-saved
    /// when&#160;a different asset is selected.
    /// </summary>
    public ObservableCollection<string> SelectedAssetTags => _selectedAssetTags;

    /// <summary>
    /// Editable description for the currently selected single asset.
    /// </summary>
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

    /// <summary>
    /// Editable categories for the currently selected asset (single-asset mode).
    /// Each string is a category name. Changes are auto-saved on selection switch.
    /// </summary>
    public ObservableCollection<string> SelectedAssetCategories
    {
        get => _selectedAssetCategories;
        set
        {
            if (_selectedAssetCategories != null)
                _selectedAssetCategories.CollectionChanged -= (_, _) =>
                {
                    _categoriesDirty = true;
                    SaveTagsCommand.RaiseCanExecuteChanged();
                };
            _selectedAssetCategories = value ?? [];
            _selectedAssetCategories.CollectionChanged += (_, _) =>
            {
                _categoriesDirty = true;
                SaveTagsCommand.RaiseCanExecuteChanged();
            };
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// All category names in the system, used as autocomplete suggestions
    /// for the category editor.
    /// </summary>
    public IEnumerable<string>? CategoryAutoCompleteSource
    {
        get => _categoryAutoCompleteSource;
        set { _categoryAutoCompleteSource = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Command to manually save pending description/categories/keywords edits.
    /// Enabled only when an asset is selected and at least one field is dirty.
    /// </summary>
    public RelayCommand SaveTagsCommand { get; }

    public ICommand ShowGalleryCommand { get; }
    public ICommand ShowAdminCommand { get; }
    public ICommand ShowIngestionCommand { get; }
    public ICommand ShowMetadataEditorCommand { get; }
    public ICommand ShowUserManagementCommand { get; }
    public ICommand ShowAuditLogCommand { get; }

    public object? CurrentView
    {
        get => _currentView;
        set { _currentView = value; OnPropertyChanged(); }
    }

    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set { _isBusy = value; OnPropertyChanged(); }
    }

    public bool IsInitialLoading
    {
        get => _isInitialLoading;
        set { _isInitialLoading = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// True while the property inspector is loading metadata for the
    /// currently selected asset.
    /// </summary>
    public bool IsPropertyInspectorLoading
    {
        get => _isPropertyInspectorLoading;
        set { _isPropertyInspectorLoading = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// True when the save-toast notification should be visible.
    /// </summary>
    public bool ShowSaveToast
    {
        get => _showSaveToast;
        set { _showSaveToast = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Text displayed in the save-toast notification.
    /// </summary>
    public string SaveToastText
    {
        get => _saveToastText;
        set { _saveToastText = value; OnPropertyChanged(); }
    }

    private async Task LoadSelectedAssetMetadataAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => IsPropertyInspectorLoading = true);
        // Dispatch property-changed notifications to the UI thread so the
        // binding system can update the inspector even though we're running
        // on a background thread (called from Task.Run).
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            OnPropertyChanged(nameof(SelectedAsset));
            OnPropertyChanged(nameof(HasSelectedAsset));
        });

        try
        {
            if (_selectedAsset == null)
            {
                SelectedAssetMetadata = [];
                return;
            }

            var entries = new List<MetadataEntry>();

            // File info
            entries.Add(new MetadataEntry { Label = "File name:", Value = _selectedAsset.FileName });
            entries.Add(new MetadataEntry { Label = "Title:", Value = _selectedAsset.Title });

            // Load full asset + profile from DB for remaining fields
            if (_modeManager.IsStandalone)
            {
                try
                {
                    await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);
                    var asset = await db.DigitalAssets
                        .Include(a => a.Keywords)
                        .Include(a => a.Categories)
                        .Include(a => a.MetadataProfile)
                        .FirstOrDefaultAsync(a => a.Id == _selectedAsset.Id).ConfigureAwait(false);

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

                        // Populate the editable tag collection for the property inspector
                        var tagNames = asset.Keywords.Select(k => k.Name).ToList();
                        var categoryNames = asset.Categories.Select(c => c.Name).ToList();
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            _selectedAssetTags.Clear();
                            foreach (var name in tagNames)
                                _selectedAssetTags.Add(name);
                            _tagsDirty = false;

                            _selectedAssetCategories.Clear();
                            foreach (var name in categoryNames)
                                _selectedAssetCategories.Add(name);
                            _categoriesDirty = false;

                            SelectedAssetDescription = asset.Description;
                            _descriptionDirty = false;

                            SaveTagsCommand.RaiseCanExecuteChanged();
                        });

                        // Load category names for autocomplete
                        try
                        {
                            var catNames = await db.Categories
                                .Select(c => c.Name)
                                .Distinct()
                                .OrderBy(n => n)
                                .ToListAsync().ConfigureAwait(false);
                            await Dispatcher.UIThread.InvokeAsync(() => CategoryAutoCompleteSource = catNames);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to load category autocomplete source");
                        }

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
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load metadata for selected asset");
                }
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                SelectedAssetMetadata = new ObservableCollection<MetadataEntry>(entries);
            });
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsPropertyInspectorLoading = false);
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

    // ──────────────────────────────────────────────
    //  Multi-asset tag aggregation
    // ──────────────────────────────────────────────

    /// <summary>
    /// Loads all keyword names from the database for autocomplete suggestions.
    /// </summary>
    private async Task LoadTagAutoCompleteSourceAsync()
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
                await Dispatcher.UIThread.InvokeAsync(() => TagAutoCompleteSource = names);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load tag autocomplete source");
        }
    }

    /// <summary>
    /// Computes aggregated <see cref="TagOccurrence"/> levels across all
    /// currently selected assets. Tags present in all assets get
    /// <see cref="OccurrenceLevel.All"/> (green), some get
    /// <see cref="OccurrenceLevel.Some"/> (orange), and tags in exactly
    /// one asset get <see cref="OccurrenceLevel.One"/> (red).
    /// </summary>
    private async Task ComputeAggregatedTagsAsync()
    {
        if (_selectedAssets.Count <= 1 || !_modeManager.IsStandalone)
        {
            await Dispatcher.UIThread.InvokeAsync(() => AggregatedTags = null);
            return;
        }

        try
        {
            List<(string name, OccurrenceLevel level)> aggregated;

            await using (var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false))
            {
                var assetIds = _selectedAssets.Select(a => a.Id).ToList();
                var total = assetIds.Count;

                var assets = await db.DigitalAssets
                    .Include(a => a.Keywords)
                    .Where(a => assetIds.Contains(a.Id))
                    .ToListAsync().ConfigureAwait(false);

                // Count occurrences of each keyword name across all selected assets
                var occurrenceCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var asset in assets)
                {
                    foreach (var kw in asset.Keywords)
                    {
                        occurrenceCounts[kw.Name] = occurrenceCounts.GetValueOrDefault(kw.Name) + 1;
                    }
                }

                aggregated = occurrenceCounts
                    .OrderBy(kv => kv.Key)
                    .Select(kvp =>
                    {
                        var level = kvp.Value == total ? OccurrenceLevel.All
                                  : kvp.Value == 1 ? OccurrenceLevel.One
                                  : OccurrenceLevel.Some;
                        return (kvp.Key, level);
                    })
                    .ToList();

                _logger.LogInformation("[AggregatedTags] Computed {Count} aggregated tags across {AssetCount} assets", aggregated.Count, total);
            }

            // Marshal to UI thread
            var tags = new ObservableCollection<TagOccurrence>();
            foreach (var (name, level) in aggregated)
                tags.Add(new TagOccurrence { Name = name, Level = level });

            await Dispatcher.UIThread.InvokeAsync(() => AggregatedTags = tags);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to compute aggregated tags");
            await Dispatcher.UIThread.InvokeAsync(() => AggregatedTags = null);
        }
    }

    /// <summary>
    /// Computes aggregated <see cref="TagOccurrence"/> levels across all
    /// currently selected assets for categories. Works identically to
    /// <see cref="ComputeAggregatedTagsAsync"/> but operates on category names.
    /// </summary>
    private async Task ComputeAggregatedCategoriesAsync()
    {
        if (_selectedAssets.Count <= 1 || !_modeManager.IsStandalone)
        {
            await Dispatcher.UIThread.InvokeAsync(() => AggregatedCategories = null);
            return;
        }

        try
        {
            List<(string name, OccurrenceLevel level)> aggregated;

            await using (var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false))
            {
                var assetIds = _selectedAssets.Select(a => a.Id).ToList();
                var total = assetIds.Count;

                var assets = await db.DigitalAssets
                    .Include(a => a.Categories)
                    .Where(a => assetIds.Contains(a.Id))
                    .ToListAsync().ConfigureAwait(false);

                // Count occurrences of each category name across all selected assets
                var occurrenceCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var asset in assets)
                {
                    foreach (var cat in asset.Categories)
                    {
                        occurrenceCounts[cat.Name] = occurrenceCounts.GetValueOrDefault(cat.Name) + 1;
                    }
                }

                aggregated = occurrenceCounts
                    .OrderBy(kv => kv.Key)
                    .Select(kvp =>
                    {
                        var level = kvp.Value == total ? OccurrenceLevel.All
                                  : kvp.Value == 1 ? OccurrenceLevel.One
                                  : OccurrenceLevel.Some;
                        return (kvp.Key, level);
                    })
                    .ToList();

                _logger.LogInformation("[AggregatedCategories] Computed {Count} aggregated categories across {AssetCount} assets", aggregated.Count, total);
            }

            // Marshal to UI thread
            var cats = new ObservableCollection<TagOccurrence>();
            foreach (var (name, level) in aggregated)
                cats.Add(new TagOccurrence { Name = name, Level = level });

            await Dispatcher.UIThread.InvokeAsync(() => AggregatedCategories = cats);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to compute aggregated categories");
            await Dispatcher.UIThread.InvokeAsync(() => AggregatedCategories = null);
        }
    }

    /// <summary>
    /// Persists any pending tag edits in <see cref="SelectedAssetTags"/> to
    /// the database for the currently selected asset.  Called automatically
    /// before switching to a different asset.
    /// </summary>
    private async Task AutoSaveTagsAsync()
    {
        if ((!_tagsDirty && !_categoriesDirty && !_descriptionDirty) || _selectedAsset == null || !_modeManager.IsStandalone)
            return;

        try
        {
            await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);
            var asset = await db.DigitalAssets
                .Include(a => a.Keywords)
                .Include(a => a.Categories)
                .FirstOrDefaultAsync(a => a.Id == _selectedAsset.Id).ConfigureAwait(false);

            if (asset == null)
                return;

            // Save description if dirty
            if (_descriptionDirty)
            {
                asset.Description = SelectedAssetDescription;
            }

            // Clear and reassociate keywords (only if the tag collection was dirtied)
            if (_tagsDirty)
            {
                asset.Keywords.Clear();
                var tagNames = _selectedAssetTags.ToArray();
                if (tagNames.Length > 0)
                {
                    await db.AssociateKeywordsAsync(asset, tagNames).ConfigureAwait(false);
                }
            }

            // Clear and reassociate categories (only if the categories collection was dirtied)
            if (_categoriesDirty)
            {
                asset.Categories.Clear();
                var categoryNames = _selectedAssetCategories.ToArray();
                if (categoryNames.Length > 0)
                {
                    await db.AssociateCategoriesAsync(asset, categoryNames).ConfigureAwait(false);
                }
            }

            await db.SaveChangesAsync().ConfigureAwait(false);
            _logger.LogInformation("[AutoSaveTags] Saved description/categories/tags for asset {Id}",
                _selectedAsset.Id);
            _tagsDirty = false;
            _categoriesDirty = false;
            _descriptionDirty = false;
            SaveTagsCommand.RaiseCanExecuteChanged();

            // Show the save-toast notification
            await Dispatcher.UIThread.InvokeAsync(() => ShowSaveToast = true);
            _ = Task.Run(async () =>
            {
                await Task.Delay(2500).ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(() => ShowSaveToast = false);
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to auto-save tags for selected asset");
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _execute(parameter);
}
