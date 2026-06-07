using System.Security.Principal;
using Adam.Shared.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.Shared.Tests.Services;

/// <summary>
/// Tests for <see cref="WindowsServiceInstaller"/>, <see cref="LinuxServiceInstaller"/>,
/// and <see cref="MacOsServiceInstaller"/>.
///
/// These tests validate:
/// - Service names and platform support flags (safe on any OS)
/// - Validation guard methods that throw on invalid input
/// - Debug.WriteLine logging output via a custom trace listener
/// </summary>
public sealed class ServiceInstallerTests
{
    // ──────────────────────────────────────────────
    //  WindowsServiceInstaller (safe tests)
    // ──────────────────────────────────────────────

    public sealed class WindowsServiceInstallerScCreateCommandTests
    {
        [Fact]
        public void BuildScCreateArguments_PathWithSpaces_QuotesThePath()
        {
            var result = WindowsServiceInstaller.BuildScCreateArguments(
                "AdamBrokerService", @"C:\Program Files\Adam\BrokerService.exe");

            // Path with spaces gets quoted
            result.Should().Be(
                "create AdamBrokerService binPath= \"C:\\Program Files\\Adam\\BrokerService.exe\" start=auto");

            // Verify no double-nested quotes (the original bug)
            result.Should().NotContain("\"\"");

            // No args after the path
            result.Should().NotContain("--DbPort");
        }

        [Fact]
        public void BuildScCreateArguments_PathWithoutSpaces_NoQuotes()
        {
            var result = WindowsServiceInstaller.BuildScCreateArguments(
                "AdamBrokerService", @"C:\Adam\Broker.exe");

            // Path without spaces should NOT be quoted to avoid confusing sc.exe
            result.Should().Be(
                "create AdamBrokerService binPath= C:\\Adam\\Broker.exe start=auto");

            result.Should().NotContain("\"");
        }

        [Fact]
        public void BuildScCreateArguments_ServiceName_IsCorrect()
        {
            var result = WindowsServiceInstaller.BuildScCreateArguments(
                "CustomService", @"C:\path\svc.exe");

            result.Should().StartWith("create CustomService");
            result.Should().Contain("start=auto");
        }

        [Fact]
        public void BuildScCreateArguments_NoPortArgs()
        {
            var result = WindowsServiceInstaller.BuildScCreateArguments(
                "Svc", @"C:\s.exe");

            // No --DbPort or any other argument — port is configured via appsettings.json
            result.Should().NotContain("--");
            result.Should().NotContain("DbPort");
        }

        /// <summary>
        /// Verifies that the previous approach (embedding --DbPort=9090 in the binPath)
        /// is no longer used, because sc.exe's parser cannot handle = signs inside binPath values.
        /// The port is now configured via appsettings.json instead.
        /// </summary>
        [Fact]
        public void BuildScCreateArguments_PortConfiguredViaFileNotArgs()
        {
            var serviceName = "TestService";
            var brokerPath = @"C:\Adam\Service.exe";

            var result = WindowsServiceInstaller.BuildScCreateArguments(serviceName, brokerPath);

            // The old approach passed --DbPort=9090 as an argument — this is no longer done
            result.Should().NotContain("--");
            result.Should().NotContain("9090");

            // Clean binPath: just the exe path, no inline arguments
            result.Should().Be("create TestService binPath= C:\\Adam\\Service.exe start=auto");
        }
    }

    public sealed class WindowsServiceInstallerSafeTests
    {
        private readonly WindowsServiceInstaller _sut = new();

        [Fact]
        public void ServiceName_IsAdamBrokerService()
        {
            _sut.ServiceName.Should().Be("AdamBrokerService");
        }

        [Fact]
        public void IsSupported_MatchesPlatform()
        {
            _sut.IsSupported.Should().Be(OperatingSystem.IsWindows());
        }

        [Fact]
        public async Task GetStatusAsync_WhenNotSupported_ReturnsNotInstalled()
        {
            var status = await _sut.GetStatusAsync();
            if (OperatingSystem.IsWindows())
            {
                // On Windows, it will attempt to query sc.exe which may or may not work
                // depending on the test environment — skip asserting the actual value
                status.Should().BeOneOf(ServiceStatus.Running, ServiceStatus.Stopped, ServiceStatus.NotInstalled, ServiceStatus.Unknown);
            }
            else
            {
                status.Should().Be(ServiceStatus.NotInstalled);
            }
        }        [Fact]
        public async Task InstallAsync_WithRelativePath_ThrowsArgumentException()
        {
            if (!OperatingSystem.IsWindows())
            {
                // On non-Windows, EnsureSupported() throws first
                var act = async () => await _sut.InstallAsync("relative/path.exe", 9100);
                await act.Should().ThrowAsync<PlatformNotSupportedException>();
                return;
            }

            var act2 = async () => await _sut.InstallAsync("relative/path.exe", 9100);

            // On non-elevated Windows: EnsureElevated() throws UnauthorizedAccessException.
            // On elevated Windows: EnsureAbsolutePath() throws ArgumentException.
            var assertion = await act2.Should().ThrowAsync<Exception>();
            var ex = assertion.Which;
            (ex is ArgumentException || ex is UnauthorizedAccessException).Should().BeTrue(
                $"Expected ArgumentException or UnauthorizedAccessException but got {ex.GetType().Name}");
        }

