const fs = require("fs");
const path = require("path");
const sidebarPath = path.join(__dirname, "..", "src", "Adam.CatalogBrowser", "ViewModels", "SidebarViewModel.cs");

let content = fs.readFileSync(sidebarPath, "utf8");
const nl = "\r\n";

// Helper: replace with explicit CRLF matching
function replaceExact(oldStr, newStr) {
  const oldCrlf = oldStr.replace(/\n/g, "\r\n");
  if (!content.includes(oldCrlf)) {
    console.log("  NOT FOUND (trying with \\n only):", oldStr.substring(0, 80));
    // Try with \n only
    if (content.includes(oldStr)) {
      content = content.replace(oldStr, newStr);
      console.log("  REPLACED with \\n");
    }
    return;
  }
  content = content.replace(oldCrlf, newStr);
  console.log("  REPLACED");
}

// 1. Replace Collections property
const oldColProp = `    public ObservableCollection<CollectionNode> Collections
    {
        get => _collections;
        private set { _collections = value; OnPropertyChanged(); }
    }`;
console.log("1. Collections property:");
replaceExact(oldColProp, `    public ObservableCollection<CollectionNode> Collections => _collectionTreeService.Roots;`);

// 2. Replace Keywords property
const oldKwProp = `    public ObservableCollection<KeywordNode> Keywords
    {
        get => _keywords;
        private set { _keywords = value; OnPropertyChanged(); }
    }`;
console.log("2. Keywords property:");
replaceExact(oldKwProp, `    public ObservableCollection<KeywordNode> Keywords => _keywordTreeService.Roots;`);

// 3. Replace MetadataCategories property
const oldCatProp = `    public ObservableCollection<CategoryNode> MetadataCategories
    {
        get => _metadataCategories;
        private set { _metadataCategories = value; OnPropertyChanged(); }
    }`;
console.log("3. MetadataCategories property:");
replaceExact(oldCatProp, `    public ObservableCollection<CategoryNode> MetadataCategories => _categoryTreeService.Roots;`);

// 4. Remove backing fields and replace with comment
const oldBackingFields = `    private ObservableCollection<CollectionNode> _collections = [];
    private ObservableCollection<KeywordNode> _keywords = [];
    private ObservableCollection<CategoryNode> _metadataCategories = [];`;
console.log("4. Backing fields:");
replaceExact(oldBackingFields, `    // Collections, Keywords, MetadataCategories now owned by respective tree services`);

// 5. Update LoadAsync
const oldLoadAsync = `                LoadFoldersAsync(ct),
                LoadCollectionsAsync(ct),
                LoadKeywordsAsync(ct),
                _mediaFormatService.LoadAsync(ct),
                LoadMetadataCategoriesAsync(ct),`;
const newLoadAsync = `                _folderTreeService.LoadAsync(ct),
                _collectionTreeService.LoadAsync(ct),
                _keywordTreeService.LoadAsync(ct),
                _mediaFormatService.LoadAsync(ct),
                _categoryTreeService.LoadAsync(ct),`;
console.log("5. LoadAsync references:");
replaceExact(oldLoadAsync, newLoadAsync);

// 6. Remove LoadMetadataCategoriesAsync method (find from its start to before OnMediaFormatChanged)
const lmcStart = content.indexOf(`    private async Task LoadMetadataCategoriesAsync(CancellationToken ct)`);
const omfStart = content.indexOf(`    private void OnMediaFormatChanged()`, lmcStart);
if (lmcStart > 0 && omfStart > 0) {
  // Find the start of the blank line before OnMediaFormatChanged
  const beforeOmf = content.lastIndexOf(nl + nl, omfStart);
  const cutEnd = beforeOmf > lmcStart ? beforeOmf : omfStart;
  content = content.slice(0, lmcStart) + content.slice(cutEnd);
  console.log("6. Removed LoadMetadataCategoriesAsync method");
} else {
  console.log("6. LoadMetadataCategoriesAsync NOT FOUND at expected position");
}

// 7. Remove LoadFoldersAsync + FindFolderNode + GetDirectoryName + LoadCollectionsAsync + BuildTree + LoadKeywordsAsync
// These were all between the end of the EvaluatePermission section and the cascade helpers
// Actually, let me find them individually

// 7a. Remove GetDirectoryName
const gdStart = content.indexOf(`    private static string GetDirectoryName(string path)`);
// Find the next method after it
const afterGd = content.indexOf(`\n    private async Task`, gdStart);
if (gdStart > 0 && afterGd > 0) {
  content = content.slice(0, gdStart) + content.slice(afterGd);
  console.log("7a. Removed GetDirectoryName");
}

