using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.Shared.Services;

/// <summary>
/// Defines the elevated operation request sent to the helper process via a temp JSON file.
/// </summary>
public sealed record ElevatedRequest
{
    public string Operation { get; init; } = string.Empty; // "install", "uninstall", "start", "stop"
    public string? BrokerPath { get; init; }
    public int Port { get; init; }
}

/// <summary>
/// Defines the result written back by the elevated helper process.
/// </summary>
public sealed record ElevatedResponse
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class WindowsServiceInstaller : IServiceInstaller
{
    private readonly ILogger _logger;

    /// <summary>
    /// Path to the executable to launch elevated for privileged operations.
    /// Defaults to <c>Environment.ProcessPath</c>. Set this if the helper
    /// executable is different from the current process.
    /// </summary>
    public string ProcessPath { get; set; } = Environment.ProcessPath ?? string.Empty;

    /// <summary>
    /// Testing hook: when set, replaces the elevated process launch in <see cref="RunElevatedAsync"/>.
    /// The delegate receives the request file path and a cancellation token.
    /// It should write an <see cref="ElevatedResponse"/> JSON to that file before returning.
    /// </summary>
    public Func<string, CancellationToken, Task>? ElevatedProcessHandler { get; set; }

    public string ServiceName => "AdamBrokerService";
    public bool IsSupported => OperatingSystem.IsWindows();
#pragma warning disable CA1416
    public bool IsElevated => new WindowsPrincipal(WindowsIdentity.GetCurrent())
        .IsInRole(WindowsBuiltInRole.Administrator);
#pragma warning restore CA1416

    public WindowsServiceInstaller(ILogger<WindowsServiceInstaller>? logger = null)
    {
        _logger = logger ?? NullLogger<WindowsServiceInstaller>.Instance;
    }

    public async Task InstallAsync(string brokerPath, int port, CancellationToken ct = default)
    {
        _logger.LogInformation("WindowsServiceInstaller.InstallAsync(brokerPath='{BrokerPath}', port={Port})", brokerPath, port);

        EnsureSupported();
        EnsureAbsolutePath(brokerPath);

        if (!IsElevated)
        {
            _logger.LogInformation("Not elevated — launching helper process via UAC...");
            await RunElevatedAsync(new ElevatedRequest { Operation = "install", BrokerPath = brokerPath, Port = port }, ct);
            return;
        }

        _logger.LogInformation("Checking port {Port} availability...", port);
        var portFree = PortChecker.IsPortFree(port);
        if (!portFree)
        {
            var freePort = PortChecker.FindFreePort(port);
            var msg = freePort > 0
                ? $"Port {port} is already in use. Port {freePort} is available. Please update the port setting and try again."
                : $"Port {port} is already in use and no alternative ports are available in range.";
            _logger.LogWarning("Port check failed: {Message}", msg);
            throw new InvalidOperationException(msg);
        }

        // Check if service already exists — if so, update it instead of recreating
        var existingStatus = await GetStatusInternalAsync(ct);
        if (existingStatus != ServiceStatus.NotInstalled)
        {
            _logger.LogInformation("Service '{ServiceName}' already exists (status={Status}). Updating configuration...", ServiceName, existingStatus);

            if (existingStatus == ServiceStatus.Running)
            {
                _logger.LogInformation("Stopping service before updating...");
                await RunScAsync($"stop {ServiceName}", ct);
            }

            _logger.LogInformation("Updating service binary path to '{BrokerPath}'...", brokerPath);
            await RunScAsync($"config {ServiceName} binPath=\"{brokerPath}\" start=auto", ct);
            _logger.LogInformation("Updating service description...");
            await RunScAsync($"description {ServiceName} \"Adam Digital Asset Management Broker Service\"", ct);
        }
        else
        {
            _logger.LogInformation("Creating service '{ServiceName}' with binPath='{BrokerPath}'...", ServiceName, brokerPath);
            await RunScAsync($"create {ServiceName} binPath=\"{brokerPath}\" start=auto", ct);
            _logger.LogInformation("Setting service description...");
            await RunScAsync($"description {ServiceName} \"Adam Digital Asset Management Broker Service\"", ct);
        }

        _logger.LogInformation("Adding Windows Firewall rule for port {Port}...", port);
        try
        {
            await FirewallRuleManager.AddRuleAsync(port, ct, _logger);
            _logger.LogInformation("Firewall rule added successfully.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not add firewall rule for port {Port}", port);
        }

        _logger.LogInformation("Starting service '{ServiceName}'...", ServiceName);
        await RunScAsync($"start {ServiceName}", ct);
        _logger.LogInformation("Service '{ServiceName}' installed and started successfully.", ServiceName);
    }

    public async Task UninstallAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("WindowsServiceInstaller.UninstallAsync()");

        EnsureSupported();

        if (!IsElevated)
        {
            _logger.LogInformation("Not elevated — launching helper process via UAC...");
            await RunElevatedAsync(new ElevatedRequest { Operation = "uninstall" }, ct);
            return;
        }

        var status = await GetStatusInternalAsync(ct);
        _logger.LogInformation("Current service status: {Status}", status);
        if (status == ServiceStatus.Running)
        {
            _logger.LogInformation("Stopping service...");
            await RunScAsync($"stop {ServiceName}", ct);
        }

        _logger.LogInformation("Deleting service...");
        await RunScAsync($"delete {ServiceName}", ct);
        _logger.LogInformation("Removing Windows Firewall rule...");
        try
        {
            await FirewallRuleManager.RemoveRuleAsync(ct, _logger);
            _logger.LogInformation("Firewall rule removed successfully.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not remove firewall rule");
        }

        _logger.LogInformation("Service uninstalled successfully.");
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("WindowsServiceInstaller.StartAsync()");
        EnsureSupported();

        if (!IsElevated)
        {
            _logger.LogInformation("Not elevated — launching helper process via UAC...");
            await RunElevatedAsync(new ElevatedRequest { Operation = "start" }, ct);
            return;
        }

        _logger.LogInformation("Starting service '{ServiceName}'...", ServiceName);
        await RunScAsync($"start {ServiceName}", ct);
        _logger.LogInformation("Service '{ServiceName}' started successfully.", ServiceName);
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("WindowsServiceInstaller.StopAsync()");
        EnsureSupported();

        if (!IsElevated)
        {
            _logger.LogInformation("Not elevated — launching helper process via UAC...");
            await RunElevatedAsync(new ElevatedRequest { Operation = "stop" }, ct);
            return;
        }

        _logger.LogInformation("Stopping service '{ServiceName}'...", ServiceName);
        await RunScAsync($"stop {ServiceName}", ct);
        _logger.LogInformation("Service '{ServiceName}' stopped successfully.", ServiceName);
    }

    public async Task<ServiceStatus> GetStatusAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("WindowsServiceInstaller.GetStatusAsync()");
        if (!IsSupported) return ServiceStatus.NotInstalled;
        return await GetStatusInternalAsync(ct);
    }

    /// <summary>
    /// Serializes the elevated request to a temp file, launches the helper process
    /// via UAC (<c>runas</c>), waits for it to complete, and reads the result.
    /// </summary>
    private async Task RunElevatedAsync(ElevatedRequest request, CancellationToken ct)
    {
        var tempDir = Path.GetTempPath();
        var requestFile = Path.Combine(tempDir, $"adam-elevated-{Guid.NewGuid():N}.json");

        try
        {
            // Serialize request to temp file
            var json = JsonSerializer.Serialize(request);
            await File.WriteAllTextAsync(requestFile, json, ct);

            _logger.LogInformation("Launching elevated helper: {ProcessPath} --elevated {RequestFile}", ProcessPath, requestFile);

            if (ElevatedProcessHandler != null)
            {
                _logger.LogDebug("ElevatedProcessHandler is set — delegating instead of launching process.");
                await ElevatedProcessHandler(requestFile, ct);
            }
            else
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = ProcessPath,
                        Arguments = $"--elevated \"{requestFile}\"",
                        UseShellExecute = true,
                        Verb = "runas",
                        // Note: with UseShellExecute = true, we cannot redirect output;
                        // results are communicated back via the temp file.
                        WindowStyle = ProcessWindowStyle.Hidden
                    }
                };

                process.Start();
                await process.WaitForExitAsync(ct);
            }

            // Read result from temp file
            if (!File.Exists(requestFile))
            {
                var msg = "Elevated helper process did not produce a result file.";
                _logger.LogError(msg);
                throw new InvalidOperationException(msg);
            }

            var resultJson = await File.ReadAllTextAsync(requestFile, ct);
            var result = JsonSerializer.Deserialize<ElevatedResponse>(resultJson);

            if (result == null)
            {
                var msg = "Elevated helper process returned an unparseable result.";
                _logger.LogError(msg);
                throw new InvalidOperationException(msg);
            }

            if (!result.Success)
            {
                var errorMsg = result.ErrorMessage ?? "Elevated operation failed with no error message.";
                _logger.LogError("Elevated operation failed: {Error}", errorMsg);
                throw new InvalidOperationException(errorMsg);
            }

            _logger.LogInformation("Elevated operation completed successfully.");
        }
        finally
        {
            // Clean up temp file
            try { if (File.Exists(requestFile)) File.Delete(requestFile); }
            catch { /* best-effort cleanup */ }
        }
    }

    private async Task<ServiceStatus> GetStatusInternalAsync(CancellationToken ct)
    {
        try
        {
            var output = await RunScRawAsync($"query {ServiceName}", ct);
            var status = ParseScQuery(output);
            _logger.LogInformation("Service status: {Status}", status);
            return status;
        }
        catch (Exception ex) when (ex is not PlatformNotSupportedException && ex is not UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "GetStatusInternalAsync: service not found or not accessible");
            return ServiceStatus.NotInstalled;
        }
    }

    private ServiceStatus ParseScQuery(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            if (!line.Contains("STATE", StringComparison.OrdinalIgnoreCase)) continue;
            var upper = line.ToUpperInvariant();
            if (upper.Contains("RUNNING")) return ServiceStatus.Running;
            if (upper.Contains("STOPPED") || upper.Contains("STOP_PENDING")) return ServiceStatus.Stopped;
            if (upper.Contains("START_PENDING")) return ServiceStatus.Stopped;
            return ServiceStatus.Unknown;
        }
        return ServiceStatus.NotInstalled;
    }

    private async Task RunScAsync(string arguments, CancellationToken ct)
    {
        await RunScRawAsync(arguments, ct);
    }

    private async Task<string> RunScRawAsync(string arguments, CancellationToken ct)
    {
        _logger.LogDebug("Running sc.exe {Arguments}", arguments);

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
        var output = await process.StandardOutput.ReadToEndAsync(ct);
        var error = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        // Log the terminal output for debugging purposes
        if (!string.IsNullOrWhiteSpace(output))
            _logger.LogDebug("sc.exe stdout: {Output}", output.TrimEnd());
        if (!string.IsNullOrWhiteSpace(error))
            _logger.LogWarning("sc.exe stderr: {Error}", error.TrimEnd());

        if (process.ExitCode != 0)
        {
            var message = string.IsNullOrWhiteSpace(error) ? output.Trim() : error.Trim();
            _logger.LogError("sc.exe {Arguments} failed (exit code {ExitCode}): {Message}",
                arguments, process.ExitCode, message);
            throw new InvalidOperationException(
                $"sc.exe {arguments} failed (exit code {process.ExitCode}): {message}");
        }

        _logger.LogDebug("sc.exe {Arguments} completed successfully (exit code 0)", arguments);
        return output;
    }

    private void EnsureSupported()
    {
        if (!IsSupported)
            throw new PlatformNotSupportedException("Windows Service is only supported on Windows.");
    }

    private void EnsureAbsolutePath(string path)
    {
        if (!Path.IsPathFullyQualified(path))
            throw new ArgumentException("brokerPath must be an absolute path.", nameof(path));
    }
}
