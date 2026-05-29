using System.Diagnostics;
using System.Security.Principal;

namespace Adam.Shared.Services;

public sealed class WindowsServiceInstaller : IServiceInstaller
{
    public string ServiceName => "AdamBrokerService";
    public bool IsSupported => OperatingSystem.IsWindows();
#pragma warning disable CA1416
    public bool IsElevated => new WindowsPrincipal(WindowsIdentity.GetCurrent())
        .IsInRole(WindowsBuiltInRole.Administrator);
#pragma warning restore CA1416

    public async Task InstallAsync(string brokerPath, int port, CancellationToken ct = default)
    {
        Debug.WriteLine($"[adam] WindowsServiceInstaller.InstallAsync(brokerPath='{brokerPath}', port={port})");

        EnsureSupported();
        EnsureElevated();
        EnsureAbsolutePath(brokerPath);

        Debug.WriteLine($"[adam] Checking port {port} availability...");
        var portFree = PortChecker.IsPortFree(port);
        if (!portFree)
        {
            var freePort = PortChecker.FindFreePort(port);
            var msg = freePort > 0
                ? $"Port {port} is already in use. Port {freePort} is available. Please update the port setting and try again."
                : $"Port {port} is already in use and no alternative ports are available in range.";
            Debug.WriteLine($"[adam] Port check failed: {msg}");
            throw new InvalidOperationException(msg);
        }

        Debug.WriteLine($"[adam] Creating service '{ServiceName}' with binPath='{brokerPath}'...");
        await RunScAsync($"create {ServiceName} binPath=\"{brokerPath}\" start=auto", ct);
        Debug.WriteLine("[adam] Setting service description...");
        await RunScAsync($"description {ServiceName} \"Adam Digital Asset Management Broker Service\"", ct);

        Debug.WriteLine($"[adam] Adding Windows Firewall rule for port {port}...");
        try
        {
            await FirewallRuleManager.AddRuleAsync(port, ct);
            Debug.WriteLine("[adam] Firewall rule added successfully.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Debug.WriteLine($"[adam] Warning: could not add firewall rule: {ex.Message}");
        }

        Debug.WriteLine($"[adam] Starting service '{ServiceName}'...");
        await RunScAsync($"start {ServiceName}", ct);
        Debug.WriteLine($"[adam] Service '{ServiceName}' installed and started successfully.");
    }

    public async Task UninstallAsync(CancellationToken ct = default)
    {
        Debug.WriteLine("[adam] WindowsServiceInstaller.UninstallAsync()");

        EnsureSupported();
        EnsureElevated();

        var status = await GetStatusInternalAsync(ct);
        Debug.WriteLine($"[adam] Current service status: {status}");
        if (status == ServiceStatus.Running)
        {
            Debug.WriteLine("[adam] Stopping service...");
            await RunScAsync($"stop {ServiceName}", ct);
        }

        Debug.WriteLine("[adam] Deleting service...");
        await RunScAsync($"delete {ServiceName}", ct);

        Debug.WriteLine("[adam] Removing Windows Firewall rule...");
        try
        {
            await FirewallRuleManager.RemoveRuleAsync(ct);
            Debug.WriteLine("[adam] Firewall rule removed successfully.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Debug.WriteLine($"[adam] Warning: could not remove firewall rule: {ex.Message}");
        }

        Debug.WriteLine("[adam] Service uninstalled successfully.");
    }

    public async Task<ServiceStatus> GetStatusAsync(CancellationToken ct = default)
    {
        Debug.WriteLine("[adam] WindowsServiceInstaller.GetStatusAsync()");
        if (!IsSupported) return ServiceStatus.NotInstalled;
        return await GetStatusInternalAsync(ct);
    }

    private async Task<ServiceStatus> GetStatusInternalAsync(CancellationToken ct)
    {
        try
        {
            var output = await RunScRawAsync($"query {ServiceName}", ct);
            var status = ParseScQuery(output);
            Debug.WriteLine($"[adam] Service status: {status}");
            return status;
        }
        catch (Exception ex) when (ex is not PlatformNotSupportedException && ex is not UnauthorizedAccessException)
        {
            Debug.WriteLine($"[adam] GetStatusInternalAsync: service not found or not accessible: {ex.Message}");
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
