using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Adam.CatalogBrowser.Models;
using Adam.CatalogBrowser.Services;
using Adam.Shared.Contracts;
using Adam.Shared.Data;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;

namespace Adam.CatalogBrowser.ViewModels;

public class AssetGalleryViewModel : INotifyPropertyChanged
{
    private readonly ModeManager _modeManager;
    private int _thumbnailSize = 150;
    private string _sortBy = "FileName";
    private string _statusText = string.Empty;
    private AssetListItem? _selectedAsset;
    private bool _hasAssets;

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
        set { _selectedAsset = value; OnPropertyChanged(); }
    }

    public bool HasAssets
    {
        get => _hasAssets;
        set { _hasAssets = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public async Task LoadAssetsAsync(CancellationToken ct = default)
    {
        Assets.Clear();

        if (_modeManager.IsStandalone)
        {
            await using var db = _modeManager.CreateDbContext();
            var assets = await db.DigitalAssets
                .OrderBy(a => a.FileName)
                .ToListAsync(ct);

            foreach (var asset in assets)
            {
                Assets.Add(new AssetListItem
                {
                    Id = asset.Id,
                    Title = asset.Title,
                    FileName = asset.FileName,
                    FileType = asset.MimeType,
                    FileSize = asset.FileSize,
                    Width = asset.Width,
                    Height = asset.Height,
                    CreatedAt = asset.CreatedAt
                });
            }
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
        }

        HasAssets = Assets.Count > 0;
        StatusText = $"{Assets.Count} asset(s)";
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

public class CollectionNode : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private bool _isSelected;
    private int _assetCount;

    public Guid Id { get; set; }
    public Guid? ParentId { get; set; }

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public int AssetCount
    {
        get => _assetCount;
        set { _assetCount = value; OnPropertyChanged(); }
    }

    public ObservableCollection<CollectionNode> Children { get; } = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
