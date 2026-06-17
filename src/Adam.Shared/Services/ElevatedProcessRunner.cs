using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Adam.Shared.Services;

/// <summary>
/// Manages elevation to administrator privileges via a UAC helper process.
/// Serializes requests to temp JSON files, launches the helper process with <c>runas</c>,
/// and reads back the result. Supports configurable timeout, hang detection, and testing hooks.
/// </summary>
public sealed class ElevatedProcessRunner
{
    private readonly ILogger _logger;

    /// <summary>
    /// Path to the executable to launch elevated for privileged operations.
    /// Defaults to <c>Environment.ProcessPath</c>. Set this if the helper
    /// executable is different from the current process.
    /// </summary>
    public string ProcessPath { get; set; } = Environment.ProcessPath ?? string.Empty;

    /// <summary>
    /// Testing hook: when set, replaces the elevated process launch.
    /// The delegate receives the request file path and a cancellation token.
    /// It should write an <see cref="ElevatedResponse"/> JSON to that file before returning.
    /// </summary>
    public Func<string, CancellationToken, Task>? ElevatedProcessHandler { get; set; }

    /// <summary>
    /// Timeout in milliseconds for the elevated process to complete. Default 60 seconds.
    /// </summary>
    public int ElevatedTimeoutMs { get; set; } = 60_000;

