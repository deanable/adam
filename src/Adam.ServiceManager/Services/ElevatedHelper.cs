using System.Diagnostics;
using System.Text.Json;
using Adam.Shared.Services;

namespace Adam.ServiceManager.Services;

/// <summary>
/// Handles headless elevated operations when ServiceManager is launched with
/// <c>--elevated &lt;requestFile&gt;</c>. Runs the requested service operation
/// (install, uninstall, start, stop) and writes the result back to the request file.
/// Also writes a companion log file for the parent process to read back.
/// </summary>
internal static class ElevatedHelper
{
    /// <summary>
    /// Runs an elevated operation from command-line arguments.
    /// Called from <c>Program.Main</c> when <c>--elevated</c> is detected.
    /// </summary>
    /// <param name="requestFilePath">Path to the temp JSON file with the <see cref="ElevatedRequest"/>.</param>
    /// <param name="logFilePath">Optional path to a companion log file the parent process can read.</param>
    /// <returns>Exit code: 0 on success, 1 on failure.</returns>
    public static async Task<int> RunAsync(string requestFilePath, string? logFilePath = null)
    {
        var sw = Stopwatch.StartNew();
        var logger = new ElevatedLogger(logFilePath);

        ElevatedRequest? request = null;
        try
        {
            logger.Log($"=== ELEVATED HELPER STARTED ===");
            logger.Log($"Request file: {requestFilePath}");
            logger.Log($"Log file: {logFilePath ?? "(none)"}");
            logger.Log($"Process ID: {Environment.ProcessId}");
            logger.Log($"Process path: {Environment.ProcessPath}");
            logger.Log($"Command line: {Environment.CommandLine}");
            logger.Log($"OS: {Environment.OSVersion}");
            logger.Log($"User: {Environment.UserDomainName}\\{Environment.UserName}");
            logger.Log($"Is64Bit: {Environment.Is64BitProcess}");

            if (!File.Exists(requestFilePath))
            {
                var msg = $"Request file not found: {requestFilePath}";
                logger.Log($"FATAL: {msg}");
                await WriteErrorAsync(requestFilePath, msg);
                return 1;
            }

            var json = await File.ReadAllTextAsync(requestFilePath);
            request = JsonSerializer.Deserialize<ElevatedRequest>(json);

            if (request == null || string.IsNullOrWhiteSpace(request.Operation))
            {
                var msg = "Invalid elevated request: missing or unparseable operation.";
                logger.Log($"FATAL: {msg}");
                logger.Log($"Raw request JSON: {json}");
                await WriteErrorAsync(requestFilePath, msg);
                return 1;
            }

            logger.Log($"Parsed request: Operation='{request.Operation}', BrokerPath='{request.BrokerPath ?? "(null)"}', Port={request.Port}");

            var installer = new WindowsServiceInstaller();
            logger.Log($"Created WindowsServiceInstaller (IsElevated={installer.IsElevated}, IsSupported={installer.IsSupported})");

            logger.Log($"Starting '{request.Operation}' operation...");
            var opSw = Stopwatch.StartNew();

            switch (request.Operation.ToLowerInvariant())
            {
                case "install":
                    if (string.IsNullOrWhiteSpace(request.BrokerPath))
                    {
                        await WriteErrorAsync(requestFilePath, "Install operation requires 'BrokerPath'.");
                        logger.Log("FATAL: Missing BrokerPath for install");
                        return 1;
                    }
                    logger.Log($"Calling installer.InstallAsync(BrokerPath='{request.BrokerPath}', Port={request.Port})...");
                    await installer.InstallAsync(request.BrokerPath, request.Port);
                    logger.Log($"InstallAsync completed in {opSw.Elapsed.TotalMilliseconds:F0}ms");
                    break;

                case "uninstall":
                    logger.Log("Calling installer.UninstallAsync()...");
                    await installer.UninstallAsync();
                    logger.Log($"UninstallAsync completed in {opSw.Elapsed.TotalMilliseconds:F0}ms");
                    break;

                case "start":
                    logger.Log("Calling installer.StartAsync()...");
                    await installer.StartAsync();
                    logger.Log($"StartAsync completed in {opSw.Elapsed.TotalMilliseconds:F0}ms");
                    break;

                case "stop":
                    logger.Log("Calling installer.StopAsync()...");
                    await installer.StopAsync();
                    logger.Log($"StopAsync completed in {opSw.Elapsed.TotalMilliseconds:F0}ms");
                    break;

                default:
                    var msg = $"Unknown elevated operation: '{request.Operation}'";
                    logger.Log($"FATAL: {msg}");
                    await WriteErrorAsync(requestFilePath, msg);
                    return 1;
            }

            // Write success result
            var successResult = JsonSerializer.Serialize(new ElevatedResponse { Success = true });
            await File.WriteAllTextAsync(requestFilePath, successResult);
            logger.Log($"Result file written. Elevated operation '{request.Operation}' completed successfully in {sw.Elapsed.TotalMilliseconds:F0}ms total.");
            logger.Log("=== ELEVATED HELPER COMPLETED SUCCESSFULLY ===");
            return 0;
        }
        catch (Exception ex)
        {
            logger.Log($"FATAL EXCEPTION: {ex.GetType().Name}: {ex.Message}");
            logger.Log($"Stack trace: {ex.StackTrace}");
            if (ex.InnerException != null)
                logger.Log($"Inner exception: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");

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

    /// <summary>
    /// Lightweight logger that writes to both stderr (for real-time console capture)
    /// and an optional companion log file (for parent process to read).
    /// </summary>
    private sealed class ElevatedLogger
    {
        private readonly string? _logFilePath;
        private readonly string? _logDir;
        private static readonly SemaphoreSlim _writeLock = new(1, 1);

        public ElevatedLogger(string? logFilePath)
        {
            _logFilePath = logFilePath;
            _logDir = logFilePath != null ? Path.GetDirectoryName(logFilePath) : null;
        }

        public void Log(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var entry = $"[{timestamp}|ELEVATED|{Environment.ProcessId}] {message}";

            // Always write to stderr for immediate visibility
            Console.Error.WriteLine(entry);

            // Also write to companion log file if path is available
            if (_logFilePath != null)
            {
                try
                {
                    // Ensure directory exists
                    if (_logDir != null && !Directory.Exists(_logDir))
                        Directory.CreateDirectory(_logDir);

                    _writeLock.Wait();
                    try
                    {
                        File.AppendAllText(_logFilePath, entry + Environment.NewLine);
                    }
                    finally
                    {
                        _writeLock.Release();
                    }
                }
                catch
                {
                    // Best-effort file logging
                }
            }
        }
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
