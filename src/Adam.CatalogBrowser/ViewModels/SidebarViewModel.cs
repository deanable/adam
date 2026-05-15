using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Adam.CatalogBrowser.Services;
using Adam.Shared.Contracts;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;

namespace Adam.CatalogBrowser.ViewModels;

public class SidebarViewModel : INotifyPropertyChanged
{
    private readonly ModeManager _modeManager;
    private CategoryNode _selectedMediaFormat;
    private CategoryNode? _selectedMetadataCategory;
    private FolderNode? _selectedFolder;
    private CollectionNode? _selectedCollection;
    private KeywordNode? _selectedKeyword;
    private ObservableCollection<FolderNode> _folders = [];
    private ObservableCollection<CollectionNode> _collections = [];
    private ObservableCollection<KeywordNode> _keywords = [];
    private ObservableCollection<CategoryNode> _metadataCategories = [];
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    public SidebarViewModel(ModeManager modeManager)
    {
        _modeManager = modeManager;
        _selectedMediaFormat = MediaFormats[0];
    }

    public ObservableCollection<FolderNode> Folders
    {
        get => _folders;
        private set { _folders = value; OnPropertyChanged(); }
    }

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

    public ObservableCollection<CategoryNode> MetadataCategories
    {
        get => _metadataCategories;
        private set { _metadataCategories = value; OnPropertyChanged(); }
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

    public event Action? FilterChanged;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        await _loadLock.WaitAsync(ct);
        try
        {
            await Task.WhenAll(
                LoadFoldersAsync(ct),
                LoadCollectionsAsync(ct),
                LoadKeywordsAsync(ct),
                LoadMediaFormatCountsAsync(ct),
                LoadMetadataCategoriesAsync(ct));
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private async Task LoadFoldersAsync(CancellationToken ct = default)
    {
        var dirs = new HashSet<string>();

        if (_modeManager.IsStandalone)
        {
            await using var db = _modeManager.CreateDbContext();
            var storagePaths = await db.DigitalAssets
                .Select(a => a.StoragePath)
                .Where(p => p != null && p.Length > 0)
                .Distinct()
                .ToListAsync(ct);

            dirs = storagePaths
                .Select(p => Path.GetDirectoryName(p.Replace("\\", "/")) ?? "")
                .Where(d => d.Length > 0)
                .ToHashSet();
        }

        var roots = BuildFolderTrees(dirs);

        // Populate asset counts for each folder node
        if (_modeManager.IsStandalone && roots.Count > 0)
        {
            await using var db = _modeManager.CreateDbContext();
            var allAssets = await db.DigitalAssets
                .Select(a => a.StoragePath)
                .ToListAsync(ct);

            foreach (var root in roots)
            {
                PopulateFolderCounts(root, allAssets);
            }
        }

        var newFolders = new ObservableCollection<FolderNode>();
        foreach (var root in roots)
            newFolders.Add(root);
        Folders = newFolders;
    }

    private static List<FolderNode> BuildFolderTrees(HashSet<string> dirs)
    {
        if (dirs.Count == 0)
            return [new FolderNode { Name = "No Folders", Path = "", IsExpanded = true }];

        // Normalize all paths
        var normalizedDirs = dirs.Select(d => d.Replace("\\", "/").TrimEnd('/')).ToList();

        // Find common prefix
        var commonPrefix = FindCommonPathPrefix(normalizedDirs);

        // If common prefix is empty or just a drive letter, show multiple roots
        if (string.IsNullOrEmpty(commonPrefix) || commonPrefix.Length <= 1)
        {
            // Group by top-level directory (skip empty first segment from leading /)
            var byRoot = normalizedDirs
                .GroupBy(d => d.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Folders")
                .ToList();

            var roots = new List<FolderNode>();
            foreach (var group in byRoot.OrderBy(g => g.Key))
            {
                var folderRoot = new FolderNode { Name = group.Key, Path = group.Key, IsExpanded = false };
                foreach (var dir in group.OrderBy(d => d))
                {
                    var relative = dir[(group.Key.Length)..].TrimStart('/');
                    AddPathToTree(folderRoot, relative, dir);
                }
                roots.Add(folderRoot);
            }
            return roots;
        }

        // Strip common prefix and use last segment as root name
        var rootName = commonPrefix.Split('/').LastOrDefault() ?? "Folders";
        var root = new FolderNode { Name = rootName, Path = commonPrefix, IsExpanded = true };

        foreach (var dir in normalizedDirs.OrderBy(d => d))
        {
            var relative = dir[(commonPrefix.Length)..].TrimStart('/');
            if (string.IsNullOrEmpty(relative)) continue;
            AddPathToTree(root, relative, dir);
        }

        return [root];
    }

    private static void AddPathToTree(FolderNode root, string relativePath, string fullPath)
    {
        var parts = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = root;
        var cumulative = root.Path;

        foreach (var part in parts)
        {
            cumulative = cumulative.Length == 0 ? part : $"{cumulative}/{part}";
            var existing = current.Children.FirstOrDefault(c => c.Name == part);
            if (existing == null)
            {
                existing = new FolderNode { Name = part, Path = cumulative };
                current.Children.Add(existing);
            }
            current = existing;
        }
    }

    private static void PopulateFolderCounts(FolderNode node, List<string> assetPaths)
    {
        var prefix = node.Path.Replace("\\", "/");
        var directCount = assetPaths.Count(p =>
        {
            var dir = Path.GetDirectoryName(p.Replace("\\", "/")) ?? "";
            return dir == prefix;
        });

        // Count also includes assets in subfolders
        var totalCount = assetPaths.Count(p =>
        {
            var dir = Path.GetDirectoryName(p.Replace("\\", "/")) ?? "";
            return dir.StartsWith(prefix + "/") || dir == prefix;
        });

        node.AssetCount = totalCount;

        foreach (var child in node.Children)
            PopulateFolderCounts(child, assetPaths);
    }

    private static string FindCommonPathPrefix(IEnumerable<string> paths)
    {
        var list = paths.Where(p => p.Length > 0).OrderBy(p => p.Length).ToList();
        if (list.Count < 2) return "";

        var first = list[0];
        var last = list[^1];
        var prefixLen = 0;
        var minLen = Math.Min(first.Length, last.Length);

        for (var i = 0; i < minLen; i++)
        {
            if (first[i] != last[i]) break;
            prefixLen = i + 1;
        }

        if (prefixLen == 0) return "";

        var trimmed = first[..prefixLen];
        var lastSep = trimmed.LastIndexOf('/');
        return lastSep > 0 ? trimmed[..lastSep] : "";
    }

    private async Task LoadCollectionsAsync(CancellationToken ct = default)
    {
        var newCollections = new List<CollectionNode>();

        if (_modeManager.IsStandalone)
        {
            await using var db = _modeManager.CreateDbContext();
            var all = await db.Collections
                .Select(c => new { c.Id, c.Name, c.ParentId, AssetCount = c.Assets.Count })
                .ToListAsync(ct);
            var allCols = all.Select(c => new CollectionNode
            {
                Id = c.Id, Name = c.Name, ParentId = c.ParentId, AssetCount = c.AssetCount
            }).ToList();

            foreach (var col in allCols.Where(c => c.ParentId == null))
                newCollections.Add(BuildTree(col, allCols));
        }

        var coll = new ObservableCollection<CollectionNode>();
        foreach (var col in newCollections)
            coll.Add(col);
        Collections = coll;
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
            await using var db = _modeManager.CreateDbContext();
            var keywords = await db.Keywords
                .Include(k => k.Assets)
                .ToListAsync(ct);

            root = new KeywordNode { Name = "All Keywords", Path = "", IsExpanded = true };

            // Build hierarchy from flat list
            var keywordDict = keywords.ToDictionary(k => k.Id);
            var nodeDict = new Dictionary<Guid, KeywordNode>();

            foreach (var kw in keywords)
            {
                var node = new KeywordNode
                {
                    Name = kw.Name,
                    Path = kw.Name,
                    KeywordId = kw.Id,
                    AssetCount = kw.Assets.Count
                };
                nodeDict[kw.Id] = node;
            }

            // Link parents and build tree
            foreach (var kw in keywords.Where(k => k.ParentId.HasValue))
            {
                if (nodeDict.TryGetValue(kw.Id, out var childNode) &&
                    nodeDict.TryGetValue(kw.ParentId!.Value, out var parentNode))
                {
                    childNode.Path = $"{parentNode.Path}|{childNode.Name}";
                    parentNode.Children.Add(childNode);
                }
            }

            // Add root-level keywords to the tree root
            foreach (var kw in keywords.Where(k => !k.ParentId.HasValue))
            {
                if (nodeDict.TryGetValue(kw.Id, out var node))
                {
                    root.Children.Add(node);
                }
            }

            // Propagate counts upward (leaf counts already set, add children to parents)
            PropagateKeywordCounts(root);
        }
        else
        {
            root = new KeywordNode { Name = "All Keywords", Path = "", IsExpanded = true };
        }

        Keywords = new ObservableCollection<KeywordNode> { root };
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

    private async Task LoadMediaFormatCountsAsync(CancellationToken ct = default)
    {
        if (_modeManager.IsStandalone)
        {
            await using var db = _modeManager.CreateDbContext();
            var total = await db.DigitalAssets.CountAsync(ct);
            var images = await db.DigitalAssets.CountAsync(a => a.Type == Adam.Shared.Models.AssetType.Image, ct);
            var videos = await db.DigitalAssets.CountAsync(a => a.Type == Adam.Shared.Models.AssetType.Video, ct);
            var docs = await db.DigitalAssets.CountAsync(a => a.Type == Adam.Shared.Models.AssetType.Document, ct);
            var audio = await db.DigitalAssets.CountAsync(a => a.Type == Adam.Shared.Models.AssetType.Audio, ct);

            MediaFormats[0].Count = total;
            MediaFormats[1].Count = images;
            MediaFormats[2].Count = videos;
            MediaFormats[3].Count = docs;
            MediaFormats[4].Count = audio;
        }
    }

    private async Task LoadMetadataCategoriesAsync(CancellationToken ct)
    {
        var newCats = new List<CategoryNode>();

        if (_modeManager.IsStandalone)
        {
            await using var db = _modeManager.CreateDbContext();
            var categories = await db.Categories
                .Include(c => c.Assets)
                .ToListAsync(ct);

            var total = categories.Sum(c => c.Assets.Count);
            var root = new CategoryNode { Name = "All", Count = total, IsExpanded = true };

            // Build hierarchy from flat list
            var nodeDict = new Dictionary<Guid, CategoryNode>();

            foreach (var cat in categories)
            {
                var node = new CategoryNode
                {
                    Name = cat.Name,
                    CategoryId = cat.Id,
                    Count = cat.Assets.Count
                };
                nodeDict[cat.Id] = node;
            }

            // Link parents and build tree
            foreach (var cat in categories.Where(c => c.ParentId.HasValue))
            {
                if (nodeDict.TryGetValue(cat.Id, out var childNode) &&
                    nodeDict.TryGetValue(cat.ParentId!.Value, out var parentNode))
                {
                    parentNode.Children.Add(childNode);
                }
            }

            // Add root-level categories to the tree root
            foreach (var cat in categories.Where(c => !c.ParentId.HasValue))
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
        else
        {
            newCats.Add(new CategoryNode { Name = "All", Count = 0, IsExpanded = true });
        }

        var cats = new ObservableCollection<CategoryNode>();
        foreach (var cat in newCats)
            cats.Add(cat);
        MetadataCategories = cats;
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
