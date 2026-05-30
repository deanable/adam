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
    private string _newWatchedFolderPath = string.Empty;
    private ObservableCollection<WatchedFolderItem> _watchedFolders = new();
    private string _serviceHost = "localhost";
    private int _servicePort = 9100;
    private ObservableCollection<string> _logMessages = new();

    public AdminPanelViewModel(ModeManager modeManager, IEnumerable<IServiceInstaller> serviceInstallers, ILogger<AdminPanelViewModel>? logger = null)
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

        // Load persisted settings
        var config = App.Config;
        _serviceHost = config.ServiceHost;
        _servicePort = config.ServicePort;

        SaveModeCommand = new RelayCommand(async _ => await SaveModeAsync());
        RefreshStatusCommand = new RelayCommand(async _ => await RefreshStatusAsync());
        InstallServiceCommand = new RelayCommand(async _ => await InstallServiceAsync(), _ => !_isServiceInstalled);
        UninstallServiceCommand = new RelayCommand(async _ => await UninstallServiceAsync(), _ => _isServiceInstalled);
        OpenMigrationWizardCommand = new RelayCommand(_ => OpenMigrationWizard());
        AddWatchedFolderCommand = new RelayCommand(async _ => await AddWatchedFolderAsync(), _ => !string.IsNullOrWhiteSpace(NewWatchedFolderPath));
        RemoveWatchedFolderCommand = new RelayCommand(async param => await RemoveWatchedFolderAsync(param), _ => true);
        RefreshWatchedFoldersCommand = new RelayCommand(async _ => await LoadWatchedFoldersAsync());
        ClearLogCommand = new RelayCommand(_ => LogMessages.Clear());

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

    public bool IsServiceInstalled
    {
        get => _isServiceInstalled;
        set
        {
            _isServiceInstalled = value;
            OnPropertyChanged();
            ((RelayCommand)InstallServiceCommand).RaiseCanExecuteChanged();
            ((RelayCommand)UninstallServiceCommand).RaiseCanExecuteChanged();
        }
    }

    public ICommand SaveModeCommand { get; }
    public ICommand RefreshStatusCommand { get; }
    public ICommand InstallServiceCommand { get; }
    public ICommand UninstallServiceCommand { get; }
    public ICommand OpenMigrationWizardCommand { get; }
    public ICommand AddWatchedFolderCommand { get; }
    public ICommand RemoveWatchedFolderCommand { get; }
    public ICommand RefreshWatchedFoldersCommand { get; }
    public ICommand ClearLogCommand { get; }

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
                AddLog($"Local service status: {svc}, IsInstalled={IsServiceInstalled}");
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

    private async Task InstallServiceAsync()
    {
        AddLog($"=== SERVICE INSTALLATION STARTED ===");
        AddLog($"Installer type: {_serviceInstaller.GetType().Name}");
        AddLog($"Installer supported: {_serviceInstaller.IsSupported}");
        AddLog($"Installer service name: '{_serviceInstaller.ServiceName}'");
        IsBusy = true;
        try
        {
            var brokerPath = Environment.ProcessPath ?? "";
            AddLog($"Broker process path: '{brokerPath}'");
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
            AddLog($"ACCESS DENIED: {ex.Message}");
            StatusMessage = $"Access denied: {ex.Message}";
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
            AddLog($"=== SERVICE UNINSTALLATION SUCCEEDED ===");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Insufficient privileges for service uninstallation");
            AddLog($"ACCESS DENIED: {ex.Message}");
            StatusMessage = $"Access denied: {ex.Message}";
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
    public Task<ServiceStatus> GetStatusAsync(CancellationToken ct = default)
    {
        _logger.LogWarning("NullServiceInstaller.GetStatusAsync() — returning NotInstalled");
        return Task.FromResult(ServiceStatus.NotInstalled);
    }
}
