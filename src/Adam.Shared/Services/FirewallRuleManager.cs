using System.Diagnostics;

namespace Adam.Shared.Services;

/// <summary>
/// Manages Windows Firewall rules for the Adam Broker service using netsh advfirewall.
/// Only supported on Windows with administrator privileges.
/// </summary>
public static class FirewallRuleManager
{
    private const string RuleName = "Adam Broker Service (TCP)";

    /// <summary>
    /// Adds a Windows Firewall inbound rule allowing TCP traffic on the specified port.
    /// Requires administrator privileges on Windows.
    /// </summary>
    public static async Task AddRuleAsync(int port, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var args = $"advfirewall firewall add rule name=\"{RuleName}\" protocol=TCP dir=in localport={port} action=allow profile=any description=\"Allows the Adam Digital Asset Management Broker Service to receive connections on TCP port {port}.\"";

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = args,
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
                    $"Failed to add firewall rule for port {port}: {message}");
            }
        }
        catch (Exception) when (!OperatingSystem.IsWindows())
        {
            // On non-Windows, netsh won't be available — silently ignore
        }
        catch (Exception ex2)
        {
            // netsh might not be available (e.g., Windows Server Core, Nano, or non-elevated context)
            // Log and degrade gracefully — the service should still install
            Debug.WriteLine($"Failed to add firewall rule: {ex2.Message}");
        }
    }

    /// <summary>
    /// Removes the Windows Firewall inbound rule for the Adam Broker service.
    /// </summary>
    public static async Task RemoveRuleAsync(CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = $"advfirewall firewall delete rule name=\"{RuleName}\"",
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
                    $"Failed to remove firewall rule: {message}");
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception) when (!OperatingSystem.IsWindows())
        {
            // On non-Windows, netsh won't be available — silently ignore
        }
        catch (Exception ex2)
        {
            Debug.WriteLine($"Failed to remove firewall rule: {ex2.Message}");
        }
    }

    /// <summary>
    /// Checks if the firewall rule exists.
    /// </summary>
    public static async Task<bool> RuleExistsAsync(CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = $"advfirewall firewall show rule name=\"{RuleName}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        return output.Contains(RuleName, StringComparison.OrdinalIgnoreCase);
    }
}
