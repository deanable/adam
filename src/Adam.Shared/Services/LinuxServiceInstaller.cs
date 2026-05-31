using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.Shared.Services;

public sealed class LinuxServiceInstaller : IServiceInstaller
{
    private readonly ILogger _logger;

    private const string SystemdPath = "/etc/systemd/system/adam-broker.service";
    private const string ServiceNameConst = "adam-broker";
    private static bool IsRoot => Environment.UserName == "root" || Environment.GetEnvironmentVariable("SUDO_USER") != null;

    public string ServiceName => ServiceNameConst;
    public bool IsSupported => OperatingSystem.IsLinux();

    public LinuxServiceInstaller(ILogger<LinuxServiceInstaller>? logger = null)
    {
        _logger = logger ?? NullLogger<LinuxServiceInstaller>.Instance;
    }

    public async Task InstallAsync(string brokerPath, int port, CancellationToken ct = default)
    {
        _logger.LogInformation("LinuxServiceInstaller.InstallAsync(brokerPath='{BrokerPath}', port={Port})", brokerPath, port);
        _logger.LogInformation("IsRoot={IsRoot}, SystemdPath={SystemdPath}", IsRoot, SystemdPath);

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

        _logger.LogInformation("Writing systemd unit file to {SystemdPath}...", SystemdPath);
        await RunBashAsync($"tee {SystemdPath}", unit, ct);
        _logger.LogInformation("Running systemctl daemon-reload...");
        await RunBashAsync("systemctl daemon-reload", ct: ct);
        _logger.LogInformation("Enabling service...");
        await RunBashAsync($"systemctl enable {ServiceNameConst}", ct: ct);
        _logger.LogInformation("Starting service...");
        await RunBashAsync($"systemctl start {ServiceNameConst}", ct: ct);
        _logger.LogInformation("Service installed and started successfully.");
    }

    public async Task UninstallAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("LinuxServiceInstaller.UninstallAsync()");
        EnsureSupported();

        _logger.LogInformation("Stopping service...");
        await RunBashAsync($"systemctl stop {ServiceNameConst}", ct: ct);
        _logger.LogInformation("Disabling service...");
        await RunBashAsync($"systemctl disable {ServiceNameConst}", ct: ct);
        _logger.LogInformation("Removing unit file at {SystemdPath}...", SystemdPath);
        await RunBashAsync($"rm -f {SystemdPath}", ct: ct);
        _logger.LogInformation("Service uninstalled successfully.");
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("LinuxServiceInstaller.StartAsync()");
        EnsureSupported();
        _logger.LogInformation("Starting service '{ServiceNameConst}'...", ServiceNameConst);
        await RunBashAsync($"systemctl start {ServiceNameConst}", ct: ct);
        _logger.LogInformation("Service '{ServiceNameConst}' started successfully.", ServiceNameConst);
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("LinuxServiceInstaller.StopAsync()");
        EnsureSupported();
        _logger.LogInformation("Stopping service '{ServiceNameConst}'...", ServiceNameConst);
        await RunBashAsync($"systemctl stop {ServiceNameConst}", ct: ct);
        _logger.LogInformation("Service '{ServiceNameConst}' stopped successfully.", ServiceNameConst);
    }

    public async Task<ServiceStatus> GetStatusAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("LinuxServiceInstaller.GetStatusAsync()");
        if (!IsSupported) return ServiceStatus.NotInstalled;
        try
        {
            var output = await RunBashAsync($"systemctl is-active {ServiceNameConst}", ct: ct);
            var trimmed = output.Trim();
            _logger.LogInformation("systemctl is-active output: '{Output}'", trimmed);
            var status = trimmed switch
            {
                "active" => ServiceStatus.Running,
                "inactive" => ServiceStatus.Stopped,
                "failed" => ServiceStatus.Stopped,
                _ => File.Exists(SystemdPath) ? ServiceStatus.Stopped : ServiceStatus.NotInstalled
            };
            _logger.LogInformation("Resolved service status: {Status}", status);
            return status;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetStatusAsync error");
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
