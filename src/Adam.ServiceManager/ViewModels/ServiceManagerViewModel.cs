using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Adam.Shared.Services;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.ServiceManager.ViewModels;

public enum ServiceHealth
{
    Red,    // Not installed, stopped, or unknown
    Amber,  // Starting up / transitional
    Green   // Running
}

public class ServiceManagerViewModel : INotifyPropertyChanged
{
    private readonly ILogger<ServiceManagerViewModel> _logger;
    private readonly IServiceInstaller _serviceInstaller;
    private string _statusMessage = string.Empty;
    private bool _isBusy;
    private string _serviceStatusText = "Unknown";
    private bool _isServiceInstalled;
    private bool _isServiceRunning;
    private readonly bool _isElevated;
    private string _serviceHost = "localhost";
    private int _servicePort = 9100;
    private ObservableCollection<string> _logMessages = new();
    private readonly ObservableCollection<string> _serviceLogMessages;
    private readonly DispatcherTimer _autoRefreshTimer;

    public ServiceManagerViewModel(IEnumerable<IServiceInstaller> serviceInstallers, ILogger<ServiceManagerViewModel>? logger = null, ObservableCollection<string>? serviceLogMessages = null)
    {
        _logger = logger ?? NullLogger<ServiceManagerViewModel>.Instance;
        var installers = serviceInstallers.ToList();
        _serviceInstaller = installers.FirstOrDefault(s => s.IsSupported) ?? new NullServiceInstaller();
        _logger.LogInformation("ServiceManagerViewModel: Selected service installer = {InstallerType} (IsSupported={IsSupported}, ServiceName='{ServiceName}')",
            _serviceInstaller.GetType().Name, _serviceInstaller.IsSupported, _serviceInstaller.ServiceName);

        // Detect elevation state
        _isElevated = !OperatingSystem.IsWindows() ||
            new System.Security.Principal.WindowsPrincipal(
                System.Security.Principal.WindowsIdentity.GetCurrent())
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);

        // Load persisted settings
        var config = App.Config;
        _serviceHost = config.ServiceHost;
        _servicePort = config.ServicePort;

        // Commands
        RefreshStatusCommand = new RelayCommand(async _ => await RefreshStatusAsync());
        InstallServiceCommand = new RelayCommand(async _ => await InstallServiceAsync(), _ => !_isServiceInstalled);
        UninstallServiceCommand = new RelayCommand(async _ => await UninstallServiceAsync(), _ => _isServiceInstalled);
        StartServiceCommand = new RelayCommand(async _ => await StartServiceAsync(), _ => _isServiceInstalled && !_isServiceRunning);
        StopServiceCommand = new RelayCommand(async _ => await StopServiceAsync(), _ => _isServiceInstalled && _isServiceRunning);
        RelaunchAsAdminCommand = new RelayCommand(async _ => await RelaunchAsAdminAsync());
        _serviceLogMessages = serviceLogMessages ?? new ObservableCollection<string>();

        ClearLogCommand = new RelayCommand(_ => LogMessages.Clear());
        ClearServiceLogCommand = new RelayCommand(_ => ServiceLogMessages.Clear());

        // Auto-refresh timer: polls service status every 5 seconds
        _autoRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _autoRefreshTimer.Tick += async (_, _) =>
        {
            if (_isBusy) return;
            try { await RefreshStatusAsync(); }
            catch { /* logged inside */ }
        };
        _autoRefreshTimer.Start();

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

    // ─── Commands ────────────────────────────────────────────────────

    public ICommand RefreshStatusCommand { get; }
    public ICommand InstallServiceCommand { get; }
    public ICommand UninstallServiceCommand { get; }
    public ICommand StartServiceCommand { get; }
    public ICommand StopServiceCommand { get; }
    public ICommand RelaunchAsAdminCommand { get; }
    public ICommand ClearLogCommand { get; }
    public ICommand ClearServiceLogCommand { get; }

