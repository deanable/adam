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

    public Task InstallAsync(string brokerPath, CancellationToken ct = default)
    {
        if (!IsSupported) throw new PlatformNotSupportedException("launchd is only supported on macOS.");

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
        return RunBashAsync($"launchctl load {PlistPath}", ct);
    }

    public Task UninstallAsync(CancellationToken ct = default)
    {
        if (!IsSupported) throw new PlatformNotSupportedException("launchd is only supported on macOS.");
        return RunBashAsync($"launchctl unload {PlistPath} && rm -f {PlistPath}", ct);
    }

    public async Task<ServiceStatus> GetStatusAsync(CancellationToken ct = default)
    {
        if (!IsSupported) return ServiceStatus.NotInstalled;
        try
        {
            var output = await RunBashAsync($"launchctl list {ServiceName}", ct);
            if (output.Contains("PID")) return ServiceStatus.Running;
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
                Arguments = $"-c \"{command.Replace("\"", "\\\"")}\"",
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