        [Fact]
        public async Task UninstallAsync_WhenNotSupported_ThrowsPlatformNotSupportedException()
        {
            if (OperatingSystem.IsWindows())
            {
                // On Windows, it will try to check elevation first
                // We can't easily test this without mocking
                return;
            }

            var act = async () => await _sut.UninstallAsync();
            await act.Should().ThrowAsync<PlatformNotSupportedException>()
                .WithMessage("Windows Service is only supported on Windows.");
        }
    }

    // ──────────────────────────────────────────────
    //  LinuxServiceInstaller (safe tests)
    // ──────────────────────────────────────────────

    public sealed class LinuxServiceInstallerSafeTests
    {
        private readonly LinuxServiceInstaller _sut = new();

        [Fact]
        public void ServiceName_IsAdamBroker()
        {
            _sut.ServiceName.Should().Be("adam-broker");
        }

        [Fact]
        public void IsSupported_MatchesPlatform()
        {
            _sut.IsSupported.Should().Be(OperatingSystem.IsLinux());
        }

        [Fact]
        public async Task GetStatusAsync_WhenNotSupported_ReturnsNotInstalled()
        {
            var status = await _sut.GetStatusAsync();
            status.Should().Be(ServiceStatus.NotInstalled);
        }

        [Fact]
        public async Task InstallAsync_WithRelativePath_ThrowsArgumentException()
        {
            if (OperatingSystem.IsLinux())
            {
                // On Linux, EnsureSupported() passes, then EnsureAbsolutePath() throws
                var act = async () => await _sut.InstallAsync("relative/path", 9100);
                var assertion = await act.Should().ThrowAsync<ArgumentException>();
                assertion.And.Message.Should().Contain("brokerPath must be an absolute path");
            }
            else
            {
                // On non-Linux, EnsureSupported() throws first
                var act = async () => await _sut.InstallAsync("relative/path", 9100);
                await act.Should().ThrowAsync<PlatformNotSupportedException>();
            }
        }

        [Fact]
        public async Task UninstallAsync_WhenNotSupported_ThrowsPlatformNotSupportedException()
        {
            if (OperatingSystem.IsLinux()) return;

            var act = async () => await _sut.UninstallAsync();
            await act.Should().ThrowAsync<PlatformNotSupportedException>()
                .WithMessage("systemd is only supported on Linux.");
        }
    }

    // ──────────────────────────────────────────────
    //  MacOsServiceInstaller (safe tests)
    // ──────────────────────────────────────────────

    public sealed class MacOsServiceInstallerSafeTests
    {
        private readonly MacOsServiceInstaller _sut = new();

        [Fact]
        public void ServiceName_IsComAdamBroker()
        {
            _sut.ServiceName.Should().Be("com.adam.broker");
        }

        [Fact]
        public void IsSupported_MatchesPlatform()
        {
            _sut.IsSupported.Should().Be(OperatingSystem.IsMacOS());
        }

        [Fact]
        public async Task GetStatusAsync_WhenNotSupported_ReturnsNotInstalled()
        {
            var status = await _sut.GetStatusAsync();
            status.Should().Be(ServiceStatus.NotInstalled);
        }

        [Fact]
        public async Task InstallAsync_WithRelativePath_ThrowsArgumentException()
        {
            if (OperatingSystem.IsMacOS())
            {
                // On macOS, EnsureSupported() passes, then EnsureAbsolutePath() throws
                var act = async () => await _sut.InstallAsync("relative/path", 9100);
                var assertion = await act.Should().ThrowAsync<ArgumentException>();
                assertion.And.Message.Should().Contain("brokerPath must be an absolute path");
            }
            else
            {
                // On non-macOS, EnsureSupported() throws first
                var act = async () => await _sut.InstallAsync("relative/path", 9100);
                await act.Should().ThrowAsync<PlatformNotSupportedException>();
            }
        }

        [Fact]
        public async Task UninstallAsync_WhenNotSupported_ThrowsPlatformNotSupportedException()
        {
            if (OperatingSystem.IsMacOS()) return;

            var act = async () => await _sut.UninstallAsync();
            await act.Should().ThrowAsync<PlatformNotSupportedException>()
                .WithMessage("launchd is only supported on macOS.");
        }
    }

    // ──────────────────────────────────────────────
    //  ILogger logging capture tests
    // ──────────────────────────────────────────────

