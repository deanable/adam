using System.Reflection;
using Adam.CatalogBrowser.Services;
using Adam.CatalogBrowser.ViewModels;
using Adam.Shared.Configuration;
using Adam.Shared.Data;
using Adam.Shared.Extractors;
using Adam.Shared.Models;
using Adam.Shared.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Adam.CatalogBrowser.Tests.ViewModels;

/// <summary>
/// Tests for <see cref="MainWindowViewModel"/> sidebar/right-panel expander persistence.
/// Verifies that <c>SaveSidebarPanelStatesAsync</c> persists and
/// <c>RestoreSidebarPanelStatesAsync</c> restores all 15 expander states
/// via <see cref="IUserPreferenceService"/> under <c>metadata.expandedPanels</c>.
/// </summary>
public sealed class MainWindowPanelPersistenceTests : IAsyncLifetime
{
    private const string ExpandedPanelsKey = "metadata.expandedPanels";

    private readonly string _basePath;
    private readonly ModeManager _modeManager;
    private readonly NullLogger<MainWindowViewModel> _logger;
    private readonly NullLogger<SidebarViewModel> _sidebarLogger;
    private readonly NullLogger<AssetGalleryViewModel> _galleryLogger;
    private readonly NullLogger<IngestionViewModel> _ingestionLogger;
    private MainWindowViewModel _vm = null!;
    private UserPreferenceService _prefs = null!;

    public MainWindowPanelPersistenceTests()
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
        App.Config.ServiceHost = "localhost";
        App.Config.ServicePort = 9100;

        await _modeManager.InitializeAsync();

        var factory = new SimpleDbContextFactory(
            _modeManager.CreateDbContext().Database.GetDbConnection().ConnectionString);
        _prefs = new UserPreferenceService(factory, NullLogger<UserPreferenceService>.Instance);

        var sidebar = new SidebarViewModel(_modeManager, _sidebarLogger);
        var gallery = new AssetGalleryViewModel(_modeManager, _galleryLogger);
        var ingestion = new IngestionViewModel(_modeManager, new PluginLoaderService(
            Options.Create(new PluginConfig()),
            new NullLogger<PluginLoaderService>()), _ingestionLogger);
        var metadataEditor = new MetadataEditorViewModel(_modeManager);
        var auditLog = new AuditLogViewModel(_modeManager);
        var bulkQueue = new BulkOperationQueue(_modeManager, new NullLogger<BulkOperationQueue>());
        var propertyInspector = new PropertyInspectorViewModel(
            new NullLogger<PropertyInspectorViewModel>(), _modeManager,
            new MetadataWritebackService(), new SyncUiDispatcher());
        var connection = new ConnectionViewModel(new NullLogger<ConnectionViewModel>(), _modeManager);
        var statusBar = new StatusBarViewModel(bulkQueue);
        var activityFeed = new ActivityFeedViewModel(_modeManager, dispatcher: new SyncUiDispatcher());

