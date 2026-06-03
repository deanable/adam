using System.Diagnostics;

namespace Adam.BrokerService.Tests.Infrastructure;

/// <summary>
/// Helper to determine whether Docker is available in the current test environment.
/// Used to conditionally skip Testcontainers-based integration tests when Docker
/// is not installed or the daemon is not running.
/// </summary>
internal static class DockerAvailability
{
    private static readonly Lazy<bool> _isAvailable = new(CheckDocker);

    /// <summary>
    /// True if the Docker CLI responds successfully within a short timeout.
    /// </summary>
    public static bool IsAvailable => _isAvailable.Value;

    private static bool CheckDocker()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "docker.exe" : "docker",
                Arguments = "info --format {{.ServerVersion}}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            var sw = Stopwatch.StartNew();
            process.Start();

            if (!process.WaitForExit(5000))
            {
                process.Kill(entireProcessTree: true);
                return false;
            }

            sw.Stop();
            return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(process.StandardOutput.ReadToEnd());
        }
        catch
        {
            return false;
        }
    }
}
