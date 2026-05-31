using Adam.CatalogBrowser.ViewModels;
using Adam.Shared.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;

namespace Adam.CatalogBrowser.Tests.ViewModels;

/// <summary>
/// Tests for <see cref="NullServiceInstaller"/> selection logic.
///
/// These tests verify the selection behavior that mirrors what
/// <c>AdminPanelViewModel</c> does with its <c>IEnumerable&lt;IServiceInstaller&gt;</c>
/// constructor parameter: picks the first supported installer or falls back to
/// <c>NullServiceInstaller</c>.
/// </summary>
public sealed class AdminPanelInstallerSelectionTests
{
    /// <summary>
    /// Simulates the installer selection logic from AdminPanelViewModel's constructor.
    /// </summary>
    private static IServiceInstaller SelectInstaller(IEnumerable<IServiceInstaller> installers)
    {
        var list = installers.ToList();
        return list.FirstOrDefault(s => s.IsSupported) ?? new NullServiceInstaller();
    }

    [Fact]
    public void SelectInstaller_WhenNoInstallers_ReturnsNullServiceInstaller()
    {
        var installer = SelectInstaller(Enumerable.Empty<IServiceInstaller>());
        installer.Should().BeOfType<NullServiceInstaller>();
        installer.IsSupported.Should().BeFalse();
        installer.ServiceName.Should().Be("None");
    }

    [Fact]
    public void SelectInstaller_WhenNoneSupported_ReturnsNullServiceInstaller()
    {
        var unsupported = new[]
        {
            CreateInstaller(supported: false, "Windows"),
            CreateInstaller(supported: false, "Linux"),
            CreateInstaller(supported: false, "Mac"),
        };

        var installer = SelectInstaller(unsupported);
        installer.Should().BeOfType<NullServiceInstaller>();
        installer.IsSupported.Should().BeFalse();
    }

    [Fact]
    public void SelectInstaller_WhenOneSupported_ReturnsThatInstaller()
    {
        var installers = new[]
        {
            CreateInstaller(supported: false, "Linux"),
            CreateInstaller(supported: true, "Windows"),
            CreateInstaller(supported: false, "Mac"),
        };

        var installer = SelectInstaller(installers);
        installer.Should().BeOfType<PlatformSpecificInstaller>();
        installer.IsSupported.Should().BeTrue();
        installer.ServiceName.Should().Be("Windows");
    }

    [Fact]
    public void SelectInstaller_WhenMultipleSupported_ReturnsFirst()
    {
        var installers = new[]
        {
            CreateInstaller(supported: true, "First"),
            CreateInstaller(supported: true, "Second"),
        };

        var installer = SelectInstaller(installers);
        installer.ServiceName.Should().Be("First");
    }

    [Fact]
    public async Task SelectInstaller_NullServiceInstaller_InstallAsync_Throws()
    {
        var installer = new NullServiceInstaller();
        var act = async () => await installer.InstallAsync("/path", 9100);
        await act.Should().ThrowAsync<PlatformNotSupportedException>();
    }

    [Fact]
    public async Task SelectInstaller_NullServiceInstaller_UninstallAsync_Throws()
    {
        var installer = new NullServiceInstaller();
        var act = async () => await installer.UninstallAsync();
        await act.Should().ThrowAsync<PlatformNotSupportedException>();
    }

    [Fact]
    public async Task SelectInstaller_NullServiceInstaller_GetStatusAsync_ReturnsNotInstalled()
    {
        var installer = new NullServiceInstaller();
        var status = await installer.GetStatusAsync();
        status.Should().Be(ServiceStatus.NotInstalled);
    }

    [Fact]
    public async Task SelectInstaller_NullServiceInstaller_StartAsync_Throws()
    {
        var installer = new NullServiceInstaller();
        var act = async () => await installer.StartAsync();
        await act.Should().ThrowAsync<PlatformNotSupportedException>();
    }

    [Fact]
    public async Task SelectInstaller_NullServiceInstaller_StopAsync_Throws()
    {
        var installer = new NullServiceInstaller();
        var act = async () => await installer.StopAsync();
        await act.Should().ThrowAsync<PlatformNotSupportedException>();
    }

    /// <summary>
    /// Creates a stub <see cref="IServiceInstaller"/> with the given support
    /// flag and service name.
    /// </summary>
    private static IServiceInstaller CreateInstaller(bool supported, string name)
    {
        return new PlatformSpecificInstaller
        {
            IsSupportedValue = supported,
            ServiceNameValue = name
        };
    }

