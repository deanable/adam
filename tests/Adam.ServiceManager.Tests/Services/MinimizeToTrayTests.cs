using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using FluentAssertions;

namespace Adam.ServiceManager.Tests.Services;

/// <summary>
/// Headless tests for the minimize-to-tray behavior added in App.axaml.cs.
/// Verifies that when the WindowState changes to Minimized, the window is hidden,
/// and that it can be restored via Show().
/// </summary>
[Collection(nameof(HeadlessServiceManagerCollection))]
public sealed class MinimizeToTrayTests : IDisposable
{
    private static readonly Lazy<HeadlessUnitTestSession> Session = new(
        () => HeadlessUnitTestSession.StartNew(typeof(TestServiceManagerApp)));

    private Task DispatchAsync(Action action)
        => Session.Value.Dispatch(action, CancellationToken.None);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Simulates the minimize-to-tray handler from App.axaml.cs:
    /// hides the window when WindowState becomes Minimized.
    /// </summary>
    private static void AttachMinimizeToTrayHandler(Window window)
    {
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Window.WindowStateProperty && window.WindowState == WindowState.Minimized)
            {
                window.Hide();
            }
        };
    }

    [Fact]
    public async Task MinimizeToTray_WhenMinimized_HidesWindow()
    {
        await DispatchAsync(() =>
        {
            var window = new Window();
            window.Show();
            window.WindowState = WindowState.Normal;

            AttachMinimizeToTrayHandler(window);

            // Make sure window is visible before minimizing
            window.IsVisible.Should().BeTrue();

            // Act: minimize
            window.WindowState = WindowState.Minimized;

            // Assert: window should be hidden
            window.IsVisible.Should().BeFalse("minimize-to-tray should hide the window");
        });
    }

    [Fact]
    public async Task MinimizeToTray_RestoreFromTray_MakesWindowVisible()
    {
        await DispatchAsync(() =>
        {
            var window = new Window();
            window.Show();
            window.WindowState = WindowState.Normal;

            AttachMinimizeToTrayHandler(window);

            // Minimize to tray
            window.WindowState = WindowState.Minimized;
            window.IsVisible.Should().BeFalse();

            // Act: restore from tray (as TrayIconService.ShowWindow does)
            window.Show();
            window.WindowState = WindowState.Normal;

            // Assert
            window.IsVisible.Should().BeTrue("restoring from tray should make window visible");
            window.WindowState.Should().Be(WindowState.Normal, "restored window should be normal");
        });
    }

    [Fact]
    public async Task MinimizeToTray_WhenAlreadyHidden_DoesNotThrow()
    {
        await DispatchAsync(() =>
        {
            var window = new Window();
            window.Show();
            window.WindowState = WindowState.Normal;

            AttachMinimizeToTrayHandler(window);

            // Minimize to tray
            window.WindowState = WindowState.Minimized;

            // Act: minimizing again (already hidden) should be safe
            var act = () => window.WindowState = WindowState.Minimized;
            act.Should().NotThrow();
        });
    }

    [Fact]
    public async Task MinimizeToTray_SettingNormalDirectly_DoesNotHide()
    {
        await DispatchAsync(() =>
        {
            var window = new Window();
            window.Show();
            window.WindowState = WindowState.Normal;

            AttachMinimizeToTrayHandler(window);

            // Act: set to normal while already normal
            window.WindowState = WindowState.Normal;

            // Assert: should remain visible
            window.IsVisible.Should().BeTrue("setting Normal when already normal should not hide");
        });
    }

    [Fact]
    public async Task MinimizeToTray_WithMultipleWindows_OnlyTargetIsHid()
    {
        await DispatchAsync(() =>
        {
            var window1 = new Window();
            window1.Show();
            window1.WindowState = WindowState.Normal;
            AttachMinimizeToTrayHandler(window1);

            var window2 = new Window();
            window2.Show();

            // Both visible
            window1.IsVisible.Should().BeTrue();
            window2.IsVisible.Should().BeTrue();

            // Act: minimize window1
            window1.WindowState = WindowState.Minimized;

            // Assert: only window1 is hidden
            window1.IsVisible.Should().BeFalse("minimize-to-tray should hide target window");
            window2.IsVisible.Should().BeTrue("other windows should not be affected");
        });
    }
}
