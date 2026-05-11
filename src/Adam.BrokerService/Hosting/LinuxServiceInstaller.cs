using System.Diagnostics;
using System.Text;
using Adam.Shared.Services;

namespace Adam.BrokerService.Hosting;

public sealed class LinuxServiceInstaller : IServiceInstaller
{
    private const string SystemdPath = "/etc/systemd/system/adam-broker.service";

    public string ServiceName => "adam-broker";
    public bool IsSupported => OperatingSystem.IsLinux();

    public Task InstallAsync(string brokerPath, CancellationToken ct = default)
    {
        if (!IsSupported) throw new PlatformNotSupportedException("systemd is only supported on Linux.");

        var unit = $$"""
[Unit]
Description=Adam Digital Asset Management Broker Service
After=network.target

[Service]
Type=simple
ExecStart={{brokerPath}}
Restart=on-failure
RestartSec=5
StandardOutput=append:/var/log/adam-broker.log
StandardError=append:/var/log/adam-broker.err

[Install]
WantedBy=multi-user.target
""";

        File.WriteAllText(SystemdPath, unit, Encoding.UTF8);
        return RunSystemctlAsync("daemon-reload && systemctl enable adam-broker && systemctl start adam-broker", ct);
    }

    public Task UninstallAsync(CancellationToken ct = default)
    {
        if (!IsSupported) throw new PlatformNotSupportedException("systemd is only supported on Linux.");
        return RunSystemctlAsync("stop adam-broker && systemctl disable adam-broker && rm -f " + SystemdPath, ct);
    }

    public async Task<ServiceStatus> GetStatusAsync(CancellationToken ct = default)
    {
        if (!IsSupported) return ServiceStatus.NotInstalled;
        try
        {
            var output = await RunSystemctlAsync("is-active adam-broker", ct);
            return output.Trim() switch
            {
                "active" => ServiceStatus.Running,
                "inactive" => ServiceStatus.Stopped,
                _ => File.Exists(SystemdPath) ? ServiceStatus.Stopped : ServiceStatus.NotInstalled
            };
        }
        catch
        {
            return ServiceStatus.NotInstalled;
        }
    }

    private static async Task<string> RunSystemctlAsync(string arguments, CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "systemctl",
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
