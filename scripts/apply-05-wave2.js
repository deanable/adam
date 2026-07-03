const fs = require('fs');
const path = require('path');

const sidebarPath = path.join(__dirname, '..', 'src', 'Adam.CatalogBrowser', 'ViewModels', 'SidebarViewModel.cs');
const appPath = path.join(__dirname, '..', 'src', 'Adam.CatalogBrowser', 'App.axaml.cs');

// ============================================================
// 1. Transform SidebarViewModel.cs
// ============================================================
let content = fs.readFileSync(sidebarPath, 'utf8');

// --- A: Add 4 new service fields to the constructor ---
// Pattern: existing constructor with 6 params → 10 params

// A1: Add fields after _savedSearchService
content = content.replace(
    '    private readonly SavedSearchService _savedSearchService;',
    '    private readonly SavedSearchService _savedSearchService;\n    private readonly FolderTreeService _folderTreeService;\n    private readonly CollectionTreeService _collectionTreeService;\n    private readonly KeywordTreeService _keywordTreeService;\n    private readonly CategoryTreeService _categoryTreeService;'
);

// A2: Change constructor signature and add assignment lines
content = content.replace(
    'public SidebarViewModel(ModeManager modeManager, ILogger<SidebarViewModel> logger, MediaFormatService mediaFormatService, DateTakenTreeService dateTakenTreeService, SavedSearchService savedSearchService, FolderScanService? folderScanService = null)',
    'public SidebarViewModel(ModeManager modeManager, ILogger<SidebarViewModel> logger, MediaFormatService mediaFormatService, DateTakenTreeService dateTakenTreeService, SavedSearchService savedSearchService, FolderTreeService folderTreeService, CollectionTreeService collectionTreeService, KeywordTreeService keywordTreeService, CategoryTreeService categoryTreeService, FolderScanService? folderScanService = null)'
);

// A3: Add assignments after _savedSearchService = savedSearchService;
content = content.replace(
    '        _savedSearchService = savedSearchService;\n        _selectedMediaFormat = MediaFormats[0];',
    '        _savedSearchService = savedSearchService;\n        _folderTreeService = folderTreeService;\n        _collectionTreeService = collectionTreeService;\n        _keywordTreeService = keywordTreeService;\n        _categoryTreeService = categoryTreeService;\n        _selectedMediaFormat = MediaFormats[0];'
);

// --- B: Remove backing fields for _collections, _keywords, _metadataCategories ---
content = content.replace(
    '    private ObservableCollection<CollectionNode> _collections = [];\n    private ObservableCollection<KeywordNode> _keywords = [];\n    private ObservableCollection<CategoryNode> _metadataCategories = [];',
    '    // Collections, Keywords, MetadataCategories now owned by their respective services'
);

// --- C: Replace Folders property to delegate to service ---
content = content.replace(
    '    public ObservableCollection<FolderNode> Folders { get; } = [];',
    '    public ObservableCollection<FolderNode> Folders => _folderTreeService.Roots;'
);

// --- D: Replace Collections property to delegate to service ---
content = content.replace(
    '    public ObservableCollection<CollectionNode> Collections\n    {\n        get => _collections;\n        private set { _collections = value; OnPropertyChanged(); }\n    }',
    '    public ObservableCollection<CollectionNode> Collections => _collectionTreeService.Roots;'
);

// --- E: Replace Keywords property to delegate to service ---
content = content.replace(
    '    public ObservableCollection<KeywordNode> Keywords\n    {\n        get => _keywords;\n        private set { _keywords = value; OnPropertyChanged(); }\n    }',
    '    public ObservableCollection<KeywordNode> Keywords => _keywordTreeService.Roots;'
);

// --- F: Replace MetadataCategories property to delegate to service ---
content = content.replace(
    '    public ObservableCollection<CategoryNode> MetadataCategories\n    {\n        get => _metadataCategories;\n        private set { _metadataCategories = value; OnPropertyChanged(); }\n    }',
    '    public ObservableCollection<CategoryNode> MetadataCategories => _categoryTreeService.Roots;'
);

// --- G: Update LoadAsync to call service methods instead of inline methods ---
content = content.replace(
    '            await Task.WhenAll(\n                LoadFoldersAsync(ct),\n                LoadCollectionsAsync(ct),\n                LoadKeywordsAsync(ct),\n                _mediaFormatService.LoadAsync(ct),\n                LoadMetadataCategoriesAsync(ct),\n                _dateTakenTreeService.LoadAsync(ct),',
    '            await Task.WhenAll(\n                _folderTreeService.LoadAsync(ct),\n                _collectionTreeService.LoadAsync(ct),\n                _keywordTreeService.LoadAsync(ct),\n                _mediaFormatService.LoadAsync(ct),\n                _categoryTreeService.LoadAsync(ct),\n                _dateTakenTreeService.LoadAsync(ct),'
);

// --- H: Remove GetDirectoryName ---
content = content.replace(
    '    private static string GetDirectoryName(string path)\n    {\n        if (string.IsNullOrEmpty(path)) return "";\n        var lastSep = path.LastIndexOfAny([\'/\', \'\\\\\']);\n        return lastSep > 0 ? path[..lastSep] : "";\n    }\n',
    ''
);

// --- I: Remove LoadFoldersAsync ---
const loadFoldersEndMarker = '        _logger.LogInformation("[LoadFoldersAsync] Completed");\n    }';
const loadFoldersNext = '\n    private static FolderNode? FindFolderNode(FolderNode root, string path)';
content = content.replace(
    '    private async Task LoadFoldersAsync(CancellationToken ct = default)\n    {' +
    content.substring(
        content.indexOf('    private async Task LoadFoldersAsync(CancellationToken ct = default)\n    {'),
        content.indexOf(loadFoldersEndMarker) + loadFoldersEndMarker.length
    ).replace('    private async Task LoadFoldersAsync(CancellationToken ct = default)\n    {', '')
    ) + '\n    }\n\n    private static FolderNode? FindFolderNode(FolderNode root, string path)',
    // Just try to find and remove the whole thing via start/end markers
    ''
);

// Let me do this differently - find the exact blocks and remove them
// Actually, let me just find and remove the old methods by their signatures

// The script approach is getting too complex with all the escaping. Let me use a simpler approach.
console.log("Script approach too fragile for complex multi-line removals. Using str_replace instead.");
console.log("Write the file as-is for now and use targeted str_replace calls.");
fs.writeFileSync(sidebarPath, content, 'utf8');
console.log("Saved intermediate state");

// ============================================================
// 2. Register 4 services in App.axaml.cs
// ============================================================
let appContent = fs.readFileSync(appPath, 'utf8');

// Add service registrations after SavedSearchService line
appContent = appContent.replace(
    "            services.AddSingleton<Adam.CatalogBrowser.Services.SavedSearchService>();",
    "            services.AddSingleton<Adam.CatalogBrowser.Services.SavedSearchService>();\n            services.AddSingleton<FolderTreeService>();\n            services.AddSingleton<CollectionTreeService>();\n            services.AddSingleton<KeywordTreeService>();\n            services.AddSingleton<CategoryTreeService>();"
);

fs.writeFileSync(appPath, appContent, 'utf8');
console.log("Updated App.axaml.cs with 4 new service registrations");
