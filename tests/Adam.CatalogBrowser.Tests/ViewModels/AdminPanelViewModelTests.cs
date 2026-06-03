using Adam.CatalogBrowser.Services;
using Adam.CatalogBrowser.ViewModels;
using Adam.Shared.Services;
using FluentAssertions;

namespace Adam.CatalogBrowser.Tests.ViewModels;

/// <summary>
/// Tests for <see cref="AdminPanelViewModel"/> — the admin dashboard that
/// consolidates mode toggling, service connection, service status display,
/// migration wizard, and Service Manager launcher.
/// </summary>
public class AdminPanelViewModelTests : IDisposable
{
    private readonly string _basePath;
    private readonly ModeManager _modeManager;
    private readonly AdminPanelViewModel _vm;

    public AdminPanelViewModelTests()
    {
        _basePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _modeManager = new ModeManager(_basePath);
        _vm = new AdminPanelViewModel(_modeManager);
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
    public void Constructor_StartsInLocalMode()
    {
        _vm.IsServiceMode.Should().BeFalse();
        _vm.IsLocalMode.Should().BeTrue();
        _vm.ModeLabel.Should().Be("Standalone");
        _vm.IsConnectedToService.Should().BeFalse();
    }

    [Fact]
    public void Constructor_InitializesServiceHostAndPort_FromModeManager()
    {
        _vm.ServiceHost.Should().Be("localhost");
        _vm.ServicePort.Should().Be(9100);
    }

    [Fact]
    public void Constructor_MigrationWizard_IsNotNull()
    {
        _vm.MigrationWizard.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_Commands_AreNotNull()
    {
        _vm.ToggleLocalCommand.Should().NotBeNull();
        _vm.ToggleServiceCommand.Should().NotBeNull();
        _vm.ConnectCommand.Should().NotBeNull();
        _vm.DisconnectCommand.Should().NotBeNull();
        _vm.LaunchServiceManagerCommand.Should().NotBeNull();
        _vm.RefreshServiceStatusCommand.Should().NotBeNull();
        _vm.ToggleMigrationWizardCommand.Should().NotBeNull();
    }

    // ──────────────────────────────────────────────
    //  Mode properties
    // ──────────────────────────────────────────────

    [Fact]
    public void IsServiceMode_WhenSet_UpdatesRelatedProperties()
    {
        _vm.IsServiceMode = true;

        _vm.IsServiceMode.Should().BeTrue();
        _vm.IsLocalMode.Should().BeFalse();
        _vm.ModeLabel.Should().Be("Multi-User");
    }

    [Fact]
    public void IsServiceMode_WhenCleared_UpdatesRelatedProperties()
    {
        _vm.IsServiceMode = true;
        _vm.IsServiceMode = false;

        _vm.IsServiceMode.Should().BeFalse();
        _vm.IsLocalMode.Should().BeTrue();
        _vm.ModeLabel.Should().Be("Standalone");
    }

    [Fact]
    public void IsServiceMode_SameValue_DoesNotFireChanges()
    {
        var changedProperties = new List<string?>();
        _vm.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);

        // Set to false when already false — no event
        _vm.IsServiceMode = false;
        changedProperties.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────
    //  Service connection properties
    // ──────────────────────────────────────────────

    [Fact]
    public void ServicePort_ClampsToValidRange()
    {
        _vm.ServicePort = 0;
        _vm.ServicePort.Should().Be(9100); // unchanged — below minimum

        _vm.ServicePort = 65536;
        _vm.ServicePort.Should().Be(9100); // unchanged — above maximum

        _vm.ServicePort = 8080;
        _vm.ServicePort.Should().Be(8080); // changed — valid
    }

    [Fact]
    public void ServiceHost_RoundTrips()
    {
        _vm.ServiceHost = "192.168.1.100";
        _vm.ServiceHost.Should().Be("192.168.1.100");
    }

    [Fact]
    public void IsConnectedToService_UpdatesIndicatorColor()
    {
        _vm.IsConnectedToService = false;
        _vm.ConnectionIndicatorColor.Should().Be("#F44336"); // red

        _vm.IsConnectedToService = true;
        _vm.ConnectionIndicatorColor.Should().Be("#4CAF50"); // green
    }

    [Fact]
    public void ConnectionStatus_RoundTrips()
    {
        _vm.ConnectionStatus = "Connected to localhost:9100";
        _vm.ConnectionStatus.Should().Be("Connected to localhost:9100");
    }

    // ──────────────────────────────────────────────
    //  Service status properties
    // ──────────────────────────────────────────────

    [Fact]
    public void ServiceState_UpdatesStateColor()
    {
        _vm.ServiceState = "Running";
        _vm.ServiceStateColor.Should().Be("#4CAF50");
        _vm.ServiceStateLabel.Should().Be("Running");

        _vm.ServiceState = "Stopped";
        _vm.ServiceStateColor.Should().Be("#F44336");
        _vm.ServiceStateLabel.Should().Be("Stopped");

        _vm.ServiceState = "Unknown";
        _vm.ServiceStateColor.Should().Be("#FF9800");
        _vm.ServiceStateLabel.Should().Be("Unknown");
    }

    [Fact]
    public void ServiceState_WithFallback_DefaultsToGray()
    {
        _vm.ServiceState = "SomeOtherState";
        _vm.ServiceStateColor.Should().Be("#9E9E9E");
        _vm.ServiceStateLabel.Should().Be("Not Available");
    }

    [Fact]
    public void ConnectedClients_RoundTrips()
    {
        _vm.ConnectedClients = 5;
        _vm.ConnectedClients.Should().Be(5);
    }

    [Fact]
    public void Uptime_RoundTrips()
    {
        _vm.Uptime = "5d 12h 30m";
        _vm.Uptime.Should().Be("5d 12h 30m");
    }

    [Fact]
    public void IsServiceStatusAvailable_RoundTrips()
    {
        _vm.IsServiceStatusAvailable = true;
        _vm.IsServiceStatusAvailable.Should().BeTrue();

        _vm.IsServiceStatusAvailable = false;
        _vm.IsServiceStatusAvailable.Should().BeFalse();
    }

    [Fact]
    public void StatusError_RoundTrips()
    {
        _vm.StatusError = "Error 5: Not found";
        _vm.StatusError.Should().Be("Error 5: Not found");
    }

    // ──────────────────────────────────────────────
    //  UI state properties
    // ──────────────────────────────────────────────

    [Fact]
    public void IsMigrationWizardVisible_RoundTrips()
    {
        _vm.IsMigrationWizardVisible.Should().BeFalse(); // default
        _vm.IsMigrationWizardVisible = true;
        _vm.IsMigrationWizardVisible.Should().BeTrue();
    }

    [Fact]
    public void IsBusy_RoundTrips()
    {
        _vm.IsBusy = true;
        _vm.IsBusy.Should().BeTrue();
    }

    [Fact]
    public void StatusText_RoundTrips()
    {
        _vm.StatusText = "Migration completed";
        _vm.StatusText.Should().Be("Migration completed");
    }

    // ──────────────────────────────────────────────
    //  FormatUptime utility
    // ──────────────────────────────────────────────

    [Fact]
    public void FormatUptime_LessThanOneDay_ReturnsHoursMinutesSeconds()
    {
        // Invoke the private method via reflection
        var method = typeof(AdminPanelViewModel)
            .GetMethod("FormatUptime",
                System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.NonPublic)!;

        var result = (string)method.Invoke(null, [(long)3661])!;
        result.Should().Be("1h 1m 1s");
    }

    [Fact]
    public void FormatUptime_MoreThanOneDay_ReturnsDaysHoursMinutes()
    {
        var method = typeof(AdminPanelViewModel)
            .GetMethod("FormatUptime",
                System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.NonPublic)!;

        var result = (string)method.Invoke(null, [(long)90061])!;
        result.Should().Be("1d 1h 1m");
    }

    [Fact]
    public void FormatUptime_Zero_ReturnsHoursMinutesSeconds()
    {
        var method = typeof(AdminPanelViewModel)
            .GetMethod("FormatUptime",
                System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.NonPublic)!;

        var result = (string)method.Invoke(null, [(long)0])!;
        result.Should().Be("0h 0m 0s");
    }

    // ──────────────────────────────────────────────
    //  ResolveServiceManagerPath (when exe not found)
    // ──────────────────────────────────────────────

    [Fact]
    public void ResolveServiceManagerPath_ReturnsEitherEmptyOrValidPath()
    {
        var method = typeof(AdminPanelViewModel)
            .GetMethod("ResolveServiceManagerPath",
                System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.NonPublic)!;

        var result = (string)method.Invoke(null, null)!;

        // The path may be empty (if ServiceManager isn't built yet) or
        // a valid path to the ServiceManager executable (if it exists in
        // the build output). Either is acceptable.
        if (string.IsNullOrEmpty(result))
        {
            result.Should().BeEmpty();
        }
        else
        {
            result.Should().EndWith("Adam.ServiceManager.exe");
            File.Exists(result).Should().BeTrue();
        }
    }

    // ──────────────────────────────────────────────
    //  PropertyChanged notifications
    // ──────────────────────────────────────────────

    [Fact]
    public void SettingProperties_RaisesPropertyChanged()
    {
        var changedProperties = new List<string?>();
        _vm.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);

        _vm.IsServiceMode = true;
        _vm.IsBusy = true;
        _vm.StatusText = "test";

        changedProperties.Should().Contain(nameof(AdminPanelViewModel.IsServiceMode));
        changedProperties.Should().Contain(nameof(AdminPanelViewModel.IsLocalMode));
        changedProperties.Should().Contain(nameof(AdminPanelViewModel.ModeLabel));
        changedProperties.Should().Contain(nameof(AdminPanelViewModel.IsBusy));
        changedProperties.Should().Contain(nameof(AdminPanelViewModel.StatusText));
    }

    [Fact]
    public void SettingServiceState_RaisesStateColorAndLabelProperties()
    {
        var changedProperties = new List<string?>();
        _vm.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);

        _vm.ServiceState = "Running";

        changedProperties.Should().Contain(nameof(AdminPanelViewModel.ServiceState));
        changedProperties.Should().Contain(nameof(AdminPanelViewModel.ServiceStateColor));
        changedProperties.Should().Contain(nameof(AdminPanelViewModel.ServiceStateLabel));
    }

    [Fact]
    public void ToggleMigrationWizardCommand_TogglesVisibility()
    {
        _vm.IsMigrationWizardVisible.Should().BeFalse();

        _vm.ToggleMigrationWizardCommand.Execute(null);
        _vm.IsMigrationWizardVisible.Should().BeTrue();

        _vm.ToggleMigrationWizardCommand.Execute(null);
        _vm.IsMigrationWizardVisible.Should().BeFalse();
    }

    [Fact]
    public void LaunchServiceManagerCommand_DoesNotThrow()
    {
        // The command should either find the ServiceManager and attempt to
        // launch it (which may fail with an elevation error in test context),
        // or set a failure status — but should never throw an unhandled exception.
        Action act = () => _vm.LaunchServiceManagerCommand.Execute(null);
        act.Should().NotThrow();
    }
}
