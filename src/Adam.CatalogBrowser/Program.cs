using Adam.CatalogBrowser.Services;
using Avalonia;

namespace Adam.CatalogBrowser;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Handle headless elevated mode: CatalogBrowser.exe --elevated <requestFilePath>
        if (args.Length >= 2 && args[0] == "--elevated")
        {
            return await ElevatedHelper.RunAsync(args[1]);
        }

        // Normal GUI mode
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}
