using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
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

namespace Adam.CatalogBrowser.ViewModels;

public class AssetGalleryViewModel : INotifyPropertyChanged
{
    private readonly ModeManager _modeManager;
    private readonly ThumbnailService _thumbnailService = new();
    private readonly ILogger<AssetGalleryViewModel> _logger;
    private int _thumbnailSize = 150;
    private string _viewMode = "Grid";
    private string _sortBy = "File Name";
    private string _statusText = string.Empty;
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

    public AssetGalleryViewModel(ModeManager modeManager, ILogger<AssetGalleryViewModel> logger)
    {
        _modeManager = modeManager;
        _logger = logger;
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
                        ThumbnailPath = thumbnailPath
                    };

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
