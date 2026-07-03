const fs = require("fs");
const path = require("path");

const sidebarPath = path.join(__dirname, "..", "src", "Adam.CatalogBrowser", "ViewModels", "SidebarViewModel.cs");
const appPath = path.join(__dirname, "..", "src", "Adam.CatalogBrowser", "App.axaml.cs");

// Read files
let content = fs.readFileSync(sidebarPath, "utf8");

// === 1. Add 4 service fields after _savedSearchService ===
content = content.replace(
  "    private readonly SavedSearchService _savedSearchService;",
  "    private readonly SavedSearchService _savedSearchService;\n    private readonly FolderTreeService _folderTreeService;\n    private readonly CollectionTreeService _collectionTreeService;\n    private readonly KeywordTreeService _keywordTreeService;\n    private readonly CategoryTreeService _categoryTreeService;"
);

// === 2. Update constructor signature ===
content = content.replace(
  "public SidebarViewModel(ModeManager modeManager, ILogger<SidebarViewModel> logger, MediaFormatService mediaFormatService, DateTakenTreeService dateTakenTreeService, SavedSearchService savedSearchService, FolderScanService? folderScanService = null)",
  "public SidebarViewModel(ModeManager modeManager, ILogger<SidebarViewModel> logger, MediaFormatService mediaFormatService, DateTakenTreeService dateTakenTreeService, SavedSearchService savedSearchService, FolderTreeService folderTreeService, CollectionTreeService collectionTreeService, KeywordTreeService keywordTreeService, CategoryTreeService categoryTreeService, FolderScanService? folderScanService = null)"
);

// === 3. Add service assignments after _savedSearchService = savedSearchService; ===
content = content.replace(
  "        _savedSearchService = savedSearchService;\n        _selectedMediaFormat = MediaFormats[0];",
  "        _savedSearchService = savedSearchService;\n        _folderTreeService = folderTreeService;\n        _collectionTreeService = collectionTreeService;\n        _keywordTreeService = keywordTreeService;\n        _categoryTreeService = categoryTreeService;\n        _selectedMediaFormat = MediaFormats[0];"
);

// === 4. Remove backing fields _collections, _keywords, _metadataCategories ===
content = content.replace(
  "    private ObservableCollection<CollectionNode> _collections = [];\n    private ObservableCollection<KeywordNode> _keywords = [];\n    private ObservableCollection<CategoryNode> _metadataCategories = [];",
  "    // Collections, Keywords, MetadataCategories now owned by respective tree services"
);

// === 5. Replace Folders property ===
content = content.replace(
  "    public ObservableCollection<FolderNode> Folders { get; } = [];",
  "    public ObservableCollection<FolderNode> Folders => _folderTreeService.Roots;"
);

// === 6. Replace Collections property ===
content = content.replace(
  "    public ObservableCollection<CollectionNode> Collections\n    {\n        get => _collections;\n        private set { _collections = value; OnPropertyChanged(); }\n    }",
  "    public ObservableCollection<CollectionNode> Collections => _collectionTreeService.Roots;"
);

// === 7. Replace Keywords property ===
content = content.replace(
  "    public ObservableCollection<KeywordNode> Keywords\n    {\n        get => _keywords;\n        private set { _keywords = value; OnPropertyChanged(); }\n    }",
  "    public ObservableCollection<KeywordNode> Keywords => _keywordTreeService.Roots;"
);

// === 8. Replace MetadataCategories property ===
content = content.replace(
  "    public ObservableCollection<CategoryNode> MetadataCategories\n    {\n        get => _metadataCategories;\n        private set { _metadataCategories = value; OnPropertyChanged(); }\n    }",
  "    public ObservableCollection<CategoryNode> MetadataCategories => _categoryTreeService.Roots;"
);

// === 9. Update LoadAsync to call services ===
content = content.replace(
  "            await Task.WhenAll(\n                LoadFoldersAsync(ct),\n                LoadCollectionsAsync(ct),\n                LoadKeywordsAsync(ct),\n                _mediaFormatService.LoadAsync(ct),\n                LoadMetadataCategoriesAsync(ct),\n                _dateTakenTreeService.LoadAsync(ct),",
  "            await Task.WhenAll(\n                _folderTreeService.LoadAsync(ct),\n                _collectionTreeService.LoadAsync(ct),\n                _keywordTreeService.LoadAsync(ct),\n                _mediaFormatService.LoadAsync(ct),\n                _categoryTreeService.LoadAsync(ct),\n                _dateTakenTreeService.LoadAsync(ct),"
);

// === 10. Remove GetDirectoryName, LoadFoldersAsync, FindFolderNode ===
// Find and remove from "private static string GetDirectoryName" to after "private static FolderNode? FindFolderNode"
const getDirIdx = content.indexOf("    private static string GetDirectoryName(string path)");
const findFolderEndIdx = content.indexOf("    private async Task LoadCollectionsAsync", getDirIdx);
if (getDirIdx > 0 && findFolderEndIdx > 0) {
  content = content.slice(0, getDirIdx) + content.slice(findFolderEndIdx);
}
// Remove the extra blank line left behind
content = content.replace("\n\n\n    private async Task LoadCollectionsAsync", "\n\n    private async Task LoadCollectionsAsync");

