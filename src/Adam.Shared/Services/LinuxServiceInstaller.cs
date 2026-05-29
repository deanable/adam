using System.Diagnostics;

namespace Adam.Shared.Services;

public sealed class LinuxServiceInstaller : IServiceInstaller
{
    private const string SystemdPath = "/etc/systemd/system/adam-broker.service";
    private const string ServiceNameConst = "adam-broker";
    private static bool IsRoot => Environment.UserName == "root" || Environment.GetEnvironmentVariable("SUDO_USER") != null;

    public string ServiceName => ServiceNameConst;
    public bool IsSupported => OperatingSystem.IsLinux();

    public async Task InstallAsync(string brokerPath, int port, CancellationToken ct = default)
    {
        Debug.WriteLine($"[adam] LinuxServiceInstaller.InstallAsync(brokerPath='{brokerPath}', port={port})");
        Debug.WriteLine($"[adam] IsRoot={IsRoot}, SystemdPath={SystemdPath}");

        EnsureSupported();
        EnsureAbsolutePath(brokerPath);

        var unit = $$"""
[Unit]
Description=Adam Digital Asset Management Broker Service
After=network.target

[Service]
Type=simple
ExecStart={{brokerPath}} --port {{port}}
Restart=on-failure
RestartSec=5
StandardOutput=append:/var/log/adam-broker.log
StandardError=append:/var/log/adam-broker.err

[Install]
WantedBy=multi-user.target
""";

        Debug.WriteLine("[adam] Writing systemd unit file...");
        await RunBashAsync($"tee {SystemdPath}", unit, ct);
        Debug.WriteLine("[adam] Running systemctl daemon-reload...");
        await RunBashAsync("systemctl daemon-reload", ct: ct);
        Debug.WriteLine("[adam] Enabling service...");
        await RunBashAsync($"systemctl enable {ServiceNameConst}", ct: ct);
        Debug.WriteLine("[adam] Starting service...");
        await RunBashAsync($"systemctl start {ServiceNameConst}", ct: ct);
        Debug.WriteLine("[adam] Service installed and started successfully.");
    }

    public async Task UninstallAsync(CancellationToken ct = default)
    {
        Debug.WriteLine("[adam] LinuxServiceInstaller.UninstallAsync()");
        EnsureSupported();

        Debug.WriteLine("[adam] Stopping service...");
        await RunBashAsync($"systemctl stop {ServiceNameConst}", ct: ct);
        Debug.WriteLine("[adam] Disabling service...");
        await RunBashAsync($"systemctl disable {ServiceNameConst}", ct: ct);
        Debug.WriteLine("[adam] Removing unit file...");
        await RunBashAsync($"rm -f {SystemdPath}", ct: ct);
        Debug.WriteLine("[adam] Service uninstalled successfully.");
    }

    public async Task<ServiceStatus> GetStatusAsync(CancellationToken ct = default)
    {
        Debug.WriteLine("[adam] LinuxServiceInstaller.GetStatusAsync()");
        if (!IsSupported) return ServiceStatus.NotInstalled;
        try
        {
            var output = await RunBashAsync($"systemctl is-active {ServiceNameConst}", ct: ct);
            var trimmed = output.Trim();
            Debug.WriteLine($"[adam] systemctl is-active output: '{trimmed}'");
            var status = trimmed switch
            {
                "active" => ServiceStatus.Running,
                "inactive" => ServiceStatus.Stopped,
                "failed" => ServiceStatus.Stopped,
                _ => File.Exists(SystemdPath) ? ServiceStatus.Stopped : ServiceStatus.NotInstalled
            };
            Debug.WriteLine($"[adam] Resolved service status: {status}");
            return status;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[adam] GetStatusAsync error: {ex.Message}");
            return ServiceStatus.NotInstalled;
        }
    }

    private static async Task<string> RunBashAsync(string command, string? stdin = null, CancellationToken ct = default)
    {
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
