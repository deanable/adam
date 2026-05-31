using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Adam.CatalogBrowser.Services;
using Adam.Shared.Contracts;
using Adam.Shared.Services;
using Avalonia.Threading;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.CatalogBrowser.ViewModels;

public class AdminPanelViewModel : INotifyPropertyChanged
{
    private readonly ILogger<AdminPanelViewModel> _logger;
    private readonly ModeManager _modeManager;
    private readonly IServiceInstaller _serviceInstaller;
    private string _selectedMode = "Standalone";
    private string _statusMessage = string.Empty;
    private bool _isBusy;
    private string _serviceStatusText = "Unknown";
    private int _activeConnections;
    private string _uptimeText = "";
    private bool _isServiceInstalled;
    private bool _isServiceRunning;
    private bool _isElevated;
    private string _newWatchedFolderPath = string.Empty;
    private ObservableCollection<WatchedFolderItem> _watchedFolders = new();
    private string _serviceHost = "localhost";
    private int _servicePort = 9100;
    private ObservableCollection<string> _logMessages = new();
    private readonly ObservableCollection<string> _serviceLogMessages;
    private readonly DispatcherTimer _autoRefreshTimer;

    public AdminPanelViewModel(ModeManager modeManager, IEnumerable<IServiceInstaller> serviceInstallers, ILogger<AdminPanelViewModel>? logger = null, ObservableCollection<string>? serviceLogMessages = null)
    {
        _logger = logger ?? NullLogger<AdminPanelViewModel>.Instance;
        _modeManager = modeManager;
        _selectedMode = modeManager.Mode;
        var installers = serviceInstallers.ToList();
        _serviceInstaller = installers.FirstOrDefault(s => s.IsSupported) ?? new NullServiceInstaller();
        _logger.LogInformation("AdminPanelViewModel: Selected service installer = {InstallerType} (IsSupported={IsSupported}, ServiceName='{ServiceName}')",
            _serviceInstaller.GetType().Name, _serviceInstaller.IsSupported, _serviceInstaller.ServiceName);
        _logger.LogInformation("AdminPanelViewModel: Available installers count = {Count}", installers.Count);
        foreach (var installer in installers)
        {
            _logger.LogInformation("  Installer: {InstallerType}, IsSupported={IsSupported}, ServiceName='{ServiceName}'",
                installer.GetType().Name, installer.IsSupported, installer.ServiceName);
        }

        // Detect elevation state — used to show a reminder when admin access is needed
        _isElevated = !OperatingSystem.IsWindows() ||
            new System.Security.Principal.WindowsPrincipal(
                System.Security.Principal.WindowsIdentity.GetCurrent())
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);

        // Load persisted settings
        var config = App.Config;
        _serviceHost = config.ServiceHost;
        _servicePort = config.ServicePort;

        SaveModeCommand = new RelayCommand(async _ => await SaveModeAsync());
        RefreshStatusCommand = new RelayCommand(async _ => await RefreshStatusAsync());
        InstallServiceCommand = new RelayCommand(async _ => await InstallServiceAsync(), _ => !_isServiceInstalled);
        UninstallServiceCommand = new RelayCommand(async _ => await UninstallServiceAsync(), _ => _isServiceInstalled);
        StartServiceCommand = new RelayCommand(async _ => await StartServiceAsync(), _ => _isServiceInstalled && !_isServiceRunning);
        StopServiceCommand = new RelayCommand(async _ => await StopServiceAsync(), _ => _isServiceInstalled && _isServiceRunning);
        OpenMigrationWizardCommand = new RelayCommand(_ => OpenMigrationWizard());
        AddWatchedFolderCommand = new RelayCommand(async _ => await AddWatchedFolderAsync(), _ => !string.IsNullOrWhiteSpace(NewWatchedFolderPath));
        RemoveWatchedFolderCommand = new RelayCommand(async param => await RemoveWatchedFolderAsync(param), _ => true);
        RefreshWatchedFoldersCommand = new RelayCommand(async _ => await LoadWatchedFoldersAsync());
        _serviceLogMessages = serviceLogMessages ?? new ObservableCollection<string>();