// === 11. Remove LoadCollectionsAsync + BuildTree ===
const loadColIdx = content.indexOf("    private async Task LoadCollectionsAsync(CancellationToken ct = default)");
const loadKwIdx = content.indexOf("    private async Task LoadKeywordsAsync(CancellationToken ct = default)");
if (loadColIdx > 0 && loadKwIdx > 0) {
  content = content.slice(0, loadColIdx) + content.slice(loadKwIdx);
}

// Remove BuildTree that was between LoadCollections and LoadKeywords
const buildTreeIdx = content.indexOf("    private static CollectionNode BuildTree");
if (buildTreeIdx > 0 && buildTreeIdx < content.indexOf("    private async Task LoadKeywordsAsync")) {
  const afterBuildTree = content.indexOf("\n\n    private async Task LoadKeywordsAsync", buildTreeIdx);
  if (afterBuildTree > 0) {
    content = content.slice(0, buildTreeIdx) + content.slice(afterBuildTree);
  }
}

// === 12. Remove LoadKeywordsAsync ===
const loadKwStart = content.indexOf("    private async Task LoadKeywordsAsync(CancellationToken ct = default)");
const loadCatStart = content.indexOf("    private async Task LoadMetadataCategoriesAsync", loadKwStart);
if (loadKwStart > 0 && loadCatStart > 0) {
  content = content.slice(0, loadKwStart) + content.slice(loadCatStart);
}

// === 13. Remove LoadMetadataCategoriesAsync ===
const loadCatEnd = content.indexOf("    private async Task LoadMetadataCategoriesAsync(CancellationToken ct)");
const onMediaFormatStart = content.indexOf("\n\n    private void OnMediaFormatChanged()", loadCatEnd);
if (loadCatEnd > 0 && onMediaFormatStart > 0) {
  content = content.slice(0, loadCatEnd) + content.slice(onMediaFormatStart);
}

// === 14. Update RevealFolder to delegate ===
content = content.replace(
  "    private void RevealFolder(object? parameter)\n    {\n        FolderNode? folder = parameter as FolderNode;\n        if (folder == null || string.IsNullOrEmpty(folder.Path))\n            folder = SelectedFolder;\n        if (folder == null || string.IsNullOrEmpty(folder.Path)) return;\n\n        try\n        {\n            if (OperatingSystem.IsWindows())\n                System.Diagnostics.Process.Start(\"explorer.exe\", folder.Path);\n            else if (OperatingSystem.IsMacOS())\n                System.Diagnostics.Process.Start(\"open\", folder.Path);\n            else if (OperatingSystem.IsLinux())\n                System.Diagnostics.Process.Start(\"xdg-open\", folder.Path);\n        }\n        catch (Exception ex)\n        {\n            _logger.LogError(ex, \"Failed to reveal folder: {Path}\", folder.Path);\n        }\n    }",
  "    private void RevealFolder(object? parameter)\n    {\n        FolderNode? folder = parameter as FolderNode;\n        if (folder == null || string.IsNullOrEmpty(folder.Path))\n            folder = SelectedFolder;\n        _folderTreeService.RevealFolder(folder);\n    }"
);

// === 15. Update RescanFolderAsync to delegate ===
content = content.replace(
  "    private async Task RescanFolderAsync()\n    {\n        if (SelectedFolder == null || string.IsNullOrEmpty(SelectedFolder.Path)) return;\n\n        try\n        {\n            _logger.LogInformation(\"Re-scanning folder: {Path}\", SelectedFolder.Path);\n\n            var ingested = await _folderScanService.ScanFolderAsync(SelectedFolder.Path, recursive: true);\n\n            _logger.LogInformation(\"Re-scan complete for folder: {Path} — {Count} new asset(s) ingested\", SelectedFolder.Path, ingested);\n\n            // Refresh the sidebar data to update asset counts\n            await LoadAsync();\n        }\n        catch (Exception ex)\n        {\n            _logger.LogError(ex, \"Failed to rescan folder: {Path}\", SelectedFolder.Path);\n        }\n    }",
  "    private async Task RescanFolderAsync()\n    {\n        if (SelectedFolder == null || string.IsNullOrEmpty(SelectedFolder.Path)) return;\n        await _folderTreeService.RescanFolderAsync(SelectedFolder);\n        await LoadAsync();\n    }"
);

// === 16. Remove _folderScanService field (no longer needed separately, FolderTreeService owns it) ===
content = content.replace(
  "    private readonly FolderScanService _folderScanService;\n    ",
  "    "
);

// === 17. Remove _folderScanService fallback from constructor ===
content = content.replace(
  "        _folderScanService = folderScanService ?? new FolderScanService(modeManager, new PluginLoaderService(\n            Microsoft.Extensions.Options.Options.Create(new Adam.Shared.Configuration.PluginConfig()),\n            logger as ILogger<PluginLoaderService> ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<PluginLoaderService>.Instance));\n        ",
  "        "
);

