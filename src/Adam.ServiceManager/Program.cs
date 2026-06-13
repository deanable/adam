using Adam.ServiceManager.Services;
using Avalonia;

namespace Adam.ServiceManager;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Handle headless elevated mode: ServiceManager.exe --elevated <requestFilePath> [--log <logFilePath>]
        if (args.Length >= 2 && args[0] == "--elevated")
        {
            var requestFile = args[1];
            var logFile = args.Length >= 4 && args[2] == "--log" ? args[3] : null;

            Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}|ELEVATED] Elevated mode: requestFile={requestFile}, logFile={logFile ?? "(none)"}");
            Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}|ELEVATED] Program.Main: Elevated mode starting");
            Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}|ELEVATED] PID={Environment.ProcessId}, ProcessPath={Environment.ProcessPath}");
            Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}|ELEVATED] Args: {string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a))}");

            return await ElevatedHelper.RunAsync(requestFile, logFile);
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
