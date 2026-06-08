using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Adam.ServiceManager.Services;
using Adam.Shared.Configuration;
using Adam.Shared.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.ServiceManager.ViewModels;

public enum ServiceHealth
{
    Red,    // Not installed, stopped, or unknown
    Amber,  // Starting up / transitional
    Green   // Running
}

public class ServiceManagerViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ILogger<ServiceManagerViewModel> _logger;
    private readonly IServiceInstaller _serviceInstaller;
    private string _statusMessage = string.Empty;
    private volatile bool _isBusy;
    private string _serviceStatusText = "Unknown";
    private bool _isServiceInstalled;
    private bool _isServiceRunning;
    private readonly bool _isElevated;
    private string _serviceHost = "localhost";
    private int _servicePort = 9100;
    private bool _useTls;
    private bool _allowSelfSigned = true;
    private ObservableCollection<string> _logMessages = new();
    private readonly ObservableCollection<string> _serviceLogMessages;
    private Timer? _pollTimer;
    private object? _currentView;
    private bool _isServiceTabSelected = true;
    private bool _disposed;
    private int _pollingIntervalSeconds;
    private readonly IUiDispatcher _dispatcher;

    public ServiceManagerViewModel(IEnumerable<IServiceInstaller> serviceInstallers, ILogger<ServiceManagerViewModel>? logger = null, ObservableCollection<string>? serviceLogMessages = null, UserManagementViewModel? userManagement = null, AuditLogViewModel? auditLog = null, IUiDispatcher? dispatcher = null)
    {
        _logger = logger ?? NullLogger<ServiceManagerViewModel>.Instance;
        _dispatcher = dispatcher ?? new AvaloniaUiDispatcher();
        var installers = serviceInstallers.ToList();
        _serviceInstaller = installers.FirstOrDefault(s => s.IsSupported) ?? new NullServiceInstaller();
        _logger.LogInformation("ServiceManagerViewModel: Selected service installer = {InstallerType} (IsSupported={IsSupported}, ServiceName='{ServiceName}')",
            _serviceInstaller.GetType().Name, _serviceInstaller.IsSupported, _serviceInstaller.ServiceName);

        UserManagement = userManagement ?? throw new ArgumentNullException(nameof(userManagement));
        AuditLog = auditLog ?? throw new ArgumentNullException(nameof(auditLog));
        _currentView = this; // Start with service view

        // Detect elevation state
        _isElevated = !OperatingSystem.IsWindows() ||
            new System.Security.Principal.WindowsPrincipal(
                System.Security.Principal.WindowsIdentity.GetCurrent())
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);

        // Load persisted settings
        var config = App.Config;
        _serviceHost = config.ServiceHost;
        _servicePort = config.ServicePort;
        _useTls = config.UseTls;
        _allowSelfSigned = config.AllowSelfSigned;
        _pollingIntervalSeconds = Math.Clamp(config.PollingIntervalSeconds, 1, 300);

        // Default the host to this server's routable IP so clients get an address
        // they can actually reach, rather than "localhost" (which only works on this
        // machine). Only override when nothing meaningful has been configured yet.
        if (string.IsNullOrWhiteSpace(_serviceHost) || IsLocalHost(_serviceHost))
        {
            var ip = NetworkInfo.GetPrimaryIPv4Address();
            if (!string.IsNullOrEmpty(ip))
                _serviceHost = ip;
        }

        // Commands
        RefreshStatusCommand = new RelayCommand(async _ => await RefreshStatusAsync());
        InstallServiceCommand = new RelayCommand(async _ => await InstallServiceAsync(), _ => !_isServiceInstalled);
        UninstallServiceCommand = new RelayCommand(async _ => await UninstallServiceAsync(), _ => _isServiceInstalled);
        StartServiceCommand = new RelayCommand(async _ => await StartServiceAsync(), _ => _isServiceInstalled && !_isServiceRunning);
        StopServiceCommand = new RelayCommand(async _ => await StopServiceAsync(), _ => _isServiceInstalled && _isServiceRunning);
        RelaunchAsAdminCommand = new RelayCommand(async _ => await RelaunchAsAdminAsync());
        SaveToRegistryCommand = new RelayCommand(_ => SaveSettingsToRegistry());
        _serviceLogMessages = serviceLogMessages ?? new ObservableCollection<string>();

        ClearLogCommand = new RelayCommand(_ => LogMessages.Clear());
        ClearServiceLogCommand = new RelayCommand(_ => ServiceLogMessages.Clear());
        OpenLogFolderCommand = new RelayCommand(_ => OpenLogFolder());

        // Tab navigation commands
        ShowServiceViewCommand = new RelayCommand(_ =>
        {
            IsServiceTabSelected = true;
            CurrentView = this;
        });
        ShowUserManagementCommand = new RelayCommand(async _ =>
        {
            IsServiceTabSelected = false;
            CurrentView = UserManagement;
            await UserManagement.LoadUsersAsync();
        });
        ShowAuditLogCommand = new RelayCommand(async _ =>
        {
            IsServiceTabSelected = false;
            CurrentView = AuditLog;
            await AuditLog.LoadLogsAsync();
        });

        // Auto-refresh timer: polls service status on a background thread.
        // Uses System.Threading.Timer with async void callback so the status polling
        // does not block or flicker the UI thread. Log and property-change dispatch back
        // to the UI thread automatically via AddLog and OnPropertyChanged.
        _pollTimer = CreateTimer(_pollingIntervalSeconds);

        _logMessages.Add($"[{DateTime.Now:HH:mm:ss.fff}] Service manager initialized");
        _logMessages.Add($"[{DateTime.Now:HH:mm:ss.fff}] Running as: {AdministratorAccount} {(IsElevated ? "(Administrator)" : "(Standard user)")}");
    }

    // ─── Status Indicator ────────────────────────────────────────────

    /// <summary>
    /// Current service health for the traffic-light indicator.
    /// Red = not installed/unknown, Amber = installed but not running, Green = running.
    /// </summary>
    public ServiceHealth Health => _isServiceRunning ? ServiceHealth.Green
        : _isServiceInstalled ? ServiceHealth.Amber
        : ServiceHealth.Red;

    /// <summary>
    /// Descriptive label for the current health state.
    /// </summary>
    public string HealthLabel => Health switch
    {
        ServiceHealth.Green => "Running",
        ServiceHealth.Amber => "Starting Up",
        ServiceHealth.Red => "Not Installed",
        _ => "Unknown"
    };

    // ─── Administrator Info ──────────────────────────────────────────

    public string AdministratorAccount =>
        Environment.UserDomainName + "\\" + Environment.UserName;

    public bool IsElevated => _isElevated;
    public bool IsElevationRequired => !_isElevated;

    // ─── Properties ──────────────────────────────────────────────────

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set { _isBusy = value; OnPropertyChanged(); }
    }

    public string ServiceStatusText
    {
        get => _serviceStatusText;
        set
        {
            _serviceStatusText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Health));
            OnPropertyChanged(nameof(HealthLabel));
        }
    }

    public bool IsServiceInstalled
    {
        get => _isServiceInstalled;
        set
        {
            _isServiceInstalled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Health));
            OnPropertyChanged(nameof(HealthLabel));
            ((RelayCommand)InstallServiceCommand).RaiseCanExecuteChanged();
            ((RelayCommand)UninstallServiceCommand).RaiseCanExecuteChanged();
            ((RelayCommand)StartServiceCommand).RaiseCanExecuteChanged();
            ((RelayCommand)StopServiceCommand).RaiseCanExecuteChanged();
        }
    }

    public bool IsServiceRunning
    {
        get => _isServiceRunning;
        set
        {
            _isServiceRunning = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Health));
            OnPropertyChanged(nameof(HealthLabel));
            ((RelayCommand)StartServiceCommand).RaiseCanExecuteChanged();
            ((RelayCommand)StopServiceCommand).RaiseCanExecuteChanged();
        }
    }

    // ─── Tab Navigation ───────────────────────────────────────────────

    public bool IsServiceTabSelected
    {
        get => _isServiceTabSelected;
        set
        {
            _isServiceTabSelected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsUserTabSelected));
        }
    }

    public bool IsUserTabSelected => !_isServiceTabSelected && CurrentView != AuditLog;
    public bool IsAuditTabSelected => CurrentView == AuditLog;

    public UserManagementViewModel UserManagement { get; }
    public AuditLogViewModel AuditLog { get; }

    public object? CurrentView
    {
        get => _currentView;
        set
        {
            _currentView = value;
            OnPropertyChanged();
        }
    }

    // ─── Commands ────────────────────────────────────────────────────

    public ICommand RefreshStatusCommand { get; }
    public ICommand InstallServiceCommand { get; }
    public ICommand UninstallServiceCommand { get; }
    public ICommand StartServiceCommand { get; }
    public ICommand StopServiceCommand { get; }
    public ICommand RelaunchAsAdminCommand { get; }
    public ICommand SaveToRegistryCommand { get; }
    public ICommand ClearLogCommand { get; }
    public ICommand ClearServiceLogCommand { get; }
    public ICommand OpenLogFolderCommand { get; }
    public ICommand ShowServiceViewCommand { get; }
    public ICommand ShowUserManagementCommand { get; }
    public ICommand ShowAuditLogCommand { get; }

    // ─── Configuration ───────────────────────────────────────────────

    public string ServiceHost
    {
        get => _serviceHost;
        set { _serviceHost = value; OnPropertyChanged(); OnPropertyChanged(nameof(Endpoint)); }
    }

    public int ServicePort
    {
        get => _servicePort;
        set
        {
            if (value < 1 || value > 65535) return;
            _servicePort = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Endpoint));
        }
    }

    /// <summary>
    /// The address clients use to connect to the Broker service, in <c>host:port</c> form.
    /// Shown on the dashboard so the user can verify it when connecting from the Catalog Browser.
    /// </summary>
    public string Endpoint => $"{_serviceHost}:{_servicePort}";

    /// <summary>
    /// Whether clients should connect using TLS. Published to the registry on save.
    /// </summary>
    public bool UseTls
    {
        get => _useTls;
        set { _useTls = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Whether clients should accept the service's self-signed certificate.
    /// </summary>
    public bool AllowSelfSigned
    {
        get => _allowSelfSigned;
        set { _allowSelfSigned = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Auto-refresh polling interval in seconds (1–300).
    /// When changed, the timer is restarted immediately and the new value
    /// is persisted to disk via <see cref="App.Config"/>.
    /// </summary>
    public int PollingIntervalSeconds
    {
        get => _pollingIntervalSeconds;
        set
        {
            var clamped = Math.Clamp(value, 1, 300);
            if (_pollingIntervalSeconds == clamped) return;
            _pollingIntervalSeconds = clamped;
            App.Config.PollingIntervalSeconds = clamped;
            App.Config.Save();
            OnPropertyChanged();
            RestartPollTimer();
            AddLog($"Polling interval changed to {clamped}s");
        }
    }

    /// <summary>
    /// When true, closing the window minimizes to tray instead of exiting.
    /// Changes are persisted immediately to disk via <see cref="App.Config"/>.
    /// </summary>
    public bool MinimizeToTrayOnClose
    {
        get => App.Config.MinimizeToTrayOnClose;
        set
        {
            if (App.Config.MinimizeToTrayOnClose == value) return;
            App.Config.MinimizeToTrayOnClose = value;
            App.Config.Save();
            OnPropertyChanged();
        }
    }

    public string StatusText => $"Service: {_serviceStatusText}";

    public ObservableCollection<string> LogMessages
    {
        get => _logMessages;
        set { _logMessages = value; OnPropertyChanged(); }
    }

    public ObservableCollection<string> ServiceLogMessages => _serviceLogMessages;

    // ─── Path Resolution ─────────────────────────────────────────────

    private string ResolveBrokerServicePath()
    {
        var baseDir = AppContext.BaseDirectory;
        var sameDir = Path.Combine(baseDir, "Adam.BrokerService.exe");
        if (File.Exists(sameDir))
            return sameDir;

        var dir = new DirectoryInfo(baseDir);
        while (dir != null)
        {
            var hasGit = Directory.Exists(Path.Combine(dir.FullName, ".git"));
            var hasSln = dir.GetFiles("*.sln").Length > 0;
            if (hasGit || hasSln)
            {
                var candidate = Path.Combine(dir.FullName, "src", "Adam.BrokerService", "bin", "Debug", "net10.0", "Adam.BrokerService.exe");
                if (File.Exists(candidate))
                    return candidate;
                candidate = Path.Combine(dir.FullName, "src", "Adam.BrokerService", "bin", "Release", "net10.0", "Adam.BrokerService.exe");
                if (File.Exists(candidate))
                    return candidate;
                break;
            }
            dir = dir.Parent;
        }
        return Environment.ProcessPath ?? string.Empty;
    }

    // ─── Logging ─────────────────────────────────────────────────────

    private void AddLog(string message)
    {
        _logger.LogInformation("[ServiceManager] {Message}", message);
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        var entry = $"[{timestamp}] {message}";

        if (_dispatcher.CheckAccess())
            AddLogEntry(entry);
        else
            _dispatcher.Post(() => AddLogEntry(entry));
    }

    /// <summary>
    /// Dispatches an action to run on the UI thread, awaiting completion.
    /// </summary>
    private async Task RunOnUiThreadAsync(Action action)
    {
        if (_dispatcher.CheckAccess())
            action();
        else
            await _dispatcher.InvokeAsync(action);
    }

    private void AddLogEntry(string entry)
    {
        if (_logMessages.Count > 500)
            _logMessages.RemoveAt(0);
        _logMessages.Add(entry);
    }

    // ─── Relaunch as Admin ───────────────────────────────────────────

    private async Task RelaunchAsAdminAsync()
    {
        AddLog("=== RELAUNCHING AS ADMINISTRATOR ===");
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                AddLog("Relaunch as administrator is only supported on Windows.");
                await RunOnUiThreadAsync(() => StatusMessage = "Administrator relaunch is only supported on Windows.");
                return;
            }

            var processPath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(processPath))
            {
                AddLog("Could not determine process path.");
                await RunOnUiThreadAsync(() => StatusMessage = "Could not determine process path.");
                return;
            }

            // When running via `dotnet run` (framework-dependent), ProcessPath points to
            // dotnet.exe with no arguments — launching that would just open a blank dotnet
            // console. Detect this and pass the assembly DLL as an argument.
            var isDotnetHost = Path.GetFileNameWithoutExtension(processPath)
                .Equals("dotnet", StringComparison.OrdinalIgnoreCase);

            AddLog($"Launching: {processPath} (isDotnetHost={isDotnetHost})");

            ProcessStartInfo startInfo;
            if (isDotnetHost)
            {
                var assemblyPath = typeof(ServiceManagerViewModel).Assembly.Location;
                AddLog($"Assembly path: {assemblyPath}");
                startInfo = new ProcessStartInfo(processPath)
                {
                    Arguments = $"\"{assemblyPath}\"",
                    UseShellExecute = true,
                    Verb = "runas"
                };
            }
            else
            {
                startInfo = new ProcessStartInfo(processPath)
                {
                    UseShellExecute = true,
                    Verb = "runas"
                };
            }

            Process.Start(startInfo);
            AddLog("Elevated process launched. Closing this instance...");

            await Task.Delay(500);
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to relaunch as administrator");
            AddLog($"ERROR: {ex.GetType().Name}: {ex.Message}");
            await RunOnUiThreadAsync(() => StatusMessage = $"Failed to relaunch: {ex.Message}");
        }
    }

    // ─── Save Settings to Registry ───────────────────────────────────

    /// <summary>
    /// Persists the current connection settings to the local app config and
    /// publishes them to HKLM so the Catalog Browser (a standard-user process)
    /// can read the correct endpoint at launch. Requires elevation, which the
    /// Service Manager always has via its manifest.
    /// </summary>
    private void SaveSettingsToRegistry()
    {
        AddLog("=== SAVING CONNECTION SETTINGS FOR CLIENTS ===");
        try
        {
            // Clients connect over the network, so a local-only host (localhost,
            // 127.0.0.1, …) would always fail for them. Publish the server's
            // routable IP instead, and reflect it back into the UI.
            if (IsLocalHost(ServiceHost))
            {
                var ip = NetworkInfo.GetPrimaryIPv4Address();
                if (!string.IsNullOrEmpty(ip))
                {
                    AddLog($"Host '{ServiceHost}' is local-only; publishing server IP '{ip}' for clients instead.");
                    ServiceHost = ip;
                }
                else
                {
                    AddLog($"WARNING: could not determine the server's IP; publishing '{ServiceHost}', which remote clients cannot reach.");
                }
            }

            // Persist to this app's own config first so the values survive a restart.
            var config = App.Config;
            config.ServiceHost = ServiceHost;
            config.ServicePort = ServicePort;
            config.UseTls = UseTls;
            config.AllowSelfSigned = AllowSelfSigned;
            config.Save();

            var settings = new RegistrySettings
            {
                ServiceHost = ServiceHost,
                ServicePort = ServicePort,
                UseTls = UseTls,
                AllowSelfSigned = AllowSelfSigned,
            };
            settings.Save();

            AddLog($"Published to HKLM\\{RegistrySettings.KeyPath}: Host={ServiceHost}, Port={ServicePort}, Tls={UseTls}, AllowSelfSigned={AllowSelfSigned}");
            StatusMessage = $"Settings saved for clients ({Endpoint}). The Catalog Browser will use these on next launch.";
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Insufficient privileges to write registry settings");
            AddLog($"ADMIN REQUIRED: {ex.Message}");
            StatusMessage = "Administrator privileges are required to save settings for clients.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings to registry");
            AddLog($"ERROR: {ex.GetType().Name}: {ex.Message}");
            StatusMessage = $"Failed to save settings: {ex.Message}";
        }
    }

    // ─── Refresh Status ──────────────────────────────────────────────
    private async Task RefreshStatusAsync()
    {
        AddLog("Refreshing service status...");
        await RunOnUiThreadAsync(() => IsBusy = true);
        try
        {
            AddLog($"Checking local service via {_serviceInstaller.GetType().Name}...");
            var svc = await _serviceInstaller.GetStatusAsync();
            var svcStr = svc.ToString();
            var isInstalled = svc != ServiceStatus.NotInstalled;
            var isRunning = svc == ServiceStatus.Running;

            await RunOnUiThreadAsync(() =>
            {
                ServiceStatusText = svcStr;
                IsServiceInstalled = isInstalled;
                IsServiceRunning = isRunning;
            });

            AddLog($"Service status: {svc} (raw sc.exe output will be in the service log), Installed={isInstalled}, Running={isRunning}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Status refresh failed");
            AddLog($"ERROR refreshing status: {ex.GetType().Name}: {ex.Message}");
            await RunOnUiThreadAsync(() => StatusMessage = $"Status error: {ex.Message}");
        }
        finally
        {
            await RunOnUiThreadAsync(() => IsBusy = false);
        }
    }

    // ─── Start / Stop ────────────────────────────────────────────────

    private async Task StartServiceAsync()
    {
        var sw = Stopwatch.StartNew();
        AddLog("=== STARTING SERVICE ===");
        AddLog($"Installer type: {_serviceInstaller.GetType().Name}, IsSupported: {_serviceInstaller.IsSupported}");
        AddLog($"Installer IsElevated: {(OperatingSystem.IsWindows() ? new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator).ToString() : "N/A")}");
        AddLog($"Environment: PID={Environment.ProcessId}, ProcessPath={Environment.ProcessPath}");
        AddLog($"OS: {Environment.OSVersion}, UserInteractive: {Environment.UserInteractive}");
        await RunOnUiThreadAsync(() => IsBusy = true);
        try
        {
            if (!_serviceInstaller.IsSupported)
            {
                var msg = "No service installer available for this platform.";
                _logger.LogError("Start aborted: {Message}", msg);
                AddLog($"FAILED: {msg}");
                await RunOnUiThreadAsync(() => StatusMessage = msg);
                return;
            }

            AddLog("Calling installer.StartAsync...");
            var before = DateTime.UtcNow;
            await _serviceInstaller.StartAsync();
            var elapsed = sw.Elapsed;
            AddLog($"Installer.StartAsync completed successfully. Elapsed: {elapsed.TotalMilliseconds:F0}ms (started={before:O}, ended={DateTime.UtcNow:O})");

            await RunOnUiThreadAsync(() =>
            {
                IsServiceRunning = true;
                ServiceStatusText = ServiceStatus.Running.ToString();
                StatusMessage = "Service started.";
                OnPropertyChanged(nameof(StatusText));
            });
            AddLog("=== SERVICE STARTED ===");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Insufficient privileges to start service (elapsed={ElapsedMs:F0}ms)", sw.Elapsed.TotalMilliseconds);
            AddLog($"ADMIN REQUIRED: {ex.Message}");
            await RunOnUiThreadAsync(() => StatusMessage = "Administrator privileges required. Use 'Relaunch as Administrator' and try again.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start service (elapsed={ElapsedMs:F0}ms)", sw.Elapsed.TotalMilliseconds);
            AddLog($"ERROR: {ex.GetType().Name}: {ex.Message}");
            AddLog($"STACK TRACE: {ex.StackTrace}");
            if (ex.InnerException != null)
                AddLog($"INNER EXCEPTION: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            await RunOnUiThreadAsync(() => StatusMessage = $"Start error: {ex.Message}");
        }
        finally
        {
            await RunOnUiThreadAsync(() => IsBusy = false);
            AddLog($"Total start operation elapsed: {sw.Elapsed.TotalMilliseconds:F0}ms");
        }
    }

    private async Task StopServiceAsync()
    {
        var sw = Stopwatch.StartNew();
        AddLog("=== STOPPING SERVICE ===");
        AddLog($"Installer type: {_serviceInstaller.GetType().Name}, IsSupported: {_serviceInstaller.IsSupported}");
        await RunOnUiThreadAsync(() => IsBusy = true);
        try
        {
            if (!_serviceInstaller.IsSupported)
            {
                var msg = "No service installer available for this platform.";
                _logger.LogError("Stop aborted: {Message}", msg);
                AddLog($"FAILED: {msg}");
                await RunOnUiThreadAsync(() => StatusMessage = msg);
                return;
            }

            AddLog("Calling installer.StopAsync...");
            var before = DateTime.UtcNow;
            await _serviceInstaller.StopAsync();
            var elapsed = sw.Elapsed;
            AddLog($"Installer.StopAsync completed successfully. Elapsed: {elapsed.TotalMilliseconds:F0}ms (started={before:O}, ended={DateTime.UtcNow:O})");

            await RunOnUiThreadAsync(() =>
            {
                IsServiceRunning = false;
                ServiceStatusText = ServiceStatus.Stopped.ToString();
                StatusMessage = "Service stopped.";
                OnPropertyChanged(nameof(StatusText));
            });
            AddLog("=== SERVICE STOPPED ===");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Insufficient privileges to stop service (elapsed={ElapsedMs:F0}ms)", sw.Elapsed.TotalMilliseconds);
            AddLog($"ADMIN REQUIRED: {ex.Message}");
            await RunOnUiThreadAsync(() => StatusMessage = "Administrator privileges required. Use 'Relaunch as Administrator' and try again.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop service (elapsed={ElapsedMs:F0}ms)", sw.Elapsed.TotalMilliseconds);
            AddLog($"ERROR: {ex.GetType().Name}: {ex.Message}");
            AddLog($"STACK TRACE: {ex.StackTrace}");
            if (ex.InnerException != null)
                AddLog($"INNER EXCEPTION: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            await RunOnUiThreadAsync(() => StatusMessage = $"Stop error: {ex.Message}");
        }
        finally
        {
            await RunOnUiThreadAsync(() => IsBusy = false);
            AddLog($"Total stop operation elapsed: {sw.Elapsed.TotalMilliseconds:F0}ms");
        }
    }

    // ─── Install / Uninstall ─────────────────────────────────────────

    private async Task InstallServiceAsync()
    {
        AddLog("=== SERVICE INSTALLATION STARTED ===");
        AddLog($"Installer type: {_serviceInstaller.GetType().Name}");
        AddLog($"Installer supported: {_serviceInstaller.IsSupported}");
        AddLog($"Installer service name: '{_serviceInstaller.ServiceName}'");
        await RunOnUiThreadAsync(() => IsBusy = true);
        try
        {
            var brokerPath = ResolveBrokerServicePath();
            AddLog($"Broker service path: '{brokerPath}'");
            AddLog($"Target port: {ServicePort}");

            if (!_serviceInstaller.IsSupported)
            {
                var msg = "No service installer available for this platform. Cannot install service.";
                _logger.LogError("Installation aborted: {Message}", msg);
                AddLog($"FAILED: {msg}");
                await RunOnUiThreadAsync(() => StatusMessage = msg);
                return;
            }

            AddLog("Calling installer.InstallAsync...");
            await _serviceInstaller.InstallAsync(brokerPath, ServicePort);
            AddLog("Installer.InstallAsync completed successfully.");

            // Publish the server's routable IP, not a local-only host, so clients can connect.
            if (IsLocalHost(ServiceHost))
            {
                var ip = NetworkInfo.GetPrimaryIPv4Address();
                if (!string.IsNullOrEmpty(ip))
                {
                    AddLog($"Host '{ServiceHost}' is local-only; using server IP '{ip}' for clients.");
                    ServiceHost = ip;
                }
            }

            var config = App.Config;
            config.ServiceHost = ServiceHost;
            config.ServicePort = ServicePort;
            config.UseTls = UseTls;
            config.AllowSelfSigned = AllowSelfSigned;
            config.Save();
            AddLog($"Configuration saved: Host={ServiceHost}, Port={ServicePort}");

            // Publish to the registry so standard-user clients pick up the endpoint.
            try
            {
                new RegistrySettings
                {
                    ServiceHost = ServiceHost,
                    ServicePort = ServicePort,
                    UseTls = UseTls,
                    AllowSelfSigned = AllowSelfSigned,
                }.Save();
                AddLog($"Published connection settings to HKLM\\{RegistrySettings.KeyPath} for clients.");
            }
            catch (Exception regEx)
            {
                _logger.LogWarning(regEx, "Could not publish connection settings to registry during install");
                AddLog($"WARNING: could not publish settings to registry: {regEx.Message}");
            }

            await RunOnUiThreadAsync(() =>
            {
                StatusMessage = "Service installed and started.";
                IsServiceInstalled = true;
                IsServiceRunning = true;
                OnPropertyChanged(nameof(StatusText));
            });
            AddLog("=== SERVICE INSTALLATION SUCCEEDED ===");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already in use"))
        {
            _logger.LogWarning(ex, "Port already in use during installation");
            AddLog($"WARNING: {ex.Message}");
            await RunOnUiThreadAsync(() => StatusMessage = $"Warning: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Insufficient privileges for service installation");
            AddLog($"ADMIN REQUIRED: {ex.Message}");
            await RunOnUiThreadAsync(() => StatusMessage = "Administrator privileges required. Use 'Relaunch as Administrator' and try again.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Service installation failed");
            AddLog($"ERROR: {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException != null)
                AddLog($"  Inner exception: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            await RunOnUiThreadAsync(() => StatusMessage = $"Install error: {ex.Message}");
        }
        finally
        {
            await RunOnUiThreadAsync(() => IsBusy = false);
        }
    }

    private async Task UninstallServiceAsync()
    {
        AddLog("=== SERVICE UNINSTALLATION STARTED ===");
        AddLog($"Installer type: {_serviceInstaller.GetType().Name}");
        AddLog($"Installer supported: {_serviceInstaller.IsSupported}");
        AddLog($"Installer service name: '{_serviceInstaller.ServiceName}'");
        await RunOnUiThreadAsync(() => IsBusy = true);
        try
        {
            if (!_serviceInstaller.IsSupported)
            {
                var msg = "No service installer available for this platform. Cannot uninstall service.";
                _logger.LogError("Uninstallation aborted: {Message}", msg);
                AddLog($"FAILED: {msg}");
                await RunOnUiThreadAsync(() => StatusMessage = msg);
                return;
            }

            AddLog("Calling installer.UninstallAsync...");
            await _serviceInstaller.UninstallAsync();
            AddLog("Installer.UninstallAsync completed successfully.");

            await RunOnUiThreadAsync(() =>
            {
                StatusMessage = "Service stopped and removed.";
                IsServiceInstalled = false;
                IsServiceRunning = false;
                OnPropertyChanged(nameof(StatusText));
            });
            AddLog("=== SERVICE UNINSTALLATION SUCCEEDED ===");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Insufficient privileges for service uninstallation");
            AddLog($"ADMIN REQUIRED: {ex.Message}");
            await RunOnUiThreadAsync(() => StatusMessage = "Administrator privileges required. Use 'Relaunch as Administrator' and try again.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Service uninstallation failed");
            AddLog($"ERROR: {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException != null)
                AddLog($"  Inner exception: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            await RunOnUiThreadAsync(() => StatusMessage = $"Uninstall error: {ex.Message}");
        }
        finally
        {
            await RunOnUiThreadAsync(() => IsBusy = false);
        }
    }

    private static string FormatUptime(long seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalDays >= 1
            ? $"{ts.Days}d {ts.Hours}h {ts.Minutes}m"
            : $"{ts.Hours}h {ts.Minutes}m {ts.Seconds}s";
    }

    /// <summary>
    /// True for hostnames that only resolve on the local machine and so must not
    /// be published to remote clients.
    /// </summary>
    private static bool IsLocalHost(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        host == "127.0.0.1" ||
        host == "::1" ||
        host == "0.0.0.0";

    /// <summary>
    /// Creates a <see cref="System.Threading.Timer"/> that calls <see cref="RefreshStatusAsync"/>
    /// at the specified interval on a background thread pool thread.
    /// </summary>
    private Timer CreateTimer(int intervalSeconds)
    {
        return new Timer(
            async _ =>
            {
                if (_disposed || _isBusy) return;
                try
                {
                    await RefreshStatusAsync().ConfigureAwait(false);
                }
                catch
                {
                    // Exception details are logged inside RefreshStatusAsync
                }
            },
            null,
            TimeSpan.FromSeconds(intervalSeconds),
            TimeSpan.FromSeconds(intervalSeconds));
    }

    /// <summary>
    /// Disposes the current polling timer and creates a new one with the
    /// configured <see cref="PollingIntervalSeconds"/>.
    /// </summary>
    private void RestartPollTimer()
    {
        _pollTimer?.Dispose();
        _pollTimer = CreateTimer(_pollingIntervalSeconds);
    }

    private void OpenLogFolder()
    {
        try
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, "adam-service-manager.log");
            if (!File.Exists(logPath))
            {
                // Try just opening the directory if the file doesn't exist yet
                Process.Start(new ProcessStartInfo
                {
                    FileName = AppContext.BaseDirectory,
                    UseShellExecute = true,
                    Verb = "open"
                });
                return;
            }

            // Open the file with the default editor
            Process.Start(new ProcessStartInfo
            {
                FileName = logPath,
                UseShellExecute = true,
                Verb = "open"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open logs");
            AddLog($"ERROR opening logs: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pollTimer?.Dispose();
        _pollTimer = null;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
