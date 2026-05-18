using System.Diagnostics;
using System.Security.Principal;
using Adam.Shared.Services;

namespace Adam.BrokerService.Hosting;

public sealed class WindowsServiceInstaller : IServiceInstaller
{
    public string ServiceName => "AdamBrokerService";
    public bool IsSupported => OperatingSystem.IsWindows();
#pragma warning disable CA1416
    public bool IsElevated => new WindowsPrincipal(WindowsIdentity.GetCurrent())
        .IsInRole(WindowsBuiltInRole.Administrator);
#pragma warning restore CA1416

    public async Task InstallAsync(string brokerPath, CancellationToken ct = default)
    {
        EnsureSupported();
        EnsureElevated();
        EnsureAbsolutePath(brokerPath);

        await RunScAsync($"create {ServiceName} binPath=\"{brokerPath}\" start=auto", ct);
        await RunScAsync($"description {ServiceName} \"Adam Digital Asset Management Broker Service\"", ct);
        await RunScAsync($"start {ServiceName}", ct);
    }

    public async Task UninstallAsync(CancellationToken ct = default)
    {
        EnsureSupported();
        EnsureElevated();

        var status = await GetStatusInternalAsync(ct);
        if (status == ServiceStatus.Running)
            await RunScAsync($"stop {ServiceName}", ct);
        await RunScAsync($"delete {ServiceName}", ct);
    }

    public async Task<ServiceStatus> GetStatusAsync(CancellationToken ct = default)
    {
        if (!IsSupported) return ServiceStatus.NotInstalled;
        return await GetStatusInternalAsync(ct);
    }

    private async Task<ServiceStatus> GetStatusInternalAsync(CancellationToken ct)
    {
        try
        {
            var output = await RunScRawAsync($"query {ServiceName}", ct);
            return ParseScQuery(output);
        }
        catch (Exception ex) when (ex is not PlatformNotSupportedException && ex is not UnauthorizedAccessException)
        {
            return ServiceStatus.NotInstalled;
        }
    }

    private ServiceStatus ParseScQuery(string output)
    {
        // sc query output format:
        // SERVICE_NAME: AdamBrokerService
        //         STATE              : 4  RUNNING
        // or for stopped: STATE : 1  STOPPED
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
        var output = await RunScRawAsync(arguments, ct);
        // sc returns exit code 0 on success
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
