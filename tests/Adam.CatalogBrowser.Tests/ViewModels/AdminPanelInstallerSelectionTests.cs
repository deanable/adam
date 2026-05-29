using System.Diagnostics;
using Adam.CatalogBrowser.ViewModels;
using Adam.Shared.Services;
using FluentAssertions;

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
        public bool IsSupportedValue { get; init; }
        public string ServiceNameValue { get; init; } = string.Empty;

        public bool IsSupported => IsSupportedValue;
        public string ServiceName => ServiceNameValue;

        public Task InstallAsync(string brokerPath, int port, CancellationToken ct = default)
        {
            Debug.WriteLine($"[adam] PlatformSpecificInstaller[{ServiceNameValue}].InstallAsync(brokerPath='{brokerPath}', port={port})");
            return Task.CompletedTask;
        }

        public Task UninstallAsync(CancellationToken ct = default)
        {
            Debug.WriteLine($"[adam] PlatformSpecificInstaller[{ServiceNameValue}].UninstallAsync()");
            return Task.CompletedTask;
        }

        public Task<ServiceStatus> GetStatusAsync(CancellationToken ct = default)
        {
            Debug.WriteLine($"[adam] PlatformSpecificInstaller[{ServiceNameValue}].GetStatusAsync()");
            return Task.FromResult(ServiceStatus.NotInstalled);
        }
    }
}

// ─────────────────────────────────────────────────────
//  Debug logging tests for NullServiceInstaller
// ─────────────────────────────────────────────────────

/// <summary>
/// Tests that <see cref="NullServiceInstaller"/> writes <c>[adam]</c>-prefixed
/// debug traces when its methods are called.
/// </summary>
public sealed class NullServiceInstallerDebugLoggingTests : IDisposable
{
    private readonly TestTraceListener _listener;

    public NullServiceInstallerDebugLoggingTests()
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
    public async Task InstallAsync_WritesDebugTrace()
    {
        var installer = new NullServiceInstaller();
        var act = async () => await installer.InstallAsync("/path", 9100);

        await act.Should().ThrowAsync<PlatformNotSupportedException>();

        _listener.Messages.Should().Contain(m =>
            m.Contains("[adam]") && m.Contains("NullServiceInstaller.InstallAsync"));
    }

    [Fact]
    public async Task UninstallAsync_WritesDebugTrace()
    {
        var installer = new NullServiceInstaller();
        var act = async () => await installer.UninstallAsync();

        await act.Should().ThrowAsync<PlatformNotSupportedException>();

        _listener.Messages.Should().Contain(m =>
            m.Contains("[adam]") && m.Contains("NullServiceInstaller.UninstallAsync"));
    }

    [Fact]
    public async Task GetStatusAsync_WritesDebugTrace()
    {
        var installer = new NullServiceInstaller();
        await installer.GetStatusAsync();

        _listener.Messages.Should().Contain(m =>
            m.Contains("[adam]") && m.Contains("NullServiceInstaller.GetStatusAsync"));
    }
}

/// <summary>
/// Custom <see cref="TraceListener"/> that captures <see cref="Trace.WriteLine"/>
/// messages for test assertions.
/// </summary>
internal sealed class TestTraceListener : TraceListener
{
    private readonly List<string> _messages = [];

    public IReadOnlyList<string> Messages => _messages.AsReadOnly();

    public override void Write(string? message)
    {
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
