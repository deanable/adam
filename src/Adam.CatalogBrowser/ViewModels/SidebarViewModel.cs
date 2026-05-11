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
    private string _selectedCategory = "All";
    private FolderNode? _selectedFolder;
    private CollectionNode? _selectedCollection;
    private KeywordNode? _selectedKeyword;

    public SidebarViewModel(ModeManager modeManager)
    {
        _modeManager = modeManager;
    }

    public ObservableCollection<FolderNode> Folders { get; } = [];
    public ObservableCollection<CollectionNode> Collections { get; } = [];
    public ObservableCollection<KeywordNode> Keywords { get; } = [];
    public ObservableCollection<CategoryNode> Categories { get; } =
    [
        new() { Name = "All", Count = 0 },
        new() { Name = "Images", Count = 0 },
        new() { Name = "Videos", Count = 0 },
        new() { Name = "Documents", Count = 0 },
        new() { Name = "Audio", Count = 0 },
    ];

    public string SelectedCategory
    {
        get => _selectedCategory;
        set { _selectedCategory = value; OnPropertyChanged(); OnCategoryChanged(); }
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
        await Task.WhenAll(
            LoadFoldersAsync(ct),
            LoadCollectionsAsync(ct),
            LoadKeywordsAsync(ct),
            LoadCategoryCountsAsync(ct));
    }

    private async Task LoadFoldersAsync(CancellationToken ct = default)
    {
        Folders.Clear();
        var paths = new HashSet<string>();

        if (_modeManager.IsStandalone)
        {
            await using var db = _modeManager.CreateDbContext();
            paths = await db.DigitalAssets
                .Select(a => a.StoragePath)
                .Where(p => p != null)
                .Select(p => System.IO.Path.GetDirectoryName(p) ?? "")
                .Distinct()
                .Where(d => d.Length > 0)
                .ToHashSetAsync(ct);
        }
        else if (_modeManager.IsMultiUser && _modeManager.BrokerClient?.IsConnected == true)
        {
            // multi-user folder enumeration through broker
        }

        var root = new FolderNode { Name = "All Folders", Path = "", IsExpanded = true };
        foreach (var dir in paths.OrderBy(p => p))
        {
            var parts = dir.Split('/', '\\');
            var current = root;
            foreach (var part in parts)
            {
                if (part.Length == 0) continue;
                var existing = current.Children.FirstOrDefault(c => c.Name == part);
                if (existing == null)
                {
                    existing = new FolderNode { Name = part, Path = dir };
                    current.Children.Add(existing);
                }
                current = existing;
            }
        }
        Folders.Add(root);
    }

    private async Task LoadCollectionsAsync(CancellationToken ct = default)
    {
        Collections.Clear();

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
                Collections.Add(BuildTree(col, allCols));
        }
    }

    private static CollectionNode BuildTree(CollectionNode node, List<CollectionNode> all)
    {
        foreach (var child in all.Where(c => c.ParentId == node.Id))
            node.Children.Add(BuildTree(child, all));
        return node;
    }

    private async Task LoadKeywordsAsync(CancellationToken ct = default)
    {
        Keywords.Clear();

        if (_modeManager.IsStandalone)
        {
            await using var db = _modeManager.CreateDbContext();
            var names = await db.Keywords
                .Select(k => k.Name)
                .Distinct()
                .ToListAsync(ct);
            foreach (var name in names.OrderBy(n => n))
                Keywords.Add(new KeywordNode { Name = name });
        }
    }

    private async Task LoadCategoryCountsAsync(CancellationToken ct = default)
    {
        if (_modeManager.IsStandalone)
        {
            await using var db = _modeManager.CreateDbContext();
            var total = await db.DigitalAssets.CountAsync(ct);
            Categories[0].Count = total;
            Categories[1].Count = await db.DigitalAssets.CountAsync(a => a.Type == Adam.Shared.Models.AssetType.Image, ct);
            Categories[2].Count = await db.DigitalAssets.CountAsync(a => a.Type == Adam.Shared.Models.AssetType.Video, ct);
            Categories[3].Count = await db.DigitalAssets.CountAsync(a => a.Type == Adam.Shared.Models.AssetType.Document, ct);
            Categories[4].Count = await db.DigitalAssets.CountAsync(a => a.Type == Adam.Shared.Models.AssetType.Audio, ct);
        }
    }

    private void OnCategoryChanged() => FilterChanged?.Invoke();
    private void OnFilterChanged() => FilterChanged?.Invoke();

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public class FolderNode : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isSelected;

    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;

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
    private bool _isSelected;
    public string Name { get; set; } = string.Empty;
    public bool IsSelected { get => _isSelected; set { _isSelected = value; OnPropertyChanged(); } }

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
