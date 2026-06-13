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

namespace Adam.CatalogBrowser.ViewModels;

public class AssetGalleryViewModel : INotifyPropertyChanged
{
    private readonly ModeManager _modeManager;
    private readonly ThumbnailService _thumbnailService = new();
    private readonly ILogger<AssetGalleryViewModel> _logger;
    private readonly ThumbnailCache _thumbnailCache = new();
    private readonly IFtsService? _ftsService;
    private CancellationTokenSource? _backfillCts;
    private CancellationTokenSource? _searchCts;
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
    public ICommand SelectAllCommand { get; }
    public ICommand ClearSearchCommand { get; }

    public AssetGalleryViewModel(ModeManager modeManager, ILogger<AssetGalleryViewModel> logger, IFtsService? ftsService = null)
    {
        _modeManager = modeManager;
        _logger = logger;
        _ftsService = ftsService;

        SelectAllCommand = new RelayCommand(_ => SelectAll());
        ClearSearchCommand = new RelayCommand(_ => ClearSearch());

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
    /// Current search query text. Triggers debounced FTS search on change.
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
    /// True when the gallery is displaying FTS search results (T11.9).
    /// </summary>
    public bool IsSearchActive
    {
        get => _isSearchActive;
        set { _isSearchActive = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Autocomplete suggestions from FTS index (T11.11).
    /// Populated asynchronously as the user types.
    /// </summary>
    public ObservableCollection<string> SearchSuggestions { get; } = [];

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

        // Update autocomplete suggestions (fast, immediate)
        if (text.Length >= 2 && _ftsService != null)
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
                Assets.Clear();

                foreach (var item in newItems)
                    Assets.Add(item);

                HasAssets = Assets.Count > 0;
                HasMore = false;
                StatusText = Assets.Count > 0
                    ? $"Search: {Assets.Count} result(s) for \"{query}\""
                    : $"No results for \"{query}\"";
            });

            // Load thumbnails off the UI thread
            foreach (var item in newItems)
                _ = item.LoadThumbnailAsync((int)ThumbnailSize);
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

                query = _sortBy switch
                {
                    "Date Added" => query.OrderByDescending(a => a.CreatedAt).ThenBy(a => a.Id),
                    "File Type" => query.OrderBy(a => a.MimeType).ThenBy(a => a.Id),
                    "File Size" => query.OrderBy(a => a.FileSize).ThenBy(a => a.Id),
                    _ => query.OrderBy(a => a.FileName).ThenBy(a => a.Id)
                };
                _logger.LogInformation("[LoadPageAsync] Ordering by {SortBy}", _sortBy);

                var assets = await query
                    .Skip(_page * _pageSize)
                    .Take(_pageSize)
                    .ToListAsync(ct).ConfigureAwait(false);
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

                // Load thumbnails off the UI thread after items are added
                foreach (var item in newItems)
                {
                    _ = item.LoadThumbnailAsync((int)ThumbnailSize);
                }

                _page++;
                if (assets.Count < _pageSize)
                    _hasMore = false;

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

        return query;
    }

    public void ApplyFilter(string? mediaFormat, string? folderPath, List<Guid>? keywordIds = null, List<Guid>? categoryIds = null, DateTime? dateFrom = null, DateTime? dateTo = null)
    {
        _activeCategory = mediaFormat;
        _activeFolderPath = folderPath;
        _activeKeywordIds = keywordIds ?? [];
        _activeCategoryIds = categoryIds ?? [];
        _filterDateFrom = dateFrom;
        _filterDateTo = dateTo;
        _ = LoadAssetsAsync();
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