    /// <summary>
    /// Minimal test double that implements <see cref="IServiceInstaller"/>
    /// with configurable properties.
    /// </summary>
    private sealed class PlatformSpecificInstaller : IServiceInstaller
    {
        private readonly ILogger _logger;

        public PlatformSpecificInstaller()
        {
            _logger = new TestLogger<PlatformSpecificInstaller>();
        }

        public bool IsSupportedValue { get; init; }
        public string ServiceNameValue { get; init; } = string.Empty;

        public bool IsSupported => IsSupportedValue;
        public string ServiceName => ServiceNameValue;

        public Task InstallAsync(string brokerPath, int port, CancellationToken ct = default)
        {
            _logger.LogInformation("PlatformSpecificInstaller[{Name}].InstallAsync(brokerPath='{BrokerPath}', port={Port})", ServiceNameValue, brokerPath, port);
            return Task.CompletedTask;
        }

        public Task UninstallAsync(CancellationToken ct = default)
        {
            _logger.LogInformation("PlatformSpecificInstaller[{Name}].UninstallAsync()", ServiceNameValue);
            return Task.CompletedTask;
        }

        public Task StartAsync(CancellationToken ct = default)
        {
            _logger.LogInformation("PlatformSpecificInstaller[{Name}].StartAsync()", ServiceNameValue);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken ct = default)
        {
            _logger.LogInformation("PlatformSpecificInstaller[{Name}].StopAsync()", ServiceNameValue);
            return Task.CompletedTask;
        }

        public Task<ServiceStatus> GetStatusAsync(CancellationToken ct = default)
        {
            _logger.LogInformation("PlatformSpecificInstaller[{Name}].GetStatusAsync()", ServiceNameValue);
            return Task.FromResult(ServiceStatus.NotInstalled);
        }
    }
}

// ─────────────────────────────────────────────────────
//  ILogger logging tests for NullServiceInstaller
// ─────────────────────────────────────────────────────

/// <summary>
/// Tests that <see cref="NullServiceInstaller"/> writes log entries via ILogger
/// when its methods are called.
/// </summary>
public sealed class NullServiceInstallerLoggingTests
{
    [Fact]
    public async Task InstallAsync_WritesLog()
    {
        var logger = new TestLogger<NullServiceInstaller>();
        var installer = new NullServiceInstaller(logger);
        var act = async () => await installer.InstallAsync("/path", 9100);

        await act.Should().ThrowAsync<PlatformNotSupportedException>();

        logger.Messages.Should().Contain(m =>
            m.Contains("NullServiceInstaller.InstallAsync"));
    }

    [Fact]
    public async Task UninstallAsync_WritesLog()
    {
        var logger = new TestLogger<NullServiceInstaller>();
        var installer = new NullServiceInstaller(logger);
        var act = async () => await installer.UninstallAsync();

        await act.Should().ThrowAsync<PlatformNotSupportedException>();

        logger.Messages.Should().Contain(m =>
            m.Contains("NullServiceInstaller.UninstallAsync"));
    }

    [Fact]
    public async Task GetStatusAsync_WritesLog()
    {
        var logger = new TestLogger<NullServiceInstaller>();
        var installer = new NullServiceInstaller(logger);
        await installer.GetStatusAsync();

        logger.Messages.Should().Contain(m =>
            m.Contains("NullServiceInstaller.GetStatusAsync"));
    }

    [Fact]
    public async Task StartAsync_WritesLog()
    {
        var logger = new TestLogger<NullServiceInstaller>();
        var installer = new NullServiceInstaller(logger);
        var act = async () => await installer.StartAsync();

        await act.Should().ThrowAsync<PlatformNotSupportedException>();

        logger.Messages.Should().Contain(m =>
            m.Contains("NullServiceInstaller.StartAsync"));
    }

    [Fact]
    public async Task StopAsync_WritesLog()
    {
        var logger = new TestLogger<NullServiceInstaller>();
        var installer = new NullServiceInstaller(logger);
        var act = async () => await installer.StopAsync();

        await act.Should().ThrowAsync<PlatformNotSupportedException>();

        logger.Messages.Should().Contain(m =>
            m.Contains("NullServiceInstaller.StopAsync"));
    }
}