// 7b. Remove LoadFoldersAsync
const lfStart = content.indexOf(`    private async Task LoadFoldersAsync(CancellationToken ct = default)`);
const afterLf = content.indexOf(`\n    private`, lfStart + 10);
if (lfStart > 0 && afterLf > 0) {
  content = content.slice(0, lfStart) + content.slice(afterLf);
  console.log("7b. Removed LoadFoldersAsync");
}

// 7c. Remove FindFolderNode  
const ffStart = content.indexOf(`    private static FolderNode? FindFolderNode(FolderNode root, string path)`);
const afterFf = content.indexOf(`\n    private`, ffStart + 10);
if (ffStart > 0 && afterFf > 0) {
  content = content.slice(0, ffStart) + content.slice(afterFf);
  console.log("7c. Removed FindFolderNode");
}

// 7d. Remove LoadCollectionsAsync
const lcStart = content.indexOf(`    private async Task LoadCollectionsAsync(CancellationToken ct = default)`);
const afterLc = content.indexOf(`\n    private`, lcStart + 10);
if (lcStart > 0 && afterLc > 0) {
  content = content.slice(0, lcStart) + content.slice(afterLc);
  console.log("7d. Removed LoadCollectionsAsync");
}

// 7e. Remove BuildTree
const btStart = content.indexOf(`    private static CollectionNode BuildTree`);
const afterBt = content.indexOf(`\n    private`, btStart + 10);
if (btStart > 0 && afterBt > 0) {
  content = content.slice(0, btStart) + content.slice(afterBt);
  console.log("7e. Removed BuildTree");
}

// 7f. Remove LoadKeywordsAsync
const lkStart = content.indexOf(`    private async Task LoadKeywordsAsync(CancellationToken ct = default)`);
const afterLk = content.indexOf(`\n    private`, lkStart + 10);
if (lkStart > 0 && afterLk > 0) {
  content = content.slice(0, lkStart) + content.slice(afterLk);
  console.log("7f. Removed LoadKeywordsAsync");
}

// 8. Remove _folderScanService field
const oldField = `    private readonly FolderScanService _folderScanService;`;
console.log("8. _folderScanService field:");
replaceExact(oldField, `    // _folderScanService moved into FolderTreeService`);

// 9. Remove _folderScanService fallback from constructor 
const oldFallback = `        _folderScanService = folderScanService ?? new FolderScanService(modeManager, new PluginLoaderService(
            Microsoft.Extensions.Options.Options.Create(new Adam.Shared.Configuration.PluginConfig()),
            logger as ILogger<PluginLoaderService> ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<PluginLoaderService>.Instance));
        `;
console.log("9. _folderScanService fallback:");
replaceExact(oldFallback, `        `);

// 10. Update RevealFolder to delegate
const oldReveal = `    private void RevealFolder(object? parameter)
    {
        FolderNode? folder = parameter as FolderNode;
        if (folder == null || string.IsNullOrEmpty(folder.Path))
            folder = SelectedFolder;
        if (folder == null || string.IsNullOrEmpty(folder.Path)) return;

        try
        {
            if (OperatingSystem.IsWindows())
                System.Diagnostics.Process.Start("explorer.exe", folder.Path);
            else if (OperatingSystem.IsMacOS())
                System.Diagnostics.Process.Start("open", folder.Path);
            else if (OperatingSystem.IsLinux())
                System.Diagnostics.Process.Start("xdg-open", folder.Path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reveal folder: {Path}", folder.Path);
        }
    }`;
const newReveal = `    private void RevealFolder(object? parameter)
    {
        FolderNode? folder = parameter as FolderNode;
        if (folder == null || string.IsNullOrEmpty(folder.Path))
            folder = SelectedFolder;
        _folderTreeService.RevealFolder(folder);
    }`;
console.log("10. RevealFolder:");
replaceExact(oldReveal, newReveal);

// 11. Update RescanFolderAsync to delegate
const oldRescan = `    private async Task RescanFolderAsync()
    {
        if (SelectedFolder == null || string.IsNullOrEmpty(SelectedFolder.Path)) return;

        try
        {
            _logger.LogInformation("Re-scanning folder: {Path}", SelectedFolder.Path);

            var ingested = await _folderScanService.ScanFolderAsync(SelectedFolder.Path, recursive: true);

            _logger.LogInformation("Re-scan complete for folder: {Path} — {Count} new asset(s) ingested", SelectedFolder.Path, ingested);

            // Refresh the sidebar data to update asset counts
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rescan folder: {Path}", SelectedFolder.Path);
        }
    }`;