// === 18. Delegate CRUD methods to services ===

// 18a. PromptCreateCollectionAsync
content = content.replace(
  "    private async Task PromptCreateCollectionAsync()\n    {\n        var parentId = SelectedCollection?.Id;\n        var name = await Views.InputDialog.ShowAsync(\n            App.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop\n                ? desktop.MainWindow\n                : null,\n            \"New Collection\",\n            \"Enter collection name:\",\n            \"Create\",\n            \"Cancel\");\n        if (string.IsNullOrWhiteSpace(name)) return;\n\n        try\n        {\n            if (_modeManager.IsMultiUser)\n            {\n                var resp = await SendBrokerRequestAsync(\n                    new CreateCollectionRequest { Name = name.Trim(), ParentId = parentId?.ToString() ?? \"\" },\n                    MessageTypeCode.CreateCollectionRequest);\n                if (resp == null || resp.StatusCode != 0)\n                {\n                    _logger.LogWarning(\"Broker rejected create collection: status={StatusCode}\", resp?.StatusCode);\n                    return;\n                }\n                await LoadAsync();\n                return;\n            }\n\n            await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);\n            db.Collections.Add(new Collection\n            {\n                Id = Guid.NewGuid(),\n                Name = name.Trim(),\n                ParentId = parentId\n            });\n            await db.SaveChangesAsync().ConfigureAwait(false);\n            await LoadAsync();\n        }\n        catch (Exception ex)\n        {\n            _logger.LogError(ex, \"Failed to create collection\");\n        }\n    }",
  "    private async Task PromptCreateCollectionAsync()\n    {\n        var parentId = SelectedCollection?.Id;\n        var name = await ShowInputDialog(\"New Collection\", \"Enter collection name:\", \"Create\");\n        if (string.IsNullOrWhiteSpace(name)) return;\n\n        try\n        {\n            await _collectionTreeService.CreateAsync(name.Trim(), parentId);\n            await LoadAsync();\n        }\n        catch (Exception ex)\n        {\n            _logger.LogError(ex, \"Failed to create collection\");\n        }\n    }"
);

// 18b. PromptRenameCollectionAsync
content = content.replace(
  "    private async Task PromptRenameCollectionAsync()\n    {\n        if (SelectedCollection == null) return;\n\n        var newName = await Views.InputDialog.ShowAsync(\n            App.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop\n                ? desktop.MainWindow\n                : null,\n            \"Rename Collection\",\n            $\"Rename '{SelectedCollection.Name}' to:\",\n            \"Rename\",\n            \"Cancel\",\n            defaultValue: SelectedCollection.Name);\n        if (string.IsNullOrWhiteSpace(newName)) return;\n\n        try\n        {\n            if (_modeManager.IsMultiUser)\n            {\n                var resp = await SendBrokerRequestAsync(\n                    new UpdateCollectionRequest { Id = SelectedCollection.Id.ToString(), Name = newName.Trim() },\n                    MessageTypeCode.UpdateCollectionRequest);\n                if (resp == null || resp.StatusCode != 0)\n                {\n                    _logger.LogWarning(\"Broker rejected rename collection: status={StatusCode}\", resp?.StatusCode);\n                    return;\n                }\n                await LoadAsync();\n                return;\n            }\n\n            await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);\n            var col = await db.Collections.FirstOrDefaultAsync(c => c.Id == SelectedCollection.Id).ConfigureAwait(false);\n            if (col == null) return;\n            col.Name = newName.Trim();\n            await db.SaveChangesAsync().ConfigureAwait(false);\n            await LoadAsync();\n        }\n        catch (Exception ex)\n        {\n            _logger.LogError(ex, \"Failed to rename collection\");\n        }\n    }",
  "    private async Task PromptRenameCollectionAsync()\n    {\n        if (SelectedCollection == null) return;\n\n        var newName = await ShowInputDialog(\"Rename Collection\", $\"Rename '{SelectedCollection.Name}' to:\", \"Rename\", SelectedCollection.Name);\n        if (string.IsNullOrWhiteSpace(newName)) return;\n\n        try\n        {\n            await _collectionTreeService.RenameAsync(SelectedCollection, newName.Trim());\n            await LoadAsync();\n        }\n        catch (Exception ex)\n        {\n            _logger.LogError(ex, \"Failed to rename collection\");\n        }\n    }"
);

