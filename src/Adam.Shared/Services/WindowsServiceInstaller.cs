using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.Shared.Services;

/// <summary>
/// Defines the elevated operation request sent to the helper process via a temp JSON file.
/// </summary>
public sealed record ElevatedRequest
{
    public string Operation { get; init; } = string.Empty; // "install", "uninstall", "start", "stop"
    public string? BrokerPath { get; init; }
    public int Port { get; init; }
}

/// <summary>
/// Defines the result written back by the elevated helper process.
/// </summary>
public sealed record ElevatedResponse
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class WindowsServiceInstaller : IServiceInstaller
{
    private readonly ILogger _logger;

    /// <summary>
    /// Path to the executable to launch elevated for privileged operations.
    /// Defaults to <c>Environment.ProcessPath</c>. Set this if the helper
    /// executable is different from the current process.
    /// </summary>
    public string ProcessPath { get; set; } = Environment.ProcessPath ?? string.Empty;

    /// <summary>
    /// Testing hook: when set, replaces the elevated process launch in <see cref="RunElevatedAsync"/>.
    /// The delegate receives the request file path and a cancellation token.
    /// It should write an <see cref="ElevatedResponse"/> JSON to that file before returning.
    /// </summary>
    public Func<string, CancellationToken, Task>? ElevatedProcessHandler { get; set; }

    public string ServiceName => "AdamBrokerService";
    public bool IsSupported => OperatingSystem.IsWindows();
#pragma warning disable CA1416
    public bool IsElevated => new WindowsPrincipal(WindowsIdentity.GetCurrent())
        .IsInRole(WindowsBuiltInRole.Administrator);
#pragma warning restore CA1416

    public WindowsServiceInstaller(ILogger<WindowsServiceInstaller>? logger = null)
    {
        _logger = logger ?? NullLogger<WindowsServiceInstaller>.Instance;
    }

    public async Task InstallAsync(string brokerPath, int port, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("[TIMING] WindowsServiceInstaller.InstallAsync(brokerPath='{BrokerPath}', port={Port}) — entering at {Timestamp:O}", brokerPath, port, DateTime.UtcNow);

        EnsureSupported();
        EnsureAbsolutePath(brokerPath);

        if (!IsElevated)
        {
            _logger.LogInformation("Not elevated (IsElevated={IsElevated}) — launching helper process via UAC...", IsElevated);
            LogDiagnosticState();
            await RunElevatedAsync(new ElevatedRequest { Operation = "install", BrokerPath = brokerPath, Port = port }, ct);
            _logger.LogInformation("[TIMING] InstallAsync via elevation completed in {ElapsedMs:F0}ms", sw.Elapsed.TotalMilliseconds);
            return;
        }

        _logger.LogInformation("[TIMING] Checking port {Port} availability...", port);
        var portFree = PortChecker.IsPortFree(port);
        _logger.LogInformation("[TIMING] Port check completed in {ElapsedMs:F0}ms: portFree={PortFree}", sw.Elapsed.TotalMilliseconds, portFree);
        if (!portFree)
        {
            var freePort = PortChecker.FindFreePort(port);
            var msg = freePort > 0
                ? $"Port {port} is already in use. Port {freePort} is available. Please update the port setting and try again."
                : $"Port {port} is already in use and no alternative ports are available in range.";
            _logger.LogWarning("Port check failed: {Message}", msg);
            throw new InvalidOperationException(msg);
        }

        // Check if service already exists — if so, update it instead of recreating
        _logger.LogInformation("[TIMING] Checking if service '{ServiceName}' already exists (elapsed: {ElapsedMs:F0}ms)...", ServiceName, sw.Elapsed.TotalMilliseconds);
        var existingStatus = await GetStatusInternalAsync(ct);
        _logger.LogInformation("[TIMING] Existing service status: {Status} (elapsed: {ElapsedMs:F0}ms)", existingStatus, sw.Elapsed.TotalMilliseconds);

        if (existingStatus != ServiceStatus.NotInstalled)
        {
            _logger.LogInformation("Service '{ServiceName}' already exists (status={Status}). Updating configuration...", ServiceName, existingStatus);

            if (existingStatus == ServiceStatus.Running)
            {
                _logger.LogInformation("[TIMING] Stopping running service before update...");
                await RunScAsync($"stop {ServiceName}", ct);
            }

            _logger.LogInformation("[TIMING] Updating service config with brokerPath='{BrokerPath}'...", brokerPath);
            await UpdateBrokerConfigAsync(brokerPath, port);
            await RunScAsync($"config {ServiceName} binPath= \"{brokerPath}\" start=auto", ct);
            _logger.LogInformation("[TIMING] Updating service description...");
            await RunScAsync($"description {ServiceName} \"Adam Digital Asset Management Broker Service\"", ct);
        }
        else
        {
            _logger.LogInformation("[TIMING] Creating new service '{ServiceName}' with brokerPath='{BrokerPath}'...", ServiceName, brokerPath);
            await UpdateBrokerConfigAsync(brokerPath, port);
            await RunScAsync(BuildScCreateArguments(ServiceName, brokerPath), ct);
            _logger.LogInformation("[TIMING] Setting service description...");
            await RunScAsync($"description {ServiceName} \"Adam Digital Asset Management Broker Service\"", ct);
        }

        _logger.LogInformation("[TIMING] Adding Windows Firewall rule for port {Port} (elapsed: {ElapsedMs:F0}ms)...", port, sw.Elapsed.TotalMilliseconds);
        try
        {
            await FirewallRuleManager.AddRuleAsync(port, ct, _logger);
            _logger.LogInformation("[TIMING] Firewall rule added successfully.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not add firewall rule for port {Port}", port);
        }

        _logger.LogInformation("[TIMING] Starting service '{ServiceName}' via sc.exe (elapsed: {ElapsedMs:F0}ms)...", ServiceName, sw.Elapsed.TotalMilliseconds);
        await RunScAsync($"start {ServiceName}", ct);
        _logger.LogInformation("[TIMING] Service '{ServiceName}' installed and started successfully in {ElapsedMs:F0}ms", ServiceName, sw.Elapsed.TotalMilliseconds);
    }

    public async Task UninstallAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("[TIMING] WindowsServiceInstaller.UninstallAsync() — entering at {Timestamp:O}", DateTime.UtcNow);

        EnsureSupported();

        if (!IsElevated)
        {
            _logger.LogInformation("Not elevated (IsElevated={IsElevated}) — launching helper process via UAC...", IsElevated);
            await RunElevatedAsync(new ElevatedRequest { Operation = "uninstall" }, ct);
            _logger.LogInformation("[TIMING] UninstallAsync via elevation completed in {ElapsedMs:F0}ms", sw.Elapsed.TotalMilliseconds);
            return;
        }

        _logger.LogInformation("[TIMING] Querying current service status (elapsed: {ElapsedMs:F0}ms)...", sw.Elapsed.TotalMilliseconds);
        var status = await GetStatusInternalAsync(ct);
        _logger.LogInformation("[TIMING] Service status: {Status} (elapsed: {ElapsedMs:F0}ms)", status, sw.Elapsed.TotalMilliseconds);

        if (status == ServiceStatus.Running)
        {
            _logger.LogInformation("[TIMING] Stopping service before uninstall...");
            await RunScAsync($"stop {ServiceName}", ct);
        }

        _logger.LogInformation("[TIMING] Deleting service '{ServiceName}'...", ServiceName);
        await RunScAsync($"delete {ServiceName}", ct);

        _logger.LogInformation("[TIMING] Removing Windows Firewall rule (elapsed: {ElapsedMs:F0}ms)...", sw.Elapsed.TotalMilliseconds);
        try
        {
            await FirewallRuleManager.RemoveRuleAsync(ct, _logger);
            _logger.LogInformation("[TIMING] Firewall rule removed successfully.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not remove firewall rule");
        }

        _logger.LogInformation("[TIMING] Service '{ServiceName}' uninstalled successfully in {ElapsedMs:F0}ms", ServiceName, sw.Elapsed.TotalMilliseconds);
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("[TIMING] WindowsServiceInstaller.StartAsync() — entering at {Timestamp:O}", DateTime.UtcNow);
        EnsureSupported();

        if (!IsElevated)
        {
            _logger.LogInformation("Not elevated (IsElevated={IsElevated}) — launching helper process via UAC...", IsElevated);
            LogDiagnosticState();
            await RunElevatedAsync(new ElevatedRequest { Operation = "start" }, ct);
            _logger.LogInformation("[TIMING] StartAsync via elevation completed in {ElapsedMs:F0}ms", sw.Elapsed.TotalMilliseconds);
            return;
        }

        // Pre-check: if the service is in a transitional state (e.g. START_PENDING from a hung start),
        // stop it first to reset, then start. sc.exe start fails with error 1056 if already START_PENDING.
        _logger.LogInformation("[TIMING] Pre-checking service status before start (elapsed: {ElapsedMs:F0}ms)...", sw.Elapsed.TotalMilliseconds);
        var preStatus = await GetStatusInternalAsync(ct);
        _logger.LogInformation("[TIMING] Pre-start status: {Status} (elapsed: {ElapsedMs:F0}ms)", preStatus, sw.Elapsed.TotalMilliseconds);

        if (preStatus == ServiceStatus.Unknown)
        {
            _logger.LogWarning("[TIMING] Service in transitional/unknown state ({Status}). Stopping first to reset state, then starting.", preStatus);
            try
            {
                await RunScAsync($"stop {ServiceName}", ct);
                _logger.LogInformation("[TIMING] Stop (pre-start reset) completed in {ElapsedMs:F0}ms", sw.Elapsed.TotalMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[TIMING] Stop (pre-start reset) failed — proceeding with start anyway");
            }
        }
        else if (preStatus == ServiceStatus.Running)
        {
            _logger.LogInformation("[TIMING] Service is already running — nothing to do.");
            return;
        }

        _logger.LogInformation("[TIMING] Starting service '{ServiceName}' via sc.exe (elapsed so far: {ElapsedMs:F0}ms)...", ServiceName, sw.Elapsed.TotalMilliseconds);
        await RunScAsync($"start {ServiceName}", ct);
        _logger.LogInformation("[TIMING] Service '{ServiceName}' started successfully in {ElapsedMs:F0}ms", ServiceName, sw.Elapsed.TotalMilliseconds);
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("[TIMING] WindowsServiceInstaller.StopAsync() — entering at {Timestamp:O}", DateTime.UtcNow);
        EnsureSupported();

        if (!IsElevated)
        {
            _logger.LogInformation("Not elevated (IsElevated={IsElevated}) — launching helper process via UAC...", IsElevated);
            await RunElevatedAsync(new ElevatedRequest { Operation = "stop" }, ct);
            _logger.LogInformation("[TIMING] StopAsync via elevation completed in {ElapsedMs:F0}ms", sw.Elapsed.TotalMilliseconds);
            return;
        }

        _logger.LogInformation("[TIMING] Stopping service '{ServiceName}' via sc.exe (elapsed so far: {ElapsedMs:F0}ms)...", ServiceName, sw.Elapsed.TotalMilliseconds);
        await RunScAsync($"stop {ServiceName}", ct);
        _logger.LogInformation("[TIMING] Service '{ServiceName}' stopped successfully in {ElapsedMs:F0}ms", ServiceName, sw.Elapsed.TotalMilliseconds);
    }

    public async Task<ServiceStatus> GetStatusAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("WindowsServiceInstaller.GetStatusAsync()");
        if (!IsSupported) return ServiceStatus.NotInstalled;
        return await GetStatusInternalAsync(ct);
    }

    /// <summary>
    /// Serializes the elevated request to a temp file, launches the helper process
    /// via UAC (<c>runas</c>), waits for it to complete, and reads the result.
    /// </summary>
    private async Task RunElevatedAsync(ElevatedRequest request, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var tempDir = Path.GetTempPath();
        var requestFile = Path.Combine(tempDir, $"adam-elevated-{Guid.NewGuid():N}.json");
        var logFile = Path.Combine(tempDir, $"adam-elevated-{Path.GetFileNameWithoutExtension(requestFile)}.log");

        _logger.LogInformation("[TIMING] RunElevatedAsync: operation={Operation}, requestFile={RequestFile}, logFile={LogFile}",
            request.Operation, requestFile, logFile);
        _logger.LogInformation("[DIAG] ProcessPath={ProcessPath}, IsElevated={IsElevated}, ElevatedProcessHandler={HandlerSet}",
            ProcessPath, IsElevated, ElevatedProcessHandler != null ? "set" : "null");
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
                const int elevatedTimeoutMs = 60_000; // 1 minute — user selected

                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = ProcessPath,
                        Arguments = $"--elevated \"{requestFile}\" --log \"{logFile}\"",
                        UseShellExecute = true,
                        Verb = "runas",
                        // Note: with UseShellExecute = true, we cannot redirect output;
                        // results are communicated back via the temp file.
                        WindowStyle = ProcessWindowStyle.Hidden
                    }
                };

                try
                {
                    process.Start();
                }
                catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED
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
                    pid, sw.Elapsed.TotalMilliseconds, elevatedTimeoutMs);

                // Create a timeout CTS linked to the caller's token
                var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(elevatedTimeoutMs);

                // Log warnings at 20s and 40s (before the 60s timeout)
                var warningTask = WarnIfSlowAsync(pid, sw, timeoutCts.Token, [20_000, 40_000]);

                try
                {
                    await process.WaitForExitAsync(timeoutCts.Token);
                    _logger.LogInformation("[TIMING] Elevated process (PID={Pid}) exited with code={ExitCode} after {ElapsedMs:F0}ms",
                        pid, process.ExitCode, sw.Elapsed.TotalMilliseconds);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // Timeout expired — the user didn't cancel, the timeout triggered
                    var elapsed = sw.Elapsed;
                    _logger.LogCritical("[TIMEOUT] Elevated process (PID={Pid}) did not exit within {TimeoutMs}ms (elapsed={ElapsedMs:F0}ms). Killing process.",
                        pid, elevatedTimeoutMs, elapsed.TotalMilliseconds);

                    // Try to read whatever the elevated log has so far
                    TryReadElevatedLog(logFile);

                    // Kill the hung process
                    try
                    {
                        process.Kill(entireProcessTree: true);
                        var killSw = Stopwatch.StartNew();
                        if (!process.WaitForExit(5000))
                        {
                            _logger.LogError("[TIMEOUT] Failed to kill elevated process (PID={Pid}) within 5s", pid);
                        }
                        else
                        {
                            _logger.LogInformation("[TIMEOUT] Elevated process (PID={Pid}) killed in {KillMs:F0}ms", pid, killSw.Elapsed.TotalMilliseconds);
                        }
                    }
                    catch (Exception killEx)
                    {
                        _logger.LogError(killEx, "[TIMEOUT] Exception while killing elevated process (PID={Pid})", pid);
                    }

                    var timeoutMsg = $"Elevated operation '{request.Operation}' timed out after {elapsed.TotalSeconds:F0}s. " +
                        $"The elevated helper process (PID={pid}) did not respond within the {elevatedTimeoutMs / 1000}s timeout. " +
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

                    // Clean up log file
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
            // Clean up temp file
            try { if (File.Exists(requestFile)) File.Delete(requestFile); }
            catch { /* best-effort cleanup */ }
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

    private async Task<ServiceStatus> GetStatusInternalAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            _logger.LogInformation("[TIMING] Querying service '{ServiceName}' status via sc.exe...", ServiceName);
            var output = await RunScRawAsync($"query {ServiceName}", ct);
            _logger.LogInformation("[DIAG] Raw sc.exe query output:\n{Output}", output.TrimEnd());
            var status = ParseScQuery(output);
            _logger.LogInformation("[TIMING] Service status queried in {ElapsedMs:F0}ms: {Status}", sw.Elapsed.TotalMilliseconds, status);
            return status;
        }
        catch (Exception ex) when (ex is not PlatformNotSupportedException && ex is not UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "GetStatusInternalAsync: service not found or not accessible (elapsed={ElapsedMs:F0}ms)", sw.Elapsed.TotalMilliseconds);
            return ServiceStatus.NotInstalled;
        }
    }

    private ServiceStatus ParseScQuery(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            if (!line.Contains("STATE", StringComparison.OrdinalIgnoreCase)) continue;
            var upper = line.ToUpperInvariant();
            if (upper.Contains("RUNNING")) return ServiceStatus.Running;
            if (upper.Contains("STOPPED")) return ServiceStatus.Stopped;
            if (upper.Contains("STOP_PENDING")) return ServiceStatus.Running;
            if (upper.Contains("START_PENDING")) return ServiceStatus.Unknown;
            return ServiceStatus.Unknown;
        }
        return ServiceStatus.NotInstalled;
    }

    private async Task RunScAsync(string arguments, CancellationToken ct)
    {
        await RunScRawAsync(arguments, ct);
    }

    private async Task<string> RunScRawAsync(string arguments, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("[TIMING] Running: sc.exe {Arguments}", arguments);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var pid = process.Id;
        _logger.LogInformation("[TIMING] sc.exe started (PID={Pid}) at {Timestamp:O}", pid, DateTime.UtcNow);

        // Use a timeout-aware approach: warn if sc.exe takes >15 seconds
        var scTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var scWarningTask = WarnIfSlowAsync(pid, sw, scTimeoutCts.Token);

        try
        {
            var output = await process.StandardOutput.ReadToEndAsync(ct);
            var error = await process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            _logger.LogInformation("[TIMING] sc.exe (PID={Pid}) exited (code={ExitCode}) in {ElapsedMs:F0}ms",
                pid, process.ExitCode, sw.Elapsed.TotalMilliseconds);

            // Log the terminal output for debugging purposes
            if (!string.IsNullOrWhiteSpace(output))
                _logger.LogInformation("[DIAG] sc.exe stdout ({Len} chars): {Output}", output.Length, output.TrimEnd());
            if (!string.IsNullOrWhiteSpace(error))
                _logger.LogWarning("[DIAG] sc.exe stderr ({Len} chars): {Error}", error.Length, error.TrimEnd());

            if (process.ExitCode != 0)
            {
                var message = string.IsNullOrWhiteSpace(error) ? output.Trim() : error.Trim();
                _logger.LogError("sc.exe {Arguments} FAILED (exit code {ExitCode}, elapsed={ElapsedMs:F0}ms): {Message}",
                    arguments, process.ExitCode, sw.Elapsed.TotalMilliseconds, message);
                throw new InvalidOperationException(
                    $"sc.exe {arguments} failed (exit code {process.ExitCode}): {message}");
            }

            _logger.LogInformation("[TIMING] sc.exe {Arguments} completed OK (PID={Pid}, elapsed={ElapsedMs:F0}ms)",
                arguments, pid, sw.Elapsed.TotalMilliseconds);
            return output;
        }
        finally
        {
            await scTimeoutCts.CancelAsync();
            scTimeoutCts.Dispose();
        }
    }

    /// <summary>
    /// Updates the <c>appsettings.json</c> file alongside the broker executable with the
    /// configured port, so the broker reads the correct port from configuration rather than
    /// relying on command-line arguments (which confuse <c>sc.exe</c>'s parser).
    /// </summary>
    private async Task UpdateBrokerConfigAsync(string brokerPath, int port)
    {
        var brokerDir = Path.GetDirectoryName(brokerPath);
        if (string.IsNullOrEmpty(brokerDir)) return;

        var configPath = Path.Combine(brokerDir, "appsettings.json");
        if (!File.Exists(configPath))
        {
            _logger.LogWarning("appsettings.json not found at {ConfigPath} — skipping port configuration.", configPath);
            return;
        }

        try
        {
            _logger.LogInformation("[TIMING] Updating broker port to {Port} in {ConfigPath}...", port, configPath);
            var json = await File.ReadAllTextAsync(configPath);
            var node = System.Text.Json.Nodes.JsonNode.Parse(json);
            if (node is System.Text.Json.Nodes.JsonObject root &&
                root["Broker"] is System.Text.Json.Nodes.JsonObject broker)
            {
                broker["Port"] = port;
                var opts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                await File.WriteAllTextAsync(configPath, root.ToJsonString(opts));
                _logger.LogInformation("[TIMING] Broker port updated to {Port} in appsettings.json.", port);
            }
            else
            {
                _logger.LogWarning("Could not find 'Broker:Port' in appsettings.json — config structure may differ.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update appsettings.json — continuing anyway (broker will use default port).");
        }
    }

    /// <summary>
    /// Logs diagnostic state information before elevation to help debug issues.
    /// </summary>
    private void LogDiagnosticState()
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

    private void EnsureSupported()
    {
        if (!IsSupported)
            throw new PlatformNotSupportedException("Windows Service is only supported on Windows.");
    }

    /// <summary>
    /// Builds the <c>sc.exe create</c> arguments string for a service installation.
    /// The binPath uses only the exe path (no additional arguments) because:
    /// 1. <c>sc.exe</c>'s parser doesn't understand quoted values with embedded <c>=</c> signs
    /// 2. Service configuration (port, etc.) is configured via <c>appsettings.json</c>
    ///    alongside the broker executable during installation.
    /// Format: <c>create &lt;serviceName&gt; binPath= "&lt;brokerPath&gt;" start=auto</c>
    /// </summary>
    internal static string BuildScCreateArguments(string serviceName, string brokerPath)
    {
        // Quote the path if it contains spaces so sc.exe sees it as one argument
        var pathArg = brokerPath.Contains(' ') ? $"\"{brokerPath}\"" : brokerPath;
        return $"create {serviceName} binPath= {pathArg} start=auto";
    }

    private void EnsureAbsolutePath(string path)
    {
        if (!Path.IsPathFullyQualified(path))
            throw new ArgumentException("brokerPath must be an absolute path.", nameof(path));
    }
}
