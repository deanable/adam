using System.Diagnostics;
using System.Text;
using Adam.Shared.Services;

namespace Adam.BrokerService.Hosting;

public sealed class MacOsServiceInstaller : IServiceInstaller
{
    private static readonly string PlistPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "LaunchAgents", "com.adam.broker.plist");

    public string ServiceName => "com.adam.broker";
    public bool IsSupported => OperatingSystem.IsMacOS();

    public async Task InstallAsync(string brokerPath, int port, CancellationToken ct = default)
    {
        EnsureSupported();
        EnsureAbsolutePath(brokerPath);

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

        File.WriteAllText(PlistPath, plist, Encoding.UTF8);
        await RunBashAsync($"launchctl load {EscapePath(PlistPath)}", ct);
    }

    public async Task UninstallAsync(CancellationToken ct = default)
    {
        EnsureSupported();
        await RunBashAsync($"launchctl unload {EscapePath(PlistPath)}", ct);
        if (File.Exists(PlistPath))
            File.Delete(PlistPath);
    }

    public async Task<ServiceStatus> GetStatusAsync(CancellationToken ct = default)
    {
        if (!IsSupported) return ServiceStatus.NotInstalled;
        try
        {
            var output = await RunBashAsync($"launchctl list {ServiceName}", ct);
            if (output.Contains("\"PID\""))
                return ServiceStatus.Running;
            return File.Exists(PlistPath) ? ServiceStatus.Stopped : ServiceStatus.NotInstalled;
        }
        catch
        {
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
        // Quote the path for shell safety
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
