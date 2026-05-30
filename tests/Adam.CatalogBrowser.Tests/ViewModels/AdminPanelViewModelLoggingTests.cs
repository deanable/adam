using Adam.CatalogBrowser.Services;
using Adam.CatalogBrowser.ViewModels;
using Adam.Shared.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;

namespace Adam.CatalogBrowser.Tests.ViewModels;

/// <summary>
/// Tests that <see cref="AdminPanelViewModel"/> writes log messages correctly
/// during service installation, uninstallation, and status refresh operations.
/// </summary>
public sealed class AdminPanelViewModelLoggingTests
{
    [Fact]
    public void Constructor_WithNoSupportedInstallers_LogsInstallerSelection()
    {
        var logger = new TestLogger<AdminPanelViewModel>();
        var modeManager = new ModeManager(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));

        var vm = new AdminPanelViewModel(modeManager, Enumerable.Empty<IServiceInstaller>(), logger);

        logger.Messages.Should().Contain(m =>
            m.Contains("AdminPanelViewModel") && m.Contains("NullServiceInstaller"));
    }

    [Fact]
    public void Constructor_WithAllInstallers_LogsAvailableInstallers()
    {
        var logger = new TestLogger<AdminPanelViewModel>();
        var modeManager = new ModeManager(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        var installers = new IServiceInstaller[]
        {
            new WindowsServiceInstaller(),
            new LinuxServiceInstaller(),
            new MacOsServiceInstaller(),
        };

        var vm = new AdminPanelViewModel(modeManager, installers, logger);

        logger.Messages.Should().Contain(m =>
            m.Contains("AdminPanelViewModel") && m.Contains("Available installers count = 3"));
        logger.Messages.Should().Contain(m =>
            m.Contains("WindowsServiceInstaller"));
    }

    [Fact]
    public async Task SaveModeAsync_WithStandalone_WritesLogEntries()
    {
        var logger = new TestLogger<AdminPanelViewModel>();
        var basePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var modeManager = new ModeManager(basePath);
        await modeManager.InitializeAsync();

        var vm = new AdminPanelViewModel(modeManager, Enumerable.Empty<IServiceInstaller>(), logger);
        vm.SelectedMode = "Standalone";

        // Trigger SaveMode via the command
        vm.SaveModeCommand.Execute(null);

        logger.Messages.Should().Contain(m =>
            m.Contains("Saving mode") && m.Contains("Standalone"));
        logger.Messages.Should().Contain(m =>
            m.Contains("Initializing standalone mode"));
        logger.Messages.Should().Contain(m =>
            m.Contains("Settings saved"));
    }

    [Fact]
    public async Task SaveModeAsync_WithMultiUser_WritesLogEntries()
    {
        var logger = new TestLogger<AdminPanelViewModel>();
        var basePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var modeManager = new ModeManager(basePath);
        await modeManager.InitializeAsync();

        var vm = new AdminPanelViewModel(modeManager, Enumerable.Empty<IServiceInstaller>(), logger);
        vm.SelectedMode = "MultiUser";
        vm.ServiceHost = "localhost";
        vm.ServicePort = 9100;

        // Trigger SaveMode via the command
        vm.SaveModeCommand.Execute(null);

        logger.Messages.Should().Contain(m =>
            m.Contains("Saving mode") && m.Contains("MultiUser"));
        logger.Messages.Should().Contain(m =>
            m.Contains("Initializing multi-user mode"));
        logger.Messages.Should().Contain(m =>
            m.Contains("Settings saved"));
    }

    [Fact]
    public async Task InstallServiceAsync_WithUnsupportedInstaller_LogsError()
    {
        var logger = new TestLogger<AdminPanelViewModel>();
        var basePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var modeManager = new ModeManager(basePath);

        var vm = new AdminPanelViewModel(modeManager, Enumerable.Empty<IServiceInstaller>(), logger);

        // Trigger InstallService via the command
        vm.InstallServiceCommand.Execute(null);

        logger.Messages.Should().Contain(m =>
            m.Contains("SERVICE INSTALLATION STARTED"));
        logger.Messages.Should().Contain(m =>
            m.Contains("No service installer available for this platform"));
    }

    [Fact]
    public async Task RefreshStatusAsync_Standalone_WritesLogEntries()
    {
        var logger = new TestLogger<AdminPanelViewModel>();
        var basePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var modeManager = new ModeManager(basePath);
        await modeManager.InitializeAsync();

        var vm = new AdminPanelViewModel(modeManager, Enumerable.Empty<IServiceInstaller>(), logger);

        // Trigger RefreshStatus via the command
        vm.RefreshStatusCommand.Execute(null);

        logger.Messages.Should().Contain(m =>
            m.Contains("Refreshing service status"));
        logger.Messages.Should().Contain(m =>
            m.Contains("Standalone mode"));
    }

    [Fact]
    public void ClearLogCommand_ClearsAllMessages()
    {
        var logger = new TestLogger<AdminPanelViewModel>();
        var modeManager = new ModeManager(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));

        var vm = new AdminPanelViewModel(modeManager, Enumerable.Empty<IServiceInstaller>(), logger);

        // Constructor adds "Admin panel initialized" message
        vm.LogMessages.Should().Contain(m => m.Contains("Admin panel initialized"));

        // Clear the log
        vm.ClearLogCommand.Execute(null);

        vm.LogMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task UninstallServiceAsync_WithUnsupportedInstaller_LogsError()
    {
        var logger = new TestLogger<AdminPanelViewModel>();
        var basePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var modeManager = new ModeManager(basePath);

        var vm = new AdminPanelViewModel(modeManager, Enumerable.Empty<IServiceInstaller>(), logger);

        // Trigger UninstallService via the command
        vm.UninstallServiceCommand.Execute(null);

        logger.Messages.Should().Contain(m =>
            m.Contains("SERVICE UNINSTALLATION STARTED"));
        logger.Messages.Should().Contain(m =>
            m.Contains("No service installer available for this platform"));
    }

}

/// <summary>
/// Test <see cref="ILogger{T}"/> implementation that captures formatted log messages
/// for test assertions.
/// </summary>
internal sealed class TestLogger<T> : ILogger<T>
{
    private readonly List<string> _messages = [];

    public IReadOnlyList<string> Messages => _messages.AsReadOnly();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var msg = formatter(state, exception);
        lock (_messages)
        {
            _messages.Add(msg);
        }
    }
}
