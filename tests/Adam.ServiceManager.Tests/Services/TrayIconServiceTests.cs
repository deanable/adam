using Adam.ServiceManager.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using FluentAssertions;

namespace Adam.ServiceManager.Tests.Services;

/// <summary>
/// Headless tests for <see cref="TrayIconService"/> — verifies construction,
/// menu structure, ShowWindow behavior, and disposal.
/// </summary>
[Collection(nameof(HeadlessServiceManagerCollection))]
public sealed class TrayIconServiceTests : IDisposable
{
    private static readonly Lazy<HeadlessUnitTestSession> Session = new(
        () => HeadlessUnitTestSession.StartNew(typeof(TestServiceManagerApp)));

    private Task DispatchAsync(Action action)
        => Session.Value.Dispatch(action, CancellationToken.None);

    private Task<T> DispatchAsync<T>(Func<T> func)
        => Session.Value.Dispatch(func, CancellationToken.None);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    // ──────────────────────────────────────────────
    //  Construction — tooltip and menu structure
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Constructor_SetsCorrectToolTipText()
    {
        await DispatchAsync(() =>
        {
            var window = new Window();
            using var service = new TrayIconService(window);

            var trayIcon = GetTrayIconField(service);
            trayIcon.ToolTipText.Should().Be("Adam Service Manager");
        });
    }

    [Fact]
    public async Task Constructor_CreatesMenu_WithExpectedItems()
    {
        await DispatchAsync(() =>
        {
            var window = new Window();
            using var service = new TrayIconService(window);

            var trayIcon = GetTrayIconField(service);
            var menu = trayIcon.Menu;
            menu.Should().NotBeNull();
            menu!.Items.Should().HaveCount(4, "menu should have: Open, Install, separator, Exit");

            menu.Items[0].Should().BeOfType<NativeMenuItem>()
                .Which.Header.Should().Be("Open Service Manager");
            menu.Items[1].Should().BeOfType<NativeMenuItem>()
                .Which.Header.Should().Be("Install Service");
            menu.Items[2].Should().BeOfType<NativeMenuItemSeparator>();
            menu.Items[3].Should().BeOfType<NativeMenuItem>()
                .Which.Header.Should().Be("Exit");
        });
    }

    // ──────────────────────────────────────────────
    //  ShowWindow
    // ──────────────────────────────────────────────

    [Fact]
    public async Task ShowWindow_RestoresWindow_ToNormalState()
    {
        await DispatchAsync(() =>
        {
            var window = new Window();
            window.Show(); // Must show first to change state
            window.WindowState = WindowState.Minimized;
            window.IsVisible.Should().BeTrue("window was minimized but not hidden yet");

            using var service = new TrayIconService(window);

            // Act
            service.ShowWindow();

            // Assert
            window.WindowState.Should().Be(WindowState.Normal, "ShowWindow should restore to normal");
            window.IsVisible.Should().BeTrue("ShowWindow should make the window visible");
        });
    }

    [Fact]
    public async Task ShowWindow_WhenAlreadyNormal_StaysNormal()
    {
        await DispatchAsync(() =>
        {
            var window = new Window();
            window.Show();
            window.WindowState = WindowState.Normal;

            using var service = new TrayIconService(window);

            service.ShowWindow();

            window.WindowState.Should().Be(WindowState.Normal);
            window.IsVisible.Should().BeTrue();
        });
    }

    [Fact]
    public async Task ShowWindow_WhenWindowHidden_ShowsAndRestores()
    {
        await DispatchAsync(() =>
        {
            var window = new Window();
            window.Show();
            window.Hide();
            window.IsVisible.Should().BeFalse("window was hidden");

            using var service = new TrayIconService(window);

            service.ShowWindow();

            window.IsVisible.Should().BeTrue("ShowWindow should make the window visible");
            window.WindowState.Should().Be(WindowState.Normal);
        });
    }

    // ──────────────────────────────────────────────
    //  Dispose
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Dispose_DoesNotThrow()
    {
        await DispatchAsync(() =>
        {
            var window = new Window();
            var service = new TrayIconService(window);

            var act = () => service.Dispose();

            act.Should().NotThrow();
        });
    }

    [Fact]
    public async Task Dispose_Twice_DoesNotThrow()
    {
        await DispatchAsync(() =>
        {
            var window = new Window();
            var service = new TrayIconService(window);
            service.Dispose();

            var act = () => service.Dispose();

            act.Should().NotThrow("double dispose should be safe");
        });
    }

    [Fact]
    public async Task ShowWindow_AfterDispose_DoesNotThrow()
    {
        await DispatchAsync(() =>
        {
            var window = new Window();
            var service = new TrayIconService(window);
            service.Dispose();

            // After dispose, calling ShowWindow should be safe
            var act = () => service.ShowWindow();
            act.Should().NotThrow();
        });
    }

    /// <summary>
    /// Retrieves the internal <see cref="TrayIcon"/> from a <see cref="TrayIconService"/>
    /// via reflection on the <c>_trayIcon</c> field.
    /// </summary>
    private static TrayIcon GetTrayIconField(TrayIconService service)
    {
        var field = typeof(TrayIconService)
            .GetField("_trayIcon", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        field.Should().NotBeNull("_trayIcon field should exist on TrayIconService");
        var icon = field!.GetValue(service) as TrayIcon;
        icon.Should().NotBeNull("TrayIcon should be non-null after construction");
        return icon!;
    }
}