        ClearLogCommand = new RelayCommand(_ => LogMessages.Clear());
        ClearServiceLogCommand = new RelayCommand(_ => ServiceLogMessages.Clear());

        // Start auto-refresh timer: polls service status every 5 seconds
        // Guard with IsBusy to prevent concurrent refreshes if a poll takes longer than the interval.
        _autoRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _autoRefreshTimer.Tick += async (_, _) =>
        {
            if (_isBusy) return;
            try { await RefreshStatusAsync(); }
            catch { /* exceptions are already logged inside RefreshStatusAsync */ }
        };
        _autoRefreshTimer.Start();

        // Log initialization to the UI log directly (constructor always runs on UI thread)
        _logMessages.Add($"[{DateTime.Now:HH:mm:ss.fff}] Admin panel initialized");

        if (_modeManager.IsMultiUser)
        {
            _ = LoadWatchedFoldersAsync();
        }
    }

    public string SelectedMode
    {
        get => _selectedMode;
        set { _selectedMode = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsMultiUser)); }
    }

    public bool IsMultiUser => SelectedMode == "MultiUser";

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
        set { _serviceStatusText = value; OnPropertyChanged(); }
    }

    public int ActiveConnections
    {
        get => _activeConnections;
        set { _activeConnections = value; OnPropertyChanged(); }
    }

    public string UptimeText
    {
        get => _uptimeText;
        set { _uptimeText = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// True when running on Windows without Administrator privileges.
    /// Shows a visual reminder prompting the user to restart as Administrator
    /// before attempting service installation or uninstallation.
    /// </summary>
    public bool IsElevationRequired => !_isElevated;

    public bool IsServiceInstalled
    {
        get => _isServiceInstalled;
        set
        {
            _isServiceInstalled = value;
            OnPropertyChanged();
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
            ((RelayCommand)StartServiceCommand).RaiseCanExecuteChanged();
            ((RelayCommand)StopServiceCommand).RaiseCanExecuteChanged();
        }
    }

    public ICommand SaveModeCommand { get; }
    public ICommand RefreshStatusCommand { get; }
    public ICommand InstallServiceCommand { get; }
    public ICommand UninstallServiceCommand { get; }
    public ICommand StartServiceCommand { get; }
    public ICommand StopServiceCommand { get; }
    public ICommand OpenMigrationWizardCommand { get; }
    public ICommand AddWatchedFolderCommand { get; }
    public ICommand RemoveWatchedFolderCommand { get; }
    public ICommand RefreshWatchedFoldersCommand { get; }
    public ICommand ClearLogCommand { get; }
    public ICommand ClearServiceLogCommand { get; }

    public ObservableCollection<WatchedFolderItem> WatchedFolders
    {
        get => _watchedFolders;
        set { _watchedFolders = value; OnPropertyChanged(); }
    }

    public string NewWatchedFolderPath
    {
        get => _newWatchedFolderPath;
        set { _newWatchedFolderPath = value; OnPropertyChanged(); ((RelayCommand)AddWatchedFolderCommand).RaiseCanExecuteChanged(); }
    }

    public string ServiceHost
    {
        get => _serviceHost;
        set
        {
            _serviceHost = value;
            OnPropertyChanged();
        }
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

    public event Action? NavigateToMigrationWizard;

    public ObservableCollection<string> LogMessages
    {
        get => _logMessages;
        set { _logMessages = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Collection of all ILogger messages (including terminal output from sc.exe/netsh
    /// and forwarded elevated process logs). Populated by <see cref="LogCaptureProvider"/>.
    /// </summary>
    public ObservableCollection<string> ServiceLogMessages => _serviceLogMessages;

    /// <summary>
    /// Resolves the path to Adam.BrokerService.exe by searching relative to the current
    /// assembly location. Falls back to <c>Environment.ProcessPath</c> if not found.
    /// </summary>
    private static string ResolveBrokerServicePath()
    {
        var baseDir = AppContext.BaseDirectory;

        // First check: same directory (side-by-side deployment or published scenario)
        var sameDir = Path.Combine(baseDir, "Adam.BrokerService.exe");
        if (File.Exists(sameDir))
            return sameDir;

        // Second check: walk up directory tree looking for solution root (presence of .git or *.sln)
        // Then construct path to BrokerService build output
        var dir = new DirectoryInfo(baseDir);
        while (dir != null)
        {
            var hasGit = Directory.Exists(Path.Combine(dir.FullName, ".git"));
            var hasSln = dir.GetFiles("*.sln").Length > 0;
            if (hasGit || hasSln)
            {
                // Try Debug net10.0
                var candidate = Path.Combine(dir.FullName, "src", "Adam.BrokerService", "bin", "Debug", "net10.0", "Adam.BrokerService.exe");
                if (File.Exists(candidate))
                    return candidate;

                // Try Release net10.0
                candidate = Path.Combine(dir.FullName, "src", "Adam.BrokerService", "bin", "Release", "net10.0", "Adam.BrokerService.exe");
                if (File.Exists(candidate))
                    return candidate;

                break;
            }
            dir = dir.Parent;
        }

        // Fallback: use the current process path (might be BrokerService itself, or CatalogBrowser)
        return Environment.ProcessPath ?? string.Empty;
    }

    private void AddLog(string message)
    {
        _logger.LogInformation("[AdminPanel] {Message}", message);
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        var entry = $"[{timestamp}] {message}";

        // Ensure we modify ObservableCollection on the UI thread to avoid
        // InvalidOperationException from Avalonia's dispatcher checks.
        if (Dispatcher.UIThread.CheckAccess())
        {
            AddLogEntry(entry);
        }
        else
        {
            Dispatcher.UIThread.Post(() => AddLogEntry(entry));
        }
    }

    private void AddLogEntry(string entry)
    {
        if (_logMessages.Count > 500)
            _logMessages.RemoveAt(0);
        _logMessages.Add(entry);
    }

    private async Task SaveModeAsync()
    {
        AddLog($"Saving mode: switching to {SelectedMode} (Host={ServiceHost}, Port={ServicePort})");
        try
        {
            if (SelectedMode == "Standalone")
            {
                AddLog("Initializing standalone mode...");
                await _modeManager.InitializeAsync();
            }
            else
            {
                AddLog($"Initializing multi-user mode with {ServiceHost}:{ServicePort}...");
                await _modeManager.InitializeMultiUserAsync(ServiceHost, ServicePort);
            }

            // Persist settings
            var config = App.Config;
            config.Mode = SelectedMode;
            config.ServiceHost = ServiceHost;
            config.ServicePort = ServicePort;
            config.Save();
            AddLog($"Settings saved: Mode={SelectedMode}, Host={ServiceHost}, Port={ServicePort}");

            StatusMessage = $"Mode switched to {SelectedMode}. Restart to apply.";
            AddLog($"Mode switched to {SelectedMode}. Restart to apply.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save mode");
            AddLog($"ERROR saving mode: {ex.GetType().Name}: {ex.Message}");
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    private async Task RefreshStatusAsync()
    {
        AddLog("Refreshing service status...");
        IsBusy = true;
        try
        {
            if (_modeManager.IsMultiUser && _modeManager.BrokerClient?.IsConnected == true)
            {
                AddLog("Multi-user mode: querying broker for service status...");
                var auth = _modeManager.AuthSession;
                var req = new Envelope
                {
                    AuthToken = auth?.Token ?? "",
                    CorrelationId = Guid.NewGuid().ToString(),
                    MessageType = MessageTypeCode.GetServiceStatusRequest,
                    Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new GetServiceStatusRequest()))
                };
                var resp = await _modeManager.BrokerClient.SendAsync(req);
                AddLog($"Broker response status code: {resp.StatusCode}");
                if (resp.StatusCode == 0)
                {
                    var status = ProtoHelper.Deserialize<GetServiceStatusResponse>(resp.Payload.ToByteArray());
                    ActiveConnections = status.ActiveConnections;
                    ServiceStatusText = status.ServiceState;
                    UptimeText = FormatUptime(status.UptimeSeconds);
                    AddLog($"Service state: {status.ServiceState}, Connections: {status.ActiveConnections}, Uptime: {status.UptimeSeconds}s");
                }
                else
                {
                    AddLog($"Broker returned error: {resp.ErrorMessage}");
                }
            }
            else
            {
                AddLog($"Standalone mode: checking local service via {_serviceInstaller.GetType().Name}...");
                var svc = await _serviceInstaller.GetStatusAsync();
                ServiceStatusText = svc.ToString();
                IsServiceInstalled = svc != ServiceStatus.NotInstalled;
                IsServiceRunning = svc == ServiceStatus.Running;
                AddLog($"Local service status: {svc}, IsInstalled={IsServiceInstalled}, IsRunning={IsServiceRunning}");
            }
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

    private async Task StartServiceAsync()
    {
        AddLog($"=== STARTING SERVICE ===");
        IsBusy = true;
        try
        {
            if (_modeManager.IsMultiUser && _modeManager.BrokerClient?.IsConnected == true)
            {
                AddLog("Multi-user mode: sending StartServiceRequest to broker...");
                var auth = _modeManager.AuthSession;
                var result = await _modeManager.BrokerClient.StartServiceAsync(auth?.Token ?? "");
                AddLog($"Broker response: Success={result.Success}, Message='{result.Message}', StatusCode={result.StatusCode}");

                if (result.Success)
                {
                    AddLog($"Remote start succeeded: {result.Message}");
                    IsServiceRunning = true;
                    ServiceStatusText = ServiceStatus.Running.ToString();
                    StatusMessage = result.Message;
                }
                else
                {
                    AddLog($"Remote start returned failure: {result.Message}");
                    StatusMessage = $"Remote start failed: {result.Message}";
                }
            }
            else
            {
                AddLog($"Standalone mode: using installer {_serviceInstaller.GetType().Name}");
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
            }

            AddLog($"=== SERVICE STARTED ===");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Insufficient privileges to start service");
            AddLog($"❌ ADMIN REQUIRED: {ex.Message}");
            StatusMessage = $"❌ Administrator privileges required. Restart the app as Administrator and try again.";
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
        AddLog($"=== STOPPING SERVICE ===");
        IsBusy = true;
        try
        {
            if (_modeManager.IsMultiUser && _modeManager.BrokerClient?.IsConnected == true)
            {
                AddLog("Multi-user mode: sending StopServiceRequest to broker...");
                var auth = _modeManager.AuthSession;
                var result = await _modeManager.BrokerClient.StopServiceAsync(auth?.Token ?? "");
                AddLog($"Broker response: Success={result.Success}, Message='{result.Message}', StatusCode={result.StatusCode}");

                if (result.Success)
                {
                    AddLog($"Remote stop succeeded: {result.Message}");
                    IsServiceRunning = false;
                    ServiceStatusText = ServiceStatus.Stopped.ToString();
                    StatusMessage = result.Message;
                }
                else
                {
                    AddLog($"Remote stop returned failure: {result.Message}");
                    StatusMessage = $"Remote stop failed: {result.Message}";
                }
            }
            else
            {
                AddLog($"Standalone mode: using installer {_serviceInstaller.GetType().Name}");
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
            }

            AddLog($"=== SERVICE STOPPED ===");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Insufficient privileges to stop service");
            AddLog($"❌ ADMIN REQUIRED: {ex.Message}");
            StatusMessage = $"❌ Administrator privileges required. Restart the app as Administrator and try again.";
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

    private async Task InstallServiceAsync()
    {
        AddLog($"=== SERVICE INSTALLATION STARTED ===");
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
                var msg = $"No service installer available for this platform. Cannot install service.";
                _logger.LogError("Installation aborted: {Message}", msg);
                AddLog($"FAILED: {msg}");
                StatusMessage = msg;
                return;
            }

            AddLog("Calling installer.InstallAsync...");
            await _serviceInstaller.InstallAsync(brokerPath, ServicePort);
            AddLog("Installer.InstallAsync completed successfully.");

            // Persist port/host settings after successful install
            AddLog("Persisting configuration settings...");
            var config = App.Config;
            config.ServiceHost = ServiceHost;
            config.ServicePort = ServicePort;
            config.Mode = "MultiUser";
            config.Save();
            AddLog($"Configuration saved: Mode=MultiUser, Host={ServiceHost}, Port={ServicePort}");

            StatusMessage = "Service installed and started.";
            IsServiceInstalled = true;
            IsServiceRunning = true;
            AddLog($"=== SERVICE INSTALLATION SUCCEEDED ===");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already in use"))
        {
            _logger.LogWarning(ex, "Port already in use during installation");
            AddLog($"WARNING: {ex.Message}");
            StatusMessage = $"⚠ {ex.Message}";
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Insufficient privileges for service installation");
            AddLog($"❌ ADMIN REQUIRED: {ex.Message}");
            AddLog("To install the service, close this app and restart it as Administrator:");
            AddLog("  1. Close Adam CatalogBrowser");
            AddLog("  2. Right-click the Adam CatalogBrowser shortcut or executable");
            AddLog("  3. Select 'Run as administrator'");
            AddLog("  4. Navigate to Admin Panel and click 'Install Service' again");
            StatusMessage = $"❌ Administrator privileges required. Restart the app as Administrator and try again.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Service installation failed");
            AddLog($"ERROR: {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException != null)
            {
                AddLog($"  Inner exception: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            }
            StatusMessage = $"Install error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task UninstallServiceAsync()
    {
        AddLog($"=== SERVICE UNINSTALLATION STARTED ===");
        AddLog($"Installer type: {_serviceInstaller.GetType().Name}");
        AddLog($"Installer supported: {_serviceInstaller.IsSupported}");
        AddLog($"Installer service name: '{_serviceInstaller.ServiceName}'");
        IsBusy = true;
        try
        {
            if (!_serviceInstaller.IsSupported)
            {
                var msg = $"No service installer available for this platform. Cannot uninstall service.";
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
            AddLog($"=== SERVICE UNINSTALLATION SUCCEEDED ===");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Insufficient privileges for service uninstallation");
            AddLog($"❌ ADMIN REQUIRED: {ex.Message}");
            AddLog("To uninstall the service, restart this app as Administrator:");
            AddLog("  1. Close Adam CatalogBrowser");
            AddLog("  2. Right-click the executable and select 'Run as administrator'");
            AddLog("  3. Navigate to Admin Panel and click 'Uninstall Service' again");
            StatusMessage = $"❌ Administrator privileges required. Restart the app as Administrator and try again.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Service uninstallation failed");
            AddLog($"ERROR: {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException != null)
            {
                AddLog($"  Inner exception: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            }
            StatusMessage = $"Uninstall error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OpenMigrationWizard()
    {
        NavigateToMigrationWizard?.Invoke();
    }

    private async Task LoadWatchedFoldersAsync()
    {
        if (_modeManager.BrokerClient?.IsConnected != true) return;

        IsBusy = true;
        try
        {
            var auth = _modeManager.AuthSession;
            var req = new Envelope
            {
                AuthToken = auth?.Token ?? "",
                CorrelationId = Guid.NewGuid().ToString(),
                MessageType = MessageTypeCode.ListWatchedFoldersRequest,
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new ListWatchedFoldersRequest()))
            };
            var resp = await _modeManager.BrokerClient.SendAsync(req);
            if (resp.StatusCode == 0)
            {
                var data = ProtoHelper.Deserialize<ListWatchedFoldersResponse>(resp.Payload.ToByteArray());
                WatchedFolders = new ObservableCollection<WatchedFolderItem>(
                    data.Folders.Select(f => new WatchedFolderItem
                    {
                        Id = f.Id,
                        Path = f.Path,
                        IsEnabled = f.IsEnabled
                    }));
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load watched folders: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AddWatchedFolderAsync()
    {
        if (string.IsNullOrWhiteSpace(NewWatchedFolderPath) || _modeManager.BrokerClient?.IsConnected != true) return;

        IsBusy = true;
        try
        {
            var auth = _modeManager.AuthSession;
            var req = new Envelope
            {
                AuthToken = auth?.Token ?? "",
                CorrelationId = Guid.NewGuid().ToString(),
                MessageType = MessageTypeCode.CreateWatchedFolderRequest,
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new CreateWatchedFolderRequest { Path = NewWatchedFolderPath.Trim() }))
            };
            var resp = await _modeManager.BrokerClient.SendAsync(req);
            if (resp.StatusCode == 0)
            {
                NewWatchedFolderPath = string.Empty;
                await LoadWatchedFoldersAsync();
                StatusMessage = "Watched folder added successfully.";
            }
            else
            {
                StatusMessage = $"Failed to add watched folder: {resp.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to add watched folder: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RemoveWatchedFolderAsync(object? param)
    {
        if (param is not WatchedFolderItem item || _modeManager.BrokerClient?.IsConnected != true) return;

        IsBusy = true;
        try
        {
            var auth = _modeManager.AuthSession;
            var req = new Envelope
            {
                AuthToken = auth?.Token ?? "",
                CorrelationId = Guid.NewGuid().ToString(),
                MessageType = MessageTypeCode.DeleteWatchedFolderRequest,
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new DeleteWatchedFolderRequest { Id = item.Id }))
            };
            var resp = await _modeManager.BrokerClient.SendAsync(req);
            if (resp.StatusCode == 0)
            {
                await LoadWatchedFoldersAsync();
                StatusMessage = "Watched folder removed successfully.";
            }
            else
            {
                StatusMessage = $"Failed to remove watched folder: {resp.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to remove watched folder: {ex.Message}";
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

public class WatchedFolderItem : INotifyPropertyChanged
{
    private bool _isEnabled;

    public string Id { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public bool IsEnabled
    {
        get => _isEnabled;
        set { _isEnabled = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class NullServiceInstaller : IServiceInstaller
{
    private readonly ILogger _logger;

    public string ServiceName => "None";
    public bool IsSupported => false;

    public NullServiceInstaller(ILogger<NullServiceInstaller>? logger = null)
    {
        _logger = logger ?? NullLogger<NullServiceInstaller>.Instance;
    }

    public Task InstallAsync(string brokerPath, int port, CancellationToken ct = default)
    {
        _logger.LogWarning("NullServiceInstaller.InstallAsync() — no platform installer found, throwing PlatformNotSupportedException");
        throw new PlatformNotSupportedException("No service installer available for this platform.");
    }
    public Task UninstallAsync(CancellationToken ct = default)
    {
        _logger.LogWarning("NullServiceInstaller.UninstallAsync() — no platform installer found, throwing PlatformNotSupportedException");
        throw new PlatformNotSupportedException("No service installer available for this platform.");
    }
    public Task StartAsync(CancellationToken ct = default)
    {
        _logger.LogWarning("NullServiceInstaller.StartAsync() — no platform installer found, throwing PlatformNotSupportedException");
        throw new PlatformNotSupportedException("No service installer available for this platform.");
    }
    public Task StopAsync(CancellationToken ct = default)
    {
        _logger.LogWarning("NullServiceInstaller.StopAsync() — no platform installer found, throwing PlatformNotSupportedException");
        throw new PlatformNotSupportedException("No service installer available for this platform.");
    }
    public Task<ServiceStatus> GetStatusAsync(CancellationToken ct = default)
    {
        _logger.LogWarning("NullServiceInstaller.GetStatusAsync() — returning NotInstalled");
        return Task.FromResult(ServiceStatus.NotInstalled);
    }
}