// 18c. PromptDeleteCollectionAsync
content = content.replace(
  "    private async Task PromptDeleteCollectionAsync()\n    {\n        if (SelectedCollection == null) return;\n\n        var owner = GetOwnerWindow();\n        if (owner == null) return;\n\n        // T10.4: Count descendants for cascade confirmation\n            var descIds = new List<object>();\n            SelectedCollection?.CollectDescendantIds(descIds);\n            var descendantCount = descIds.Count;\n        var message = descendantCount > 0\n            ? $\"Are you sure you want to delete '{SelectedCollection.Name}' and all {descendantCount} sub-collections?\\\\n\\\\n\" +\n              $\"The collection(s) will be removed but the assets within them will not be deleted.\"\n            : $\"Are you sure you want to delete '{SelectedCollection.Name}'?\\\\n\\\\n\" +\n              $\"This will remove the collection but not the assets within it.\";\n\n        var confirmed = await Views.ConfirmationDialog.ShowAsync(owner, \"Delete Collection\",\n            message, \"Delete\", \"Cancel\", isDestructive: true);\n        if (!confirmed) return;\n\n        try\n        {\n            // Collect all descendant IDs recursively\n            var allIds = new List<Guid> { SelectedCollection.Id };\n            CollectDescendantCollectionIds(SelectedCollection, allIds);\n\n            if (_modeManager.IsMultiUser)\n            {\n                var resp = await SendBrokerRequestAsync(\n                    new DeleteCollectionRequest\n                    {\n                        Id = SelectedCollection.Id.ToString(),\n                        CascadeChildren = true\n                    },\n                    MessageTypeCode.DeleteCollectionRequest);\n                if (resp == null || resp.StatusCode != 0)\n                {\n                    _logger.LogWarning(\"Broker rejected delete collection: status={StatusCode}\", resp?.StatusCode);\n                    return;\n                }\n                SelectedCollection = null;\n                await LoadAsync();\n                return;\n            }\n\n            await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);\n            var cols = await db.Collections\n                .Where(c => allIds.OfType<Guid>().Contains(c.Id))\n                .ToListAsync().ConfigureAwait(false);\n            db.Collections.RemoveRange(cols);\n            await db.SaveChangesAsync().ConfigureAwait(false);\n            SelectedCollection = null;\n            await LoadAsync();\n        }\n        catch (Exception ex)\n        {\n            _logger.LogError(ex, \"Failed to delete collection (cascade)\");\n        }\n    }",
  "    private async Task PromptDeleteCollectionAsync()\n    {\n        if (SelectedCollection == null) return;\n\n        var owner = GetOwnerWindow();\n        if (owner == null) return;\n\n        // T10.4: Count descendants for cascade confirmation\n        var descIds = new List<object>();\n        SelectedCollection?.CollectDescendantIds(descIds);\n        var descendantCount = descIds.Count;\n        var message = descendantCount > 0\n            ? $\"Are you sure you want to delete '{SelectedCollection.Name}' and all {descendantCount} sub-collections?\\\\n\\\\n\" +\n              $\"The collection(s) will be removed but the assets within them will not be deleted.\"\n            : $\"Are you sure you want to delete '{SelectedCollection.Name}'?\\\\n\\\\n\" +\n              $\"This will remove the collection but not the assets within it.\";\n\n        var confirmed = await Views.ConfirmationDialog.ShowAsync(owner, \"Delete Collection\",\n            message, \"Delete\", \"Cancel\", isDestructive: true);\n        if (!confirmed) return;\n\n        try\n        {\n            await _collectionTreeService.DeleteWithCascadeAsync(SelectedCollection);\n            SelectedCollection = null;\n            await LoadAsync();\n        }\n        catch (Exception ex)\n        {\n            _logger.LogError(ex, \"Failed to delete collection (cascade)\");\n        }\n    }"
);

// 18d. PromptCreateKeywordAsync
content = content.replace(
  "    private async Task PromptCreateKeywordAsync()\n    {\n        var parentId = SelectedKeyword?.KeywordId;\n        var name = await Views.InputDialog.ShowAsync(\n            App.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop\n                ? desktop.MainWindow\n                : null,\n            \"New Keyword\",\n            \"Enter keyword name:\",\n            \"Create\",\n            \"Cancel\");\n        if (string.IsNullOrWhiteSpace(name)) return;\n\n        try\n        {\n            if (_modeManager.IsMultiUser)\n            {\n                var resp = await SendBrokerRequestAsync(\n                    new CreateKeywordRequest { Name = name.Trim(), ParentId = parentId?.ToString() ?? \"\" },\n                    MessageTypeCode.CreateKeywordRequest);\n                if (resp == null || resp.StatusCode != 0)\n                {\n                    _logger.LogWarning(\"Broker rejected create keyword: status={StatusCode}\", resp?.StatusCode);\n                    return;\n                }\n                await LoadAsync();\n                return;\n            }\n\n            await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);\n            db.Keywords.Add(new Keyword\n            {\n                Id = Guid.NewGuid(),\n                Name = name.Trim(),\n                NormalizedName = name.Trim().ToUpperInvariant(),\n                ParentId = parentId\n            });\n            await db.SaveChangesAsync().ConfigureAwait(false);\n            await LoadAsync();\n        }\n        catch (Exception ex)\n        {\n            _logger.LogError(ex, \"Failed to create keyword\");\n        }\n    }",
  "    private async Task PromptCreateKeywordAsync()\n    {\n        var parentId = SelectedKeyword?.KeywordId;\n        var name = await ShowInputDialog(\"New Keyword\", \"Enter keyword name:\", \"Create\");\n        if (string.IsNullOrWhiteSpace(name)) return;\n\n        try\n        {\n            await _keywordTreeService.CreateAsync(name.Trim(), parentId);\n            await LoadAsync();\n        }\n        catch (Exception ex)\n        {\n            _logger.LogError(ex, \"Failed to create keyword\");\n        }\n    }"
);

