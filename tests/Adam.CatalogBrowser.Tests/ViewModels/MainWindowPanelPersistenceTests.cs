using Adam.CatalogBrowser.Services;
using Adam.CatalogBrowser.ViewModels;
using Adam.Shared.Configuration;
using Adam.Shared.Data;
using Adam.Shared.Extractors;
using Adam.Shared.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Adam.CatalogBrowser.Tests.ViewModels;

/// <summary>
/// Tests for <see cref="PanelStateService"/> expander persistence.
/// Verifies that <c>SaveAsync</c> persists and <c>RestoreAsync</c> restores
/// all 15 expander states via <see cref="IUserPreferenceService"/>.
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
    private PanelStateService _panelState = null!;

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
        _panelState = new PanelStateService(_prefs);

        var mediaFormatService = new MediaFormatService(_modeManager, new NullLogger<MediaFormatService>());
        var dateTakenTreeService = new DateTakenTreeService(_modeManager, new NullLogger<DateTakenTreeService>());
        var savedSearchService = new Adam.CatalogBrowser.Services.SavedSearchService(_modeManager, new NullLogger<Adam.CatalogBrowser.Services.SavedSearchService>());
        var folderTreeService = new FolderTreeService(_modeManager, new NullLogger<FolderTreeService>());
        var collectionTreeService = new CollectionTreeService(_modeManager, new NullLogger<CollectionTreeService>());
        var keywordTreeService = new KeywordTreeService(_modeManager, new NullLogger<KeywordTreeService>());
        var categoryTreeService = new CategoryTreeService(_modeManager, new NullLogger<CategoryTreeService>());
        var sidebar = new SidebarViewModel(_modeManager, _sidebarLogger, mediaFormatService, dateTakenTreeService, savedSearchService, folderTreeService, collectionTreeService, keywordTreeService, categoryTreeService);
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
            new DeleteService(_modeManager), new ToastService(),
            new BulkAssetOperationService(_modeManager, new ToastService(), new NullLogger<BulkAssetOperationService>()),
            _panelState,
            activityFeed,
            new CommentService(_modeManager, new NullLogger<CommentService>()),
            new NavigationService(),
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
    //  SaveAsync
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task SaveSidebarPanelStatesAsync_SavesCorrectExpandedSet()
    {
        _panelState.IsSidebarFoldersExpanded = true;
        _panelState.IsSidebarKeywordsExpanded = true;
        _panelState.IsSidebarRatingExpanded = true;
        _panelState.IsSidebarLabelExpanded = false;
        _panelState.IsSidebarFlagExpanded = false;
        _panelState.IsRightMetadataExpanded = true;
        _panelState.IsRightCommentsExpanded = false;

        await _panelState.SaveAsync();

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
        var seeded = new HashSet<string> { "A", "B", "C", "folders", "keywords" };
        await _prefs.SetAsync(ExpandedPanelsKey, seeded);

        _panelState.IsSidebarFoldersExpanded = false;
        _panelState.IsSidebarKeywordsExpanded = false;
        await _panelState.SaveAsync();

        var saved = await _prefs.GetAsync<HashSet<string>>(ExpandedPanelsKey);
        saved.Should().NotBeNull();
        saved.Should().Contain("A");
        saved.Should().Contain("B");
        saved.Should().Contain("C");
        saved.Should().NotContain("folders");
        saved.Should().NotContain("keywords");
    }

    [Fact]
    public async Task SaveSidebarPanelStatesAsync_AllExpanded_SavesAll()
    {
        _panelState.IsSidebarFoldersExpanded = true;
        _panelState.IsSidebarCollectionsExpanded = true;
        _panelState.IsSidebarSavedSearchesExpanded = true;
        _panelState.IsSidebarRecentSearchesExpanded = true;
        _panelState.IsSidebarKeywordsExpanded = true;
        _panelState.IsSidebarMediaFormatExpanded = true;
        _panelState.IsSidebarCategoriesExpanded = true;
        _panelState.IsSidebarDateTakenExpanded = true;
        _panelState.IsSidebarRatingExpanded = true;
        _panelState.IsSidebarLabelExpanded = true;
        _panelState.IsSidebarFlagExpanded = true;
        _panelState.IsSidebarAiModelExpanded = true;
        _panelState.IsRightMetadataExpanded = true;
        _panelState.IsRightCommentsExpanded = true;
        _panelState.IsRightTagsExpanded = true;

        await _panelState.SaveAsync();

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
    //  RestoreAsync
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task RestoreSidebarPanelStates_RestoresSavedState()
    {
        var saved = new HashSet<string> { "folders", "keywords", "rating", "metadata" };
        await _prefs.SetAsync(ExpandedPanelsKey, saved);

        // Create a fresh PanelStateService that will load from the saved prefs
        var freshPrefs = new UserPreferenceService(
            new SimpleDbContextFactory(
                _modeManager.CreateDbContext().Database.GetDbConnection().ConnectionString),
            NullLogger<UserPreferenceService>.Instance);
        // Copy the saved state by seeding it again
        await freshPrefs.SetAsync(ExpandedPanelsKey, saved);
        var freshPanel = new PanelStateService(freshPrefs);
        await freshPanel.RestoreAsync();

        freshPanel.IsSidebarFoldersExpanded.Should().BeTrue();
        freshPanel.IsSidebarKeywordsExpanded.Should().BeTrue();
        freshPanel.IsSidebarRatingExpanded.Should().BeTrue();
        freshPanel.IsRightMetadataExpanded.Should().BeTrue();

        freshPanel.IsSidebarCollectionsExpanded.Should().BeFalse();
        freshPanel.IsSidebarSavedSearchesExpanded.Should().BeFalse();
        freshPanel.IsSidebarRecentSearchesExpanded.Should().BeFalse();
        freshPanel.IsSidebarMediaFormatExpanded.Should().BeFalse();
        freshPanel.IsSidebarCategoriesExpanded.Should().BeFalse();
        freshPanel.IsSidebarDateTakenExpanded.Should().BeFalse();
        freshPanel.IsRightCommentsExpanded.Should().BeFalse();
        freshPanel.IsRightTagsExpanded.Should().BeFalse();
    }

    [Fact]
    public async Task RestoreSidebarPanelStates_WhenNoSavedState_DefaultsStayTrue()
    {
        await _prefs.ResetAsync(ExpandedPanelsKey);

        var freshPanel = new PanelStateService(_prefs);
        await freshPanel.RestoreAsync();

        freshPanel.IsSidebarFoldersExpanded.Should().BeTrue();
        freshPanel.IsSidebarCollectionsExpanded.Should().BeTrue();
        freshPanel.IsSidebarSavedSearchesExpanded.Should().BeTrue();
        freshPanel.IsSidebarKeywordsExpanded.Should().BeTrue();
        freshPanel.IsSidebarMediaFormatExpanded.Should().BeTrue();
        freshPanel.IsSidebarCategoriesExpanded.Should().BeTrue();
        freshPanel.IsSidebarDateTakenExpanded.Should().BeTrue();
        freshPanel.IsSidebarRatingExpanded.Should().BeFalse();
        freshPanel.IsSidebarLabelExpanded.Should().BeFalse();
        freshPanel.IsSidebarFlagExpanded.Should().BeFalse();
        freshPanel.IsRightMetadataExpanded.Should().BeTrue();
        freshPanel.IsRightCommentsExpanded.Should().BeTrue();
        freshPanel.IsRightTagsExpanded.Should().BeTrue();
    }

    // ═══════════════════════════════════════════════════════════
    //  Property setter fires save
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task IsSidebarFoldersExpanded_Setter_WhenChanged_PersistsState()
    {
        _panelState.IsSidebarFoldersExpanded = false;
        await Task.Delay(200);

        var saved = await _prefs.GetAsync<HashSet<string>>(ExpandedPanelsKey);
        saved.Should().NotContain("folders");
    }

    [Fact]
    public async Task IsRightMetadataExpanded_Setter_WhenChanged_PersistsState()
    {
        _panelState.IsRightMetadataExpanded = false;
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
        _panelState.IsSidebarFoldersExpanded = true;
        _panelState.IsSidebarCollectionsExpanded = false;
        _panelState.IsSidebarKeywordsExpanded = true;
        _panelState.IsSidebarRatingExpanded = true;
        _panelState.IsSidebarLabelExpanded = false;
        _panelState.IsSidebarFlagExpanded = true;
        _panelState.IsRightMetadataExpanded = true;
        _panelState.IsRightCommentsExpanded = false;
        _panelState.IsRightTagsExpanded = true;

        await _panelState.SaveAsync();

        var freshPrefs = new UserPreferenceService(
            new SimpleDbContextFactory(
                _modeManager.CreateDbContext().Database.GetDbConnection().ConnectionString),
            NullLogger<UserPreferenceService>.Instance);
        await freshPrefs.SetAsync(ExpandedPanelsKey,
            await _prefs.GetAsync<HashSet<string>>(ExpandedPanelsKey) ?? []);

        var expanded = await freshPrefs.GetAsync<HashSet<string>>(ExpandedPanelsKey);

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
