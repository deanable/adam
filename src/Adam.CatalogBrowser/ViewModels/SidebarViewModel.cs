using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Adam.CatalogBrowser.Services;
using Adam.Shared.Services;
using Adam.Shared.Contracts;
using Avalonia.Threading;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Adam.CatalogBrowser.ViewModels;

public class SidebarViewModel : INotifyPropertyChanged
{
    private readonly ModeManager _modeManager;
    private readonly ILogger<SidebarViewModel> _logger;
    private CategoryNode _selectedMediaFormat;
    private CategoryNode? _selectedMetadataCategory;
    private FolderNode? _selectedFolder;
    private CollectionNode? _selectedCollection;
    private KeywordNode? _selectedKeyword;
    private ObservableCollection<CollectionNode> _collections = [];
    private ObservableCollection<KeywordNode> _keywords = [];
    private ObservableCollection<CategoryNode> _metadataCategories = [];
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private bool _isLoading;
    private DateTakenNode? _selectedDateTaken;
    private ObservableCollection<DateTakenNode> _dateTakenTree = [];

    public SidebarViewModel(ModeManager modeManager, ILogger<SidebarViewModel> logger)
    {
        _modeManager = modeManager;
        _logger = logger;
        _selectedMediaFormat = MediaFormats[0];
    }

    public ObservableCollection<FolderNode> Folders { get; } = [];

    public ObservableCollection<CollectionNode> Collections
    {
        get => _collections;
        private set { _collections = value; OnPropertyChanged(); }
    }

    public ObservableCollection<KeywordNode> Keywords
    {
        get => _keywords;
        private set { _keywords = value; OnPropertyChanged(); }
    }