// 18e. PromptRenameKeywordAsync
content = content.replace(
  "    private async Task PromptRenameKeywordAsync()\n    {\n        if (SelectedKeyword == null) return;\n\n        var newName = await Views.InputDialog.ShowAsync(\n            App.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop\n                ? desktop.MainWindow\n                : null,\n            \"Rename Keyword\",\n            $\"Rename '{SelectedKeyword.Name}' to:\",\n            \"Rename\",\n            \"Cancel\",\n            defaultValue: SelectedKeyword.Name);\n        if (string.IsNullOrWhiteSpace(newName)) return;\n\n        try\n        {\n            if (_modeManager.IsMultiUser)\n            {\n                var resp = await SendBrokerRequestAsync(\n                    new UpdateKeywordRequest { Id = SelectedKeyword.KeywordId.ToString(), Name = newName.Trim() },\n                    MessageTypeCode.UpdateKeywordRequest);\n                if (resp == null || resp.StatusCode != 0)\n                {\n                    _logger.LogWarning(\"Broker rejected rename keyword: status={StatusCode}\", resp?.StatusCode);\n                    return;\n                }\n                await LoadAsync();\n                return;\n            }\n\n            await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);\n            var kw = await db.Keywords.FirstOrDefaultAsync(k => k.Id == SelectedKeyword.KeywordId).ConfigureAwait(false);\n            if (kw == null) return;\n            kw.Name = newName.Trim();\n            kw.NormalizedName = newName.Trim().ToUpperInvariant();\n            await db.SaveChangesAsync().ConfigureAwait(false);\n            await LoadAsync();\n        }\n        catch (Exception ex)\n        {\n            _logger.LogError(ex, \"Failed to rename keyword\");\n        }\n    }",
  "    private async Task PromptRenameKeywordAsync()\n    {\n        if (SelectedKeyword == null) return;\n\n        var newName = await ShowInputDialog(\"Rename Keyword\", $\"Rename '{SelectedKeyword.Name}' to:\", \"Rename\", SelectedKeyword.Name);\n        if (string.IsNullOrWhiteSpace(newName)) return;\n\n        try\n        {\n            await _keywordTreeService.RenameAsync(SelectedKeyword, newName.Trim());\n            await LoadAsync();\n        }\n        catch (Exception ex)\n        {\n            _logger.LogError(ex, \"Failed to rename keyword\");\n        }\n    }"
);

// 18f. PromptDeleteKeywordAsync
content = content.replace(
  "    private async Task PromptDeleteKeywordAsync()\n    {\n        if (SelectedKeyword == null) return;\n\n        var owner = GetOwnerWindow();\n        if (owner == null) return;\n\n        // T10.4: Count descendants for cascade confirmation\n        var descIds = new List<object>(); SelectedKeyword?.CollectDescendantIds(descIds);\n        var descendantCount = descIds.Count;\n        var message = descendantCount > 0\n            ? $\"Are you sure you want to delete '{SelectedKeyword.Name}' and all {descendantCount} sub-keywords?\\\\n\\\\n\" +\n              $\"The keyword(s) will be removed from all assets. This action cannot be undone.\"\n            : $\"Are you sure you want to delete '{SelectedKeyword.Name}'?\\\\n\\\\n\" +\n              $\"The keyword will be removed from all assets. This action cannot be undone.\";\n\n        var confirmed = await Views.ConfirmationDialog.ShowAsync(owner, \"Delete Keyword\",\n            message, \"Delete\", \"Cancel\", isDestructive: true);\n        if (!confirmed) return;\n\n        try\n        {\n            // Collect all descendant IDs recursively\n            var allIds = new List<Guid> { SelectedKeyword.KeywordId };\n            CollectDescendantKeywordIds(SelectedKeyword, allIds);\n\n            if (_modeManager.IsMultiUser)\n            {\n                var resp = await SendBrokerRequestAsync(\n                    new DeleteKeywordRequest\n                    {\n                        Id = SelectedKeyword.KeywordId.ToString(),\n                        CascadeChildren = true\n                    },\n                    MessageTypeCode.DeleteKeywordRequest);\n                if (resp == null || resp.StatusCode != 0)\n                {\n                    _logger.LogWarning(\"Broker rejected delete keyword: status={StatusCode}\", resp?.StatusCode);\n                    return;\n                }\n                SelectedKeyword = null;\n                await LoadAsync();\n                return;\n            }\n\n            await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);\n            var keywords = await db.Keywords\n                .Where(k => allIds.OfType<Guid>().Contains(k.Id))\n                .ToListAsync().ConfigureAwait(false);\n            db.Keywords.RemoveRange(keywords);\n            await db.SaveChangesAsync().ConfigureAwait(false);\n            SelectedKeyword = null;\n            await LoadAsync();\n        }\n        catch (Exception ex)\n        {\n            _logger.LogError(ex, \"Failed to delete keyword (cascade)\");\n        }\n    }",
  "    private async Task PromptDeleteKeywordAsync()\n    {\n        if (SelectedKeyword == null) return;\n\n        var owner = GetOwnerWindow();\n        if (owner == null) return;\n\n        // T10.4: Count descendants for cascade confirmation\n        var descIds = new List<object>(); SelectedKeyword?.CollectDescendantIds(descIds);\n        var descendantCount = descIds.Count;\n        var message = descendantCount > 0\n            ? $\"Are you sure you want to delete '{SelectedKeyword.Name}' and all {descendantCount} sub-keywords?\\\\n\\\\n\" +\n              $\"The keyword(s) will be removed from all assets. This action cannot be undone.\"\n            : $\"Are you sure you want to delete '{SelectedKeyword.Name}'?\\\\n\\\\n\" +\n              $\"The keyword will be removed from all assets. This action cannot be undone.\";\n\n        var confirmed = await Views.ConfirmationDialog.ShowAsync(owner, \"Delete Keyword\",\n            message, \"Delete\", \"Cancel\", isDestructive: true);\n        if (!confirmed) return;\n\n        try\n        {\n            await _keywordTreeService.DeleteWithCascadeAsync(SelectedKeyword);\n            SelectedKeyword = null;\n            await LoadAsync();\n        }\n        catch (Exception ex)\n        {\n            _logger.LogError(ex, \"Failed to delete keyword (cascade)\");\n        }\n    }"
);

