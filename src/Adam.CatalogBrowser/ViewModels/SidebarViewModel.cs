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
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    public SidebarViewModel(ModeManager modeManager)
    {
        _modeManager = modeManager;
        _selectedMediaFormat = MediaFormats[0];
    }

    public ObservableCollection<FolderNode> Folders { get; } = [];
    public ObservableCollection<CollectionNode> Collections { get; } = [];
    public ObservableCollection<KeywordNode> Keywords { get; } = [];
    public ObservableCollection<CategoryNode> MediaFormats { get; } =
    [
        new() { Name = "All", Count = 0 },
        new() { Name = "Images", Count = 0 },
        new() { Name = "Videos", Count = 0 },
        new() { Name = "Documents", Count = 0 },
        new() { Name = "Audio", Count = 0 },
    ];
    public ObservableCollection<CategoryNode> MetadataCategories { get; } = [];

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
                .Select(p => Path.GetDirectoryName(p.Replace('\\', '/')) ?? "")
                .Where(d => d.Length > 0)
                .ToHashSet();
        }
        else if (_modeManager.IsMultiUser && _modeManager.BrokerClient?.IsConnected == true)
        {
            // multi-user folder enumeration through broker
        }

        var commonPrefix = FindCommonPathPrefix(dirs);

        var root = new FolderNode { Name = commonPrefix.Length > 0 ? ToDisplayName(commonPrefix) : "All Folders", Path = commonPrefix, IsExpanded = true };
        foreach (var dir in dirs.OrderBy(p => p))
        {
            var relative = dir;
            if (commonPrefix.Length > 0 && dir.StartsWith(commonPrefix, StringComparison.OrdinalIgnoreCase))
                relative = dir[commonPrefix.Length..].TrimStart('/');

            if (relative.Length == 0) continue;

            var parts = relative.Split('/');
            var current = root;
            var cumulative = commonPrefix.Length > 0 ? commonPrefix : "";
            foreach (var part in parts)
            {
                if (part.Length == 0) continue;
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

        if (_modeManager.IsStandalone)
        {
            await using var db = _modeManager.CreateDbContext();
            var allStoragePaths = await db.DigitalAssets
                .Select(a => a.StoragePath)
                .ToListAsync(ct);

            var folderCounts = allStoragePaths
                .Where(p => !string.IsNullOrEmpty(p))
                .GroupBy(p => Path.GetDirectoryName(p.Replace('\\', '/')) ?? "")
                .Where(g => g.Key.Length > 0)
                .Select(g => new { Dir = g.Key, Count = g.Count() })
                .ToList();

            foreach (var dir in folderCounts)
            {
                var node = FindFolderNode(root, dir.Dir);
                if (node != null)
                    node.AssetCount = dir.Count;
            }
        }

        Folders.Clear();
        Folders.Add(root);
    }

    private static string FindCommonPathPrefix(IEnumerable<string> paths)
    {
        var list = paths.Where(p => p.Length > 0).OrderBy(p => p.Length).ToList();
        if (list.Count < 2) return "";

        var first = list[0].Replace('\\', '/');
        var last = list[^1].Replace('\\', '/');
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

    private static string ToDisplayName(string path)
    {
        if (path.StartsWith("//"))
        {
            var afterUnc = path.IndexOf('/', 2);
            if (afterUnc > 0) return path[(afterUnc + 1)..];
        }
        if (path.Length >= 2 && path[1] == ':')
            return path.Length > 3 ? path[3..] : path;
        return path;
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

        Collections.Clear();
        foreach (var col in newCollections)
            Collections.Add(col);
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
            var names = await db.Keywords
                .Select(k => k.Name)
                .Distinct()
                .ToListAsync(ct);

            root = new KeywordNode { Name = "All Keywords", Path = "", IsExpanded = true };
            foreach (var name in names.OrderBy(n => n))
            {
                var parts = name.Split('|', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;

                var current = root;
                var cumulative = "";
                foreach (var part in parts)
                {
                    var trimmed = part.Trim();
                    if (trimmed.Length == 0) continue;
                    cumulative = cumulative.Length == 0 ? trimmed : $"{cumulative}|{trimmed}";
                    var existing = current.Children.FirstOrDefault(c => c.Name == trimmed);
                    if (existing == null)
                    {
                        existing = new KeywordNode { Name = trimmed, Path = cumulative, IsExpanded = true };
                        current.Children.Add(existing);
                    }
                    current = existing;
                }
            }
        }
        else
        {
            root = new KeywordNode { Name = "All Keywords", Path = "", IsExpanded = true };
        }

        Keywords.Clear();
        Keywords.Add(root);
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
            var allCats = await db.MetadataProfiles
                .Where(m => m.Category != null && m.Category.Length > 0)
                .Select(m => m.Category!)
                .ToListAsync(ct);

            var distinct = allCats
                .SelectMany(c => c.Split(';', StringSplitOptions.RemoveEmptyEntries))
                .Select(c => c.Trim())
                .Where(c => c.Length > 0)
                .Distinct()
                .OrderBy(c => c)
                .ToList();

            var totalProfiles = await db.MetadataProfiles.CountAsync(m => m.Category != null && m.Category.Length > 0, ct);
            newCats.Add(new CategoryNode { Name = "All", Count = totalProfiles });

            foreach (var cat in distinct)
            {
                var count = await db.MetadataProfiles
                    .CountAsync(m => m.Category != null && m.Category.Contains(";" + cat + ";") || 
                                     m.Category != null && m.Category.StartsWith(cat + ";") ||
                                     m.Category != null && m.Category.EndsWith(";" + cat) ||
                                     m.Category != null && m.Category == cat, ct);
                newCats.Add(new CategoryNode { Name = cat, Count = count });
            }
        }
        else
        {
            newCats.Add(new CategoryNode { Name = "All", Count = 0 });
        }

        MetadataCategories.Clear();
        foreach (var cat in newCats)
            MetadataCategories.Add(cat);
    }

    private void OnMediaFormatChanged() => FilterChanged?.Invoke();
    private void OnFilterChanged() => FilterChanged?.Invoke();

    private static FolderNode? FindFolderNode(FolderNode root, string path)
    {
        if (root.Path == path) return root;
        foreach (var child in root.Children)
        {
            var found = FindFolderNode(child, path);
            if (found != null) return found;
        }
        return null;
    }

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
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public bool IsExpanded { get => _isExpanded; set { _isExpanded = value; OnPropertyChanged(); } }
    public bool IsSelected { get => _isSelected; set { _isSelected = value; OnPropertyChanged(); } }
    public ObservableCollection<KeywordNode> Children { get; } = [];

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public class CategoryNode : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private int _count;

    public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
    public int Count { get => _count; set { _count = value; OnPropertyChanged(); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
