using Adam.CatalogBrowser.Models;
using Adam.CatalogBrowser.Services;
using Adam.CatalogBrowser.ViewModels;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Adam.Shared.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.CatalogBrowser.Tests.ViewModels;

/// <summary>
/// Tests for the MainWindowViewModel event handler threading changes.
/// Focuses on the core business logic (DB persistence, property tracking)
/// that doesn't require a pumping UI dispatcher.
/// </summary>
public class MainWindowViewModelTests : IAsyncLifetime
{
    private readonly string _basePath;
    private readonly ModeManager _modeManager;
    private readonly NullLogger<MainWindowViewModel> _logger;
    private readonly NullLogger<SidebarViewModel> _sidebarLogger;
    private readonly NullLogger<AssetGalleryViewModel> _galleryLogger;
    private readonly NullLogger<IngestionViewModel> _ingestionLogger;
    private MainWindowViewModel _vm = null!;
    private SidebarViewModel _sidebar = null!;
    private AssetGalleryViewModel _gallery = null!;
    private AppDbContext _db = null!;

    public MainWindowViewModelTests()
    {
        _basePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _modeManager = new ModeManager(_basePath);
        _logger = new NullLogger<MainWindowViewModel>();
        _sidebarLogger = new NullLogger<SidebarViewModel>();
        _galleryLogger = new NullLogger<AssetGalleryViewModel>();
        _ingestionLogger = new NullLogger<IngestionViewModel>();
    }

    public async Task InitializeAsync()
    {
        await _modeManager.InitializeAsync();

        _sidebar = new SidebarViewModel(_modeManager, _sidebarLogger);
        _gallery = new AssetGalleryViewModel(_modeManager, _galleryLogger);
        var ingestion = new IngestionViewModel(_modeManager, _ingestionLogger);
        var metadataEditor = new MetadataEditorViewModel(_modeManager);
        var auditLog = new AuditLogViewModel(_modeManager);
        var bulkQueue = new BulkOperationQueue(_modeManager, new NullLogger<BulkOperationQueue>());

        // Construct the ViewModel with startUp: false to avoid the background
        // startup pipeline (which dispatches to the UI thread and would hang
        // without a pumping Avalonia dispatcher). We manually set the
        // IsInitialLoading state via reflection afterward.
        _vm = new MainWindowViewModel(
            _logger, _modeManager, new Adam.Shared.Services.MetadataWritebackService(), _sidebar, _gallery,
            ingestion, metadataEditor,
            auditLog, bulkQueue,
            startUp: false);

        // Open a DB connection for seeding/verifying test data
        _db = _modeManager.CreateDbContext();
    }

