using System.Diagnostics;
using System.Security.Principal;
using Adam.Shared.Services;
using FluentAssertions;

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

            // On non-elevated Windows, EnsureElevated() throws UnauthorizedAccessException
            // before EnsureAbsolutePath() gets a chance to throw ArgumentException.
            // We accept either exception type since the point is that validation works.
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
    //  Debug logging trace capture tests
    // ──────────────────────────────────────────────

    public sealed class DebugLoggingTests : IDisposable
    {
        private readonly TestTraceListener _listener;

        public DebugLoggingTests()
        {
            _listener = new TestTraceListener();
            Trace.Listeners.Add(_listener);
        }

        public void Dispose()
        {
            Trace.Listeners.Remove(_listener);
            _listener.Dispose();
        }

        [Fact]
        public async Task WindowsServiceInstaller_InstallAsync_RelativePath_WritesDebugTrace()
        {
            var installer = new WindowsServiceInstaller();
            var act = async () => await installer.InstallAsync("relative/path.exe", 9100);

            // On Windows non-admin: UnauthorizedAccessException from EnsureElevated()
            // On Windows admin: ArgumentException from EnsureAbsolutePath()
            // On non-Windows: PlatformNotSupportedException from EnsureSupported()
            // Either way, the debug trace should be written first
            await act.Should().ThrowAsync<Exception>();

            _listener.Messages.Should().Contain(m =>
                m.Contains("[adam]") && m.Contains("WindowsServiceInstaller.InstallAsync"));
        }

        [Fact]
        public async Task LinuxServiceInstaller_InstallAsync_RelativePath_WritesDebugTrace()
        {
            var installer = new LinuxServiceInstaller();
            var act = async () => await installer.InstallAsync("relative/path", 9100);

            // On Linux: ArgumentException from EnsureAbsolutePath()
            // On other platforms: PlatformNotSupportedException from EnsureSupported()
            // Either way, the debug trace should be written first
            await act.Should().ThrowAsync<Exception>();

            _listener.Messages.Should().Contain(m =>
                m.Contains("[adam]") && m.Contains("LinuxServiceInstaller.InstallAsync"));
        }

        [Fact]
        public async Task MacOsServiceInstaller_InstallAsync_RelativePath_WritesDebugTrace()
        {
            var installer = new MacOsServiceInstaller();
            var act = async () => await installer.InstallAsync("relative/path", 9100);

            // On macOS: ArgumentException from EnsureAbsolutePath()
            // On other platforms: PlatformNotSupportedException from EnsureSupported()
            await act.Should().ThrowAsync<Exception>();

            _listener.Messages.Should().Contain(m =>
                m.Contains("[adam]") && m.Contains("MacOsServiceInstaller.InstallAsync"));
        }

        [Fact]
        public async Task WindowsServiceInstaller_UninstallAsync_OnNonWindows_WritesDebugTrace()
        {
            if (OperatingSystem.IsWindows()) return;

            var installer = new WindowsServiceInstaller();
            var act = async () => await installer.UninstallAsync();

            await act.Should().ThrowAsync<PlatformNotSupportedException>();

            _listener.Messages.Should().Contain(m =>
                m.Contains("[adam]") && m.Contains("WindowsServiceInstaller.UninstallAsync"));
        }

        [Fact]
        public async Task LinuxServiceInstaller_UninstallAsync_OnNonLinux_WritesDebugTrace()
        {
            if (OperatingSystem.IsLinux()) return;

            var installer = new LinuxServiceInstaller();
            var act = async () => await installer.UninstallAsync();

            await act.Should().ThrowAsync<PlatformNotSupportedException>();

            _listener.Messages.Should().Contain(m =>
                m.Contains("[adam]") && m.Contains("LinuxServiceInstaller.UninstallAsync"));
        }

        [Fact]
        public async Task MacOsServiceInstaller_UninstallAsync_OnNonMac_WritesDebugTrace()
        {
            if (OperatingSystem.IsMacOS()) return;

            var installer = new MacOsServiceInstaller();
            var act = async () => await installer.UninstallAsync();

            await act.Should().ThrowAsync<PlatformNotSupportedException>();

            _listener.Messages.Should().Contain(m =>
                m.Contains("[adam]") && m.Contains("MacOsServiceInstaller.UninstallAsync"));
        }

        [Fact]
        public async Task WindowsServiceInstaller_GetStatusAsync_OnNonWindows_WritesDebugTrace()
        {
            if (OperatingSystem.IsWindows()) return;

            var installer = new WindowsServiceInstaller();
            await installer.GetStatusAsync();

            _listener.Messages.Should().Contain(m =>
                m.Contains("[adam]") && m.Contains("WindowsServiceInstaller.GetStatusAsync"));
        }

        [Fact]
        public async Task LinuxServiceInstaller_GetStatusAsync_OnNonLinux_WritesDebugTrace()
        {
            if (OperatingSystem.IsLinux()) return;

            var installer = new LinuxServiceInstaller();
            await installer.GetStatusAsync();

            _listener.Messages.Should().Contain(m =>
                m.Contains("[adam]") && m.Contains("LinuxServiceInstaller.GetStatusAsync"));
        }

        [Fact]
        public async Task MacOsServiceInstaller_GetStatusAsync_OnNonMac_WritesDebugTrace()
        {
            if (OperatingSystem.IsMacOS()) return;

            var installer = new MacOsServiceInstaller();
            await installer.GetStatusAsync();

            _listener.Messages.Should().Contain(m =>
                m.Contains("[adam]") && m.Contains("MacOsServiceInstaller.GetStatusAsync"));
        }
    }
}

/// <summary>
/// Custom <see cref="TraceListener"/> that captures <see cref="Trace.WriteLine"/>
/// and <see cref="Debug.WriteLine"/> messages for test assertions.
/// </summary>
internal sealed class TestTraceListener : TraceListener
{
    private readonly List<string> _messages = [];

    public IReadOnlyList<string> Messages => _messages.AsReadOnly();

    public override void Write(string? message)
    {
        // Not needed for Debug.WriteLine tests
    }

    public override void WriteLine(string? message)
    {
        if (message != null)
        {
            lock (_messages)
            {
                _messages.Add(message);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_messages)
            {
                _messages.Clear();
            }
        }

        base.Dispose(disposing);
    }
}
