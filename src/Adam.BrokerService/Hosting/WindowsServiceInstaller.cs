using System.Diagnostics;
using Adam.Shared.Services;

namespace Adam.BrokerService.Hosting;

public sealed class WindowsServiceInstaller : IServiceInstaller
{
    public string ServiceName => "AdamBrokerService";
    public bool IsSupported => OperatingSystem.IsWindows();

    public async Task InstallAsync(string brokerPath, CancellationToken ct = default)
    {
        if (!IsSupported) throw new PlatformNotSupportedException("Windows Service is only supported on Windows.");
        await RunScAsync($"create {ServiceName} binPath=\"{brokerPath}\" start=auto", ct);
        await RunScAsync($"description {ServiceName} \"Adam Digital Asset Management Broker Service\"", ct);
        await RunScAsync($"start {ServiceName}", ct);
    }

    public async Task UninstallAsync(CancellationToken ct = default)
    {
        if (!IsSupported) throw new PlatformNotSupportedException("Windows Service is only supported on Windows.");
        await RunScAsync($"stop {ServiceName}", ct);
        await RunScAsync($"delete {ServiceName}", ct);
    }

    public async Task<ServiceStatus> GetStatusAsync(CancellationToken ct = default)
    {
        if (!IsSupported) return ServiceStatus.NotInstalled;
        try
        {
            var output = await RunScAsync($"query {ServiceName}", ct);
            if (output.Contains("STATE"))
            {
                if (output.Contains("RUNNING")) return ServiceStatus.Running;
                if (output.Contains("STOPPED")) return ServiceStatus.Stopped;
                return ServiceStatus.Stopped;
            }
            return ServiceStatus.NotInstalled;
        }
        catch
        {
            return ServiceStatus.NotInstalled;
        }
    }

    private static async Task<string> RunScAsync(string arguments, CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "sc",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        return output;
    }
}