    public ElevatedProcessRunner(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Serializes the elevated request to a temp file, launches the helper process
    /// via UAC (<c>runas</c>), waits for it to complete, and reads the result.
    /// </summary>
    public async Task RunElevatedAsync(ElevatedRequest request, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var tempDir = Path.GetTempPath();
        var requestFile = Path.Combine(tempDir, $"adam-elevated-{Guid.NewGuid():N}.json");
        var logFile = Path.Combine(tempDir, $"adam-elevated-{Path.GetFileNameWithoutExtension(requestFile)}.log");

        _logger.LogInformation("[TIMING] RunElevatedAsync: operation={Operation}, requestFile={RequestFile}, logFile={LogFile}",
            request.Operation, requestFile, logFile);
        _logger.LogInformation("[DIAG] ProcessPath={ProcessPath}, ElevatedProcessHandler={HandlerSet}",
            ProcessPath, ElevatedProcessHandler != null ? "set" : "null");
        _logger.LogInformation("[DIAG] TempDir={TempDir}, Request: Operation={Op}, BrokerPath={Bp}, Port={Port}",
            tempDir, request.Operation, request.BrokerPath ?? "(null)", request.Port);

        try
        {
            // Serialize request to temp file
            var json = JsonSerializer.Serialize(request);
            await File.WriteAllTextAsync(requestFile, json, ct);
            _logger.LogInformation("[TIMING] Request file written in {ElapsedMs:F0}ms ({Size} bytes)",
                sw.Elapsed.TotalMilliseconds, json.Length);

            _logger.LogInformation("Launching elevated helper: {ProcessPath} --elevated {RequestFile}", ProcessPath, requestFile);

            if (ElevatedProcessHandler != null)
            {
                _logger.LogDebug("ElevatedProcessHandler is set — delegating instead of launching process.");
                await ElevatedProcessHandler(requestFile, ct);
                _logger.LogInformation("[TIMING] ElevatedProcessHandler completed in {ElapsedMs:F0}ms", sw.Elapsed.TotalMilliseconds);
            }
            else
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = ProcessPath,
                        Arguments = $"--elevated \"{requestFile}\" --log \"{logFile}\"",
                        UseShellExecute = true,
                        Verb = "runas",
                        WindowStyle = ProcessWindowStyle.Hidden
                    }
                };

                try
                {
                    process.Start();
                }
                catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
                {
                    _logger.LogWarning("UAC prompt was cancelled by the user.");
                    throw new OperationCanceledException("Elevation was cancelled by the user.", ex);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to start elevated process");
                    throw;
                }

                var pid = process.Id;
                _logger.LogInformation("[TIMING] Elevated process started with PID={Pid} after {ElapsedMs:F0}ms — now waiting for exit (timeout={TimeoutMs}ms)...",
                    pid, sw.Elapsed.TotalMilliseconds, ElevatedTimeoutMs);

                var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(ElevatedTimeoutMs);

                var warningTask = WarnIfSlowAsync(pid, sw, timeoutCts.Token);

                try
                {
                    await process.WaitForExitAsync(timeoutCts.Token);
                    _logger.LogInformation("[TIMING] Elevated process (PID={Pid}) exited with code={ExitCode} after {ElapsedMs:F0}ms",
                        pid, process.ExitCode, sw.Elapsed.TotalMilliseconds);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    var elapsed = sw.Elapsed;
                    _logger.LogCritical("[TIMEOUT] Elevated process (PID={Pid}) did not exit within {TimeoutMs}ms (elapsed={ElapsedMs:F0}ms). Killing process.",
                        pid, ElevatedTimeoutMs, elapsed.TotalMilliseconds);

                    TryReadElevatedLog(logFile);

                    try
                    {
                        process.Kill(entireProcessTree: true);
                        var killSw = Stopwatch.StartNew();
                        if (!process.WaitForExit(5000))
                            _logger.LogError("[TIMEOUT] Failed to kill elevated process (PID={Pid}) within 5s", pid);
                        else
                            _logger.LogInformation("[TIMEOUT] Elevated process (PID={Pid}) killed in {KillMs:F0}ms", pid, killSw.Elapsed.TotalMilliseconds);
                    }
                    catch (Exception killEx)
                    {
                        _logger.LogError(killEx, "[TIMEOUT] Exception while killing elevated process (PID={Pid})", pid);
                    }

                    var timeoutMsg = $"Elevated operation '{request.Operation}' timed out after {elapsed.TotalSeconds:F0}s. " +
                        $"The elevated helper process (PID={pid}) did not respond within the {ElevatedTimeoutMs / 1000}s timeout. " +
                        "This may indicate the UAC prompt was dismissed, the helper process crashed, or a sc.exe/netsh command hung.";

                    _logger.LogCritical("[TIMEOUT] {Message}", timeoutMsg);
                    throw new TimeoutException(timeoutMsg);
                }
                finally
                {
                    await timeoutCts.CancelAsync();
                    timeoutCts.Dispose();
                }

                // Read the elevated log file if it was written
                if (File.Exists(logFile))
                {
                    try
                    {
                        var logContent = await File.ReadAllTextAsync(logFile, ct);
                        _logger.LogInformation("[ELEVATED LOG] === Begin elevated process log ({Len} chars) ===", logContent.Length);
                        foreach (var line in logContent.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                            _logger.LogInformation("[ELEVATED] {Line}", line.TrimEnd());
                        _logger.LogInformation("[ELEVATED LOG] === End elevated process log ===");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to read elevated log file: {LogFile}", logFile);
                    }

                    try { File.Delete(logFile); } catch { /* best-effort */ }
                }
                else
                {
                    _logger.LogWarning("Elevated process did not produce a log file at {LogFile}", logFile);
                }
            }

            // Read result from temp file
            if (!File.Exists(requestFile))
            {
                var msg = "Elevated helper process did not produce a result file.";
                _logger.LogError(msg);
                throw new InvalidOperationException(msg);
            }

            _logger.LogInformation("[TIMING] Reading result file at {ElapsedMs:F0}ms...", sw.Elapsed.TotalMilliseconds);
            var resultJson = await File.ReadAllTextAsync(requestFile, ct);
            _logger.LogInformation("[DIAG] Result file contents ({Len} bytes): {Json}", resultJson.Length, resultJson);
            var result = JsonSerializer.Deserialize<ElevatedResponse>(resultJson);

            if (result == null)
            {
                var msg = "Elevated helper process returned an unparseable result.";
                _logger.LogError(msg);
                throw new InvalidOperationException(msg);
            }

            if (!result.Success)
            {
                var errorMsg = result.ErrorMessage ?? "Elevated operation failed with no error message.";
                _logger.LogError("Elevated operation failed: {Error}", errorMsg);
                throw new InvalidOperationException(errorMsg);
            }

            _logger.LogInformation("[TIMING] Elevated operation '{Operation}' completed successfully in {ElapsedMs:F0}ms",
                request.Operation, sw.Elapsed.TotalMilliseconds);
        }
        finally
        {
            try { if (File.Exists(requestFile)) File.Delete(requestFile); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Logs diagnostic state information before elevation to help debug issues.
    /// </summary>
    public void LogDiagnosticState()
    {
        try
        {
            _logger.LogInformation("[DIAG] ProcessPath exists: {Exists}, CWD: {Cwd}",
                File.Exists(ProcessPath), Environment.CurrentDirectory);
            _logger.LogInformation("[DIAG] OS: {Os}, User: {User}, Domain: {Domain}, Interactive: {Interactive}",
                Environment.OSVersion, Environment.UserName, Environment.UserDomainName, Environment.UserInteractive);
            _logger.LogInformation("[DIAG] CLR: {RuntimeVersion}, Process: {ProcessName} (PID={Pid})",
                Environment.Version, Process.GetCurrentProcess().ProcessName,
                Environment.ProcessId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DIAG] Failed to capture diagnostic state");
        }
    }

    /// <summary>
    /// Logs a warning at each threshold if the process is still running.
    /// Helps detect hangs before a hard timeout fires.
    /// </summary>
    private async Task WarnIfSlowAsync(int pid, Stopwatch sw, CancellationToken ct, int[]? thresholds = null)
    {
        thresholds ??= [10_000, 30_000, 60_000, 120_000];
        foreach (var thresholdMs in thresholds)
        {
            try
            {
                await Task.Delay(thresholdMs, ct);
                if (ct.IsCancellationRequested) break;
                _logger.LogWarning("[HANG WARNING] Process (PID={Pid}) still running after {ElapsedMs:F0}ms — threshold={ThresholdMs}ms",
                    pid, sw.Elapsed.TotalMilliseconds, thresholdMs);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Best-effort read of the elevated log file. Called when the process times out
    /// so we can capture any diagnostics it managed to write.
    /// </summary>
    private void TryReadElevatedLog(string logFile)
    {
        if (!File.Exists(logFile)) return;
        try
        {
            var logContent = File.ReadAllText(logFile);
            _logger.LogInformation("[ELEVATED LOG (before kill)] === Begin ({Len} chars) ===", logContent.Length);
            foreach (var line in logContent.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                _logger.LogInformation("[ELEVATED (before kill)] {Line}", line.TrimEnd());
            _logger.LogInformation("[ELEVATED LOG (before kill)] === End ===");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read elevated log file on timeout: {LogFile}", logFile);
        }
    }
}
