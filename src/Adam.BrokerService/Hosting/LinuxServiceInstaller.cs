using System.Diagnostics;
using System.Text;
using Adam.Shared.Services;

namespace Adam.BrokerService.Hosting;

public sealed class LinuxServiceInstaller : IServiceInstaller
{
    private const string SystemdPath = "/etc/systemd/system/adam-broker.service";
    private const string ServiceNameConst = "adam-broker";
    private static bool IsRoot => Environment.UserName == "root" || Environment.GetEnvironmentVariable("SUDO_USER") != null;

    public string ServiceName => ServiceNameConst;
    public bool IsSupported => OperatingSystem.IsLinux();

    public async Task InstallAsync(string brokerPath, CancellationToken ct = default)
    {
        EnsureSupported();
        EnsureAbsolutePath(brokerPath);

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

        // Write unit file (needs root for /etc/systemd/system/)
        await RunBashAsync($"tee {SystemdPath}", unit, ct);
        await RunBashAsync("systemctl daemon-reload", ct: ct);
        await RunBashAsync($"systemctl enable {ServiceNameConst}", ct: ct);
        await RunBashAsync($"systemctl start {ServiceNameConst}", ct: ct);
    }

    public async Task UninstallAsync(CancellationToken ct = default)
    {
        EnsureSupported();

        await RunBashAsync($"systemctl stop {ServiceNameConst}", ct: ct);
        await RunBashAsync($"systemctl disable {ServiceNameConst}", ct: ct);
        await RunBashAsync($"rm -f {SystemdPath}", ct: ct);
    }

    public async Task<ServiceStatus> GetStatusAsync(CancellationToken ct = default)
    {
        if (!IsSupported) return ServiceStatus.NotInstalled;
        try
        {
            var output = await RunBashAsync($"systemctl is-active {ServiceNameConst}", ct: ct);
            return output.Trim() switch
            {
                "active" => ServiceStatus.Running,
                "inactive" => ServiceStatus.Stopped,
                "failed" => ServiceStatus.Stopped,
                _ => File.Exists(SystemdPath) ? ServiceStatus.Stopped : ServiceStatus.NotInstalled
            };
        }
        catch
        {
            return ServiceStatus.NotInstalled;
        }
    }

    private static async Task<string> RunBashAsync(string command, string? stdin = null, CancellationToken ct = default)
    {
        // Prepend sudo unless already root
        if (!IsRoot && !command.StartsWith("sudo "))
            command = $"sudo {command}";

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-c \"{command.Replace("\"", "\\\"")}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = stdin != null,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();

        if (stdin != null)
        {
            await process.StandardInput.WriteAsync(stdin.AsMemory(), ct);
            process.StandardInput.Close();
        }

        var output = await process.StandardOutput.ReadToEndAsync(ct);
        var error = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            var message = string.IsNullOrWhiteSpace(error) ? output.Trim() : error.Trim();
            throw new InvalidOperationException(
                $"Command failed (exit code {process.ExitCode}): /bin/bash -c \"{command.Replace("\"", "\\\"")}\" => {message}");
        }

        return output;
    }

    private void EnsureSupported()
    {
        if (!IsSupported)
            throw new PlatformNotSupportedException("systemd is only supported on Linux.");
    }

    private static void EnsureAbsolutePath(string path)
    {
        if (!Path.IsPathFullyQualified(path))
            throw new ArgumentException("brokerPath must be an absolute path.", nameof(path));
    }
}
