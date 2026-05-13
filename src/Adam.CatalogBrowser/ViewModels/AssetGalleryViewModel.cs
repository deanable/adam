using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Adam.CatalogBrowser.Models;
using Adam.CatalogBrowser.Services;
using Adam.Shared.Contracts;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Adam.Shared.Services;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;

namespace Adam.CatalogBrowser.ViewModels;

public class AssetGalleryViewModel : INotifyPropertyChanged
{
    private readonly ModeManager _modeManager;
    private readonly ThumbnailService _thumbnailService = new();
    private int _thumbnailSize = 150;
    private string _sortBy = "FileName";
    private string _statusText = string.Empty;
    private AssetListItem? _selectedAsset;
    private bool _hasAssets;
    private int _page;
    private int _pageSize = 50;
    private int _totalCount;
    private bool _hasMore = true;
    private bool _isLoadingMore;
    private string? _activeCategory;
    private string? _activeFolderPath;

    public AssetGalleryViewModel(ModeManager modeManager)
    {
        _modeManager = modeManager;
    }

    public ObservableCollection<AssetListItem> Assets { get; } = [];
    public ObservableCollection<CollectionNode> Collections { get; } = [];

    public int ThumbnailSize
    {
        get => _thumbnailSize;
        set { _thumbnailSize = value; OnPropertyChanged(); OnPropertyChanged(nameof(ThumbnailSizeText)); }
    }

    public string ThumbnailSizeText => $"{_thumbnailSize}px";

    public string SortBy
    {
        get => _sortBy;
        set { _sortBy = value; OnPropertyChanged(); }
    }

    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    public AssetListItem? SelectedAsset
    {
        get => _selectedAsset;
        set
        {
            _selectedAsset = value;
            OnPropertyChanged();
            SelectionChanged?.Invoke(value);
        }
    }

    public event Action<AssetListItem?>? SelectionChanged;

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

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public async Task LoadAssetsAsync(CancellationToken ct = default)
    {
        _page = 0;
        _hasMore = true;
        Assets.Clear();

        await LoadPageAsync(ct);

        HasMore = _hasMore;
        HasAssets = Assets.Count > 0;
        StatusText = _totalCount > 0 ? $"{Assets.Count} of {_totalCount} asset(s)" : "0 asset(s)";
    }

    public async Task LoadMoreAsync(CancellationToken ct = default)
    {
        if (!_hasMore || _isLoadingMore) return;
        await LoadPageAsync(ct);
        HasMore = _hasMore;
        StatusText = $"{Assets.Count} of {_totalCount} asset(s)";
    }

    private async Task LoadPageAsync(CancellationToken ct = default)
    {
        IsLoadingMore = true;
        try
        {
            if (_modeManager.IsStandalone)
            {
                await using var db = _modeManager.CreateDbContext();

                if (_page == 0)
                    _totalCount = await db.DigitalAssets.CountAsync(ct);

                var query = ApplyFilters(db.DigitalAssets.AsQueryable());
                var assets = await query
                    .OrderBy(a => a.FileName)
                    .Skip(_page * _pageSize)
                    .Take(_pageSize)
                    .ToListAsync(ct);

                var dbDir = Path.GetDirectoryName(_modeManager.DbPath) ?? ".";
                var storageDir = Path.Combine(dbDir, "storage");
                var thumbnailDir = Path.Combine(dbDir, "thumbnails");

                foreach (var asset in assets)
                {
                    var fullStoredPath = Path.Combine(storageDir, asset.StoragePath);
                    var thumbnailPath = _thumbnailService.GetThumbnailPath(fullStoredPath, thumbnailDir);

                    Assets.Add(new AssetListItem
                    {
                        Id = asset.Id,
                        Title = asset.Title,
                        FileName = asset.FileName,
                        FileType = asset.MimeType,
                        FileSize = asset.FileSize,
                        Width = asset.Width,
                        Height = asset.Height,
                        CreatedAt = asset.CreatedAt,
                        ThumbnailPath = thumbnailPath
                    });
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

                var correlationId = Guid.NewGuid().ToString();
                var request = new Envelope
                {
                    AuthToken = auth.Token ?? "",
                    CorrelationId = correlationId,
                    MessageType = nameof(ListAssetsRequest),
                    Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new ListAssetsRequest()))
                };

                var response = await broker.SendAsync(request, ct);
                if (response.StatusCode != 0) return;

                var listResponse = ProtoHelper.Deserialize<ListAssetsResponse>(response.Payload.ToByteArray());
                foreach (var item in listResponse.Items)
                {
                    Assets.Add(new AssetListItem
                    {
                        Id = Guid.Parse(item.Id),
                        Title = item.Title,
                        FileName = item.FileName,
                        FileType = item.MimeType,
                        FileSize = item.FileSize,
                        CreatedAt = DateTimeOffset.FromUnixTimeSeconds(item.CreatedAt)
                    });
                }

                _hasMore = false;
            }
        }
        finally
        {
            IsLoadingMore = false;
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

        if (!string.IsNullOrEmpty(_activeFolderPath))
        {
            var prefix = _activeFolderPath.Replace('\\', '/');
            query = query.Where(a => a.StoragePath.StartsWith(prefix));
        }

        return query;
    }

    public void ApplyFilter(string? category, string? folderPath)
    {
        _activeCategory = category;
        _activeFolderPath = folderPath;
        _ = LoadAssetsAsync();
    }

    public async Task LoadCollectionsAsync(CancellationToken ct = default)
    {
        Collections.Clear();

        if (_modeManager.IsStandalone)
        {
            await using var db = _modeManager.CreateDbContext();
            var collections = await db.Collections
                .Include(c => c.Children)
                .ToListAsync(ct);

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
                MessageType = nameof(ListCollectionsRequest),
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