// 18g. PromptCreateCategoryAsync
content = content.replace(
  "    private async Task PromptCreateCategoryAsync()\n    {\n        var parentId = SelectedMetadataCategory?.CategoryId;\n        var name = await Views.InputDialog.ShowAsync(\n            App.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop\n                ? desktop.MainWindow\n                : null,\n            \"New Category\",\n            \"Enter category name:\",\n            \"Create\",\n            \"Cancel\");\n        if (string.IsNullOrWhiteSpace(name)) return;\n\n        try\n        {\n            if (_modeManager.IsMultiUser)\n            {\n                var resp = await SendBrokerRequestAsync(\n                    new CreateCategoryRequest { Name = name.Trim(), ParentId = parentId?.ToString() ?? \"\" },\n                    MessageTypeCode.CreateCategoryRequest);\n                if (resp == null || resp.StatusCode != 0)\n                {\n                    _logger.LogWarning(\"Broker rejected create category: status={StatusCode}\", resp?.StatusCode);\n                    return;\n                }\n                await LoadAsync();\n                return;\n            }\n\n            await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);\n            db.Categories.Add(new Category\n            {\n                Id = Guid.NewGuid(),\n                Name = name.Trim(),\n                NormalizedName = name.Trim().ToUpperInvariant(),\n                ParentId = parentId\n            });\n            await db.SaveChangesAsync().ConfigureAwait(false);\n            await LoadAsync();\n        }\n        catch (Exception ex)\n        {\n            _logger.LogError(ex, \"Failed to create category\");\n        }\n    }",
  "    private async Task PromptCreateCategoryAsync()\n    {\n        var parentId = SelectedMetadataCategory?.CategoryId;\n        var name = await ShowInputDialog(\"New Category\", \"Enter category name:\", \"Create\");\n        if (string.IsNullOrWhiteSpace(name)) return;\n\n        try\n        {\n            await _categoryTreeService.CreateAsync(name.Trim(), parentId);\n            await LoadAsync();\n        }\n        catch (Exception ex)\n        {\n            _logger.LogError(ex, \"Failed to create category\");\n        }\n    }"
);

// 18h. PromptRenameCategoryAsync
content = content.replace(
  "    private async Task PromptRenameCategoryAsync()\n    {\n        if (SelectedMetadataCategory == null) return;\n\n        var newName = await Views.InputDialog.ShowAsync(\n            App.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop\n                ? desktop.MainWindow\n                : null,\n            \"Rename Category\",\n            $\"Rename '{SelectedMetadataCategory.Name}' to:\",\n            \"Rename\",\n            \"Cancel\",\n            defaultValue: SelectedMetadataCategory.Name);\n        if (string.IsNullOrWhiteSpace(newName)) return;\n\n        try\n        {\n            if (_modeManager.IsMultiUser)\n            {\n                var resp = await SendBrokerRequestAsync(\n                    new UpdateCategoryRequest { Id = SelectedMetadataCategory.CategoryId.ToString(), Name = newName.Trim() },\n                    MessageTypeCode.UpdateCategoryRequest);\n                if (resp == null || resp.StatusCode != 0)\n                {\n                    _logger.LogWarning(\"Broker rejected rename category: status={StatusCode}\", resp?.StatusCode);\n                    return;\n                }\n                await LoadAsync();\n                return;\n            }\n\n            await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);\n            var cat = await db.Categories.FirstOrDefaultAsync(c => c.Id == SelectedMetadataCategory.CategoryId).ConfigureAwait(false);\n            if (cat == null) return;\n            cat.Name = newName.Trim();\n            cat.NormalizedName = newName.Trim().ToUpperInvariant();\n            await db.SaveChangesAsync().ConfigureAwait(false);\n            await LoadAsync();\n        }\n        catch (Exception ex)\n        {\n            _logger.LogError(ex, \"Failed to rename category\");\n        }\n    }",
  "    private async Task PromptRenameCategoryAsync()\n    {\n        if (SelectedMetadataCategory == null) return;\n\n        var newName = await ShowInputDialog(\"Rename Category\", $\"Rename '{SelectedMetadataCategory.Name}' to:\", \"Rename\", SelectedMetadataCategory.Name);\n        if (string.IsNullOrWhiteSpace(newName)) return;\n\n        try\n        {\n            await _categoryTreeService.RenameAsync(SelectedMetadataCategory, newName.Trim());\n            await LoadAsync();\n        }\n        catch (Exception ex)\n        {\n            _logger.LogError(ex, \"Failed to rename category\");\n        }\n    }"
);

