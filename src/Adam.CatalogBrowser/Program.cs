using Adam.CatalogBrowser.Services;
using Adam.Shared.Services;
using Avalonia;

namespace Adam.CatalogBrowser;

public static class Program
{
    public static void Main(string[] args)
    {
        ConnectionDebugLogger.Reset("catalog");

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            ConnectionDebugLogger.Error(ex ?? new Exception("Unknown unhandled exception"),
                "AppDomain unhandled exception");
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            ConnectionDebugLogger.Error(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            ConnectionDebugLogger.Error("=== Fatal: Main loop threw ===");
            for (var current = ex; current != null; current = current.InnerException)
            {
                ConnectionDebugLogger.Error(
                    $"[{current.GetType().Name}] {current.Message}");
                if (current.StackTrace is { } st)
                    ConnectionDebugLogger.Error($"  Stack: {st}");
            }
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}
