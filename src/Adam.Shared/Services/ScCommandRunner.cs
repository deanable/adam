using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Adam.Shared.Services;

/// <summary>
/// Runs sc.exe commands for Windows service management.
/// Provides raw and wrapped execution, output parsing, and service status queries.
/// </summary>
public sealed class ScCommandRunner
{
    private readonly ILogger _logger;

    public ScCommandRunner(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Runs a sc.exe command and throws on non-zero exit code.
    /// </summary>
    public async Task RunAsync(string arguments, CancellationToken ct)
    {
        await RunRawAsync(arguments, ct);
    }

    /// <summary>
    /// Runs a sc.exe command and returns the stdout output.
    /// Throws on non-zero exit code.
    /// </summary>
    public async Task<string> RunRawAsync(string arguments, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("[TIMING] Running: sc.exe {Arguments}", arguments);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var pid = process.Id;
        _logger.LogInformation("[TIMING] sc.exe started (PID={Pid}) at {Timestamp:O}", pid, DateTime.UtcNow);

        var output = await process.StandardOutput.ReadToEndAsync(ct);
        var error = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        _logger.LogInformation("[TIMING] sc.exe (PID={Pid}) exited (code={ExitCode}) in {ElapsedMs:F0}ms",
            pid, process.ExitCode, sw.Elapsed.TotalMilliseconds);

        if (!string.IsNullOrWhiteSpace(output))
            _logger.LogInformation("[DIAG] sc.exe stdout ({Len} chars): {Output}", output.Length, output.TrimEnd());
        if (!string.IsNullOrWhiteSpace(error))
            _logger.LogWarning("[DIAG] sc.exe stderr ({Len} chars): {Error}", error.Length, error.TrimEnd());

        if (process.ExitCode != 0)
        {
            var message = string.IsNullOrWhiteSpace(error) ? output.Trim() : error.Trim();
            _logger.LogError("sc.exe {Arguments} FAILED (exit code {ExitCode}, elapsed={ElapsedMs:F0}ms): {Message}",
                arguments, process.ExitCode, sw.Elapsed.TotalMilliseconds, message);
            throw new InvalidOperationException(
                $"sc.exe {arguments} failed (exit code {process.ExitCode}): {message}");
        }

        _logger.LogInformation("[TIMING] sc.exe {Arguments} completed OK (PID={Pid}, elapsed={ElapsedMs:F0}ms)",
            arguments, pid, sw.Elapsed.TotalMilliseconds);
        return output;
    }

    /// <summary>
    /// Queries the status of a Windows service via sc.exe.
    /// </summary>
    public async Task<ServiceStatus> GetServiceStatusAsync(string serviceName, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            _logger.LogInformation("[TIMING] Querying service '{ServiceName}' status via sc.exe...", serviceName);
            var output = await RunRawAsync($"query {serviceName}", ct);
            _logger.LogInformation("[DIAG] Raw sc.exe query output:\n{Output}", output.TrimEnd());
            var status = ParseScQuery(output);
            _logger.LogInformation("[TIMING] Service status queried in {ElapsedMs:F0}ms: {Status}", sw.Elapsed.TotalMilliseconds, status);
            return status;
        }
        catch (Exception ex) when (ex is not PlatformNotSupportedException && ex is not UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "GetServiceStatusAsync: service not found or not accessible (elapsed={ElapsedMs:F0}ms)", sw.Elapsed.TotalMilliseconds);
            return ServiceStatus.NotInstalled;
        }
    }

    /// <summary>
    /// Parses the output of sc.exe query to determine the service status.
    /// </summary>
    public static ServiceStatus ParseScQuery(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            if (!line.Contains("STATE", StringComparison.OrdinalIgnoreCase)) continue;
            var upper = line.ToUpperInvariant();
            if (upper.Contains("RUNNING")) return ServiceStatus.Running;
            if (upper.Contains("STOPPED")) return ServiceStatus.Stopped;
            if (upper.Contains("STOP_PENDING")) return ServiceStatus.Running;
            if (upper.Contains("START_PENDING")) return ServiceStatus.Unknown;
            return ServiceStatus.Unknown;
        }
        return ServiceStatus.NotInstalled;
    }

    /// <summary>
    /// Builds the <c>sc.exe create</c> arguments string for a service installation.
    /// The binPath uses only the exe path (no additional arguments) because:
    /// 1. <c>sc.exe</c>'s parser doesn't understand quoted values with embedded <c>=</c> signs
    /// 2. Service configuration (port, etc.) is configured via <c>appsettings.json</c>
    ///    alongside the broker executable during installation.
    /// Format: <c>create &lt;serviceName&gt; binPath= "&lt;brokerPath&gt;" start=auto</c>
    /// </summary>
    public static string BuildCreateArguments(string serviceName, string brokerPath)
    {
        var pathArg = brokerPath.Contains(' ') ? $"\"{brokerPath}\"" : brokerPath;
        return $"create {serviceName} binPath= {pathArg} start=auto";
    }
}
