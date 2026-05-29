using System.Diagnostics;
using System.Text;

namespace Adam.Shared.Services;

public sealed class MacOsServiceInstaller : IServiceInstaller
{
    private static readonly string PlistPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "LaunchAgents", "com.adam.broker.plist");

    public string ServiceName => "com.adam.broker";
    public bool IsSupported => OperatingSystem.IsMacOS();

    public async Task InstallAsync(string brokerPath, int port, CancellationToken ct = default)
    {
        Debug.WriteLine($"[adam] MacOsServiceInstaller.InstallAsync(brokerPath='{brokerPath}', port={port})");
        Debug.WriteLine($"[adam] PlistPath={PlistPath}");

        EnsureSupported();
        EnsureAbsolutePath(brokerPath);

        Debug.WriteLine("[adam] Creating plist directory...");
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

        Debug.WriteLine("[adam] Writing plist file...");
        File.WriteAllText(PlistPath, plist, Encoding.UTF8);
        Debug.WriteLine("[adam] Loading launchd plist...");
        await RunBashAsync($"launchctl load {EscapePath(PlistPath)}", ct);
        Debug.WriteLine("[adam] Service installed successfully.");
    }

    public async Task UninstallAsync(CancellationToken ct = default)
    {
        Debug.WriteLine("[adam] MacOsServiceInstaller.UninstallAsync()");
        EnsureSupported();

        Debug.WriteLine("[adam] Unloading launchd plist...");
        await RunBashAsync($"launchctl unload {EscapePath(PlistPath)}", ct);
        if (File.Exists(PlistPath))
        {
            Debug.WriteLine("[adam] Deleting plist file...");
            File.Delete(PlistPath);
        }
        Debug.WriteLine("[adam] Service uninstalled successfully.");
    }

    public async Task<ServiceStatus> GetStatusAsync(CancellationToken ct = default)
    {
        Debug.WriteLine("[adam] MacOsServiceInstaller.GetStatusAsync()");
        if (!IsSupported) return ServiceStatus.NotInstalled;
        try
        {
            var output = await RunBashAsync($"launchctl list {ServiceName}", ct);
            Debug.WriteLine($"[adam] launchctl list output length: {output.Length}");
            if (output.Contains("\"PID\""))
            {
                Debug.WriteLine("[adam] Service is running (PID found).");
                return ServiceStatus.Running;
            }
            var status = File.Exists(PlistPath) ? ServiceStatus.Stopped : ServiceStatus.NotInstalled;
            Debug.WriteLine($"[adam] Resolved service status: {status}");
            return status;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[adam] GetStatusAsync error: {ex.Message}");
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
