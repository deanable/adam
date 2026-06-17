using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.Shared.Services;

public sealed class WindowsServiceInstaller : IServiceInstaller
{
    private readonly ILogger _logger;
    private readonly ScCommandRunner _scRunner;
    private readonly ElevatedProcessRunner _elevatedRunner;
    private readonly ServiceConfigWriter _configWriter;

    /// <summary>
    /// Path to the executable to launch elevated for privileged operations.
    /// Defaults to <c>Environment.ProcessPath</c>. Set this if the helper
    /// executable is different from the current process.
    /// Pass-through to <see cref="ElevatedProcessRunner.ProcessPath"/>.
    /// </summary>
    public string ProcessPath
    {
        get => _elevatedRunner.ProcessPath;
        set => _elevatedRunner.ProcessPath = value;
    }

    /// <summary>
    /// Testing hook: when set, replaces the elevated process launch.
    /// Pass-through to <see cref="ElevatedProcessRunner.ElevatedProcessHandler"/>.
    /// </summary>
    public Func<string, CancellationToken, Task>? ElevatedProcessHandler
    {
        get => _elevatedRunner.ElevatedProcessHandler;
        set => _elevatedRunner.ElevatedProcessHandler = value;
    }

    public string ServiceName => "AdamBrokerService";
    public bool IsSupported => OperatingSystem.IsWindows();
#pragma warning disable CA1416
    public bool IsElevated => new WindowsPrincipal(WindowsIdentity.GetCurrent())
        .IsInRole(WindowsBuiltInRole.Administrator);
#pragma warning restore CA1416

    /// <summary>
    /// Creates a WindowsServiceInstaller with the provided runner dependencies.
    /// If runners are omitted, they are created with default settings using the given logger.
    /// </summary>
    public WindowsServiceInstaller(
        ScCommandRunner? scRunner = null,
        ElevatedProcessRunner? elevatedRunner = null,
        ServiceConfigWriter? configWriter = null,
        ILogger<WindowsServiceInstaller>? logger = null)
    {
        _logger = logger ?? NullLogger<WindowsServiceInstaller>.Instance;
        _scRunner = scRunner ?? new ScCommandRunner(_logger);
        _elevatedRunner = elevatedRunner ?? new ElevatedProcessRunner(_logger);
        _configWriter = configWriter ?? new ServiceConfigWriter(_logger);
    }

    /// <summary>
    /// Backward-compatible constructor that only accepts a logger.
    /// Runners are created with default settings.
    /// </summary>
    /// <remarks>
    /// This overload exists to support the legacy constructor signature used by tests.
    /// Prefer the full constructor with explicit runner dependencies for DI scenarios.
    /// </remarks>
    public WindowsServiceInstaller(ILogger<WindowsServiceInstaller> logger)
        : this(scRunner: null, elevatedRunner: null, configWriter: null, logger)
    {
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
            _elevatedRunner.LogDiagnosticState();
            await _elevatedRunner.RunElevatedAsync(new ElevatedRequest { Operation = "install", BrokerPath = brokerPath, Port = port }, ct);
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
        var existingStatus = await _scRunner.GetServiceStatusAsync(ServiceName, ct);
        _logger.LogInformation("[TIMING] Existing service status: {Status} (elapsed: {ElapsedMs:F0}ms)", existingStatus, sw.Elapsed.TotalMilliseconds);

        if (existingStatus != ServiceStatus.NotInstalled)
        {
            _logger.LogInformation("Service '{ServiceName}' already exists (status={Status}). Updating configuration...", ServiceName, existingStatus);

            if (existingStatus == ServiceStatus.Running)
            {
                _logger.LogInformation("[TIMING] Stopping running service before update...");
                await _scRunner.RunAsync($"stop {ServiceName}", ct);
            }

            _logger.LogInformation("[TIMING] Updating service config with brokerPath='{BrokerPath}'...", brokerPath);
            await _configWriter.UpdateBrokerPortAsync(brokerPath, port);
            await _scRunner.RunAsync($"config {ServiceName} binPath= \"{brokerPath}\" start=auto", ct);
            _logger.LogInformation("[TIMING] Updating service description...");
            await _scRunner.RunAsync($"description {ServiceName} \"Adam Digital Asset Management Broker Service\"", ct);
        }
        else
        {
            _logger.LogInformation("[TIMING] Creating new service '{ServiceName}' with brokerPath='{BrokerPath}'...", ServiceName, brokerPath);
            await _configWriter.UpdateBrokerPortAsync(brokerPath, port);
            await _scRunner.RunAsync(ScCommandRunner.BuildCreateArguments(ServiceName, brokerPath), ct);
            _logger.LogInformation("[TIMING] Setting service description...");
            await _scRunner.RunAsync($"description {ServiceName} \"Adam Digital Asset Management Broker Service\"", ct);
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
        await _scRunner.RunAsync($"start {ServiceName}", ct);
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
            await _elevatedRunner.RunElevatedAsync(new ElevatedRequest { Operation = "uninstall" }, ct);
            _logger.LogInformation("[TIMING] UninstallAsync via elevation completed in {ElapsedMs:F0}ms", sw.Elapsed.TotalMilliseconds);
            return;
        }

        _logger.LogInformation("[TIMING] Querying current service status (elapsed: {ElapsedMs:F0}ms)...", sw.Elapsed.TotalMilliseconds);
        var status = await _scRunner.GetServiceStatusAsync(ServiceName, ct);
        _logger.LogInformation("[TIMING] Service status: {Status} (elapsed: {ElapsedMs:F0}ms)", status, sw.Elapsed.TotalMilliseconds);

        if (status == ServiceStatus.Running)
        {
            _logger.LogInformation("[TIMING] Stopping service before uninstall...");
            await _scRunner.RunAsync($"stop {ServiceName}", ct);
        }

        _logger.LogInformation("[TIMING] Deleting service '{ServiceName}'...", ServiceName);
        await _scRunner.RunAsync($"delete {ServiceName}", ct);

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
            _elevatedRunner.LogDiagnosticState();
            await _elevatedRunner.RunElevatedAsync(new ElevatedRequest { Operation = "start" }, ct);
            _logger.LogInformation("[TIMING] StartAsync via elevation completed in {ElapsedMs:F0}ms", sw.Elapsed.TotalMilliseconds);
            return;
        }

