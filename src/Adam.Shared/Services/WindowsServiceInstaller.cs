using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.Shared.Services;

public sealed class WindowsServiceInstaller : IServiceInstaller
{
    private readonly ILogger _logger;

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
        EnsureElevated();
        EnsureAbsolutePath(brokerPath);

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

        _logger.LogInformation("Creating service '{ServiceName}' with binPath='{BrokerPath}'...", ServiceName, brokerPath);
        await RunScAsync($"create {ServiceName} binPath=\"{brokerPath}\" start=auto", ct);
        _logger.LogInformation("Setting service description...");
        await RunScAsync($"description {ServiceName} \"Adam Digital Asset Management Broker Service\"", ct);

        _logger.LogInformation("Adding Windows Firewall rule for port {Port}...", port);
        try
        {
            await FirewallRuleManager.AddRuleAsync(port, ct);
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
        EnsureElevated();

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
            await FirewallRuleManager.RemoveRuleAsync(ct);
            _logger.LogInformation("Firewall rule removed successfully.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not remove firewall rule");
        }

        _logger.LogInformation("Service uninstalled successfully.");
    }

    public async Task<ServiceStatus> GetStatusAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("WindowsServiceInstaller.GetStatusAsync()");
        if (!IsSupported) return ServiceStatus.NotInstalled;
        return await GetStatusInternalAsync(ct);
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

    private static async Task RunScAsync(string arguments, CancellationToken ct)
    {
        await RunScRawAsync(arguments, ct);
    }

    private static async Task<string> RunScRawAsync(string arguments, CancellationToken ct)
    {
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

        if (process.ExitCode != 0)
        {
            var message = string.IsNullOrWhiteSpace(error) ? output.Trim() : error.Trim();
            throw new InvalidOperationException(
                $"sc.exe {arguments} failed (exit code {process.ExitCode}): {message}");
        }

        return output;
    }

    private void EnsureSupported()
    {
        if (!IsSupported)
            throw new PlatformNotSupportedException("Windows Service is only supported on Windows.");
    }

    private void EnsureElevated()
    {
        if (!IsElevated)
            throw new UnauthorizedAccessException(
                "Administrator privileges required. Please run the application as Administrator.");
    }

    private static void EnsureAbsolutePath(string path)
    {
        if (!Path.IsPathFullyQualified(path))
            throw new ArgumentException("brokerPath must be an absolute path.", nameof(path));
    }
}
