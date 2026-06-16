using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Adam.CatalogBrowser.Controls;
using Adam.CatalogBrowser.Models;
using Adam.CatalogBrowser.Services;
using Adam.Shared.Contracts;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Adam.Shared.Services;
using Avalonia.Threading;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;

namespace Adam.CatalogBrowser.ViewModels;

public class AssetGalleryViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ModeManager _modeManager;
    private readonly ThumbnailService _thumbnailService = new();
    private readonly ILogger<AssetGalleryViewModel> _logger;
    private readonly ThumbnailCache _thumbnailCache = new();
    private readonly IFtsService? _ftsService;
    private CancellationTokenSource? _backfillCts;
    private CancellationTokenSource? _searchCts;

    // T21.1: Viewport tracking for visibility-based thumbnail loading
    private double _viewportTop;
    private double _viewportHeight;
    private const double ViewportOverscanPercent = 0.5; // Load thumbnails 50% beyond viewport
    private CancellationTokenSource? _thumbnailBatchCts;
    private int _thumbnailSize = 150;
    private string _viewMode = "Grid";
    private string _sortBy = "File Name";
    private string _statusText = string.Empty;
    private string _searchText = string.Empty;
    private bool _isSearchActive;
    private AssetListItem? _selectedAsset;
    private readonly ObservableCollection<AssetListItem> _selectedAssets = [];
    private bool _hasAssets;
    private bool _isLoading;
    private int _page;
    private int _pageSize = 50;
    private int _totalCount;
    private bool _hasMore = true;
    private bool _isLoadingMore;
    private string? _activeCategory;
    private string? _activeFolderPath;
    private List<Guid> _activeKeywordIds = [];
    private List<Guid> _activeCategoryIds = [];
    private DateTime? _filterDateFrom;
    private DateTime? _filterDateTo;
    private int _activeRatingFilter;
    private int _activeLabelFilter;
    private int _activeFlagFilter;

    // T21.4: Keyset pagination tracking — stores the last item's sort values
    // for seek-based pagination on "load more" calls.
    private string? _lastSeekFileName;
    private DateTimeOffset _lastSeekCreatedAt;
    private DateTimeOffset _lastSeekModifiedAt;
    private string? _lastSeekMimeType;
    private long _lastSeekFileSize;
    private Guid _lastSeekId;
    public ICommand SelectAllCommand { get; }
    public ICommand ClearSearchCommand { get; }
    public ICommand ClearSelectionCommand { get; }

    public AssetGalleryViewModel(ModeManager modeManager, ILogger<AssetGalleryViewModel> logger, IFtsService? ftsService = null)
    {
        _modeManager = modeManager;
        _logger = logger;
        _ftsService = ftsService;

        SelectAllCommand = new RelayCommand(_ => SelectAll());
        ClearSearchCommand = new RelayCommand(_ => ClearSearch());
        ClearSelectionCommand = new RelayCommand(_ => ClearSelection());
        ToggleSearchModeCommand = new RelayCommand(_ => IsSemanticSearch = !IsSemanticSearch);

        // T12.1: Wire shared in-memory thumbnail cache
        AssetListItem.SharedThumbnailCache = _thumbnailCache;
    }

    public ObservableCollection<AssetListItem> Assets { get; } = [];
    public ObservableCollection<CollectionNode> Collections { get; } = [];
    public ObservableCollection<string> ViewOptions { get; } = new() { "Grid", "List" };
    public ObservableCollection<string> SortOptions { get; } = new() { "File Name", "Date Added", "File Type", "File Size" };

    public string ViewMode
    {
        get => _viewMode;
        set
        {
            if (_viewMode != value)
            {
                _viewMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsGridView));
                OnPropertyChanged(nameof(IsListView));
            }
        }
    }

    public bool IsGridView => _viewMode == "Grid";
    public bool IsListView => _viewMode == "List";

    public int ThumbnailSize
    {
        get => _thumbnailSize;
        set { _thumbnailSize = value; OnPropertyChanged(); OnPropertyChanged(nameof(ThumbnailSizeText)); }
    }

    public string ThumbnailSizeText => $"{_thumbnailSize}px";

    public string SortBy
    {
        get => _sortBy;
        set
        {
            if (_sortBy != value)
            {
                _sortBy = value;
                OnPropertyChanged();
                _logger.LogInformation("[SortBy] Changed to '{SortBy}', reloading assets...", _sortBy);
                _ = ReloadAssetsSafelyAsync();
            }
        }
    }

    private async Task ReloadAssetsSafelyAsync()
    {
        try
        {
            await LoadAssetsAsync();
            _logger.LogInformation("[SortBy] Reload completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SortBy] FAILED to reload assets: {Message}", ex.Message);
        }
    }

    // ──────────────────────────────────────────────
    //  T11.9-T11.11: Full-text search
    // ──────────────────────────────────────────────

    /// <summary>
    /// Current search query text. Triggers debounced FTS/semantic search on change.
    /// </summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText != value)
            {
                _searchText = value;
                OnPropertyChanged();
                _ = OnSearchTextChangedAsync();
            }
        }
    }

    /// <summary>
    /// True when the gallery is displaying search results (T11.9).
    /// </summary>
    public bool IsSearchActive
    {
        get => _isSearchActive;
        set { _isSearchActive = value; OnPropertyChanged(); }
    }

    // ── Phase 19: Semantic search mode toggle ────────────────

    private bool _isSemanticSearch;

    /// <summary>
    /// True when semantic (natural language) search is active instead of FTS.
    /// When toggled, changes the search icon and placeholder text.
    /// </summary>
    public bool IsSemanticSearch
    {
        get => _isSemanticSearch;
        set
        {
            if (_isSemanticSearch == value) return;
            _isSemanticSearch = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SearchModeIcon));
            OnPropertyChanged(nameof(SearchModeTooltip));
            OnPropertyChanged(nameof(SearchPlaceholderText));

            // If there's an active search, re-execute in the new mode
            if (_isSearchActive && !string.IsNullOrWhiteSpace(_searchText))
            {
                _ = OnSearchTextChangedAsync();
            }
        }
    }

    /// <summary>
    /// Display icon for the current search mode: 🔍 (FTS) or ✦ (semantic).
    /// </summary>
    public string SearchModeIcon => _isSemanticSearch ? "✦" : "🔍";

    /// <summary>
    /// Tooltip text for the search mode toggle button.
    /// </summary>
    public string SearchModeTooltip => _isSemanticSearch
        ? "Switch to keyword search (FTS)"
        : "Switch to natural language search (semantic)";

    /// <summary>
    /// Placeholder text for the search bar, changing based on the active search mode.
    /// </summary>
    public string SearchPlaceholderText => _isSemanticSearch
        ? "Describe what you're looking for..."
        : "Search assets...";

    /// <summary>
    /// Autocomplete suggestions from FTS index (T11.11).
    /// Populated asynchronously as the user types.
    /// </summary>
    public ObservableCollection<string> SearchSuggestions { get; } = [];

    /// <summary>
    /// Toggle between FTS (keyword) and semantic (natural language) search mode.
    /// </summary>
    public ICommand ToggleSearchModeCommand { get; }

    /// <summary>
    /// Handles search text changes with 300ms debounce (T11.11).
    /// Fetches autocomplete suggestions immediately, then executes
    /// the full FTS search after the debounce delay.
    /// </summary>
    private async Task OnSearchTextChangedAsync()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;

        var text = _searchText?.Trim() ?? string.Empty;

        // Update autocomplete suggestions (fast, immediate) — skip in semantic mode
        if (!_isSemanticSearch && text.Length >= 2 && _ftsService != null)
        {
            try
            {
                var suggestions = await _ftsService.GetSuggestionsAsync(text, 5, ct);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    SearchSuggestions.Clear();
                    foreach (var s in suggestions)
                        SearchSuggestions.Add(s);
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get search suggestions for '{Text}'", text);
            }
        }
        else
        {
            await Dispatcher.UIThread.InvokeAsync(() => SearchSuggestions.Clear());
        }

        // Debounce: wait 300ms before executing the full search
        try
        {
            await Task.Delay(300, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            ClearSearch();
            return;
        }

        if (_isSemanticSearch)
            await ExecuteSemanticSearchAsync(text, ct);
        else
            await ExecuteSearchAsync(text, ct);
    }

    /// <summary>
    /// Executes an FTS search and populates the gallery with ranked results (T11.9).
    /// Each result includes highlight metadata for T11.10 search highlighting.
    /// </summary>
    private async Task ExecuteSearchAsync(string query, CancellationToken ct)
    {
        if (_ftsService == null)
        {
            await Dispatcher.UIThread.InvokeAsync(() => StatusText = "Full-text search is not available");
            return;
        }

        if (!await _ftsService.IsAvailableAsync(ct).ConfigureAwait(false))
        {
            await Dispatcher.UIThread.InvokeAsync(() => StatusText = "Full-text search index is not ready — try again later");
            return;
        }

        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsLoading = true);
            IsSearchActive = true;

            var results = await _ftsService.SearchAsync(query, 200, ct).ConfigureAwait(false);

            var dbDir = Path.GetDirectoryName(_modeManager.DbPath) ?? ".";
            var thumbnailDir = Path.Combine(dbDir, "thumbnails");

            var newItems = new List<AssetListItem>(results.Count);
            foreach (var result in results)
            {
                var thumbnailPath = _thumbnailService.GetThumbnailPath(
                    result.Asset.StoragePath, thumbnailDir);

                var (colorLabel, colorBrush) = AssetListItem.MapLabelToDisplay(result.Asset.Label);

                var item = new AssetListItem
                {
                    Id = result.Asset.Id,
                    Title = result.Asset.Title,
                    FileName = result.Asset.FileName,
                    StoragePath = result.Asset.StoragePath,
                    FileType = result.Asset.MimeType,
                    FileSize = result.Asset.FileSize,
                    Width = result.Asset.Width,
                    Height = result.Asset.Height,
                    CreatedAt = result.Asset.CreatedAt,
                    ThumbnailPath = thumbnailPath,
                    Rating = result.Asset.Rating,
                    ColorLabel = colorLabel,
                    ColorBrush = colorBrush,
                    IsFlagged = result.Asset.Flag != AssetFlag.Unflagged,
                    // T11.10: Search result highlighting
                    HighlightText = query,
                    MatchedFields = result.MatchedFields
                };

                AddToolbarActions(item);
                newItems.Add(item);
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                SelectedAsset = null;
                _selectedAssets.Clear();

                // T12.7: Dispose items before clearing to free bitmaps
                foreach (var item in Assets)
                    item.Dispose();
                Assets.Clear();

                foreach (var item in newItems)
                    Assets.Add(item);

                HasAssets = Assets.Count > 0;
                HasMore = false;
                StatusText = Assets.Count > 0
                    ? $"Search: {Assets.Count} result(s) for \"{query}\""
                    : $"No results for \"{query}\"";
            });

            // T21.1/T21.2: Load visible thumbnails immediately; batch-load remaining
            LoadVisibleThumbnails(newItems);
            _ = BatchLoadRemainingThumbnailsAsync(newItems);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Search failed for query: {Query}", query);
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsLoading = false);
        }
    }

    /// <summary>
    /// Executes a semantic (natural language) search using text embeddings.
    /// In standalone mode, uses SemanticSearchService directly.
    /// In multi-user mode, sends a broker request via SemanticSearchHandler.
    /// </summary>
    private async Task ExecuteSemanticSearchAsync(string query, CancellationToken ct)
    {
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsLoading = true);
            IsSearchActive = true;

            IReadOnlyList<SemanticSearchResult> results = [];

            if (_modeManager.IsStandalone)
            {
                // Use SemanticSearchService directly (standalone mode)
                var dbDir = Path.GetDirectoryName(_modeManager.DbPath) ?? ".";
                // SemanticSearchService is resolved from DI if available
                var searchService = App.ServiceProvider?.GetService<SemanticSearchService>();
                if (searchService == null)
                {
                    await Dispatcher.UIThread.InvokeAsync(() => StatusText = "Semantic search is not available — AI model not loaded");
                    return;
                }
                results = await searchService.SearchByTextAsync(query, 50, 0.0, ct);
            }
            else if (_modeManager.IsMultiUser)
            {
                var broker = _modeManager.BrokerClient;
                var auth = _modeManager.AuthSession;
                if (broker == null || auth == null) return;

                if (!broker.IsConnected)
                    await broker.ConnectAsync(ct);

                var req = new SemanticSearchRequest
                {
                    Query = query,
                    MaxResults = 50,
                    MinScore = 0.0,
                    RecordHistory = true
                };

                var envelope = new Envelope
                {
                    AuthToken = auth.Token ?? "",
                    CorrelationId = Guid.NewGuid().ToString(),
                    MessageType = MessageTypeCode.SemanticSearchRequest,
                    Payload = ByteString.CopyFrom(ProtoHelper.Serialize(req))
                };

                var response = await broker.SendAsync(envelope, ct);
                if (response.StatusCode != 0)
                {
                    _logger.LogWarning("Semantic search broker rejected: status={StatusCode}", response.StatusCode);
                    await Dispatcher.UIThread.InvokeAsync(() => StatusText = "Semantic search failed");
                    return;
                }

                var data = ProtoHelper.Deserialize<SemanticSearchResponse>(response.Payload.ToByteArray());
                results = data.Results.Select(r => new SemanticSearchResult
                {
                    Asset = new DigitalAsset
                    {
                        Id = Guid.Parse(r.AssetId),
                        Title = r.Title,
                        FileName = r.FileName,
                        MimeType = r.MimeType,
                        FileSize = r.FileSize,
                        CreatedAt = DateTimeOffset.FromUnixTimeSeconds(r.CreatedAt)
                    },
                    Score = r.Score,
                    Rank = r.Rank
                }).ToList();
            }

            var dbDir2 = Path.GetDirectoryName(_modeManager.DbPath) ?? ".";
            var thumbnailDir = Path.Combine(dbDir2, "thumbnails");

            var newItems = new List<AssetListItem>(results.Count);
            foreach (var result in results)
            {
                var thumbnailPath = _thumbnailService.GetThumbnailPath(
                    result.Asset.StoragePath, thumbnailDir);

                var (colorLabel, colorBrush) = AssetListItem.MapLabelToDisplay(result.Asset.Label);

                var item = new AssetListItem
                {
                    Id = result.Asset.Id,
                    Title = result.Asset.Title,
                    FileName = result.Asset.FileName,
                    StoragePath = result.Asset.StoragePath,
                    FileType = result.Asset.MimeType,
                    FileSize = result.Asset.FileSize,
                    CreatedAt = result.Asset.CreatedAt,
                    ThumbnailPath = thumbnailPath,
                    Rating = result.Asset.Rating,
                    ColorLabel = colorLabel,
                    ColorBrush = colorBrush,
                    IsFlagged = result.Asset.Flag != AssetFlag.Unflagged,
                    SearchScore = result.Score
                };

                AddToolbarActions(item);
                newItems.Add(item);
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                SelectedAsset = null;
                _selectedAssets.Clear();

                foreach (var item in Assets)
                    item.Dispose();
                Assets.Clear();

                foreach (var item in newItems)
                    Assets.Add(item);

                HasAssets = Assets.Count > 0;
                HasMore = false;
                StatusText = Assets.Count > 0
                    ? $"Semantic search: {Assets.Count} result(s) for \"{query}\""
                    : $"No semantic results for \"{query}\"";
            });

            // T21.1/T21.2: Load visible thumbnails immediately; batch-load remaining
            LoadVisibleThumbnails(newItems);
            _ = BatchLoadRemainingThumbnailsAsync(newItems);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Semantic search failed for query: {Query}", query);
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsLoading = false);
        }
    }

    /// <summary>
    /// Clears the search query and returns the gallery to normal filtered view (T11.9).
    /// </summary>
    public void ClearSearch()
    {
        _searchCts?.Cancel();
        _searchText = string.Empty;
        _isSearchActive = false;
        SearchSuggestions.Clear();
        OnPropertyChanged(nameof(SearchText));
        OnPropertyChanged(nameof(IsSearchActive));
        _ = LoadAssetsAsync();
    }

    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// All currently selected assets (multi-select).
    /// </summary>
    public ObservableCollection<AssetListItem> SelectedAssets => _selectedAssets;

    /// <summary>
    /// T14.5: Number of currently selected assets. Used by the selection bar UI.
    /// </summary>
    public int SelectedCount => _selectedAssets.Count;

    /// <summary>
    /// T14.5: True when any assets are selected. Used to show/hide the selection bar.
    /// </summary>
    public bool HasSelection => _selectedAssets.Count > 0;

    public AssetListItem? SelectedAsset
    {
        get => _selectedAsset;
        set
        {
            if (_selectedAsset == value)
                return;
            _selectedAsset = value;
            OnPropertyChanged();
            SelectionChanged?.Invoke(value);
        }
    }

    /// <summary>
    /// True while a full reload is in progress (initial load or filter change).
    /// </summary>
    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(); }
    }

    public bool HasAssets
    {
        get => _hasAssets;
        set { _hasAssets = value; OnPropertyChanged(); }
    }

    public bool HasMore
    {
        get => _hasMore;
        set { _hasMore = value; OnPropertyChanged(); }
    }

    public bool IsLoadingMore
    {
        get => _isLoadingMore;
        set { _isLoadingMore = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Fires when the user wants to open an asset in the loupe view (double-click/Enter).
    /// </summary>
    public event Action<AssetListItem>? OpenAssetRequested;

    /// <summary>
    /// Fires when the user wants to compare two assets.
    /// </summary>
    public event Action<AssetListItem, AssetListItem>? CompareAssetsRequested;

    /// <summary>
    /// Called from the View code-behind to request opening an asset in the loupe.
    /// Fires the <see cref="OpenAssetRequested"/> event.
    /// </summary>
    public void RequestOpenAsset(AssetListItem asset) => OpenAssetRequested?.Invoke(asset);

    /// <summary>
    /// Fires when the multi-asset selection changes.
    /// Carries the full list of currently selected assets.
    /// </summary>
    public event Action<IReadOnlyList<AssetListItem>>? MultiSelectionChanged;

    public event Action<AssetListItem?>? SelectionChanged;

    /// <summary>
    /// Called from code-behind when the ListBox selection changes.
    /// Updates <see cref="SelectedAssets"/>, <see cref="SelectedAsset"/> (primary),
    /// and fires <see cref="MultiSelectionChanged"/>.
    /// </summary>
    public void UpdateSelection(IList<object?> selectedItems)
    {
        var items = selectedItems
            .OfType<AssetListItem>()
            .ToList();

        // Update SelectedAssets collection
        _selectedAssets.Clear();
        foreach (var item in items)
            _selectedAssets.Add(item);

        OnPropertyChanged(nameof(SelectedAssets));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelection));

        // Primary selection (first item)
        SelectedAsset = items.Count > 0 ? items[0] : null;

        // Fire multi-selection event
        MultiSelectionChanged?.Invoke(items.AsReadOnly());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// Selects all assets in the current view (T8.21 keyboard shortcut).
    /// </summary>
    private void SelectAll()
    {
        // Select all assets
        var items = Assets.ToList();
        UpdateSelection(items.Cast<object?>().ToList());
    }

    /// <summary>
    /// Clears all selection (T14.5 selection bar).
    /// </summary>
    private void ClearSelection()
    {
        UpdateSelection([]);
    }

    public async Task LoadAssetsAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("[LoadAssetsAsync] Starting - page reset to 0, sort={SortBy}", _sortBy);
        _page = 0;
        _hasMore = true;

        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsLoading = true);

            // Preserve selection across reloads
            var savedSelectedId = _selectedAsset?.Id;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                // Clear selection before clearing the collection to prevent
                // Avalonia's SelectionModel from accessing invalid indices.
                SelectedAsset = null;
                _selectedAssets.Clear();

                // T12.7: Dispose items to free bitmaps before clearing
                foreach (var item in Assets)
                    item.Dispose();
                Assets.Clear();
                _logger.LogInformation("[LoadAssetsAsync] Assets cleared, count={Count}", Assets.Count);
            });

            await LoadPageAsync(ct);

            // Restore selection if the same item still exists in the new results
            if (savedSelectedId.HasValue)
            {
                var match = Assets.FirstOrDefault(a => a.Id == savedSelectedId.Value);
                if (match != null)
                    SelectedAsset = match;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                HasMore = _hasMore;
                HasAssets = Assets.Count > 0;
                StatusText = _totalCount > 0 ? $"{Assets.Count} of {_totalCount} asset(s)" : "0 asset(s)";
                _logger.LogInformation("[LoadAssetsAsync] Completed - loaded {Count} assets, total={Total}, hasMore={HasMore}", Assets.Count, _totalCount, _hasMore);
            });
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsLoading = false);
        }
    }

    // T12.7: Ensure items are disposed when the ViewModel is no longer used.
    // This is called by MainWindowViewModel.CurrentView setter when navigating away.
    public void Dispose()
    {
        ClearThumbnailCache();
        foreach (var item in Assets)
            item.Dispose();
        Assets.Clear();
    }

    public async Task LoadMoreAsync(CancellationToken ct = default)
    {
        if (!_hasMore || _isLoadingMore) return;
        await LoadPageAsync(ct);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            HasMore = _hasMore;
            StatusText = $"{Assets.Count} of {_totalCount} asset(s)";
        });
    }

    private async Task LoadPageAsync(CancellationToken ct = default)
    {
        IsLoadingMore = true;
        _logger.LogInformation("[LoadPageAsync] Starting - page={Page}, pageSize={PageSize}, sort={SortBy}", _page, _pageSize, _sortBy);
        try
        {
            if (_modeManager.IsStandalone)
            {
                await using var db = await _modeManager.CreateDbContextAsync(ct).ConfigureAwait(false);
                _logger.LogInformation("[LoadPageAsync] DbContext created");

                var query = ApplyFilters(db.DigitalAssets.AsQueryable());
                _logger.LogInformation("[LoadPageAsync] Filters applied - activeCategory={Category}, activeFolder={Folder}", _activeCategory, _activeFolderPath);

            if (_page == 0)
                {
                    _totalCount = await query.CountAsync(ct).ConfigureAwait(false);
                    _logger.LogInformation("[LoadPageAsync] Total count={TotalCount}", _totalCount);
                }

                IQueryable<DigitalAsset> orderedQuery;
                orderedQuery = _sortBy switch
                {
                    "Date Added" => query.OrderByDescending(a => a.CreatedAt).ThenBy(a => a.Id),
                    "File Type" => query.OrderBy(a => a.MimeType).ThenBy(a => a.Id),
                    "File Size" => query.OrderBy(a => a.FileSize).ThenBy(a => a.Id),
                    _ => query.OrderBy(a => a.FileName).ThenBy(a => a.Id)
                };
                _logger.LogInformation("[LoadPageAsync] Ordering by {SortBy}", _sortBy);

                // T21.4: Keyset pagination — use WHERE instead of SKIP for "load more"
                // to avoid the OFFSET performance penalty at high page numbers.
                if (_page > 0)
                {
                    orderedQuery = ApplyKeysetPagination(orderedQuery);
                }

                var assets = await orderedQuery
                    .Take(_pageSize)
                    .ToListAsync(ct).ConfigureAwait(false);

                // T21.4: Store last item's values for next keyset seek
                if (assets.Count > 0)
                {
                    var last = assets[^1];
                    _lastSeekFileName = last.FileName;
                    _lastSeekCreatedAt = last.CreatedAt;
                    _lastSeekModifiedAt = last.ModifiedAt;
                    _lastSeekMimeType = last.MimeType;
                    _lastSeekFileSize = last.FileSize;
                    _lastSeekId = last.Id;
                }
                _logger.LogInformation("[LoadPageAsync] Retrieved {Count} assets from database", assets.Count);

                var dbDir = Path.GetDirectoryName(_modeManager.DbPath) ?? ".";
                var thumbnailDir = Path.Combine(dbDir, "thumbnails");
                _logger.LogInformation("Gallery loading: thumbnailDir={ThumbDir}, assetCount={Count}", thumbnailDir, assets.Count);

                var newItems = new List<AssetListItem>(assets.Count);
                foreach (var asset in assets)
                {
                    var thumbnailPath = _thumbnailService.GetThumbnailPath(asset.StoragePath, thumbnailDir);
                    _logger.LogDebug("[Thumbnail] AssetId={AssetId}, StoragePath={StoragePath}, ThumbnailPath={ThumbnailPath}, ThumbnailExists={Exists}", 
                        asset.Id, asset.StoragePath, thumbnailPath, File.Exists(thumbnailPath));

                    // Map label to color display (T8.20) via shared helper (T8.22)
                    var (colorLabel, colorBrush) = AssetListItem.MapLabelToDisplay(asset.Label);

                    var item = new AssetListItem
                    {
                        Id = asset.Id,
                        Title = asset.Title,
                        FileName = asset.FileName,
                        StoragePath = asset.StoragePath,
                        FileType = asset.MimeType,
                        FileSize = asset.FileSize,
                        Width = asset.Width,
                        Height = asset.Height,
                        CreatedAt = asset.CreatedAt,
                        ThumbnailPath = thumbnailPath,
                        Rating = asset.Rating,
                        ColorLabel = colorLabel,
                        ColorBrush = colorBrush,
                        IsFlagged = asset.Flag != AssetFlag.Unflagged
                    };

                    // Quick action toolbar buttons (T8.20)
                    AddToolbarActions(item);

                    newItems.Add(item);
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    foreach (var item in newItems)
                        Assets.Add(item);
                });

                // T21.1/T21.2: Load visible thumbnails immediately; batch-load remaining
                LoadVisibleThumbnails(newItems);
                _ = BatchLoadRemainingThumbnailsAsync(newItems);

                _page++;
                if (assets.Count < _pageSize)
                {
                    _hasMore = false;
                }
                else if (_page > 0)
                {
                    // T21.4: With keyset pagination, we know there are more items
                    // if we got a full page. No need to check count.
                    _hasMore = assets.Count >= _pageSize;
                }

                // T12.2: Backfill thumbnails for assets that don't have them yet
                _ = BackfillMissingThumbnailsAsync(newItems, thumbnailDir, ct);
            }
            else if (_modeManager.IsMultiUser)
            {
                var broker = _modeManager.BrokerClient;
                var auth = _modeManager.AuthSession;
                if (broker == null || auth == null) return;

                if (!broker.IsConnected)
                    await broker.ConnectAsync(ct);

                // Build the request with all active filters
                var listReq = new ListAssetsRequest
                {
                    Page = _page + 1,
                    PageSize = _pageSize,
                    SortBy = _sortBy,
                    SortDir = _sortBy switch
                    {
                        "Date Added" => "desc",
                        _ => "asc"
                    }
                };

                // Type/category filter
                if (!string.IsNullOrEmpty(_activeCategory) && _activeCategory != "All")
                    listReq.Type = _activeCategory;

                foreach (var id in _activeCategoryIds)
                    listReq.CategoryIds.Add(id.ToString());

                // Folder path filter
                if (!string.IsNullOrEmpty(_activeFolderPath))
                    listReq.FolderPath = _activeFolderPath;

                // Keyword filter
                foreach (var id in _activeKeywordIds)
                    listReq.KeywordIds.Add(id.ToString());

                // Date range filter (from DateTaken tree, as Unix timestamps)
                if (_filterDateFrom.HasValue)
                    listReq.FromDate = new DateTimeOffset(_filterDateFrom.Value, TimeSpan.Zero).ToUnixTimeSeconds();
                if (_filterDateTo.HasValue)
                    listReq.ToDate = new DateTimeOffset(_filterDateTo.Value, TimeSpan.Zero).ToUnixTimeSeconds();

                var correlationId = Guid.NewGuid().ToString();
                var request = new Envelope
                {
                    AuthToken = auth.Token ?? "",
                    CorrelationId = correlationId,
                    MessageType = MessageTypeCode.ListAssetsRequest,
                    Payload = ByteString.CopyFrom(ProtoHelper.Serialize(listReq))
                };

                var response = await broker.SendAsync(request, ct);
                if (response.StatusCode != 0) return;

                var listResponse = ProtoHelper.Deserialize<ListAssetsResponse>(response.Payload.ToByteArray());

                if (_page == 0)
                    _totalCount = listResponse.TotalCount;

                var newItems = listResponse.Items.Select(item => new AssetListItem
                {
                    Id = Guid.Parse(item.Id),
                    Title = item.Title,
                    FileName = item.FileName,
                    FileType = item.MimeType,
                    FileSize = item.FileSize,
                    CreatedAt = DateTimeOffset.FromUnixTimeSeconds(item.CreatedAt)
                }).ToList();

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    foreach (var item in newItems)
                        Assets.Add(item);
                });

                _page++;
                if (listResponse.Items.Count < _pageSize)
                    _hasMore = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LoadPageAsync] FAILED: {Message}", ex.Message);
            throw;
        }
        finally
        {
            IsLoadingMore = false;
            _logger.LogInformation("[LoadPageAsync] Finished - IsLoadingMore=false");
        }
    }

    private IQueryable<DigitalAsset> ApplyFilters(IQueryable<DigitalAsset> query)
    {
        if (!string.IsNullOrEmpty(_activeCategory) && _activeCategory != "All")
        {
            var type = _activeCategory switch
            {
                "Images" => AssetType.Image,
                "Videos" => AssetType.Video,
                "Documents" => AssetType.Document,
                "Audio" => AssetType.Audio,
                _ => (AssetType?)null
            };
            if (type.HasValue)
                query = query.Where(a => a.Type == type.Value);
        }

        if (_activeCategoryIds.Count > 0)
        {
            query = query.Where(a => a.Categories.Any(c => _activeCategoryIds.Contains(c.Id)));
        }

        if (!string.IsNullOrEmpty(_activeFolderPath))
        {
            var prefix = _activeFolderPath.Replace('\\', '/');
            if (!prefix.EndsWith("/"))
                prefix += "/";
            query = query.Where(a => a.StoragePath.StartsWith(prefix));
        }

        if (_activeKeywordIds.Count > 0)
        {
            query = query.Where(a => a.Keywords.Any(k => _activeKeywordIds.Contains(k.Id)));
        }

        // Date filter based on MetadataProfile.DateTaken, driven by sidebar tree selection
        if (_filterDateFrom.HasValue)
            query = query.Where(a => a.MetadataProfile != null && a.MetadataProfile.DateTaken >= _filterDateFrom.Value);

        if (_filterDateTo.HasValue)
            query = query.Where(a => a.MetadataProfile != null && a.MetadataProfile.DateTaken < _filterDateTo.Value);

        // T14.5: Advanced filters — rating, label, flag
        if (_activeRatingFilter > 0)
        {
            // Rating filter: 1 = Unrated (rating 0), 2..6 = 1..5 stars
            var targetRating = _activeRatingFilter - 1;
            query = query.Where(a => a.Rating == targetRating);
        }

        if (_activeLabelFilter > 0)
        {
            // Label filter: 1 = None, 2..6 = Red..Purple
            var targetLabel = (AssetLabel)(_activeLabelFilter - 1);
            query = query.Where(a => a.Label == targetLabel);
        }

        if (_activeFlagFilter > 0)
        {
            // Flag filter: 1 = Unflagged, 2 = Pick, 3 = Reject
            var targetFlag = (AssetFlag)(_activeFlagFilter - 1);
            query = query.Where(a => a.Flag == targetFlag);
        }

        return query;
    }

    public void ApplyFilter(string? mediaFormat, string? folderPath, List<Guid>? keywordIds = null, List<Guid>? categoryIds = null, DateTime? dateFrom = null, DateTime? dateTo = null, int ratingFilter = 0, int labelFilter = 0, int flagFilter = 0, string? searchQuery = null)
    {
        _activeCategory = mediaFormat;
        _activeFolderPath = folderPath;
        _activeKeywordIds = keywordIds ?? [];
        _activeCategoryIds = categoryIds ?? [];
        _filterDateFrom = dateFrom;
        _filterDateTo = dateTo;
        _activeRatingFilter = ratingFilter;
        _activeLabelFilter = labelFilter;
        _activeFlagFilter = flagFilter;

        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            // Seamless search execution: skip LoadAssetsAsync() when a search query is
            // provided. Cancel any pending debounce and execute the FTS search immediately
            // rather than going through the debounced SearchText setter, so the user sees
            // results right away instead of waiting 300ms.
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = null;
            _searchText = searchQuery;
            OnPropertyChanged(nameof(SearchText));
            IsSearchActive = true;
            _ = ExecuteSearchAsync(searchQuery, CancellationToken.None);
        }
        else
        {
            _ = LoadAssetsAsync();
        }
    }

    // ──────────────────────────────────────────────
    //  Toolbar quick actions (T8.20)
    // ──────────────────────────────────────────────

    /// <summary>
    /// Cycles the rating of the specified asset (0 → 1 → … → 5 → 0)
    /// and updates the tile's display properties in-place.
    /// </summary>
    private async Task QuickRateAssetAsync(Guid assetId, AssetListItem item)
    {
        try
        {
            await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);
            var dbAsset = await db.DigitalAssets.FirstOrDefaultAsync(a => a.Id == assetId).ConfigureAwait(false);
            if (dbAsset == null) return;

            dbAsset.Rating = (dbAsset.Rating + 1) % 6;
            await db.SaveChangesAsync().ConfigureAwait(false);

            // Update the tile in-place so the UI reflects the new rating immediately
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                item.Rating = dbAsset.Rating;
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Quick-rate failed for asset {Id}", assetId);
        }
    }

    /// <summary>
    /// Cycles the flag of the specified asset (Unflagged → Pick → Reject → Unflagged)
    /// and updates the tile's display properties in-place.
    /// </summary>
    private async Task ToggleFlagAssetAsync(Guid assetId, AssetListItem item)
    {
        try
        {
            await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);
            var dbAsset = await db.DigitalAssets.FirstOrDefaultAsync(a => a.Id == assetId).ConfigureAwait(false);
            if (dbAsset == null) return;

            dbAsset.Flag = dbAsset.Flag switch
            {
                AssetFlag.Unflagged => AssetFlag.Pick,
                AssetFlag.Pick => AssetFlag.Reject,
                AssetFlag.Reject => AssetFlag.Unflagged,
                _ => AssetFlag.Unflagged
            };
            await db.SaveChangesAsync().ConfigureAwait(false);

            // Update the tile in-place so the UI reflects the new flag state immediately
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                item.IsFlagged = dbAsset.Flag != AssetFlag.Unflagged;
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Toggle-flag failed for asset {Id}", assetId);
        }
    }

    public async Task LoadCollectionsAsync(CancellationToken ct = default)
    {
        Collections.Clear();

        if (_modeManager.IsStandalone)
        {                await using var db = await _modeManager.CreateDbContextAsync(ct).ConfigureAwait(false);
            var collections = await db.Collections
                .Include(c => c.Children)
                .ToListAsync(ct).ConfigureAwait(false);

            foreach (var col in collections.Where(c => c.ParentId == null))
                Collections.Add(BuildCollectionNode(col, collections));
        }
        else if (_modeManager.IsMultiUser)
        {
            var broker = _modeManager.BrokerClient;
            var auth = _modeManager.AuthSession;
            if (broker == null || auth == null) return;

            if (!broker.IsConnected)
                await broker.ConnectAsync(ct);

            var correlationId = Guid.NewGuid().ToString();
            var request = new Envelope
            {
                AuthToken = auth.Token ?? "",
                CorrelationId = correlationId,
                MessageType = MessageTypeCode.ListCollectionsRequest,
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new ListCollectionsRequest()))
            };

            var response = await broker.SendAsync(request, ct);
            if (response.StatusCode != 0) return;

            var listResponse = ProtoHelper.Deserialize<ListCollectionsResponse>(response.Payload.ToByteArray());
            foreach (var node in listResponse.Items)
                Collections.Add(MapCollectionNode(node));
        }
    }

    /// <summary>
    /// Adds quick-rate and toggle-flag toolbar buttons to a tile item (T8.20).
    /// Shared between LoadPageAsync and ExecuteSearchAsync to avoid duplication.
    /// </summary>
    private void AddToolbarActions(AssetListItem item)
    {
        var assetId = item.Id;
        item.ToolbarActions =
        [
            new ToolbarAction
            {
                Icon = "\u2605",  // ★
                ToolTipText = "Quick rate (cycle)",
                Command = new RelayCommand(async _ => await QuickRateAssetAsync(assetId, item))
            },
            new ToolbarAction
            {
                Icon = "\u2691",  // ⚑
                ToolTipText = "Toggle flag",
                Command = new RelayCommand(async _ => await ToggleFlagAssetAsync(assetId, item))
            }
        ];
    }

    // ──────────────────────────────────────────────
    //  T21.4: Keyset pagination helper
    // ──────────────────────────────────────────────

    /// <summary>
    /// Applies keyset (seek) pagination to an ordered query by adding a WHERE clause
    /// that picks up after the last item from the previous page.
    /// This avoids the OFFSET performance penalty at high page numbers.
    /// </summary>
    private IQueryable<DigitalAsset> ApplyKeysetPagination(IQueryable<DigitalAsset> query)
    {
        return _sortBy switch
        {
            "Date Added" => query.Where(a =>
                a.CreatedAt < _lastSeekCreatedAt ||
                (a.CreatedAt == _lastSeekCreatedAt && a.Id > _lastSeekId)),
            "File Type" => query.Where(a =>
                a.MimeType.CompareTo(_lastSeekMimeType) > 0 ||
                (a.MimeType == _lastSeekMimeType && a.Id > _lastSeekId)),
            "File Size" => query.Where(a =>
                a.FileSize > _lastSeekFileSize ||
                (a.FileSize == _lastSeekFileSize && a.Id > _lastSeekId)),
            _ => query.Where(a =>
                a.FileName.CompareTo(_lastSeekFileName) > 0 ||
                (a.FileName == _lastSeekFileName && a.Id > _lastSeekId))
        };
    }

    // ──────────────────────────────────────────────
    //  T21.1/T21.2: Viewport-based prioritized thumbnail loading
    // ──────────────────────────────────────────────

    /// <summary>
    /// Updates the current viewport position. Called from the gallery ScrollViewer's
    /// ScrollChanged event handler in code-behind.
    /// </summary>
    public void UpdateViewport(double scrollOffset, double viewportHeight)
    {
        _viewportTop = scrollOffset;
        _viewportHeight = viewportHeight;
    }

    /// <summary>
    /// Calculates which items in the assets list fall within or near the visible viewport
    /// and loads their thumbnails immediately. Items outside the viewport + overscan area
    /// are deferred to <see cref="BatchLoadRemainingThumbnailsAsync"/>.
    /// </summary>
    private void LoadVisibleThumbnails(IReadOnlyList<AssetListItem> items)
    {
        if (items.Count == 0) return;

        // Estimate tile height including margin (~ thumbnail size + 24px for labels)
        var tileHeight = _thumbnailSize + 24;
        // Estimate items per row based on viewport width (use a reasonable default)
        var itemsPerRow = Math.Max(1, (int)(1280.0 / (tileHeight + 8)));
        var visibleStart = Math.Max(0, (int)((_viewportTop - _viewportHeight * ViewportOverscanPercent) / tileHeight) * itemsPerRow);
        var visibleEnd = Math.Min(items.Count, (int)((_viewportTop + _viewportHeight * (1 + ViewportOverscanPercent)) / tileHeight + 1) * itemsPerRow);

        for (int i = 0; i < items.Count; i++)
        {
            if (i >= visibleStart && i < visibleEnd)
            {
                // In or near viewport — load immediately
                _ = items[i].LoadThumbnailAsync((int)ThumbnailSize);
            }
            else
            {
                // Outside viewport — cancel any pending load to avoid wasting work
                items[i].CancelPendingLoad();
            }
        }
    }

    /// <summary>
    /// Loads thumbnails for items not yet loaded, processing them in small batches
    /// with a short delay between batches to avoid CPU/IO bursts.
    /// </summary>
    private async Task BatchLoadRemainingThumbnailsAsync(IReadOnlyList<AssetListItem> items)
    {
        // Cancel any previous batch operation
        _thumbnailBatchCts?.Cancel();
        _thumbnailBatchCts?.Dispose();
        _thumbnailBatchCts = new CancellationTokenSource();
        var ct = _thumbnailBatchCts.Token;

        try
        {
            const int batchSize = 8;
            const int delayBetweenBatchesMs = 50;

            for (int i = 0; i < items.Count; i += batchSize)
            {
                ct.ThrowIfCancellationRequested();

                var batch = items.Skip(i).Take(batchSize).ToList();
                var tasks = batch.Select(item => item.LoadThumbnailAsync((int)ThumbnailSize));
                await Task.WhenAll(tasks);

                if (i + batchSize < items.Count)
                {
                    await Task.Delay(delayBetweenBatchesMs, ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Batch cancelled — new page load or filter change
        }
    }

    // ──────────────────────────────────────────────
    //  T12.2: Batch thumbnail backfill
    // ──────────────────────────────────────────────

    /// <summary>
    /// Generates thumbnails for assets that don't have them yet. Runs after page load,
    /// off the UI thread, with a degree of parallelism capped at 4.
    /// Previous backfill is cancelled on each new page load.
    /// </summary>
    private async Task BackfillMissingThumbnailsAsync(
        IReadOnlyList<AssetListItem> items, string thumbnailDir, CancellationToken ct)
    {
        // Cancel any previous backfill still running
        _backfillCts?.Cancel();
        _backfillCts?.Dispose();
        _backfillCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var linked = _backfillCts.Token;

        var missing = items
            .Where(i => !string.IsNullOrEmpty(i.StoragePath) && !File.Exists(i.ThumbnailPath))
            .ToList();

        if (missing.Count == 0) return;

        _logger.LogInformation("[Backfill] Generating thumbnails for {Count} assets without cached thumbnails", missing.Count);

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = 4,
            CancellationToken = linked
        };

        try
        {
            await Parallel.ForEachAsync(missing, options, async (item, token) =>
            {
                try
                {
                    var thumbPath = await _thumbnailService.GenerateThumbnailAsync(
                        item.StoragePath, thumbnailDir, token).ConfigureAwait(false);

                    if (File.Exists(thumbPath))
                    {
                        item.ThumbnailPath = thumbPath;
                        await item.LoadThumbnailAsync((int)ThumbnailSize).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "[Backfill] Failed to generate thumbnail for {Path}", item.StoragePath);
                }
            });
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("[Backfill] Cancelled due to new page load");
        }
    }

    /// <summary>
    /// Clears the shared memory cache. Call on gallery disposal or mode switch.
    /// </summary>
    public void ClearThumbnailCache()
    {
        _backfillCts?.Cancel();
        _backfillCts?.Dispose();
        _backfillCts = null;
        _thumbnailCache.Clear();
        AssetListItem.SharedThumbnailCache = null;
    }

    private static CollectionNode BuildCollectionNode(Adam.Shared.Models.Collection col, List<Adam.Shared.Models.Collection> all)
    {
        var node = new CollectionNode
        {
            Id = col.Id,
            ParentId = col.ParentId,
            Name = col.Name,
            AssetCount = col.Assets?.Count ?? 0
        };
        foreach (var child in all.Where(c => c.ParentId == col.Id))
            node.Children.Add(BuildCollectionNode(child, all));
        return node;
    }

    private static CollectionNode MapCollectionNode(Shared.Contracts.CollectionNode node)
    {
        var result = new CollectionNode
        {
            Id = Guid.Parse(node.Id),
            ParentId = string.IsNullOrEmpty(node.ParentId) ? null : Guid.Parse(node.ParentId),
            Name = node.Name,
            AssetCount = node.AssetCount
        };
        foreach (var child in node.Children)
            result.Children.Add(MapCollectionNode(child));
        return result;
    }
}