    // ─── Configuration ───────────────────────────────────────────────

    public string ServiceHost
    {
        get => _serviceHost;
        set { _serviceHost = value; OnPropertyChanged(); }
    }

    public int ServicePort
    {
        get => _servicePort;
        set
        {
            if (value < 1 || value > 65535) return;
            _servicePort = value;
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

        if (Dispatcher.UIThread.CheckAccess())
            AddLogEntry(entry);
        else
            Dispatcher.UIThread.Post(() => AddLogEntry(entry));
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
                StatusMessage = "Administrator relaunch is only supported on Windows.";
                return;
            }

            var processPath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(processPath))
            {
                AddLog("Could not determine process path.");
                StatusMessage = "Could not determine process path.";
                return;
            }

            AddLog($"Launching: {processPath}");
            var startInfo = new ProcessStartInfo(processPath)
            {
                UseShellExecute = true,
                Verb = "runas"
            };

            Process.Start(startInfo);
            AddLog("Elevated process launched. Closing this instance...");

            await Task.Delay(500);
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to relaunch as administrator");
            AddLog($"ERROR: {ex.GetType().Name}: {ex.Message}");
            StatusMessage = $"Failed to relaunch: {ex.Message}";
        }
    }

    // ─── Refresh Status ──────────────────────────────────────────────

    private async Task RefreshStatusAsync()
    {
        AddLog("Refreshing service status...");
        IsBusy = true;
        try
        {
            AddLog($"Checking local service via {_serviceInstaller.GetType().Name}...");
            var svc = await _serviceInstaller.GetStatusAsync();
            ServiceStatusText = svc.ToString();

            // Only treat Running or Stopped as "installed". Unknown → Red.
            IsServiceInstalled = svc is ServiceStatus.Running or ServiceStatus.Stopped;
            IsServiceRunning = svc == ServiceStatus.Running;

            AddLog($"Service status: {svc}, Installed={IsServiceInstalled}, Running={IsServiceRunning}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Status refresh failed");
            AddLog($"ERROR refreshing status: {ex.GetType().Name}: {ex.Message}");
            StatusMessage = $"Status error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ─── Start / Stop ────────────────────────────────────────────────

    private async Task StartServiceAsync()
    {
        AddLog("=== STARTING SERVICE ===");
        IsBusy = true;
        try
        {
            if (!_serviceInstaller.IsSupported)
            {
                var msg = "No service installer available for this platform.";
                _logger.LogError("Start aborted: {Message}", msg);
                AddLog($"FAILED: {msg}");
                StatusMessage = msg;
                return;
            }

            AddLog("Calling installer.StartAsync...");
            await _serviceInstaller.StartAsync();
            AddLog("Installer.StartAsync completed successfully.");

            IsServiceRunning = true;
            ServiceStatusText = ServiceStatus.Running.ToString();
            StatusMessage = "Service started.";
            OnPropertyChanged(nameof(StatusText));
            AddLog("=== SERVICE STARTED ===");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Insufficient privileges to start service");
            AddLog($"ADMIN REQUIRED: {ex.Message}");
            StatusMessage = "Administrator privileges required. Use 'Relaunch as Administrator' and try again.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start service");
            AddLog($"ERROR: {ex.GetType().Name}: {ex.Message}");
            StatusMessage = $"Start error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task StopServiceAsync()
    {
        AddLog("=== STOPPING SERVICE ===");
        IsBusy = true;
        try
        {
            if (!_serviceInstaller.IsSupported)
            {
                var msg = "No service installer available for this platform.";
                _logger.LogError("Stop aborted: {Message}", msg);
                AddLog($"FAILED: {msg}");
                StatusMessage = msg;
                return;
            }

            AddLog("Calling installer.StopAsync...");
            await _serviceInstaller.StopAsync();
            AddLog("Installer.StopAsync completed successfully.");

            IsServiceRunning = false;
            ServiceStatusText = ServiceStatus.Stopped.ToString();
            StatusMessage = "Service stopped.";
            OnPropertyChanged(nameof(StatusText));
            AddLog("=== SERVICE STOPPED ===");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Insufficient privileges to stop service");
            AddLog($"ADMIN REQUIRED: {ex.Message}");
            StatusMessage = "Administrator privileges required. Use 'Relaunch as Administrator' and try again.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop service");
            AddLog($"ERROR: {ex.GetType().Name}: {ex.Message}");
            StatusMessage = $"Stop error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ─── Install / Uninstall ─────────────────────────────────────────

    private async Task InstallServiceAsync()
    {
        AddLog("=== SERVICE INSTALLATION STARTED ===");
        AddLog($"Installer type: {_serviceInstaller.GetType().Name}");
        AddLog($"Installer supported: {_serviceInstaller.IsSupported}");
        AddLog($"Installer service name: '{_serviceInstaller.ServiceName}'");
        IsBusy = true;
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
                StatusMessage = msg;
                return;
            }

            AddLog("Calling installer.InstallAsync...");
            await _serviceInstaller.InstallAsync(brokerPath, ServicePort);
            AddLog("Installer.InstallAsync completed successfully.");

            var config = App.Config;
            config.ServiceHost = ServiceHost;
            config.ServicePort = ServicePort;
            config.Save();
            AddLog($"Configuration saved: Host={ServiceHost}, Port={ServicePort}");

            StatusMessage = "Service installed and started.";
            IsServiceInstalled = true;
            IsServiceRunning = true;
            OnPropertyChanged(nameof(StatusText));
            AddLog("=== SERVICE INSTALLATION SUCCEEDED ===");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already in use"))
        {
            _logger.LogWarning(ex, "Port already in use during installation");
            AddLog($"WARNING: {ex.Message}");
            StatusMessage = $"Warning: {ex.Message}";
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Insufficient privileges for service installation");
            AddLog($"ADMIN REQUIRED: {ex.Message}");
            StatusMessage = "Administrator privileges required. Use 'Relaunch as Administrator' and try again.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Service installation failed");
            AddLog($"ERROR: {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException != null)
                AddLog($"  Inner exception: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            StatusMessage = $"Install error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task UninstallServiceAsync()
    {
        AddLog("=== SERVICE UNINSTALLATION STARTED ===");
        AddLog($"Installer type: {_serviceInstaller.GetType().Name}");
        AddLog($"Installer supported: {_serviceInstaller.IsSupported}");
        AddLog($"Installer service name: '{_serviceInstaller.ServiceName}'");
        IsBusy = true;
        try
        {
            if (!_serviceInstaller.IsSupported)
            {
                var msg = "No service installer available for this platform. Cannot uninstall service.";
                _logger.LogError("Uninstallation aborted: {Message}", msg);
                AddLog($"FAILED: {msg}");
                StatusMessage = msg;
                return;
            }

            AddLog("Calling installer.UninstallAsync...");
            await _serviceInstaller.UninstallAsync();
            AddLog("Installer.UninstallAsync completed successfully.");

            StatusMessage = "Service stopped and removed.";
            IsServiceInstalled = false;
            IsServiceRunning = false;
            OnPropertyChanged(nameof(StatusText));
            AddLog("=== SERVICE UNINSTALLATION SUCCEEDED ===");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Insufficient privileges for service uninstallation");
            AddLog($"ADMIN REQUIRED: {ex.Message}");
            StatusMessage = "Administrator privileges required. Use 'Relaunch as Administrator' and try again.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Service uninstallation failed");
            AddLog($"ERROR: {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException != null)
                AddLog($"  Inner exception: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            StatusMessage = $"Uninstall error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string FormatUptime(long seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalDays >= 1
            ? $"{ts.Days}d {ts.Hours}h {ts.Minutes}m"
            : $"{ts.Hours}h {ts.Minutes}m {ts.Seconds}s";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