const newRescan = `    private async Task RescanFolderAsync()
    {
        if (SelectedFolder == null || string.IsNullOrEmpty(SelectedFolder.Path)) return;
        await _folderTreeService.RescanFolderAsync(SelectedFolder);
        await LoadAsync();
    }`;
console.log("11. RescanFolderAsync:");
replaceExact(oldRescan, newRescan);

// 12. Update PromptCreateCollectionAsync
const oldCreateCol = `    private async Task PromptCreateCollectionAsync()
    {
        var parentId = SelectedCollection?.Id;
        var name = await Views.InputDialog.ShowAsync(
            App.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null,
            "New Collection",
            "Enter collection name:",
            "Create",
            "Cancel");
        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            if (_modeManager.IsMultiUser)
            {
                var resp = await SendBrokerRequestAsync(
                    new CreateCollectionRequest { Name = name.Trim(), ParentId = parentId?.ToString() ?? "" },
                    MessageTypeCode.CreateCollectionRequest);
                if (resp == null || resp.StatusCode != 0)
                {
                    _logger.LogWarning("Broker rejected create collection: status={StatusCode}", resp?.StatusCode);
                    return;
                }
                await LoadAsync();
                return;
            }

            await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);
            db.Collections.Add(new Collection
            {
                Id = Guid.NewGuid(),
                Name = name.Trim(),
                ParentId = parentId
            });
            await db.SaveChangesAsync().ConfigureAwait(false);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create collection");
        }
    }`;
const newCreateCol = `    private async Task PromptCreateCollectionAsync()
    {
        var parentId = SelectedCollection?.Id;
        var name = await ShowInputDialog("New Collection", "Enter collection name:", "Create");
        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            await _collectionTreeService.CreateAsync(name.Trim(), parentId);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create collection");
        }
    }`;
console.log("12. PromptCreateCollectionAsync:");
replaceExact(oldCreateCol, newCreateCol);

// 13. Update PromptRenameCollectionAsync
const oldRenCol = content.match(/    private async Task PromptRenameCollectionAsync\(\).*?\n    }(\n|$)/s);
// Actually let me just use indexOf-based removal for these long methods

// Let me find all the old CRUD methods and remove/replace them using indexOf
// The CRUD section is between "// ── T8.18: Sidebar CRUD operations (standalone) ──" 
// and "// ── T10.4: Cascade delete helpers ──"

const crudStart = content.indexOf("// ──────────────────────────────────────────────\r\n    //  T8.18: Sidebar CRUD operations (standalone)");
const cascadeStart = content.indexOf("// ──────────────────────────────────────────────\r\n    //  T10.4: Cascade delete helpers");

