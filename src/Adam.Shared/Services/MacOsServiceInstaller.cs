using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.Shared.Services;

public sealed class MacOsServiceInstaller : IServiceInstaller
{
    private readonly ILogger _logger;

    private static readonly string PlistPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "LaunchAgents", "com.adam.broker.plist");

    public string ServiceName => "com.adam.broker";
    public bool IsSupported => OperatingSystem.IsMacOS();

    public MacOsServiceInstaller(ILogger<MacOsServiceInstaller>? logger = null)
    {
        _logger = logger ?? NullLogger<MacOsServiceInstaller>.Instance;
    }

    public async Task InstallAsync(string brokerPath, int port, CancellationToken ct = default)
    {
        _logger.LogInformation("MacOsServiceInstaller.InstallAsync(brokerPath='{BrokerPath}', port={Port})", brokerPath, port);
        _logger.LogInformation("PlistPath={PlistPath}", PlistPath);

        EnsureSupported();
        EnsureAbsolutePath(brokerPath);

        _logger.LogInformation("Creating plist directory...");
        Directory.CreateDirectory(Path.GetDirectoryName(PlistPath)!);

        var plist = $$"""
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>{{ServiceName}}</string>
    <key>ProgramArguments</key>
    <array>
        <string>{{brokerPath}}</string>
        <string>--port</string>
        <string>{{port}}</string>
    </array>
    <key>KeepAlive</key>
    <true/>
    <key>RunAtLoad</key>
    <true/>
    <key>StandardOutPath</key>
    <string>/usr/local/var/log/adam-broker.log</string>
    <key>StandardErrorPath</key>
    <string>/usr/local/var/log/adam-broker.err</string>
</dict>
</plist>
""";

        _logger.LogInformation("Writing plist file to {PlistPath}...", PlistPath);
        File.WriteAllText(PlistPath, plist, Encoding.UTF8);
        _logger.LogInformation("Loading launchd plist...");
        await RunBashAsync($"launchctl load {EscapePath(PlistPath)}", ct);
        _logger.LogInformation("Service installed successfully.");
    }

    public async Task UninstallAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("MacOsServiceInstaller.UninstallAsync()");
        EnsureSupported();

        _logger.LogInformation("Unloading launchd plist...");
        await RunBashAsync($"launchctl unload {EscapePath(PlistPath)}", ct);
        if (File.Exists(PlistPath))
        {
            _logger.LogInformation("Deleting plist file at {PlistPath}...", PlistPath);
            File.Delete(PlistPath);
        }
        _logger.LogInformation("Service uninstalled successfully.");
    }

    public async Task<ServiceStatus> GetStatusAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("MacOsServiceInstaller.GetStatusAsync()");
        if (!IsSupported) return ServiceStatus.NotInstalled;
        try
        {
            var output = await RunBashAsync($"launchctl list {ServiceName}", ct);
            _logger.LogInformation("launchctl list output length: {Length}", output.Length);
            if (output.Contains("\"PID\""))
            {
                _logger.LogInformation("Service is running (PID found).");
                return ServiceStatus.Running;
            }
            var status = File.Exists(PlistPath) ? ServiceStatus.Stopped : ServiceStatus.NotInstalled;
            _logger.LogInformation("Resolved service status: {Status}", status);
            return status;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetStatusAsync error");
            return ServiceStatus.NotInstalled;
        }
    }

    private static async Task<string> RunBashAsync(string command, CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-c \"{command.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"",
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
                $"Command failed (exit code {process.ExitCode}): launchctl => {message}");
        }

        return output;
    }

    private static string EscapePath(string path)
    {
        return $"\"{path.Replace("\"", "\\\"")}\"";
    }

    private void EnsureSupported()
    {
        if (!IsSupported)
            throw new PlatformNotSupportedException("launchd is only supported on macOS.");
    }

    private static void EnsureAbsolutePath(string path)
    {
        if (!Path.IsPathFullyQualified(path))
            throw new ArgumentException("brokerPath must be an absolute path.", nameof(path));
    }
}
