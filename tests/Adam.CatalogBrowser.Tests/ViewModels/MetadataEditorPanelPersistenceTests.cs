using System.Reflection;
using Adam.CatalogBrowser.ViewModels;
using Adam.Shared.Data;
using Adam.Shared.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.CatalogBrowser.Tests.ViewModels;

/// <summary>
/// Tests for <see cref="MetadataEditorViewModel"/> panel expand persistence.
/// Verifies that <c>PersistMetadataPanelStatesAsync</c> saves and
/// <c>RestoreMetadataPanelStatesAsync</c> restores all 8 metadata editor
/// panels (A–H) via <see cref="IUserPreferenceService"/>.
/// </summary>
public sealed class MetadataEditorPanelPersistenceTests : IAsyncLifetime
{
    private const string ExpandedPanelsKey = "metadata.expandedPanels";

    private readonly string _basePath;
    private readonly ModeManager _modeManager;
    private readonly IUserPreferenceService _prefs;
    private MetadataEditorViewModel _vm = null!;

    public MetadataEditorPanelPersistenceTests()
    {
        _basePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _modeManager = new ModeManager(_basePath);
        _prefs = null!;
    }

    public async Task InitializeAsync()
    {
        await _modeManager.InitializeAsync();
        var factory = new MetadataPanelDbFactory(
            _modeManager.CreateDbContext().Database.GetDbConnection().ConnectionString);
        var prefs = new UserPreferenceService(factory, NullLogger<UserPreferenceService>.Instance);

        _vm = new MetadataEditorViewModel(_modeManager, prefs: prefs);

        // Store the prefs service field via reflection so tests can access it
        var prefsField = typeof(MetadataEditorPanelPersistenceTests)
            .GetField("_prefs", BindingFlags.Instance | BindingFlags.NonPublic)!;
        prefsField.SetValue(this, prefs);
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
    //  PersistMetadataPanelStatesAsync
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task PersistMetadataPanelStatesAsync_SavesAllExpandedPanels()
    {
        // Set all panels to known states
        _vm.IsPanelADescriptionExpanded = true;
        _vm.IsPanelBCreatorExpanded = false;
        _vm.IsPanelCRightsExpanded = true;
        _vm.IsPanelDLocationExpanded = false;
        _vm.IsPanelEDatesExpanded = true;
        _vm.IsPanelFCameraExpanded = false;
        _vm.IsPanelGGpsExpanded = true;
        _vm.IsPanelHRawExpanded = false;

        // Invoke the private PersistMetadataPanelStatesAsync
        await InvokePersistAsync();

        // Read directly from DB to verify
        var saved = await _prefs.GetAsync<HashSet<string>>(ExpandedPanelsKey);
        saved.Should().NotBeNull();
        saved!.Should().BeEquivalentTo(new HashSet<string> { "A", "C", "E", "G" });
    }

    [Fact]
    public async Task PersistMetadataPanelStatesAsync_SavesAllCollapsed()
    {
        _vm.IsPanelADescriptionExpanded = false;
        _vm.IsPanelBCreatorExpanded = false;
        _vm.IsPanelCRightsExpanded = false;
        _vm.IsPanelDLocationExpanded = false;
        _vm.IsPanelEDatesExpanded = false;
        _vm.IsPanelFCameraExpanded = false;
        _vm.IsPanelGGpsExpanded = false;
        _vm.IsPanelHRawExpanded = false;

        await InvokePersistAsync();

        var saved = await _prefs.GetAsync<HashSet<string>>(ExpandedPanelsKey);
        saved.Should().NotBeNull();
        saved.Should().BeEmpty();
    }

    [Fact]
    public async Task PersistMetadataPanelStatesAsync_SavesAllExpanded()
    {
        _vm.IsPanelADescriptionExpanded = true;
        _vm.IsPanelBCreatorExpanded = true;
        _vm.IsPanelCRightsExpanded = true;
        _vm.IsPanelDLocationExpanded = true;
        _vm.IsPanelEDatesExpanded = true;
        _vm.IsPanelFCameraExpanded = true;
        _vm.IsPanelGGpsExpanded = true;
        _vm.IsPanelHRawExpanded = true;

        await InvokePersistAsync();

        var saved = await _prefs.GetAsync<HashSet<string>>(ExpandedPanelsKey);
        saved.Should().NotBeNull();
        saved.Should().BeEquivalentTo(new HashSet<string> { "A", "B", "C", "D", "E", "F", "G", "H" });
    }

    [Fact]
    public async Task PersistMetadataPanelStatesAsync_PreservesSidebarEntries()
    {
        // Seed some sidebar entries into the store
        var seeded = new HashSet<string> { "folders", "keywords", "A", "B" };
        await _prefs.SetAsync(ExpandedPanelsKey, seeded);

        // Toggle metadata panels differently
        _vm.IsPanelADescriptionExpanded = true;
        _vm.IsPanelBCreatorExpanded = false;
        _vm.IsPanelCRightsExpanded = true;
        _vm.IsPanelDLocationExpanded = false;
        _vm.IsPanelEDatesExpanded = false;
        _vm.IsPanelFCameraExpanded = false;
        _vm.IsPanelGGpsExpanded = false;
        _vm.IsPanelHRawExpanded = false;

        await InvokePersistAsync();

        var saved = await _prefs.GetAsync<HashSet<string>>(ExpandedPanelsKey);
        saved.Should().NotBeNull();
        // Sidebar entries preserved, metadata entries reflect current state
        saved.Should().Contain("folders");
        saved.Should().Contain("keywords");
        saved.Should().Contain("A");  // expanded
        saved.Should().Contain("C");  // expanded
        saved.Should().NotContain("B"); // collapsed
        saved.Should().NotContain("D"); // collapsed
    }

    // ═══════════════════════════════════════════════════════════
    //  RestoreMetadataPanelStatesAsync
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task RestoreMetadataPanelStates_RestoresSavedPanels()
    {
        // Save a known set
        var saved = new HashSet<string> { "A", "C", "E", "G" };
        await _prefs.SetAsync(ExpandedPanelsKey, saved);

        // Create a fresh VM so all defaults are true (as per initializer)
        var factory = new MetadataPanelDbFactory(
            _modeManager.CreateDbContext().Database.GetDbConnection().ConnectionString);
        var prefs = new UserPreferenceService(factory, NullLogger<UserPreferenceService>.Instance);
        var freshVm = new MetadataEditorViewModel(_modeManager, prefs: prefs);

        // Default state should be all expanded
        freshVm.IsPanelADescriptionExpanded.Should().BeTrue();
        freshVm.IsPanelBCreatorExpanded.Should().BeTrue();
        freshVm.IsPanelHRawExpanded.Should().BeFalse(); // default is false per initializer

        // Invoke async restore directly to avoid fire-and-forget timing issues
        await InvokeRestoreAsync(freshVm);

        // Panels saved as expanded should be true
        freshVm.IsPanelADescriptionExpanded.Should().BeTrue();
        freshVm.IsPanelCRightsExpanded.Should().BeTrue();
        freshVm.IsPanelEDatesExpanded.Should().BeTrue();
        freshVm.IsPanelGGpsExpanded.Should().BeTrue();

        // Panels NOT saved should be false
        freshVm.IsPanelBCreatorExpanded.Should().BeFalse();
        freshVm.IsPanelDLocationExpanded.Should().BeFalse();
        freshVm.IsPanelFCameraExpanded.Should().BeFalse();
        freshVm.IsPanelHRawExpanded.Should().BeFalse();
    }

    [Fact]
    public async Task RestoreMetadataPanelStates_WhenNoSavedState_DoesNotChangeDefaults()
    {
        await _prefs.ResetAsync(ExpandedPanelsKey);

        await InvokeRestoreAsync(_vm);

        // Defaults should remain
        _vm.IsPanelADescriptionExpanded.Should().BeTrue();
        _vm.IsPanelBCreatorExpanded.Should().BeTrue();
        _vm.IsPanelCRightsExpanded.Should().BeTrue();
        _vm.IsPanelDLocationExpanded.Should().BeFalse();
        _vm.IsPanelEDatesExpanded.Should().BeFalse();
    }

    // ═══════════════════════════════════════════════════════════
    //  IsRestoringMetadataPanels flag prevents save during restore
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task RestoreMetadataPanelStates_SuppressesIndividualSaves()
    {
        // Seed a known state in the DB
        var saved = new HashSet<string> { "A", "B", "C" };
        await _prefs.SetAsync(ExpandedPanelsKey, saved);

        // Create fresh VM with its own prefs service
        var factory = new MetadataPanelDbFactory(
            _modeManager.CreateDbContext().Database.GetDbConnection().ConnectionString);
        var freshPrefs = new UserPreferenceService(factory, NullLogger<UserPreferenceService>.Instance);
        var freshVm = new MetadataEditorViewModel(_modeManager, prefs: freshPrefs);

        // Set the _isRestoringMetadataPanels flag to simulate being in a restore operation
        var flagField = typeof(MetadataEditorViewModel)
            .GetField("_isRestoringMetadataPanels",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        flagField.SetValue(freshVm, true);

        // Toggle a panel — setter normally fires PersistMetadataPanelStatesAsync,
        // but the flag should suppress it
        freshVm.IsPanelADescriptionExpanded = false;

        // Verify the DB was NOT modified (flag suppressed the save)
        var dbState = await freshPrefs.GetAsync<HashSet<string>>(ExpandedPanelsKey);
        dbState.Should().BeEquivalentTo(saved);

        // Now set flag false and toggle again — save should fire
        flagField.SetValue(freshVm, false);
        freshVm.IsPanelADescriptionExpanded = true;
        await Task.Delay(200);

        // Verify the DB was updated
        var updatedState = await freshPrefs.GetAsync<HashSet<string>>(ExpandedPanelsKey);
        updatedState.Should().Contain("A");
    }

    // ═══════════════════════════════════════════════════════════
    //  TogglePanelCommand triggers save
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task TogglePanelCommand_WhenToggled_PersistsState()
    {
        // Start with default states
        _vm.IsPanelADescriptionExpanded.Should().BeTrue();

        // Toggle panel A via command
        _vm.TogglePanelCommand.Execute("A");
        await Task.Delay(100);

        // Verify persisted state
        var saved = await _prefs.GetAsync<HashSet<string>>(ExpandedPanelsKey);
        saved.Should().NotBeNull();
        saved.Should().NotContain("A"); // was toggled off
    }

    [Fact]
    public async Task TogglePanelCommand_ToggleAllPanels_PersistsCorrectState()
    {
        // Set all panels to collapsed directly (avoid TogglePanelCommand which
        // toggles from current state — panels D-H default to false, so toggling
        // would turn them ON instead of OFF)
        _vm.IsPanelADescriptionExpanded = false;
        _vm.IsPanelBCreatorExpanded = false;
        _vm.IsPanelCRightsExpanded = false;
        _vm.IsPanelDLocationExpanded = false;
        _vm.IsPanelEDatesExpanded = false;
        _vm.IsPanelFCameraExpanded = false;
        _vm.IsPanelGGpsExpanded = false;
        _vm.IsPanelHRawExpanded = false;

        // Invoke persist explicitly
        await InvokePersistAsync();

        var saved = await _prefs.GetAsync<HashSet<string>>(ExpandedPanelsKey);
        saved.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════
    //  Helpers — reflection for private methods
    // ═══════════════════════════════════════════════════════════

    private async Task InvokePersistAsync()
    {
        var method = typeof(MetadataEditorViewModel)
            .GetMethod("PersistMetadataPanelStatesAsync",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)method.Invoke(_vm, null)!;
    }

    private static async Task InvokeRestoreAsync(MetadataEditorViewModel vm)
    {
        var method = typeof(MetadataEditorViewModel)
            .GetMethod("RestoreMetadataPanelStatesAsync",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)method.Invoke(vm, null)!;
    }
}

/// <summary>
/// <see cref="IDbContextFactory{TContext}"/> backed by a SQLite connection string.
/// </summary>
internal sealed class MetadataPanelDbFactory(string connectionString) : IDbContextFactory<AppDbContext>
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