        // Pre-check: if the service is in a transitional state (e.g. START_PENDING from a hung start),
        // stop it first to reset, then start. sc.exe start fails with error 1056 if already START_PENDING.
        _logger.LogInformation("[TIMING] Pre-checking service status before start (elapsed: {ElapsedMs:F0}ms)...", sw.Elapsed.TotalMilliseconds);
        var preStatus = await _scRunner.GetServiceStatusAsync(ServiceName, ct);
        _logger.LogInformation("[TIMING] Pre-start status: {Status} (elapsed: {ElapsedMs:F0}ms)", preStatus, sw.Elapsed.TotalMilliseconds);

        if (preStatus == ServiceStatus.Unknown)
        {
            _logger.LogWarning("[TIMING] Service in transitional/unknown state ({Status}). Stopping first to reset state, then starting.", preStatus);
            try
            {
                await _scRunner.RunAsync($"stop {ServiceName}", ct);
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
        await _scRunner.RunAsync($"start {ServiceName}", ct);
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
            await _elevatedRunner.RunElevatedAsync(new ElevatedRequest { Operation = "stop" }, ct);
            _logger.LogInformation("[TIMING] StopAsync via elevation completed in {ElapsedMs:F0}ms", sw.Elapsed.TotalMilliseconds);
            return;
        }

        _logger.LogInformation("[TIMING] Stopping service '{ServiceName}' via sc.exe (elapsed so far: {ElapsedMs:F0}ms)...", ServiceName, sw.Elapsed.TotalMilliseconds);
        await _scRunner.RunAsync($"stop {ServiceName}", ct);
        _logger.LogInformation("[TIMING] Service '{ServiceName}' stopped successfully in {ElapsedMs:F0}ms", ServiceName, sw.Elapsed.TotalMilliseconds);
    }

    public async Task<ServiceStatus> GetStatusAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("WindowsServiceInstaller.GetStatusAsync()");
        if (!IsSupported) return ServiceStatus.NotInstalled;
        return await _scRunner.GetServiceStatusAsync(ServiceName, ct);
    }

    /// <summary>
    /// Builds the <c>sc.exe create</c> arguments string for a service installation.
    /// Delegates to <see cref="ScCommandRunner.BuildCreateArguments"/>.
    /// </summary>
    internal static string BuildScCreateArguments(string serviceName, string brokerPath)
        => ScCommandRunner.BuildCreateArguments(serviceName, brokerPath);

    private void EnsureSupported()
    {
        if (!IsSupported)
            throw new PlatformNotSupportedException("Windows Service is only supported on Windows.");
    }

    private void EnsureAbsolutePath(string path)
    {
        if (!Path.IsPathFullyQualified(path))
            throw new ArgumentException("brokerPath must be an absolute path.", nameof(path));
    }
}