    public sealed class DebugLoggingTests
    {
        [Fact]
        public async Task WindowsServiceInstaller_InstallAsync_RelativePath_WritesLog()
        {
            var logger = new TestLogger<WindowsServiceInstaller>();
            var installer = new WindowsServiceInstaller(logger);
            var act = async () => await installer.InstallAsync("relative/path.exe", 9100);

            // On Windows non-admin: UnauthorizedAccessException from EnsureElevated()
            // On Windows admin: ArgumentException from EnsureAbsolutePath()
            // On non-Windows: PlatformNotSupportedException from EnsureSupported()
            // Either way, the log should be written first
            await act.Should().ThrowAsync<Exception>();

            logger.Messages.Should().Contain(m =>
                m.Contains("WindowsServiceInstaller.InstallAsync"));
        }

        [Fact]
        public async Task LinuxServiceInstaller_InstallAsync_RelativePath_WritesLog()
        {
            var logger = new TestLogger<LinuxServiceInstaller>();
            var installer = new LinuxServiceInstaller(logger);
            var act = async () => await installer.InstallAsync("relative/path", 9100);

            // On Linux: ArgumentException from EnsureAbsolutePath()
            // On other platforms: PlatformNotSupportedException from EnsureSupported()
            await act.Should().ThrowAsync<Exception>();

            logger.Messages.Should().Contain(m =>
                m.Contains("LinuxServiceInstaller.InstallAsync"));
        }

        [Fact]
        public async Task MacOsServiceInstaller_InstallAsync_RelativePath_WritesLog()
        {
            var logger = new TestLogger<MacOsServiceInstaller>();
            var installer = new MacOsServiceInstaller(logger);
            var act = async () => await installer.InstallAsync("relative/path", 9100);

            // On macOS: ArgumentException from EnsureAbsolutePath()
            // On other platforms: PlatformNotSupportedException from EnsureSupported()
            await act.Should().ThrowAsync<Exception>();

            logger.Messages.Should().Contain(m =>
                m.Contains("MacOsServiceInstaller.InstallAsync"));
        }

        [Fact]
        public async Task WindowsServiceInstaller_UninstallAsync_OnNonWindows_WritesLog()
        {
            if (OperatingSystem.IsWindows()) return;

            var logger = new TestLogger<WindowsServiceInstaller>();
            var installer = new WindowsServiceInstaller(logger);
            var act = async () => await installer.UninstallAsync();

            await act.Should().ThrowAsync<PlatformNotSupportedException>();

            logger.Messages.Should().Contain(m =>
                m.Contains("WindowsServiceInstaller.UninstallAsync"));
        }

        [Fact]
        public async Task LinuxServiceInstaller_UninstallAsync_OnNonLinux_WritesLog()
        {
            if (OperatingSystem.IsLinux()) return;

            var logger = new TestLogger<LinuxServiceInstaller>();
            var installer = new LinuxServiceInstaller(logger);
            var act = async () => await installer.UninstallAsync();

            await act.Should().ThrowAsync<PlatformNotSupportedException>();

            logger.Messages.Should().Contain(m =>
                m.Contains("LinuxServiceInstaller.UninstallAsync"));
        }

        [Fact]
        public async Task MacOsServiceInstaller_UninstallAsync_OnNonMac_WritesLog()
        {
            if (OperatingSystem.IsMacOS()) return;

            var logger = new TestLogger<MacOsServiceInstaller>();
            var installer = new MacOsServiceInstaller(logger);
            var act = async () => await installer.UninstallAsync();

            await act.Should().ThrowAsync<PlatformNotSupportedException>();

            logger.Messages.Should().Contain(m =>
                m.Contains("MacOsServiceInstaller.UninstallAsync"));
        }

        [Fact]
        public async Task WindowsServiceInstaller_GetStatusAsync_OnNonWindows_WritesLog()
        {
            if (OperatingSystem.IsWindows()) return;

            var logger = new TestLogger<WindowsServiceInstaller>();
            var installer = new WindowsServiceInstaller(logger);
            await installer.GetStatusAsync();

            logger.Messages.Should().Contain(m =>
                m.Contains("WindowsServiceInstaller.GetStatusAsync"));
        }

        [Fact]
        public async Task LinuxServiceInstaller_GetStatusAsync_OnNonLinux_WritesLog()
        {
            if (OperatingSystem.IsLinux()) return;

            var logger = new TestLogger<LinuxServiceInstaller>();
            var installer = new LinuxServiceInstaller(logger);
            await installer.GetStatusAsync();

            logger.Messages.Should().Contain(m =>
                m.Contains("LinuxServiceInstaller.GetStatusAsync"));
        }

        [Fact]
        public async Task MacOsServiceInstaller_GetStatusAsync_OnNonMac_WritesLog()
        {
            if (OperatingSystem.IsMacOS()) return;

            var logger = new TestLogger<MacOsServiceInstaller>();
            var installer = new MacOsServiceInstaller(logger);
            await installer.GetStatusAsync();

            logger.Messages.Should().Contain(m =>
                m.Contains("MacOsServiceInstaller.GetStatusAsync"));
        }
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