// 18i. PromptDeleteCategoryAsync
content = content.replace(
  "    private async Task PromptDeleteCategoryAsync()\n    {\n        if (SelectedMetadataCategory == null) return;\n\n        var owner = GetOwnerWindow();\n        if (owner == null) return;\n\n        // T10.4: Count descendants for cascade confirmation\n        var descIds = new List<object>(); SelectedMetadataCategory?.CollectDescendantIds(descIds);\n        var descendantCount = descIds.Count;\n        var message = descendantCount > 0\n            ? $\"Are you sure you want to delete '{SelectedMetadataCategory.Name}' and all {descendantCount} sub-categories?\\\\n\\\\n\" +\n              $\"The category(s) will be removed from all assets. This action cannot be undone.\"\n            : $\"Are you sure you want to delete '{SelectedMetadataCategory.Name}'?\\\\n\\\\n\" +\n              $\"The category will be removed from all assets. This action cannot be undone.\";\n\n        var confirmed = await Views.ConfirmationDialog.ShowAsync(owner, \"Delete Category\",\n            message, \"Delete\", \"Cancel\", isDestructive: true);\n        if (!confirmed) return;\n\n        try\n        {\n            // Collect all descendant IDs recursively\n            var allIds = new List<Guid> { SelectedMetadataCategory.CategoryId };\n            CollectDescendantCategoryIds(SelectedMetadataCategory, allIds);\n\n            if (_modeManager.IsMultiUser)\n            {\n                var resp = await SendBrokerRequestAsync(\n                    new DeleteCategoryRequest\n                    {\n                        Id = SelectedMetadataCategory.CategoryId.ToString(),\n                        CascadeChildren = true\n                    },\n                    MessageTypeCode.DeleteCategoryRequest);\n                if (resp == null || resp.StatusCode != 0)\n                {\n                    _logger.LogWarning(\"Broker rejected delete category: status={StatusCode}\", resp?.StatusCode);\n                    return;\n                }\n                SelectedMetadataCategory = null;\n                await LoadAsync();\n                return;\n            }\n\n            await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);\n            var cats = await db.Categories\n                .Where(c => allIds.OfType<Guid>().Contains(c.Id))\n                .ToListAsync().ConfigureAwait(false);\n            db.Categories.RemoveRange(cats);\n            await db.SaveChangesAsync().ConfigureAwait(false);\n            SelectedMetadataCategory = null;\n            await LoadAsync();\n        }\n        catch (Exception ex)\n        {\n            _logger.LogError(ex, \"Failed to delete category (cascade)\");\n        }\n    }",
  "    private async Task PromptDeleteCategoryAsync()\n    {\n        if (SelectedMetadataCategory == null) return;\n\n        var owner = GetOwnerWindow();\n        if (owner == null) return;\n\n        // T10.4: Count descendants for cascade confirmation\n        var descIds = new List<object>(); SelectedMetadataCategory?.CollectDescendantIds(descIds);\n        var descendantCount = descIds.Count;\n        var message = descendantCount > 0\n            ? $\"Are you sure you want to delete '{SelectedMetadataCategory.Name}' and all {descendantCount} sub-categories?\\\\n\\\\n\" +\n              $\"The category(s) will be removed from all assets. This action cannot be undone.\"\n            : $\"Are you sure you want to delete '{SelectedMetadataCategory.Name}'?\\\\n\\\\n\" +\n              $\"The category will be removed from all assets. This action cannot be undone.\";\n\n        var confirmed = await Views.ConfirmationDialog.ShowAsync(owner, \"Delete Category\",\n            message, \"Delete\", \"Cancel\", isDestructive: true);\n        if (!confirmed) return;\n\n        try\n        {\n            await _categoryTreeService.DeleteWithCascadeAsync(SelectedMetadataCategory);\n            SelectedMetadataCategory = null;\n            await LoadAsync();\n        }\n        catch (Exception ex)\n        {\n            _logger.LogError(ex, \"Failed to delete category (cascade)\");\n        }\n    }"
);