if (crudStart > 0 && cascadeStart > crudStart) {
  // Replace the entire CRUD section
  const newCrudSection = `    // ──────────────────────────────────────────────
    //  T8.18: Sidebar CRUD operations (standalone)
    // ──────────────────────────────────────────────

    private async Task PromptCreateCollectionAsync()
    {
        var parentId = SelectedCollection?.Id;
        var name = await ShowInputDialog("New Collection", "Enter collection name:", "Create");
        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            await _collectionTreeService.CreateAsync(name.Trim(), parentId);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create collection");
        }
    }

    private async Task PromptRenameCollectionAsync()
    {
        if (SelectedCollection == null) return;

        var newName = await ShowInputDialog("Rename Collection", $"Rename '{SelectedCollection.Name}' to:", "Rename", SelectedCollection.Name);
        if (string.IsNullOrWhiteSpace(newName)) return;

        try
        {
            await _collectionTreeService.RenameAsync(SelectedCollection, newName.Trim());
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rename collection");
        }
    }

    private async Task PromptDeleteCollectionAsync()
    {
        if (SelectedCollection == null) return;

        var owner = GetOwnerWindow();
        if (owner == null) return;

        // T10.4: Count descendants for cascade confirmation
        var descIds = new List<object>();
        SelectedCollection?.CollectDescendantIds(descIds);
        var descendantCount = descIds.Count;
        var message = descendantCount > 0
            ? $"Are you sure you want to delete '{SelectedCollection.Name}' and all {descendantCount} sub-collections?\\n\\n" +
              $"The collection(s) will be removed but the assets within them will not be deleted."
            : $"Are you sure you want to delete '{SelectedCollection.Name}'?\\n\\n" +
              $"This will remove the collection but not the assets within it.";

        var confirmed = await Views.ConfirmationDialog.ShowAsync(owner, "Delete Collection",
            message, "Delete", "Cancel", isDestructive: true);
        if (!confirmed) return;

        try
        {
            await _collectionTreeService.DeleteWithCascadeAsync(SelectedCollection);
            SelectedCollection = null;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete collection (cascade)");
        }
    }

    private async Task PromptCreateKeywordAsync()
    {
        var parentId = SelectedKeyword?.KeywordId;
        var name = await ShowInputDialog("New Keyword", "Enter keyword name:", "Create");
        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            await _keywordTreeService.CreateAsync(name.Trim(), parentId);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create keyword");
        }
    }

    private async Task PromptRenameKeywordAsync()
    {
        if (SelectedKeyword == null) return;

        var newName = await ShowInputDialog("Rename Keyword", $"Rename '{SelectedKeyword.Name}' to:", "Rename", SelectedKeyword.Name);
        if (string.IsNullOrWhiteSpace(newName)) return;

        try
        {
            await _keywordTreeService.RenameAsync(SelectedKeyword, newName.Trim());
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rename keyword");
        }
    }

    private async Task PromptDeleteKeywordAsync()
    {
        if (SelectedKeyword == null) return;

        var owner = GetOwnerWindow();
        if (owner == null) return;

        // T10.4: Count descendants for cascade confirmation
        var descIds = new List<object>(); SelectedKeyword?.CollectDescendantIds(descIds);
        var descendantCount = descIds.Count;
        var message = descendantCount > 0
            ? $"Are you sure you want to delete '{SelectedKeyword.Name}' and all {descendantCount} sub-keywords?\\n\\n" +
              $"The keyword(s) will be removed from all assets. This action cannot be undone."
            : $"Are you sure you want to delete '{SelectedKeyword.Name}'?\\n\\n" +
              $"The keyword will be removed from all assets. This action cannot be undone.";

        var confirmed = await Views.ConfirmationDialog.ShowAsync(owner, "Delete Keyword",
            message, "Delete", "Cancel", isDestructive: true);
        if (!confirmed) return;

        try
        {
            await _keywordTreeService.DeleteWithCascadeAsync(SelectedKeyword);
            SelectedKeyword = null;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete keyword (cascade)");
        }
    }

    private async Task PromptCreateCategoryAsync()
    {
        var parentId = SelectedMetadataCategory?.CategoryId;
        var name = await ShowInputDialog("New Category", "Enter category name:", "Create");
        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            await _categoryTreeService.CreateAsync(name.Trim(), parentId);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create category");
        }
    }

    private async Task PromptRenameCategoryAsync()
    {
        if (SelectedMetadataCategory == null) return;

        var newName = await ShowInputDialog("Rename Category", $"Rename '{SelectedMetadataCategory.Name}' to:", "Rename", SelectedMetadataCategory.Name);
        if (string.IsNullOrWhiteSpace(newName)) return;

        try
        {
            await _categoryTreeService.RenameAsync(SelectedMetadataCategory, newName.Trim());
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rename category");
        }
    }

    private async Task PromptDeleteCategoryAsync()
    {
        if (SelectedMetadataCategory == null) return;

        var owner = GetOwnerWindow();
        if (owner == null) return;

        // T10.4: Count descendants for cascade confirmation
        var descIds = new List<object>(); SelectedMetadataCategory?.CollectDescendantIds(descIds);
        var descendantCount = descIds.Count;
        var message = descendantCount > 0
            ? $"Are you sure you want to delete '{SelectedMetadataCategory.Name}' and all {descendantCount} sub-categories?\\n\\n" +
              $"The category(s) will be removed from all assets. This action cannot be undone."
            : $"Are you sure you want to delete '{SelectedMetadataCategory.Name}'?\\n\\n" +
              $"The category will be removed from all assets. This action cannot be undone.";

        var confirmed = await Views.ConfirmationDialog.ShowAsync(owner, "Delete Category",
            message, "Delete", "Cancel", isDestructive: true);
        if (!confirmed) return;

        try
        {
            await _categoryTreeService.DeleteWithCascadeAsync(SelectedMetadataCategory);
            SelectedMetadataCategory = null;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete category (cascade)");
        }
    }`;

  content = content.slice(0, crudStart) + newCrudSection + content.slice(cascadeStart);
  console.log("13. Replaced entire CRUD section");
} else {
  console.log("13. CRUD section markers not found");
}

// 14. Remove cascade delete helpers section
const cascadeSectionEnd = content.indexOf("// ──────────────────────────────────────────────\r\n    //  Phase 19: Saved search & search history");
const casStart = content.indexOf("// ──────────────────────────────────────────────\r\n    //  T10.4: Cascade delete helpers");
if (casStart > 0 && cascadeSectionEnd > casStart) {
  content = content.slice(0, casStart) + content.slice(cascadeSectionEnd);
  console.log("14. Removed cascade delete helpers");
}

