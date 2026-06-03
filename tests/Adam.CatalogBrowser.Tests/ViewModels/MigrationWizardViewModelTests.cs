using Adam.CatalogBrowser.Services;
using Adam.CatalogBrowser.ViewModels;
using Adam.Shared.Services;
using FluentAssertions;

namespace Adam.CatalogBrowser.Tests.ViewModels;

/// <summary>
/// Tests for <see cref="MigrationWizardViewModel"/> — the database migration
/// wizard that migrates standalone SQLite data to PostgreSQL or SQL Server.
/// </summary>
public class MigrationWizardViewModelTests : IDisposable
{
    private readonly string _basePath;
    private readonly ModeManager _modeManager;
    private readonly MigrationWizardViewModel _vm;

    public MigrationWizardViewModelTests()
    {
        _basePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _modeManager = new ModeManager(_basePath);
        _vm = new MigrationWizardViewModel(_modeManager);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_basePath))
                Directory.Delete(_basePath, recursive: true);
        }
        catch (IOException) { }
    }

    // ──────────────────────────────────────────────
    //  Constructor & initial state
    // ──────────────────────────────────────────────

    [Fact]
    public void Constructor_InitializesSourcePath_FromModeManager()
    {
        _vm.SourcePath.Should().Be(_modeManager.DbPath);
    }

    [Fact]
    public void Constructor_Commands_AreNotNull()
    {
        _vm.StartMigrationCommand.Should().NotBeNull();
        _vm.CancelMigrationCommand.Should().NotBeNull();
        _vm.BrowseSourceCommand.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_Defaults_ToSqliteProvider()
    {
        _vm.SelectedTargetProvider.Should().Be("sqlite");
        _vm.IsProviderSqlite.Should().BeTrue();
    }

    [Fact]
    public void Constructor_Log_IsEmpty()
    {
        _vm.Log.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_IsBusy_IsFalse()
    {
        _vm.IsBusy.Should().BeFalse();
    }

    // ──────────────────────────────────────────────
    //  Provider selection
    // ──────────────────────────────────────────────

    [Fact]
    public void SelectedTargetProvider_WhenChanged_UpdatesIsProviderSqlite()
    {
        _vm.SelectedTargetProvider = "postgresql";
        _vm.IsProviderSqlite.Should().BeFalse();

        _vm.SelectedTargetProvider = "sqlserver";
        _vm.IsProviderSqlite.Should().BeFalse();

        _vm.SelectedTargetProvider = "sqlite";
        _vm.IsProviderSqlite.Should().BeTrue();
    }

    // ──────────────────────────────────────────────
    //  Property round-trips
    // ──────────────────────────────────────────────

    [Fact]
    public void SourcePath_RoundTrips()
    {
        _vm.SourcePath = "/path/to/database.db";
        _vm.SourcePath.Should().Be("/path/to/database.db");
    }

    [Fact]
    public void TargetConnectionString_RoundTrips()
    {
        _vm.TargetConnectionString = "Server=localhost;Database=adam";
        _vm.TargetConnectionString.Should().Be("Server=localhost;Database=adam");
    }

    [Fact]
    public void StatusText_RoundTrips()
    {
        _vm.StatusText = "Migration completed";
        _vm.StatusText.Should().Be("Migration completed");
    }

    [Fact]
    public void ProgressValue_RoundTrips()
    {
        _vm.ProgressValue = 50;
        _vm.ProgressValue.Should().Be(50);
    }

    // ──────────────────────────────────────────────
    //  Command can-execute guards
    // ──────────────────────────────────────────────

    [Fact]
    public void StartMigrationCommand_WhenNotBusy_CanExecute()
    {
        _vm.StartMigrationCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void StartMigrationCommand_WhenBusy_CannotExecute()
    {
        _vm.IsBusy = true;
        _vm.StartMigrationCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void CancelMigrationCommand_WhenBusy_CanExecute()
    {
        _vm.IsBusy = true;
        _vm.CancelMigrationCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void CancelMigrationCommand_WhenNotBusy_CannotExecute()
    {
        _vm.CancelMigrationCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void BrowseSourceCommand_Always_CanExecute()
    {
        _vm.BrowseSourceCommand.CanExecute(null).Should().BeTrue();
    }

    // ──────────────────────────────────────────────
    //  PropertyChanged notifications
    // ──────────────────────────────────────────────

    [Fact]
    public void SettingProperties_RaisesPropertyChanged()
    {
        var changedProperties = new List<string?>();
        _vm.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);

        _vm.SourcePath = "/new/path.db";
        _vm.SelectedTargetProvider = "postgresql";
        _vm.TargetConnectionString = "Server=localhost";
        _vm.StatusText = "Running";
        _vm.ProgressValue = 75;
        _vm.IsBusy = true;

        changedProperties.Should().Contain(nameof(MigrationWizardViewModel.SourcePath));
        changedProperties.Should().Contain(nameof(MigrationWizardViewModel.SelectedTargetProvider));
        changedProperties.Should().Contain(nameof(MigrationWizardViewModel.IsProviderSqlite));
        changedProperties.Should().Contain(nameof(MigrationWizardViewModel.TargetConnectionString));
        changedProperties.Should().Contain(nameof(MigrationWizardViewModel.StatusText));
        changedProperties.Should().Contain(nameof(MigrationWizardViewModel.ProgressValue));
        changedProperties.Should().Contain(nameof(MigrationWizardViewModel.IsBusy));
    }

    // ──────────────────────────────────────────────
    //  Start migration with empty source
    // ──────────────────────────────────────────────

    [Fact]
    public async Task StartMigrationAsync_EmptySource_SetsStatusText()
    {
        _vm.SourcePath = string.Empty;

        // Invoke the private method via reflection
        var method = typeof(MigrationWizardViewModel)
            .GetMethod("StartMigrationAsync",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)!;

        await (Task)method.Invoke(_vm, null)!;

        _vm.StatusText.Should().Be("Please select a source database file.");
    }

    [Fact]
    public async Task StartMigrationAsync_WhenMigrationServiceNull_SetsStatusText()
    {
        var vm = new MigrationWizardViewModel(_modeManager, migrationService: null);
        vm.SourcePath = "/valid/path.db";

        var method = typeof(MigrationWizardViewModel)
            .GetMethod("StartMigrationAsync",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)!;

        await (Task)method.Invoke(vm, null)!;

        vm.StatusText.Should().Be("Migration service not available.");
    }

    // ──────────────────────────────────────────────
    //  CancelMigration
    // ──────────────────────────────────────────────

    [Fact]
    public void CancelMigration_SetsStatusText()
    {
        var method = typeof(MigrationWizardViewModel)
            .GetMethod("CancelMigration",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)!;

        method.Invoke(_vm, null);

        _vm.StatusText.Should().Be("Migration cancelled.");
    }
}
