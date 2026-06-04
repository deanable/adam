using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Adam.ServiceManager.Services;

/// <summary>
/// Manages the system tray icon for the Service Manager application.
/// Provides show/hide window behavior and a context menu with quick actions.
/// </summary>
internal sealed class TrayIconService : IDisposable
{
    private readonly TrayIcon _trayIcon;
    private readonly Window _targetWindow;
    private bool _disposed;

    public TrayIconService(Window targetWindow)
    {
        _targetWindow = targetWindow;

        // Create icon bitmap and immediately dispose after WindowIcon copies the data
        WindowIcon windowIcon;
        using (var bitmap = CreateDefaultIcon())
        {
            windowIcon = new WindowIcon(bitmap);
        }

        var menu = new NativeMenu();

        var openItem = new NativeMenuItem("Open Service Manager");
        openItem.Click += OnOpenClicked;
        menu.Items.Add(openItem);

        var installItem = new NativeMenuItem("Install Service");
        installItem.Click += OnInstallClicked;
        menu.Items.Add(installItem);

        menu.Items.Add(new NativeMenuItemSeparator());

        var exitItem = new NativeMenuItem("Exit");
        exitItem.Click += OnExitClicked;
        menu.Items.Add(exitItem);

        _trayIcon = new TrayIcon
        {
            Icon = windowIcon,
            ToolTipText = "Adam Service Manager",
            Menu = menu
        };

        _trayIcon.Clicked += OnTrayIconClicked;
    }

    private void OnTrayIconClicked(object? sender, EventArgs e)
    {
        ShowWindow();
    }

    private void OnOpenClicked(object? sender, EventArgs e)
    {
        ShowWindow();
    }

    private void OnInstallClicked(object? sender, EventArgs e)
    {
        ShowWindow();
        // The Service tab is the default view — the user can click Install from there
    }

    private void OnExitClicked(object? sender, EventArgs e)
    {
        _trayIcon.Dispose();
        Environment.Exit(0);
    }

    public void ShowWindow()
    {
        if (_targetWindow == null) return;

        _targetWindow.Show();
        _targetWindow.WindowState = WindowState.Normal;
        _targetWindow.Activate();
        _targetWindow.Topmost = true;
        _targetWindow.Topmost = false;
    }

    /// <summary>
    /// Creates a simple 32x32 icon bitmap with a solid blue rounded-rect icon.
    /// Uses simple shape drawing to avoid FormattedText API differences across Avalonia versions.
    /// </summary>
    private static Bitmap CreateDefaultIcon()
    {
        var size = new PixelSize(32, 32);
        var dpi = new Vector(96, 96);
        var bitmap = new RenderTargetBitmap(size, dpi);

        using var ctx = bitmap.CreateDrawingContext();

        // Outer circle (dark blue border)
        ctx.DrawEllipse(
            new SolidColorBrush(Color.Parse("#005A9E")),
            null,
            new Rect(1, 1, 30, 30));

        // Inner circle (medium blue fill)
        ctx.DrawEllipse(
            new SolidColorBrush(Color.Parse("#1976D2")),
            null,
            new Rect(4, 4, 24, 24));

        // Center dot (white highlight)
        ctx.DrawEllipse(
            new SolidColorBrush(Colors.White),
            null,
            new Rect(13, 13, 6, 6));

        return bitmap;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _trayIcon?.Dispose();
    }
}