    public async Task DisposeAsync()
    {
        _db.Dispose();
        // Close all pools first so the DB file can be deleted
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_basePath))
                Directory.Delete(_basePath, recursive: true);
        }
        catch (IOException) { }
    }

    // ──────────────────────────────────────────────
    //  Auto-save tags on selection switch
    // ──────────────────────────────────────────────

    [Fact]
    public async Task AutoSaveTagsAsync_PersistsDirtyTagsToDatabase()
    {
        // Arrange: seed an asset with keyword "Nature"
        var assetId = await SeedAssetWithKeywordAsync("Nature");
        var assetItem = new AssetListItem { Id = assetId, FileName = "landscape.jpg", Title = "Landscape" };

        SetField("_selectedAsset", assetItem);
        _vm.SelectedAssetTags.Add("Urban");
        _vm.SelectedAssetTags.Add("Summer");
        // CollectionChanged handler sets _tagsDirty = true

        // Act: invoke AutoSaveTagsAsync via reflection
        await InvokeAutoSaveTagsAsync();

        // Assert: verify the new keywords were saved, old ones cleared
        var savedKw = await _db.DigitalAssets
            .Where(a => a.Id == assetId)
            .SelectMany(a => a.Keywords)
            .Select(k => k.Name)
            .ToListAsync();

        savedKw.Should().Contain("Urban");
        savedKw.Should().Contain("Summer");
        savedKw.Should().NotContain("Nature");
    }

    [Fact]
    public async Task AutoSaveTagsAsync_WhenNotDirty_DoesNothing()
    {
        // Arrange: seed an asset with keyword "Nature"
        var assetId = await SeedAssetWithKeywordAsync("Nature");
        var assetItem = new AssetListItem { Id = assetId, FileName = "doc.pdf", Title = "Doc" };

        SetField("_selectedAsset", assetItem);
        // _tagsDirty remains false — no tags were added

        // Act
        await InvokeAutoSaveTagsAsync();

        // Assert: "Nature" should still be on the asset
        var savedKw = await _db.DigitalAssets
            .Where(a => a.Id == assetId)
            .SelectMany(a => a.Keywords)
            .Select(k => k.Name)
            .ToListAsync();

        savedKw.Should().ContainSingle(n => n == "Nature");
    }

    [Fact]
    public async Task AutoSaveTagsAsync_WhenNoSelectedAsset_DoesNothing()
    {
        // Arrange: seed an asset
        var assetId = await SeedAssetWithKeywordAsync("Nature");
        SetField("_selectedAsset", null);

        // Set tags dirty even though no asset is selected
        _vm.SelectedAssetTags.Add("OrphanTag");

        // Act
        await InvokeAutoSaveTagsAsync();

        // Assert: original keyword untouched
        var savedKw = await _db.DigitalAssets
            .Where(a => a.Id == assetId)
            .SelectMany(a => a.Keywords)
            .Select(k => k.Name)
            .ToListAsync();

        savedKw.Should().ContainSingle(n => n == "Nature");
    }



    // ──────────────────────────────────────────────
    //  Auto-save description on selection switch
    // ──────────────────────────────────────────────

    [Fact]
    public async Task AutoSaveTagsAsync_PersistsChangedDescription()
    {
        // Arrange: seed an asset with an existing description
        var assetId = await SeedAssetWithDescriptionAsync("Old description");
        var assetItem = new AssetListItem { Id = assetId, FileName = "desc.jpg", Title = "Desc" };

        SetField("_selectedAsset", assetItem);
        _vm.SelectedAssetDescription = "Updated description";
        // SelectedAssetDescription setter sets _descriptionDirty = true

        // Act
        await InvokeAutoSaveTagsAsync();

        // Assert: description was persisted
        var saved = await _db.DigitalAssets
            .Where(a => a.Id == assetId)
            .Select(a => a.Description)
            .FirstAsync();

        saved.Should().Be("Updated description");
    }

    [Fact]
    public async Task AutoSaveTagsAsync_DescriptionNotDirty_DoesNothing()
    {
        // Arrange: seed an asset with an existing description
        var assetId = await SeedAssetWithDescriptionAsync("Original desc");
        var assetItem = new AssetListItem { Id = assetId, FileName = "desc2.jpg", Title = "Desc2" };

        SetField("_selectedAsset", assetItem);
        // Do NOT set SelectedAssetDescription — _descriptionDirty stays false

        // Act: tags are not dirty either, so the combined guard prevents save
        await InvokeAutoSaveTagsAsync();

        // Assert: description unchanged
        var saved = await _db.DigitalAssets
            .Where(a => a.Id == assetId)
            .Select(a => a.Description)
            .FirstAsync();

        saved.Should().Be("Original desc");
    }

    // ──────────────────────────────────────────────
    //  Auto-save categories on selection switch
    // ──────────────────────────────────────────────

    [Fact]
    public async Task AutoSaveTagsAsync_PersistsAddedCategories()
    {
        // Arrange: seed an asset with no categories
        var assetId = await SeedAssetWithDescriptionAsync("Cat test");
        var assetItem = new AssetListItem { Id = assetId, FileName = "cat.jpg", Title = "Cat" };

        SetField("_selectedAsset", assetItem);
        _vm.SelectedAssetCategories.Add("Nature");
        _vm.SelectedAssetCategories.Add("Urban");
        // CollectionChanged handler sets _categoriesDirty = true

        // Act
        await InvokeAutoSaveTagsAsync();

        // Assert: verify the new categories were saved
        var savedCats = await _db.DigitalAssets
            .Where(a => a.Id == assetId)
            .SelectMany(a => a.Categories)
            .Select(c => c.Name)
            .ToListAsync();

        savedCats.Should().Contain("Nature");
        savedCats.Should().Contain("Urban");
    }

    [Fact]
    public async Task AutoSaveTagsAsync_PersistsRemovedCategories()
    {
        // Arrange: seed an asset with a category "Nature"
        var assetId = await SeedAssetWithCategoryAsync("Nature");
        var assetItem = new AssetListItem { Id = assetId, FileName = "cat2.jpg", Title = "Cat2" };

        SetField("_selectedAsset", assetItem);
        // Remove the category by clearing and adding nothing new
        _vm.SelectedAssetCategories.Clear();
        // CollectionChanged handler sets _categoriesDirty = true

        // Act
        await InvokeAutoSaveTagsAsync();

        // Assert: categories should be empty
        var savedCats = await _db.DigitalAssets
            .Where(a => a.Id == assetId)
            .SelectMany(a => a.Categories)
            .ToListAsync();

        savedCats.Should().BeEmpty();
    }

    [Fact]
    public async Task AutoSaveTagsAsync_CategoriesNotDirty_DoesNothing()
    {
        // Arrange: seed an asset with a category "Nature"
        var assetId = await SeedAssetWithCategoryAsync("Nature");
        var assetItem = new AssetListItem { Id = assetId, FileName = "cat3.jpg", Title = "Cat3" };

        SetField("_selectedAsset", assetItem);
        // Do NOT touch SelectedAssetCategories — _categoriesDirty stays false

        // Act
        await InvokeAutoSaveTagsAsync();

        // Assert: "Nature" should still be on the asset
        var savedCats = await _db.DigitalAssets
            .Where(a => a.Id == assetId)
            .SelectMany(a => a.Categories)
            .Select(c => c.Name)
            .ToListAsync();

        savedCats.Should().ContainSingle(n => n == "Nature");
    }

    // ──────────────────────────────────────────────
    //  Combined dirty-state guards
    // ──────────────────────────────────────────────

    [Fact]
    public async Task AutoSaveTagsAsync_WhenOnlyDescriptionDirty_SavesDescription()
    {
        // Arrange: seed an asset — no tags dirty, but description changed
        var assetId = await SeedAssetWithKeywordAsync("ExistingTag");
        var assetItem = new AssetListItem { Id = assetId, FileName = "mixed.jpg", Title = "Mixed" };

        SetField("_selectedAsset", assetItem);
        // Tags are not changed — _tagsDirty stays false
        // Change the description only
        _vm.SelectedAssetDescription = "Only description changed";

        // Act
        await InvokeAutoSaveTagsAsync();

        // Assert: description saved, original tag preserved
        var saved = await _db.DigitalAssets
            .Include(a => a.Keywords)
            .Where(a => a.Id == assetId)
            .FirstAsync();

        saved.Description.Should().Be("Only description changed");
        saved.Keywords.Should().ContainSingle(k => k.Name == "ExistingTag");
    }

    [Fact]
    public async Task AutoSaveTagsAsync_WhenOnlyCategoriesDirty_SavesCategories()
    {
        // Arrange: seed an asset with an existing tag but no categories
        var assetId = await SeedAssetWithKeywordAsync("ExistingTag");
        var assetItem = new AssetListItem { Id = assetId, FileName = "mixed2.jpg", Title = "Mixed2" };

        SetField("_selectedAsset", assetItem);
        // Tags are not changed — _tagsDirty stays false
        // Add a category only
        _vm.SelectedAssetCategories.Add("NewCategory");

        // Act
        await InvokeAutoSaveTagsAsync();

        // Assert: categories saved, original tag preserved
        var saved = await _db.DigitalAssets
            .Include(a => a.Keywords)
            .Include(a => a.Categories)
            .Where(a => a.Id == assetId)
            .FirstAsync();

        saved.Categories.Should().ContainSingle(c => c.Name == "NewCategory");
        saved.Keywords.Should().ContainSingle(k => k.Name == "ExistingTag");
        saved.Description.Should().BeNull();
    }

    // ──────────────────────────────────────────────
    //  Dirty-tracking on description
    // ──────────────────────────────────────────────

    [Fact]
    public void SelectedAssetDescription_Setter_SetsDescriptionDirty()
    {
        // Initially not dirty
        GetDescriptionDirtyField().Should().BeFalse();

        _vm.SelectedAssetDescription = "New desc";

        GetDescriptionDirtyField().Should().BeTrue();
    }

    [Fact]
    public void SelectedAssetCategories_CollectionChanged_SetsCategoriesDirty()
    {
        GetCategoriesDirtyField().Should().BeFalse();

        _vm.SelectedAssetCategories.Add("NewCat");

        GetCategoriesDirtyField().Should().BeTrue();
    }

    [Fact]
    public void SelectedAssetCategories_CollectionChanged_Cleared_SetsCategoriesDirty()
    {
        GetCategoriesDirtyField().Should().BeFalse();

        _vm.SelectedAssetCategories.Add("CatA");
        GetCategoriesDirtyField().Should().BeTrue();

        // Reset dirty flag as though saved
        SetField("_categoriesDirty", false);
        GetCategoriesDirtyField().Should().BeFalse();

        // Clear — should set dirty
        _vm.SelectedAssetCategories.Clear();
        GetCategoriesDirtyField().Should().BeTrue();
    }

    // ──────────────────────────────────────────────
    //  Tag dirty tracking
    // ──────────────────────────────────────────────

    [Fact]
    public void SelectedAssetTags_CollectionChanged_SetsTagsDirty()
    {
        GetTagsDirtyField().Should().BeFalse();

        _vm.SelectedAssetTags.Add("NewTag");

        GetTagsDirtyField().Should().BeTrue();
    }

    [Fact]
    public void SelectedAssetTags_CollectionChanged_Cleared_ResetsTagsDirtyFromFalse()
    {
        // Start fresh
        GetTagsDirtyField().Should().BeFalse();

        // Add a tag — dirty
        _vm.SelectedAssetTags.Add("TagA");
        GetTagsDirtyField().Should().BeTrue();

        // Simulate successful save by clearing dirty flag
        SetField("_tagsDirty", false);
        GetTagsDirtyField().Should().BeFalse();

        // Clear the tags — should set dirty
        _vm.SelectedAssetTags.Clear();
        GetTagsDirtyField().Should().BeTrue();
    }

    // ──────────────────────────────────────────────
    //  SaveTagsCommand.CanExecute
    // ──────────────────────────────────────────────

    [Fact]
    public void SaveTagsCommand_CanExecute_NoAsset_ReturnsFalse()
    {
        SetField("_selectedAsset", null);
        _vm.SaveTagsCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void SaveTagsCommand_CanExecute_AssetNotDirty_ReturnsFalse()
    {
        SetField("_selectedAsset", new AssetListItem { Id = Guid.NewGuid(), FileName = "test.jpg", Title = "Test" });
        _vm.SaveTagsCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void SaveTagsCommand_CanExecute_TagsDirty_ReturnsTrue()
    {
        SetField("_selectedAsset", new AssetListItem { Id = Guid.NewGuid(), FileName = "test.jpg", Title = "Test" });
        SetField("_tagsDirty", true);
        _vm.SaveTagsCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void SaveTagsCommand_CanExecute_DescriptionDirty_ReturnsTrue()
    {
        SetField("_selectedAsset", new AssetListItem { Id = Guid.NewGuid(), FileName = "test.jpg", Title = "Test" });
        SetField("_descriptionDirty", true);
        _vm.SaveTagsCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void SaveTagsCommand_CanExecute_CategoriesDirty_ReturnsTrue()
    {
        SetField("_selectedAsset", new AssetListItem { Id = Guid.NewGuid(), FileName = "test.jpg", Title = "Test" });
        SetField("_categoriesDirty", true);
        _vm.SaveTagsCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void SaveTagsCommand_CanExecute_AllDirty_ReturnsTrue()
    {
        SetField("_selectedAsset", new AssetListItem { Id = Guid.NewGuid(), FileName = "test.jpg", Title = "Test" });
        SetField("_tagsDirty", true);
        SetField("_descriptionDirty", true);
        SetField("_categoriesDirty", true);
        _vm.SaveTagsCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task SaveTagsCommand_Execute_PersistsChanges()
    {
        // Arrange: seed an asset and set up dirty flags
        var assetId = await SeedAssetWithDescriptionAsync("Before save");
        var assetItem = new AssetListItem { Id = assetId, FileName = "execute.jpg", Title = "Execute" };

        SetField("_selectedAsset", assetItem);
        _vm.SelectedAssetDescription = "After save";
        _vm.SelectedAssetTags.Add("NewTag");
        _vm.SelectedAssetCategories.Add("NewCategory");

        // Act: click the Save button
        _vm.SaveTagsCommand.Execute(null);

        // Wait for the fire-and-forget async save to complete
        // Poll the DB until the change appears or timeout
        string? savedDesc = null;
        for (var i = 0; i < 20; i++)
        {
            await Task.Delay(100);
            savedDesc = await _db.DigitalAssets
                .Where(a => a.Id == assetId)
                .Select(a => a.Description)
                .FirstOrDefaultAsync();
            if (savedDesc == "After save")
                break;
        }

        // Assert
        savedDesc.Should().Be("After save");

        var savedKeywords = await _db.DigitalAssets
            .Where(a => a.Id == assetId)
            .SelectMany(a => a.Keywords)
            .Select(k => k.Name)
            .ToListAsync();
        savedKeywords.Should().Contain("NewTag");

        var savedCategories = await _db.DigitalAssets
            .Where(a => a.Id == assetId)
            .SelectMany(a => a.Categories)
            .Select(c => c.Name)
            .ToListAsync();
        savedCategories.Should().Contain("NewCategory");
    }

    // ──────────────────────────────────────────────
    //  LogoutCommand.CanExecute guards
    // ──────────────────────────────────────────────

    [Fact]
    public void LogoutCommand_CanExecute_WhenNotConnected_ReturnsFalse()
    {
        // Default state: _isConnectedToService is false, auth is null
        _vm.LogoutCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void LogoutCommand_CanExecute_WhenConnectedNotLoggedIn_ReturnsFalse()
    {
        SetField("_isConnectedToService", true);
        // _modeManager.AuthSession is null → null-conditional yields false
        _vm.LogoutCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task LogoutCommand_CanExecute_WhenConnectedAndLoggedIn_ReturnsTrue()
    {
        await using var ctx = new LoggedInVmContext();
        await ctx.InitializeAsync();

        ctx.Vm.LogoutCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task LogoutCommand_CanExecute_AfterDisconnect_ReturnsFalse()
    {
        await using var ctx = new LoggedInVmContext();
        await ctx.InitializeAsync();

        ctx.Vm.LogoutCommand.CanExecute(null).Should().BeTrue();

        // Disconnect should disable the command
        ctx.Vm.DisconnectFromServiceCommand.Execute(null);
        ctx.Vm.LogoutCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task LogoutCommand_CanExecute_WhenNotConnectedButAuthLoggedIn_ReturnsFalse()
    {
        await using var ctx = new LoggedInVmContext();
        await ctx.InitializeAsync();

        // Simulate disconnect but keep auth session logged in
        var connectedField = typeof(MainWindowViewModel).GetField("_isConnectedToService",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        connectedField.SetValue(ctx.Vm, false);

        ctx.Vm.LogoutCommand.CanExecute(null).Should().BeFalse();
    }

    // ──────────────────────────────────────────────
    //  Mode toggle — initial connection state
    // ──────────────────────────────────────────────

    [Fact]
    public void MainWindowViewModel_InitialState_DefaultsToLocalMode()
    {
        _vm.IsServiceMode.Should().BeFalse();
        _vm.IsLocalMode.Should().BeTrue();
        _vm.IsConnectedToService.Should().BeFalse();
        _vm.ServiceConnectionStatus.Should().Be("Disconnected");
        _vm.ServiceHost.Should().Be("localhost");
        _vm.ServicePort.Should().Be(9100);
    }

    [Fact]
    public void MainWindowViewModel_IsLocalMode_ReturnsOppositeOfIsServiceMode()
    {
        // Initially local mode
        _vm.IsLocalMode.Should().BeTrue();
        _vm.IsServiceMode.Should().BeFalse();

        // Flip to service mode (setting the field directly to avoid SwitchToLocalAsync)
        SetField("_isServiceMode", true);
        _vm.IsLocalMode.Should().BeFalse();
        _vm.IsServiceMode.Should().BeTrue();

        // Flip back
        SetField("_isServiceMode", false);
        _vm.IsLocalMode.Should().BeTrue();
        _vm.IsServiceMode.Should().BeFalse();
    }

    // ──────────────────────────────────────────────
    //  Mode toggle — PropertyChanged notifications
    // ──────────────────────────────────────────────

    [Fact]
    public void ServiceHost_Setter_RaisesPropertyChanged()
    {
        var changed = new List<string?>();
        _vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _vm.ServiceHost = "192.168.1.1";

        changed.Should().Contain(nameof(MainWindowViewModel.ServiceHost));
    }

    [Fact]
    public void ServicePort_Setter_RaisesPropertyChanged()
    {
        var changed = new List<string?>();
        _vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _vm.ServicePort = 8080;

        changed.Should().Contain(nameof(MainWindowViewModel.ServicePort));
    }

    [Fact]
    public void IsConnectedToService_Setter_RaisesPropertyChanged()
    {
        var changed = new List<string?>();
        _vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _vm.IsConnectedToService = true;

        changed.Should().Contain(nameof(MainWindowViewModel.IsConnectedToService));
    }

    // ──────────────────────────────────────────────
    //  Mode toggle — command CanExecute
    // ──────────────────────────────────────────────

    [Fact]
    public void ToggleLocalModeCommand_CanExecute_AlwaysReturnsTrue()
    {
        _vm.ToggleLocalModeCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void ToggleServiceModeCommand_CanExecute_AlwaysReturnsTrue()
    {
        _vm.ToggleServiceModeCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void ConnectToServiceCommand_CanExecute_WhenNotConnected_ReturnsTrue()
    {
        // Default state: _isConnectedToService is false
        _vm.ConnectToServiceCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void ConnectToServiceCommand_CanExecute_WhenConnected_ReturnsFalse()
    {
        SetField("_isConnectedToService", true);
        _vm.ConnectToServiceCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void DisconnectFromServiceCommand_CanExecute_WhenNotConnected_ReturnsFalse()
    {
        // Default state: _isConnectedToService is false
        _vm.DisconnectFromServiceCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void DisconnectFromServiceCommand_CanExecute_WhenConnected_ReturnsTrue()
    {
        SetField("_isConnectedToService", true);
        _vm.DisconnectFromServiceCommand.CanExecute(null).Should().BeTrue();
    }

    // ──────────────────────────────────────────────
    //  Mode toggle — property round-trips
    // ──────────────────────────────────────────────

    [Fact]
    public void ServiceHost_SetAndGet_RoundTrips()
    {
        _vm.ServiceHost = "broker.example.com";
        _vm.ServiceHost.Should().Be("broker.example.com");
    }

    [Fact]
    public void ServicePort_SetAndGet_RoundTrips()
    {
        _vm.ServicePort = 8080;
        _vm.ServicePort.Should().Be(8080);
    }

    [Fact]
    public void ServiceConnectionStatus_SetAndGet_RoundTrips()
    {
        _vm.ServiceConnectionStatus = "Connected to localhost:9100";
        _vm.ServiceConnectionStatus.Should().Be("Connected to localhost:9100");
    }

    // ──────────────────────────────────────────────
    //  Mode toggle — command execution
    // ──────────────────────────────────────────────

    [Fact]
    public void ToggleLocalModeCommand_Execute_SetsIsServiceModeToFalseSynchronously()
    {
        // Start in service mode
        SetField("_isServiceMode", true);
        _vm.IsServiceMode.Should().BeTrue();

        // Execute toggle to local — the setter sets _isServiceMode = false
        // synchronously before firing SwitchToLocalAsync (fire-and-forget),
        // so the property is immediately readable without awaiting the async method.
        _vm.ToggleLocalModeCommand.Execute(null);

        _vm.IsServiceMode.Should().BeFalse();
        _vm.IsLocalMode.Should().BeTrue();
    }

    [Fact]
    public async Task DisconnectFromServiceCommand_Execute_DisconnectsSuccessfully()
    {
        // Start connected
        SetField("_isConnectedToService", true);
        _vm.IsConnectedToService.Should().BeTrue();

        // Execute disconnect — runs DisconnectFromServiceAsync which checks
        // BrokerClient (null → skip) and sets IsConnectedToService = false
        _vm.DisconnectFromServiceCommand.Execute(null);

        // Wait briefly for the fire-and-forget async method to complete
        await Task.Delay(200);

        _vm.IsConnectedToService.Should().BeFalse();
        _vm.ServiceConnectionStatus.Should().Be("Disconnected");
    }

    // ──────────────────────────────────────────────
    //  HasSingleSelection / HasMultiSelection
    // ──────────────────────────────────────────────

    [Fact]
    public void HasSingleSelection_WhenNoSelection_ReturnsFalse()
    {
        _vm.HasSingleSelection.Should().BeFalse();
    }

    [Fact]
    public void HasSingleSelection_WhenNoAsset_HasMultiSelection_False()
    {
        // _selectedAssets contains multiple items but no _selectedAsset
        var list = new List<AssetListItem>
        {
            new() { Id = Guid.NewGuid(), FileName = "a.jpg", Title = "A" },
            new() { Id = Guid.NewGuid(), FileName = "b.jpg", Title = "B" },
        };
        SetField("_selectedAssets", list);
        // _selectedAsset is null

        _vm.HasMultiSelection.Should().BeTrue("HasMultiSelection depends on _selectedAssets.Count > 1");
        _vm.HasSingleSelection.Should().BeFalse("HasSingleSelection requires HasSelectedAsset AND !HasMultiSelection");
    }

    // ──────────────────────────────────────────────
    //  Helpers — reflection-based
    // ──────────────────────────────────────────────

    private void SetField(string fieldName, object? value)
    {
        var field = typeof(MainWindowViewModel)
            .GetField(fieldName,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public)!;
        field.SetValue(_vm, value);
    }

    private bool GetTagsDirtyField()
    {
        var field = typeof(MainWindowViewModel)
            .GetField("_tagsDirty",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)!;
        return (bool)field.GetValue(_vm)!;
    }

    private async Task InvokeAutoSaveTagsAsync()
    {
        var method = typeof(MainWindowViewModel)
            .GetMethod("AutoSaveTagsAsync",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)!;
        await (Task)method.Invoke(_vm, null)!;
    }

    private bool GetDescriptionDirtyField()
    {
        var field = typeof(MainWindowViewModel)
            .GetField("_descriptionDirty",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)!;
        return (bool)field.GetValue(_vm)!;
    }

    private bool GetCategoriesDirtyField()
    {
        var field = typeof(MainWindowViewModel)
            .GetField("_categoriesDirty",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)!;
        return (bool)field.GetValue(_vm)!;
    }

    /// <summary>
    /// Seeds a single asset with one keyword into the SQLite DB.
    /// Returns the asset ID.
    /// </summary>
    private async Task<Guid> SeedAssetWithKeywordAsync(string keywordName)
    {
        var assetId = Guid.NewGuid();
        await using var db = await _modeManager.CreateDbContextAsync();
        var asset = new DigitalAsset
        {
            Id = assetId,
            FileName = "test.jpg",
            FileExtension = ".jpg",
            MimeType = "image/jpeg",
            FileSize = 1024,
            ChecksumSha256 = new string('a', 64),
            StoragePath = "test.jpg",
            Title = "Test Asset",
            Type = AssetType.Image,
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow,
        };
        db.DigitalAssets.Add(asset);
        await db.AssociateKeywordsAsync(asset, [keywordName]);
        await db.SaveChangesAsync();
        return assetId;
    }

    /// <summary>
    /// Seeds a single asset with a description into the SQLite DB.
    /// Returns the asset ID.
    /// </summary>
    private async Task<Guid> SeedAssetWithDescriptionAsync(string description)
    {
        var assetId = Guid.NewGuid();
        await using var db = await _modeManager.CreateDbContextAsync();
        var asset = new DigitalAsset
        {
            Id = assetId,
            FileName = "desc-test.jpg",
            FileExtension = ".jpg",
            MimeType = "image/jpeg",
            FileSize = 1024,
            ChecksumSha256 = new string('b', 64),
            StoragePath = "desc-test.jpg",
            Title = "Description Test",
            Description = description,
            Type = AssetType.Image,
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow,
        };
        db.DigitalAssets.Add(asset);
        await db.SaveChangesAsync();
        return assetId;
    }

    /// <summary>
    /// Seeds a single asset with one category into the SQLite DB.
    /// Returns the asset ID.
    /// </summary>
    private async Task<Guid> SeedAssetWithCategoryAsync(string categoryName)
    {
        var assetId = Guid.NewGuid();
        await using var db = await _modeManager.CreateDbContextAsync();
        var asset = new DigitalAsset
        {
            Id = assetId,
            FileName = "cat-test.jpg",
            FileExtension = ".jpg",
            MimeType = "image/jpeg",
            FileSize = 1024,
            ChecksumSha256 = new string('c', 64),
            StoragePath = "cat-test.jpg",
            Title = "Category Test",
            Type = AssetType.Image,
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow,
        };
        db.DigitalAssets.Add(asset);
        await db.AssociateCategoriesAsync(asset, [categoryName]);
        await db.SaveChangesAsync();
        return assetId;
    }
}

/// <summary>
/// Context that creates a <see cref="MainWindowViewModel"/> connected to a fake
/// broker with a logged-in <see cref="AuthSession"/>. Used for testing
/// LogoutCommand state and execution.
/// </summary>
internal sealed class LoggedInVmContext : IAsyncDisposable
{
    private readonly string _basePath;
    private readonly BrokerClient _broker;
    private readonly AuthSession _auth;
    private readonly ModeManager _modeManager;

    public MainWindowViewModel Vm { get; private set; } = null!;
    public AuthSession AuthSession => _auth;

    public LoggedInVmContext()
    {
        _basePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _broker = new BrokerClient("localhost", 9999);
        _auth = new AuthSession(_broker);
        _modeManager = new ModeManager(_basePath, _broker, _auth);
    }

    public async Task InitializeAsync()
    {
        await _modeManager.InitializeAsync();

        // Set Token and CurrentUser via reflection to make IsLoggedIn = true
        var tokenField = typeof(AuthSession).GetField("<Token>k__BackingField",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        tokenField.SetValue(_auth, "test-jwt-token");

        var currentUserField = typeof(AuthSession).GetField("<CurrentUser>k__BackingField",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        currentUserField.SetValue(_auth, new Adam.Shared.Contracts.UserProfile { Username = "testuser" });

        var sidebar = new SidebarViewModel(_modeManager, new NullLogger<SidebarViewModel>());
        var gallery = new AssetGalleryViewModel(_modeManager, new NullLogger<AssetGalleryViewModel>());

        Vm = new MainWindowViewModel(
            new NullLogger<MainWindowViewModel>(),
            _modeManager,
            new MetadataWritebackService(),
            sidebar,
            gallery,
            new IngestionViewModel(_modeManager, new NullLogger<IngestionViewModel>()),
            new MetadataEditorViewModel(_modeManager),
            new AuditLogViewModel(_modeManager),
            new BulkOperationQueue(_modeManager, new NullLogger<BulkOperationQueue>()),
            startUp: false);

        // Set connected state via reflection to simulate a connected service
        var isConnectedField = typeof(MainWindowViewModel).GetField("_isConnectedToService",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        isConnectedField.SetValue(Vm, true);
    }

    public async ValueTask DisposeAsync()
    {
        await _broker.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_basePath))
                Directory.Delete(_basePath, recursive: true);
        }
        catch (IOException) { }
    }
}