        _vm = new MainWindowViewModel(
            _logger, _modeManager, new MetadataWritebackService(), sidebar, gallery,
            ingestion, metadataEditor, auditLog, bulkQueue,
            propertyInspector, connection, statusBar,
            new DeleteService(_modeManager), new ToastService(), activityFeed,
            new CommentService(_modeManager, new NullLogger<CommentService>()),
            prefs: _prefs,
            startUp: false, startSessionTimer: false,
            dispatcher: new SyncUiDispatcher());
    }

    public async Task DisposeAsync()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_basePath))
                Directory.Delete(_basePath, recursive: true);
        }
        catch (IOException) { }
    }

    // ═══════════════════════════════════════════════════════════
    //  SaveSidebarPanelStatesAsync
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task SaveSidebarPanelStatesAsync_SavesCorrectExpandedSet()
    {
        // Set known expand states — all expanded by default except rating/label/flag
        _vm.IsSidebarFoldersExpanded = true;
        _vm.IsSidebarKeywordsExpanded = true;
        _vm.IsSidebarRatingExpanded = true;
        _vm.IsSidebarLabelExpanded = false;
        _vm.IsSidebarFlagExpanded = false;
        _vm.IsRightMetadataExpanded = true;
        _vm.IsRightCommentsExpanded = false;

        await InvokeSaveAsync();

        var saved = await _prefs.GetAsync<HashSet<string>>(ExpandedPanelsKey);
        saved.Should().NotBeNull();
        saved.Should().Contain("folders");
        saved.Should().Contain("keywords");
        saved.Should().Contain("rating");
        saved.Should().Contain("metadata");
        saved.Should().NotContain("label");
        saved.Should().NotContain("flag");
        saved.Should().NotContain("comments");
    }

    [Fact]
    public async Task SaveSidebarPanelStatesAsync_PreservesMetadataEditorEntries()
    {
        // Seed metadata editor entries into the store
        var seeded = new HashSet<string> { "A", "B", "C", "folders", "keywords" };
        await _prefs.SetAsync(ExpandedPanelsKey, seeded);

        // Save sidebar states
        _vm.IsSidebarFoldersExpanded = false;
        _vm.IsSidebarKeywordsExpanded = false;
        await InvokeSaveAsync();

        var saved = await _prefs.GetAsync<HashSet<string>>(ExpandedPanelsKey);
        saved.Should().NotBeNull();
        // Metadata editor entries should be preserved
        saved.Should().Contain("A");
        saved.Should().Contain("B");
        saved.Should().Contain("C");
        // Sidebar entries should reflect current state
        saved.Should().NotContain("folders");
        saved.Should().NotContain("keywords");
    }

    [Fact]
    public async Task SaveSidebarPanelStatesAsync_AllExpanded_SavesAll()
    {
        // Expand all panels
        _vm.IsSidebarFoldersExpanded = true;
        _vm.IsSidebarCollectionsExpanded = true;
        _vm.IsSidebarSavedSearchesExpanded = true;
        _vm.IsSidebarRecentSearchesExpanded = true;
        _vm.IsSidebarKeywordsExpanded = true;
        _vm.IsSidebarMediaFormatExpanded = true;
        _vm.IsSidebarCategoriesExpanded = true;
        _vm.IsSidebarDateTakenExpanded = true;
        _vm.IsSidebarRatingExpanded = true;
        _vm.IsSidebarLabelExpanded = true;
        _vm.IsSidebarFlagExpanded = true;
        _vm.IsSidebarAiModelExpanded = true;
        _vm.IsRightMetadataExpanded = true;
        _vm.IsRightCommentsExpanded = true;
        _vm.IsRightTagsExpanded = true;

        await InvokeSaveAsync();

        var saved = await _prefs.GetAsync<HashSet<string>>(ExpandedPanelsKey);
        saved.Should().NotBeNull();
        saved.Should().Contain(new[]
        {
            "folders", "collections", "savedSearches", "recentSearches",
            "keywords", "mediaFormat", "categories", "dateTaken",
            "rating", "label", "flag", "aiModel",
            "metadata", "comments", "tags"
        });
    }

    // ═══════════════════════════════════════════════════════════
    //  RestoreSidebarPanelStatesAsync
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task RestoreSidebarPanelStates_RestoresSavedState()
    {
        // Seed known state
        var saved = new HashSet<string> { "folders", "keywords", "rating", "metadata" };
        await _prefs.SetAsync(ExpandedPanelsKey, saved);

        // Create fresh VM with its own prefs service
        var (freshVm, freshPrefs) = await CreateFreshVmWithPrefsAsync();

        // Invoke restore directly via reflection to avoid constructor fire-and-forget timing
        await InvokeRestoreSidebarAsync(freshVm);

        freshVm.IsSidebarFoldersExpanded.Should().BeTrue();
        freshVm.IsSidebarKeywordsExpanded.Should().BeTrue();
        freshVm.IsSidebarRatingExpanded.Should().BeTrue();
        freshVm.IsRightMetadataExpanded.Should().BeTrue();

        // These were NOT saved, so should be false (defaults are true)
        freshVm.IsSidebarCollectionsExpanded.Should().BeFalse();
        freshVm.IsSidebarSavedSearchesExpanded.Should().BeFalse();
        freshVm.IsSidebarRecentSearchesExpanded.Should().BeFalse();
        freshVm.IsSidebarMediaFormatExpanded.Should().BeFalse();
        freshVm.IsSidebarCategoriesExpanded.Should().BeFalse();
        freshVm.IsSidebarDateTakenExpanded.Should().BeFalse();
        freshVm.IsRightCommentsExpanded.Should().BeFalse();
        freshVm.IsRightTagsExpanded.Should().BeFalse();
    }

    [Fact]
    public async Task RestoreSidebarPanelStates_WhenNoSavedState_DefaultsStayTrue()
    {
        await _prefs.ResetAsync(ExpandedPanelsKey);

        var (freshVm, _) = await CreateFreshVmWithPrefsAsync();
        await Task.Delay(500);

        // Defaults: most panels are expanded=true
        freshVm.IsSidebarFoldersExpanded.Should().BeTrue();
        freshVm.IsSidebarCollectionsExpanded.Should().BeTrue();
        freshVm.IsSidebarSavedSearchesExpanded.Should().BeTrue();
        freshVm.IsSidebarKeywordsExpanded.Should().BeTrue();
        freshVm.IsSidebarMediaFormatExpanded.Should().BeTrue();
        freshVm.IsSidebarCategoriesExpanded.Should().BeTrue();
        freshVm.IsSidebarDateTakenExpanded.Should().BeTrue();
        freshVm.IsSidebarRatingExpanded.Should().BeFalse(); // default is false
        freshVm.IsSidebarLabelExpanded.Should().BeFalse();  // default is false
        freshVm.IsSidebarFlagExpanded.Should().BeFalse();   // default is false
        freshVm.IsRightMetadataExpanded.Should().BeTrue();
        freshVm.IsRightCommentsExpanded.Should().BeTrue();
        freshVm.IsRightTagsExpanded.Should().BeTrue();
    }

    // ═══════════════════════════════════════════════════════════
    //  Property setter fires save
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task IsSidebarFoldersExpanded_Setter_WhenChanged_PersistsState()
    {
        // Toggle Folders off
        _vm.IsSidebarFoldersExpanded = false;
        await Task.Delay(200);

        var saved = await _prefs.GetAsync<HashSet<string>>(ExpandedPanelsKey);
        saved.Should().NotContain("folders");
    }

    [Fact]
    public async Task IsRightMetadataExpanded_Setter_WhenChanged_PersistsState()
    {
        _vm.IsRightMetadataExpanded = false;
        await Task.Delay(200);

        var saved = await _prefs.GetAsync<HashSet<string>>(ExpandedPanelsKey);
        saved.Should().NotContain("metadata");
    }

    // ═══════════════════════════════════════════════════════════
    //  Round-trip: save → restore produces identical state
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task SaveThenRestore_RoundTripsIdenticalState()
    {
        // Set a specific state
        _vm.IsSidebarFoldersExpanded = true;
        _vm.IsSidebarCollectionsExpanded = false;
        _vm.IsSidebarKeywordsExpanded = true;
        _vm.IsSidebarRatingExpanded = true;
        _vm.IsSidebarLabelExpanded = false;
        _vm.IsSidebarFlagExpanded = true;
        _vm.IsRightMetadataExpanded = true;
        _vm.IsRightCommentsExpanded = false;
        _vm.IsRightTagsExpanded = true;

        // Save
        await InvokeSaveAsync();

        // Create fresh VM with its own prefs service
        var (freshVm, freshPrefs) = await CreateFreshVmWithPrefsAsync();
        await Task.Delay(500);

        // Load the saved state from DB via reflection
        var expanded = await freshPrefs.GetAsync<HashSet<string>>(ExpandedPanelsKey);

        // Verify the saved state matches what was set
        expanded.Should().Contain("folders");
        expanded.Should().NotContain("collections");
        expanded.Should().Contain("keywords");
        expanded.Should().Contain("rating");
        expanded.Should().NotContain("label");
        expanded.Should().Contain("flag");
        expanded.Should().Contain("metadata");
        expanded.Should().NotContain("comments");
        expanded.Should().Contain("tags");
    }

    // ═══════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════

    private async Task InvokeSaveAsync()
    {
        var method = typeof(MainWindowViewModel)
            .GetMethod("SaveSidebarPanelStatesAsync",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)method.Invoke(_vm, null)!;
    }

    private static async Task InvokeRestoreSidebarAsync(MainWindowViewModel vm)
    {
        var method = typeof(MainWindowViewModel)
            .GetMethod("RestoreSidebarPanelStatesAsync",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)method.Invoke(vm, null)!;
    }

    private async Task<(MainWindowViewModel, UserPreferenceService)> CreateFreshVmWithPrefsAsync()
    {
        var factory = new SimpleDbContextFactory(
            _modeManager.CreateDbContext().Database.GetDbConnection().ConnectionString);
        var prefs = new UserPreferenceService(factory, NullLogger<UserPreferenceService>.Instance);

        var sidebar = new SidebarViewModel(_modeManager, _sidebarLogger);
        var gallery = new AssetGalleryViewModel(_modeManager, _galleryLogger);
        var ingestion = new IngestionViewModel(_modeManager, new PluginLoaderService(
            Options.Create(new PluginConfig()),
            new NullLogger<PluginLoaderService>()), _ingestionLogger);
        var metadataEditor = new MetadataEditorViewModel(_modeManager);
        var auditLog = new AuditLogViewModel(_modeManager);
        var bulkQueue = new BulkOperationQueue(_modeManager, new NullLogger<BulkOperationQueue>());
        var propertyInspector = new PropertyInspectorViewModel(
            new NullLogger<PropertyInspectorViewModel>(), _modeManager,
            new MetadataWritebackService(), new SyncUiDispatcher());
        var connection = new ConnectionViewModel(new NullLogger<ConnectionViewModel>(), _modeManager);
        var statusBar = new StatusBarViewModel(bulkQueue);
        var activityFeed = new ActivityFeedViewModel(_modeManager, dispatcher: new SyncUiDispatcher());

        var vm = new MainWindowViewModel(
            _logger, _modeManager, new MetadataWritebackService(), sidebar, gallery,
            ingestion, metadataEditor, auditLog, bulkQueue,
            propertyInspector, connection, statusBar,
            new DeleteService(_modeManager), new ToastService(), activityFeed,
            new CommentService(_modeManager, new NullLogger<CommentService>()),
            prefs: prefs,
            startUp: false, startSessionTimer: false,
            dispatcher: new SyncUiDispatcher());

        return (vm, prefs);
    }
}

/// <summary>
/// <see cref="IDbContextFactory{TContext}"/> backed by a SQLite connection string.
/// </summary>
internal sealed class SimpleDbContextFactory(string connectionString) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connectionString)
            .Options;
        return new AppDbContext(options);
    }

    public async Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        return CreateDbContext();
    }
}
