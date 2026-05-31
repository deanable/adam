using System.Text.Json;
using Adam.Shared.Services;

namespace Adam.CatalogBrowser.Services;

/// <summary>
/// Handles headless elevated operations when CatalogBrowser is launched with
/// <c>--elevated &lt;requestFile&gt;</c>. Runs the requested service operation
/// (install, uninstall, start, stop) and writes the result back to the request file.
/// </summary>
internal static class ElevatedHelper
{
    /// <summary>
    /// Runs an elevated operation from command-line arguments.
    /// Called from <c>Program.Main</c> when <c>--elevated</c> is detected.
    /// </summary>
    /// <param name="requestFilePath">Path to the temp JSON file with the <see cref="ElevatedRequest"/>.</param>
    /// <returns>Exit code: 0 on success, 1 on failure.</returns>
    public static async Task<int> RunAsync(string requestFilePath)
    {
        ElevatedRequest? request = null;
        try
        {
            Log($"Elevated helper started. Request file: {requestFilePath}");

            if (!File.Exists(requestFilePath))
            {
                var msg = $"Request file not found: {requestFilePath}";
                Log(msg);
                await WriteErrorAsync(requestFilePath, msg);
                return 1;
            }

            var json = await File.ReadAllTextAsync(requestFilePath);
            request = JsonSerializer.Deserialize<ElevatedRequest>(json);

            if (request == null || string.IsNullOrWhiteSpace(request.Operation))
            {
                var msg = "Invalid elevated request: missing or unparseable operation.";
                Log(msg);
                await WriteErrorAsync(requestFilePath, msg);
                return 1;
            }

            Log($"Elevated operation: {request.Operation}");

            var installer = new WindowsServiceInstaller();

            switch (request.Operation.ToLowerInvariant())
            {
                case "install":
                    if (string.IsNullOrWhiteSpace(request.BrokerPath))
                    {
                        await WriteErrorAsync(requestFilePath, "Install operation requires 'BrokerPath'.");
                        return 1;
                    }
                    await installer.InstallAsync(request.BrokerPath, request.Port);
                    break;

                case "uninstall":
                    await installer.UninstallAsync();
                    break;

                case "start":
                    await installer.StartAsync();
                    break;

                case "stop":
                    await installer.StopAsync();
                    break;

                default:
                    var msg = $"Unknown elevated operation: '{request.Operation}'";
                    Log(msg);
                    await WriteErrorAsync(requestFilePath, msg);
                    return 1;
            }

            // Write success result
            var successResult = JsonSerializer.Serialize(new ElevatedResponse { Success = true });
            await File.WriteAllTextAsync(requestFilePath, successResult);
            Log($"Elevated operation '{request.Operation}' completed successfully.");
            return 0;
        }
        catch (Exception ex)
        {
            try
            {
                await WriteErrorAsync(requestFilePath, $"{ex.GetType().Name}: {ex.Message}");
            }
            catch
            {
                // Best-effort error reporting
            }

            try
            {
                await Console.Error.WriteLineAsync($"ElevatedHelper failed: {ex}");
            }
            catch
            {
                // Ignore
            }

            return 1;
        }
    }

    private static void Log(string message)
    {
        Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
    }

    private static async Task WriteErrorAsync(string requestFilePath, string errorMessage)
    {
        var errorResult = JsonSerializer.Serialize(new ElevatedResponse
        {
            Success = false,
            ErrorMessage = errorMessage
        });
        await File.WriteAllTextAsync(requestFilePath, errorResult);
    }
}