// 15. Update Persist rename methods
const oldPersistKw = `    private async Task PersistKeywordRenameAsync(KeywordNode kw)
    {
        if (_modeManager.IsMultiUser)
        {
            await SendBrokerRequestAsync(
                new UpdateKeywordRequest { Id = kw.KeywordId.ToString(), Name = kw.Name },
                MessageTypeCode.UpdateKeywordRequest);
        }
        else
        {
            await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);
            var entity = await db.Keywords.FirstOrDefaultAsync(k => k.Id == kw.KeywordId).ConfigureAwait(false);
            if (entity != null)
            {
                entity.Name = kw.Name;
                entity.NormalizedName = kw.Name.ToUpperInvariant();
                await db.SaveChangesAsync().ConfigureAwait(false);
            }
        }
    }`;
const newPersistKw = `    private async Task PersistKeywordRenameAsync(KeywordNode kw)
    {
        await _keywordTreeService.RenameAsync(kw, kw.Name);
    }`;
console.log("15a. PersistKeywordRenameAsync:");
replaceExact(oldPersistKw, newPersistKw);

const oldPersistCat = `    private async Task PersistCategoryRenameAsync(CategoryNode cat)
    {
        if (_modeManager.IsMultiUser)
        {
            await SendBrokerRequestAsync(
                new UpdateCategoryRequest { Id = cat.CategoryId.ToString(), Name = cat.Name },
                MessageTypeCode.UpdateCategoryRequest);
        }
        else
        {
            await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);
            var entity = await db.Categories.FirstOrDefaultAsync(c => c.Id == cat.CategoryId).ConfigureAwait(false);
            if (entity != null)
            {
                entity.Name = cat.Name;
                entity.NormalizedName = cat.Name.ToUpperInvariant();
                await db.SaveChangesAsync().ConfigureAwait(false);
            }
        }
    }`;
const newPersistCat = `    private async Task PersistCategoryRenameAsync(CategoryNode cat)
    {
        await _categoryTreeService.RenameAsync(cat, cat.Name);
    }`;
console.log("15b. PersistCategoryRenameAsync:");
replaceExact(oldPersistCat, newPersistCat);

const oldPersistCol = `    private async Task PersistCollectionRenameAsync(CollectionNode col)
    {
        if (_modeManager.IsMultiUser)
        {
            await SendBrokerRequestAsync(
                new UpdateCollectionRequest { Id = col.Id.ToString(), Name = col.Name },
                MessageTypeCode.UpdateCollectionRequest);
        }
        else
        {
            await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);
            var entity = await db.Collections.FirstOrDefaultAsync(c => c.Id == col.Id).ConfigureAwait(false);
            if (entity != null)
            {
                entity.Name = col.Name;
                await db.SaveChangesAsync().ConfigureAwait(false);
            }
        }
    }`;
const newPersistCol = `    private async Task PersistCollectionRenameAsync(CollectionNode col)
    {
        await _collectionTreeService.RenameAsync(col, col.Name);
    }`;
console.log("15c. PersistCollectionRenameAsync:");
replaceExact(oldPersistCol, newPersistCol);

// 16. Add ShowInputDialog helper after EvaluatePermission
const epEnd = `        return Shared.Services.PermissionEvaluator.HasPermission(role, permission);
    }`;
const crudSectionStart = `\r\n    // ──────────────────────────────────────────────\r\n    //  T8.18: Sidebar CRUD operations (standalone)\r\n    // ──────────────────────────────────────────────`;
const newHelper = `\r\n    /// <summary>\r\n    /// Shows an input dialog and returns the entered text.\r\n    /// </summary>\r\n    private static async Task<string?> ShowInputDialog(string title, string message, string okButton, string? defaultValue = null)\r\n    {\r\n        return await Views.InputDialog.ShowAsync(\r\n            App.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop\r\n                ? desktop.MainWindow\r\n                : null,\r\n            title,\r\n            message,\r\n            okButton,\r\n            \"Cancel\",\r\n            defaultValue: defaultValue);\r\n    }`;
console.log("16. ShowInputDialog helper:");
const searchStr = epEnd + crudSectionStart;
const replaceStr = epEnd + newHelper + crudSectionStart;
replaceExact(searchStr, replaceStr);

// Write file
fs.writeFileSync(sidebarPath, content, "utf8");
console.log("\nDone! All transformations applied.");