    public ObservableCollection<CategoryNode> MediaFormats { get; } =
    [
        new() { Name = "All", Count = 0 },
        new() { Name = "Images", Count = 0 },
        new() { Name = "Videos", Count = 0 },
        new() { Name = "Documents", Count = 0 },
        new() { Name = "Audio", Count = 0 },
    ];

    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(); }
    }

    public ObservableCollection<CategoryNode> MetadataCategories
    {
        get => _metadataCategories;
        private set { _metadataCategories = value; OnPropertyChanged(); }
    }

    public ObservableCollection<DateTakenNode> DateTakenTree
    {
        get => _dateTakenTree;
        private set { _dateTakenTree = value; OnPropertyChanged(); }
    }

    public CategoryNode SelectedMediaFormat
    {
        get => _selectedMediaFormat;
        set { _selectedMediaFormat = value; OnPropertyChanged(); OnMediaFormatChanged(); }
    }

    public CategoryNode? SelectedMetadataCategory
    {
        get => _selectedMetadataCategory;
        set { _selectedMetadataCategory = value; OnPropertyChanged(); OnFilterChanged(); }
    }

    public FolderNode? SelectedFolder
    {
        get => _selectedFolder;
        set { _selectedFolder = value; OnPropertyChanged(); OnFilterChanged(); }
    }

    public CollectionNode? SelectedCollection
    {
        get => _selectedCollection;
        set { _selectedCollection = value; OnPropertyChanged(); OnFilterChanged(); }
    }

    public KeywordNode? SelectedKeyword
    {
        get => _selectedKeyword;
        set { _selectedKeyword = value; OnPropertyChanged(); OnFilterChanged(); }
    }

    public DateTakenNode? SelectedDateTaken
    {
        get => _selectedDateTaken;
        set
        {
            if (_selectedDateTaken != value)
            {
                _selectedDateTaken = value;
                OnPropertyChanged();
                OnFilterChanged();
            }
        }
    }

    public event Action? FilterChanged;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        await Dispatcher.UIThread.InvokeAsync(() => IsLoading = true);
        _logger.LogInformation("[LoadAsync] Acquiring load lock...");
        await _loadLock.WaitAsync(ct).ConfigureAwait(false);
        _logger.LogInformation("[LoadAsync] Lock acquired. Starting parallel loads...");
        try
        {
            await Task.WhenAll(
                LoadFoldersAsync(ct),
                LoadCollectionsAsync(ct),
                LoadKeywordsAsync(ct),
                LoadMediaFormatCountsAsync(ct),
                LoadMetadataCategoriesAsync(ct),
                LoadDateTakenTreeAsync(ct)).ConfigureAwait(false);
            _logger.LogInformation("[LoadAsync] All parallel loads completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LoadAsync] One or more parallel loads failed. Exception type={ExType}, Message={Message}", ex.GetType().Name, ex.Message);
            throw;
        }
        finally
        {
            _loadLock.Release();
            _logger.LogInformation("[LoadAsync] Lock released");
            await Dispatcher.UIThread.InvokeAsync(() => IsLoading = false);
        }
    }

    private static string GetDirectoryName(string path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        var lastSep = path.LastIndexOfAny(['/', '\\']);
        return lastSep > 0 ? path[..lastSep] : "";
    }

    private async Task LoadFoldersAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("[LoadFoldersAsync] Starting folder load. IsStandalone={IsStandalone}", _modeManager.IsStandalone);

        var paths = new HashSet<string>();
        var folderCounts = new Dictionary<string, int>();

        if (_modeManager.IsStandalone)
        {
            await using var db = await _modeManager.CreateDbContextAsync(ct).ConfigureAwait(false);
            _logger.LogInformation("[LoadFoldersAsync] Querying directories from database...");

            var storagePaths = await db.DigitalAssets
                .Select(a => a.StoragePath)
                .Where(p => p != null)
                .ToListAsync(ct).ConfigureAwait(false);

            paths = storagePaths
                .Select(p => GetDirectoryName(p))
                .Where(d => d.Length > 0)
                .ToHashSet();

            var allPaths = await db.DigitalAssets
                .Select(a => a.StoragePath)
                .ToListAsync(ct).ConfigureAwait(false);

            folderCounts = allPaths
                .GroupBy(p => GetDirectoryName(p))
                .ToDictionary(g => g.Key, g => g.Count());

            _logger.LogInformation("[LoadFoldersAsync] Retrieved {Count} distinct directories", paths.Count);
            foreach (var p in paths.Take(10))
                _logger.LogDebug("[LoadFoldersAsync] Dir sample: {Path}", p);
        }
        else if (_modeManager.BrokerClient != null)
        {
            var req = new Envelope
            {
                MessageType = MessageTypeCode.ListFoldersRequest,
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new ListFoldersRequest()))
            };
            var resp = await _modeManager.BrokerClient.SendAsync(req, ct).ConfigureAwait(false);
            if (resp.StatusCode == 0)
            {
                var data = ProtoHelper.Deserialize<ListFoldersResponse>(resp.Payload.ToByteArray());
                foreach (var f in data.Folders)
                {
                    paths.Add(f.Path);
                    folderCounts[f.Path] = f.AssetCount;
                }
                _logger.LogInformation("[LoadFoldersAsync] Retrieved {Count} folders from broker", data.Folders.Count);
            }
        }

        // Build tree on background thread
        var root = new FolderNode { Name = "All Folders", Path = "", IsExpanded = true };
        foreach (var dir in paths.OrderBy(p => p))
        {
            var normalizedDir = dir.Replace('\\', '/');
            var isUnc = normalizedDir.StartsWith("//");
            var parts = normalizedDir.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var current = root;
            var cumulative = "";

            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (i == 0 && isUnc)
                    cumulative = "//" + part;
                else if (i == 0)
                    cumulative = part;
                else
                    cumulative = cumulative + "/" + part;

                var existing = current.Children.FirstOrDefault(c => c.Name == part);
                if (existing == null)
                {
                    existing = new FolderNode { Name = part, Path = cumulative };
                    current.Children.Add(existing);
                }
                current = existing;
            }
        }

        // Populate asset counts
        foreach (var (dir, count) in folderCounts)
        {
            var node = FindFolderNode(root, dir);
            if (node != null)
                node.AssetCount = count;
        }

        _logger.LogInformation("[LoadFoldersAsync] Applied counts for {Count} folders", folderCounts.Count);

        // Propagate counts upward so parents show totals
        PropagateFolderCounts(root);

        _logger.LogInformation("[LoadFoldersAsync] Assigning Folders collection on UI thread");
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Folders.Clear();
            Folders.Add(root);
            _logger.LogInformation("[LoadFoldersAsync] Folders assigned. Collection count={Count}, Root children={Children}", Folders.Count, root.Children.Count);
        });
        _logger.LogInformation("[LoadFoldersAsync] Completed");
    }

    private static FolderNode? FindFolderNode(FolderNode root, string path)
    {
        if (string.IsNullOrEmpty(path))
            return root;

        var normalizedPath = path.Replace('\\', '/');
        var parts = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = root;
        foreach (var part in parts)
        {
            current = current.Children.FirstOrDefault(c => c.Name == part);
            if (current == null)
                return null;
        }
        return current;
    }

    private async Task LoadCollectionsAsync(CancellationToken ct = default)
    {
        var newCollections = new List<CollectionNode>();

        if (_modeManager.IsStandalone)
        {
            await using var db = await _modeManager.CreateDbContextAsync(ct).ConfigureAwait(false);
            var all = await db.Collections
                .Select(c => new { c.Id, c.Name, c.ParentId, AssetCount = c.Assets.Count })
                .ToListAsync(ct).ConfigureAwait(false);
            var allCols = all.Select(c => new CollectionNode
            {
                Id = c.Id, Name = c.Name, ParentId = c.ParentId, AssetCount = c.AssetCount
            }).ToList();

            foreach (var col in allCols.Where(c => c.ParentId == null))
                newCollections.Add(BuildTree(col, allCols));
        }
        else if (_modeManager.BrokerClient != null)
        {
            var req = new Envelope
            {
                MessageType = MessageTypeCode.ListCollectionsRequest,
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new ListCollectionsRequest()))
            };
            var resp = await _modeManager.BrokerClient.SendAsync(req, ct).ConfigureAwait(false);
            if (resp.StatusCode == 0)
            {
                var data = ProtoHelper.Deserialize<ListCollectionsResponse>(resp.Payload.ToByteArray());
                var allCols = data.Items.Select(c => new CollectionNode
                {
                    Id = Guid.Parse(c.Id),
                    Name = c.Name,
                    ParentId = string.IsNullOrEmpty(c.ParentId) ? null : Guid.Parse(c.ParentId),
                    AssetCount = c.AssetCount
                }).ToList();

                foreach (var col in allCols.Where(c => c.ParentId == null))
                    newCollections.Add(BuildTree(col, allCols));

                _logger.LogInformation("[LoadCollectionsAsync] Loaded {Count} collections from broker", allCols.Count);
            }
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Collections = new ObservableCollection<CollectionNode>(newCollections);
        });
    }

    private static CollectionNode BuildTree(CollectionNode node, List<CollectionNode> all)
    {
        foreach (var child in all.Where(c => c.ParentId == node.Id))
            node.Children.Add(BuildTree(child, all));
        return node;
    }

    private async Task LoadKeywordsAsync(CancellationToken ct = default)
    {
        KeywordNode root;

        if (_modeManager.IsStandalone)
        {
            await using var db = await _modeManager.CreateDbContextAsync(ct).ConfigureAwait(false);
            var keywordRows = await db.Keywords
                .Select(k => new
                {
                    k.Id,
                    k.Name,
                    k.NormalizedName,
                    k.ParentId,
                    AssetCount = k.Assets.Count
                })
                .ToListAsync(ct).ConfigureAwait(false);

            root = new KeywordNode { Name = "All Keywords", Path = "", IsExpanded = true };

            var nodeDict = new Dictionary<Guid, KeywordNode>();

            foreach (var kw in keywordRows)
            {
                var node = new KeywordNode
                {
                    Name = kw.Name,
                    Path = kw.Name,
                    KeywordId = kw.Id,
                    AssetCount = kw.AssetCount
                };
                nodeDict[kw.Id] = node;
            }

            // Link parents and build tree
            foreach (var kw in keywordRows.Where(k => k.ParentId.HasValue))
            {
                if (nodeDict.TryGetValue(kw.Id, out var childNode) &&
                    nodeDict.TryGetValue(kw.ParentId!.Value, out var parentNode))
                {
                    childNode.Path = $"{parentNode.Path}|{childNode.Name}";
                    parentNode.Children.Add(childNode);
                }
            }

            // Add root-level keywords to the tree root
            foreach (var kw in keywordRows.Where(k => !k.ParentId.HasValue))
            {
                if (nodeDict.TryGetValue(kw.Id, out var node))
                {
                    root.Children.Add(node);
                }
            }

            // Propagate counts upward (leaf counts already set, add children to parents)
            PropagateKeywordCounts(root);
        }
        else if (_modeManager.BrokerClient != null)
        {
            var req = new Envelope
            {
                MessageType = MessageTypeCode.ListKeywordsRequest,
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new ListKeywordsRequest()))
            };
            var resp = await _modeManager.BrokerClient.SendAsync(req, ct).ConfigureAwait(false);
            root = new KeywordNode { Name = "All Keywords", Path = "", IsExpanded = true };
            if (resp.StatusCode == 0)
            {
                var data = ProtoHelper.Deserialize<ListKeywordsResponse>(resp.Payload.ToByteArray());
                var nodeDict = new Dictionary<Guid, KeywordNode>();

                foreach (var kw in data.Keywords)
                {
                    var node = new KeywordNode
                    {
                        Name = kw.Name,
                        Path = kw.Name,
                        KeywordId = kw.Id,
                        AssetCount = kw.AssetCount
                    };
                    nodeDict[kw.Id] = node;
                }

                foreach (var kw in data.Keywords.Where(k => k.ParentId.HasValue))
                {
                    if (nodeDict.TryGetValue(kw.Id, out var childNode) &&
                        nodeDict.TryGetValue(kw.ParentId!.Value, out var parentNode))
                    {
                        childNode.Path = $"{parentNode.Path}|{childNode.Name}";
                        parentNode.Children.Add(childNode);
                    }
                }

                foreach (var kw in data.Keywords.Where(k => !k.ParentId.HasValue))
                {
                    if (nodeDict.TryGetValue(kw.Id, out var node))
                    {
                        root.Children.Add(node);
                    }
                }

                PropagateKeywordCounts(root);
                _logger.LogInformation("[LoadKeywordsAsync] Loaded {Count} keywords from broker", data.Keywords.Count);
            }
        }
        else
        {
            root = new KeywordNode { Name = "All Keywords", Path = "", IsExpanded = true };
        }

        await Dispatcher.UIThread.InvokeAsync(() => Keywords = new ObservableCollection<KeywordNode> { root });
    }

    private static int PropagateKeywordCounts(KeywordNode node)
    {
        var childSum = 0;
        foreach (var child in node.Children)
        {
            childSum += PropagateKeywordCounts(child);
        }
        node.AssetCount += childSum;
        return node.AssetCount;
    }

    private static int PropagateFolderCounts(FolderNode node)
    {
        var childSum = 0;
        foreach (var child in node.Children)
        {
            childSum += PropagateFolderCounts(child);
        }
        node.AssetCount += childSum;
        return node.AssetCount;
    }

    private async Task LoadDateTakenTreeAsync(CancellationToken ct = default)
    {
        var root = new DateTakenNode { Name = "All Dates", IsExpanded = true };

        if (_modeManager.IsStandalone)
        {
            await using var db = await _modeManager.CreateDbContextAsync(ct).ConfigureAwait(false);

            // Group MetadataProfiles with a DateTaken by year/month and count distinct assets
            var dateGroups = await db.MetadataProfiles
                .Where(mp => mp.DateTaken.HasValue)
                .GroupBy(mp => new { mp.DateTaken!.Value.Year, mp.DateTaken.Value.Month })
                .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
                .ToListAsync(ct).ConfigureAwait(false);

            // Group by year then month
            var byYear = dateGroups
                .GroupBy(g => g.Year)
                .OrderByDescending(g => g.Key)
                .ToList();

            foreach (var yearGroup in byYear)
            {
                var yearNode = new DateTakenNode
                {
                    Name = yearGroup.Key.ToString(),
                    Year = yearGroup.Key,
                    AssetCount = yearGroup.Sum(g => g.Count),
                    IsExpanded = false
                };

                foreach (var monthGroup in yearGroup.OrderByDescending(g => g.Month))
                {
                    var monthNode = new DateTakenNode
                    {
                        Name = new DateTime(yearGroup.Key, monthGroup.Month, 1).ToString("MMMM"),
                        Year = yearGroup.Key,
                        Month = monthGroup.Month,
                        AssetCount = monthGroup.Count
                    };
                    yearNode.Children.Add(monthNode);
                }

                root.Children.Add(yearNode);
            }

            // Total count on root
            root.AssetCount = byYear.Sum(g => g.Sum(x => x.Count));
        }
        else if (_modeManager.BrokerClient != null)
        {
            var req = new Envelope
            {
                MessageType = MessageTypeCode.ListDateTakenTreeRequest,
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new ListDateTakenTreeRequest()))
            };
            var resp = await _modeManager.BrokerClient.SendAsync(req, ct).ConfigureAwait(false);
            if (resp.StatusCode == 0)
            {
                var data = ProtoHelper.Deserialize<ListDateTakenTreeResponse>(resp.Payload.ToByteArray());
                foreach (var yearInfo in data.Years)
                {
                    var yearNode = new DateTakenNode
                    {
                        Name = yearInfo.Year.ToString(),
                        Year = yearInfo.Year,
                        AssetCount = yearInfo.AssetCount,
                        IsExpanded = false
                    };

                    foreach (var monthInfo in yearInfo.Months)
                    {
                        var monthNode = new DateTakenNode
                        {
                            Name = monthInfo.MonthName,
                            Year = yearInfo.Year,
                            Month = monthInfo.Month,
                            AssetCount = monthInfo.AssetCount
                        };
                        yearNode.Children.Add(monthNode);
                    }

                    root.Children.Add(yearNode);
                }

                root.AssetCount = data.Years.Sum(y => y.AssetCount);
                _logger.LogInformation("[LoadDateTakenTreeAsync] Loaded {Count} years from broker", data.Years.Count);
            }
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            DateTakenTree = new ObservableCollection<DateTakenNode> { root };
        });
    }

    private async Task LoadMediaFormatCountsAsync(CancellationToken ct = default)
    {
        if (_modeManager.IsStandalone)
        {
            await using var db = await _modeManager.CreateDbContextAsync(ct).ConfigureAwait(false);
            var counts = await db.DigitalAssets
                .GroupBy(a => a.Type)
                .Select(g => new { Type = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Type, x => x.Count, ct).ConfigureAwait(false);

            var total = counts.Values.Sum();
            counts.TryGetValue(Adam.Shared.Models.AssetType.Image, out var images);
            counts.TryGetValue(Adam.Shared.Models.AssetType.Video, out var videos);
            counts.TryGetValue(Adam.Shared.Models.AssetType.Document, out var docs);
            counts.TryGetValue(Adam.Shared.Models.AssetType.Audio, out var audio);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                MediaFormats[0].Count = total;
                MediaFormats[1].Count = images;
                MediaFormats[2].Count = videos;
                MediaFormats[3].Count = docs;
                MediaFormats[4].Count = audio;
            });
        }
        else if (_modeManager.BrokerClient != null)
        {
            var req = new Envelope
            {
                MessageType = MessageTypeCode.ListMediaFormatCountsRequest,
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new ListMediaFormatCountsRequest()))
            };
            var resp = await _modeManager.BrokerClient.SendAsync(req, ct).ConfigureAwait(false);
            if (resp.StatusCode == 0)
            {
                var data = ProtoHelper.Deserialize<ListMediaFormatCountsResponse>(resp.Payload.ToByteArray());
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    MediaFormats[0].Count = data.TotalCount;
                    MediaFormats[1].Count = data.ImageCount;
                    MediaFormats[2].Count = data.VideoCount;
                    MediaFormats[3].Count = data.DocumentCount;
                    MediaFormats[4].Count = data.AudioCount;
                });
                _logger.LogInformation("[LoadMediaFormatCountsAsync] Loaded format counts from broker: Total={Total}", data.TotalCount);
            }
        }
    }

    private async Task LoadMetadataCategoriesAsync(CancellationToken ct)
    {
        var newCats = new List<CategoryNode>();

        if (_modeManager.IsStandalone)
        {
            await using var db = await _modeManager.CreateDbContextAsync(ct).ConfigureAwait(false);
            var categoryRows = await db.Categories
                .Select(c => new
                {
                    c.Id,
                    c.Name,
                    c.NormalizedName,
                    c.ParentId,
                    AssetCount = c.Assets.Count
                })
                .ToListAsync(ct).ConfigureAwait(false);

            var total = categoryRows.Sum(c => c.AssetCount);
            var root = new CategoryNode { Name = "All", Count = total, IsExpanded = true };

            var nodeDict = new Dictionary<Guid, CategoryNode>();

            foreach (var cat in categoryRows)
            {
                var node = new CategoryNode
                {
                    Name = cat.Name,
                    CategoryId = cat.Id,
                    Count = cat.AssetCount
                };
                nodeDict[cat.Id] = node;
            }

            // Link parents and build tree
            foreach (var cat in categoryRows.Where(c => c.ParentId.HasValue))
            {
                if (nodeDict.TryGetValue(cat.Id, out var childNode) &&
                    nodeDict.TryGetValue(cat.ParentId!.Value, out var parentNode))
                {
                    parentNode.Children.Add(childNode);
                }
            }

            // Add root-level categories to the tree root
            foreach (var cat in categoryRows.Where(c => !c.ParentId.HasValue))
            {
                if (nodeDict.TryGetValue(cat.Id, out var node))
                {
                    root.Children.Add(node);
                }
            }

            // Propagate counts upward
            PropagateCategoryCounts(root);
            newCats.Add(root);
        }
        else if (_modeManager.BrokerClient != null)
        {
            var req = new Envelope
            {
                MessageType = MessageTypeCode.ListMetadataCategoriesRequest,
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new ListMetadataCategoriesRequest()))
            };
            var resp = await _modeManager.BrokerClient.SendAsync(req, ct).ConfigureAwait(false);
            if (resp.StatusCode == 0)
            {
                var data = ProtoHelper.Deserialize<ListMetadataCategoriesResponse>(resp.Payload.ToByteArray());
                var total = data.Categories.Sum(c => c.AssetCount);
                var root = new CategoryNode { Name = "All", Count = total, IsExpanded = true };
                var nodeDict = new Dictionary<Guid, CategoryNode>();

                foreach (var cat in data.Categories)
                {
                    var node = new CategoryNode
                    {
                        Name = cat.Name,
                        CategoryId = cat.Id,
                        Count = cat.AssetCount
                    };
                    nodeDict[cat.Id] = node;
                }

                foreach (var cat in data.Categories.Where(c => c.ParentId.HasValue))
                {
                    if (nodeDict.TryGetValue(cat.Id, out var childNode) &&
                        nodeDict.TryGetValue(cat.ParentId!.Value, out var parentNode))
                    {
                        parentNode.Children.Add(childNode);
                    }
                }

                foreach (var cat in data.Categories.Where(c => !c.ParentId.HasValue))
                {
                    if (nodeDict.TryGetValue(cat.Id, out var node))
                    {
                        root.Children.Add(node);
                    }
                }

                PropagateCategoryCounts(root);
                newCats.Add(root);
                _logger.LogInformation("[LoadMetadataCategoriesAsync] Loaded {Count} categories from broker", data.Categories.Count);
            }
            else
            {
                newCats.Add(new CategoryNode { Name = "All", Count = 0, IsExpanded = true });
            }
        }
        else
        {
            newCats.Add(new CategoryNode { Name = "All", Count = 0, IsExpanded = true });
        }

        await Dispatcher.UIThread.InvokeAsync(() => MetadataCategories = new ObservableCollection<CategoryNode>(newCats));
    }

    private static int PropagateCategoryCounts(CategoryNode node)
    {
        var childSum = 0;
        foreach (var child in node.Children)
        {
            childSum += PropagateCategoryCounts(child);
        }
        node.Count += childSum;
        return node.Count;
    }

    private void OnMediaFormatChanged() => FilterChanged?.Invoke();
    private void OnFilterChanged() => FilterChanged?.Invoke();

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public class FolderNode : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isSelected;
    private int _assetCount;

    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int AssetCount { get => _assetCount; set { _assetCount = value; OnPropertyChanged(); } }

    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; OnPropertyChanged(); }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public ObservableCollection<FolderNode> Children { get; } = [];

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public class CollectionNode : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private int _assetCount;

    public Guid Id { get; set; }
    public Guid? ParentId { get; set; }
    public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
    public int AssetCount { get => _assetCount; set { _assetCount = value; OnPropertyChanged(); } }
    public ObservableCollection<CollectionNode> Children { get; } = [];

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public class KeywordNode : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isSelected;
    private int _assetCount;

    public Guid KeywordId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int AssetCount { get => _assetCount; set { _assetCount = value; OnPropertyChanged(); } }

    public bool IsExpanded { get => _isExpanded; set { _isExpanded = value; OnPropertyChanged(); } }
    public bool IsSelected { get => _isSelected; set { _isSelected = value; OnPropertyChanged(); } }
    public ObservableCollection<KeywordNode> Children { get; } = [];

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public class CategoryNode : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isSelected;
    private string _name = string.Empty;
    private int _count;

    public Guid CategoryId { get; set; }
    public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
    public int Count { get => _count; set { _count = value; OnPropertyChanged(); } }
    public bool IsExpanded { get => _isExpanded; set { _isExpanded = value; OnPropertyChanged(); } }
    public bool IsSelected { get => _isSelected; set { _isSelected = value; OnPropertyChanged(); } }
    public ObservableCollection<CategoryNode> Children { get; } = [];

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public class DateTakenNode : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isSelected;
    private string _name = string.Empty;
    private int _assetCount;

    public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
    public int? Year { get; set; }
    public int? Month { get; set; }
    public int AssetCount { get => _assetCount; set { _assetCount = value; OnPropertyChanged(); } }
    public bool IsExpanded { get => _isExpanded; set { _isExpanded = value; OnPropertyChanged(); } }
    public bool IsSelected { get => _isSelected; set { _isSelected = value; OnPropertyChanged(); } }
    public ObservableCollection<DateTakenNode> Children { get; } = [];

    /// <summary>
    /// Whether this node represents a year (true) or a month (false).
    /// </summary>
    public bool IsYear => Year.HasValue && !Month.HasValue;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