// === 19. Remove cascade delete helpers (CountDescendantKeywords, CollectDescendantKeywordIds, etc.) ===
const cascadeStart = content.indexOf("\n    // ──────────────────────────────────────────────\n    //  T10.4: Cascade delete helpers");
const savedSearchStart = content.indexOf("\n    // ──────────────────────────────────────────────\n    //  Phase 19: Saved search & search history");
if (cascadeStart > 0 && savedSearchStart > 0) {
  content = content.slice(0, cascadeStart) + content.slice(savedSearchStart);
}

// === 20. Update Persist*RenameAsync to delegate to services ===
content = content.replace(
  "    private async Task PersistKeywordRenameAsync(KeywordNode kw)\n    {\n        if (_modeManager.IsMultiUser)\n        {\n            await SendBrokerRequestAsync(\n                new UpdateKeywordRequest { Id = kw.KeywordId.ToString(), Name = kw.Name },\n                MessageTypeCode.UpdateKeywordRequest);\n        }\n        else\n        {\n            await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);\n            var entity = await db.Keywords.FirstOrDefaultAsync(k => k.Id == kw.KeywordId).ConfigureAwait(false);\n            if (entity != null)\n            {\n                entity.Name = kw.Name;\n                entity.NormalizedName = kw.Name.ToUpperInvariant();\n                await db.SaveChangesAsync().ConfigureAwait(false);\n            }\n        }\n    }",
  "    private async Task PersistKeywordRenameAsync(KeywordNode kw)\n    {\n        await _keywordTreeService.RenameAsync(kw, kw.Name);\n    }"
);

content = content.replace(
  "    private async Task PersistCategoryRenameAsync(CategoryNode cat)\n    {\n        if (_modeManager.IsMultiUser)\n        {\n            await SendBrokerRequestAsync(\n                new UpdateCategoryRequest { Id = cat.CategoryId.ToString(), Name = cat.Name },\n                MessageTypeCode.UpdateCategoryRequest);\n        }\n        else\n        {\n            await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);\n            var entity = await db.Categories.FirstOrDefaultAsync(c => c.Id == cat.CategoryId).ConfigureAwait(false);\n            if (entity != null)\n            {\n                entity.Name = cat.Name;\n                entity.NormalizedName = cat.Name.ToUpperInvariant();\n                await db.SaveChangesAsync().ConfigureAwait(false);\n            }\n        }\n    }",
  "    private async Task PersistCategoryRenameAsync(CategoryNode cat)\n    {\n        await _categoryTreeService.RenameAsync(cat, cat.Name);\n    }"
);

content = content.replace(
  "    private async Task PersistCollectionRenameAsync(CollectionNode col)\n    {\n        if (_modeManager.IsMultiUser)\n        {\n            await SendBrokerRequestAsync(\n                new UpdateCollectionRequest { Id = col.Id.ToString(), Name = col.Name },\n                MessageTypeCode.UpdateCollectionRequest);\n        }\n        else\n        {\n            await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);\n            var entity = await db.Collections.FirstOrDefaultAsync(c => c.Id == col.Id).ConfigureAwait(false);\n            if (entity != null)\n            {\n                entity.Name = col.Name;\n                await db.SaveChangesAsync().ConfigureAwait(false);\n            }\n        }\n    }",
  "    private async Task PersistCollectionRenameAsync(CollectionNode col)\n    {\n        await _collectionTreeService.RenameAsync(col, col.Name);\n    }"
);

// === 21. Add ShowInputDialog helper method ===
// Place it after the EvaluatePermission method
const evalPermEnd = "        return Shared.Services.PermissionEvaluator.HasPermission(role, permission);\n    }";
const crudSection = "\n    // ──────────────────────────────────────────────\n    //  T8.18: Sidebar CRUD operations (standalone)";
content = content.replace(
  evalPermEnd + crudSection,
  evalPermEnd + "\n\n    /// <summary>\n    /// Shows an input dialog and returns the entered text.\n    /// </summary>\n    private static async Task<string?> ShowInputDialog(string title, string message, string okButton, string? defaultValue = null)\n    {\n        return await Views.InputDialog.ShowAsync(\n            App.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop\n                ? desktop.MainWindow\n                : null,\n            title,\n            message,\n            okButton,\n            \"Cancel\",\n            defaultValue: defaultValue);\n    }" + crudSection
);

// Write SidebarViewModel
fs.writeFileSync(sidebarPath, content, "utf8");
console.log("Updated SidebarViewModel.cs");

// === Update App.axaml.cs ===
let appContent = fs.readFileSync(appPath, "utf8");

// Add 4 new service registrations after SavedSearchService line
appContent = appContent.replace(
  "            services.AddSingleton<Adam.CatalogBrowser.Services.SavedSearchService>();",
  "            services.AddSingleton<Adam.CatalogBrowser.Services.SavedSearchService>();\n            services.AddSingleton<FolderTreeService>();\n            services.AddSingleton<CollectionTreeService>();\n            services.AddSingleton<KeywordTreeService>();\n            services.AddSingleton<CategoryTreeService>();"
);

fs.writeFileSync(appPath, appContent, "utf8");
console.log("Updated App.axaml.cs");
