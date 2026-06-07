using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
    public static async Task AddRuleAsync(int port, CancellationToken ct = default, ILogger? logger = null)
    {
        if (!OperatingSystem.IsWindows())
            return;

        logger ??= NullLogger.Instance;

        if (await RuleExistsAsync(ct, logger).ConfigureAwait(false))
        {
            logger.LogInformation("Firewall rule '{RuleName}' already exists. Deleting to ensure correct configuration...", RuleName);
            await RemoveRuleAsync(ct, logger).ConfigureAwait(false);
        }

        var args = $"advfirewall firewall add rule name=\"{RuleName}\" protocol=TCP dir=in localport={port} action=allow profile=any description=\"Allows the Adam Digital Asset Management Broker Service to receive connections on TCP port {port}.\"";

        logger.LogDebug("Running netsh {Args}", args);

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

            // Log terminal output for debugging
            if (!string.IsNullOrWhiteSpace(output))
                logger.LogDebug("netsh stdout: {Output}", output.TrimEnd());
            if (!string.IsNullOrWhiteSpace(error))
                logger.LogWarning("netsh stderr: {Error}", error.TrimEnd());

            if (process.ExitCode != 0)
            {
                var message = string.IsNullOrWhiteSpace(error) ? output.Trim() : error.Trim();
                logger.LogError("netsh add rule failed (exit code {ExitCode}): {Message}", process.ExitCode, message);
                throw new InvalidOperationException(
                    $"Failed to add firewall rule for port {port}: {message}");
            }

            logger.LogDebug("netsh add rule completed successfully (exit code 0)");
        }
        catch (Exception) when (!OperatingSystem.IsWindows())
        {
            // On non-Windows, netsh won't be available — silently ignore
        }
        catch (Exception ex2)
        {
            // netsh might not be available (e.g., Windows Server Core, Nano, or non-elevated context)
            // Log and degrade gracefully — the service should still install
            logger.LogWarning(ex2, "Failed to add firewall rule for port {Port}", port);
        }
    }

    /// <summary>
    /// Removes the Windows Firewall inbound rule for the Adam Broker service.
    /// </summary>
    public static async Task RemoveRuleAsync(CancellationToken ct = default, ILogger? logger = null)
    {
        if (!OperatingSystem.IsWindows())
            return;

        logger ??= NullLogger.Instance;

        var args = $"advfirewall firewall delete rule name=\"{RuleName}\"";
        logger.LogDebug("Running netsh {Args}", args);

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

            // Log terminal output for debugging
            if (!string.IsNullOrWhiteSpace(output))
                logger.LogDebug("netsh stdout: {Output}", output.TrimEnd());
            if (!string.IsNullOrWhiteSpace(error))
                logger.LogWarning("netsh stderr: {Error}", error.TrimEnd());

            if (process.ExitCode != 0)
            {
                var message = string.IsNullOrWhiteSpace(error) ? output.Trim() : error.Trim();
                logger.LogError("netsh delete rule failed (exit code {ExitCode}): {Message}", process.ExitCode, message);
                throw new InvalidOperationException(
                    $"Failed to remove firewall rule: {message}");
            }

            logger.LogDebug("netsh delete rule completed successfully (exit code 0)");
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
            logger.LogWarning(ex2, "Failed to remove firewall rule");
        }
    }

    /// <summary>
    /// Checks if the firewall rule exists.
    /// </summary>
    public static async Task<bool> RuleExistsAsync(CancellationToken ct = default, ILogger? logger = null)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        logger ??= NullLogger.Instance;

        var args = $"advfirewall firewall show rule name=\"{RuleName}\"";
        logger.LogDebug("Running netsh {Args}", args);

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

        // Log terminal output for debugging
        if (!string.IsNullOrWhiteSpace(output))
            logger.LogDebug("netsh stdout: {Output}", output.TrimEnd());
        if (!string.IsNullOrWhiteSpace(error))
            logger.LogWarning("netsh stderr: {Error}", error.TrimEnd());

        logger.LogDebug("netsh show rule completed (exit code {ExitCode})", process.ExitCode);

        return output.Contains(RuleName, StringComparison.OrdinalIgnoreCase);
    }
}
