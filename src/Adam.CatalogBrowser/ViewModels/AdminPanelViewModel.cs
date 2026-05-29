using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Adam.CatalogBrowser.Services;
using Adam.Shared.Contracts;
using Adam.Shared.Services;
using Google.Protobuf;

namespace Adam.CatalogBrowser.ViewModels;

public class AdminPanelViewModel : INotifyPropertyChanged
{
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

    public AdminPanelViewModel(ModeManager modeManager, IEnumerable<IServiceInstaller> serviceInstallers)
    {
        _modeManager = modeManager;
        _selectedMode = modeManager.Mode;
        var installers = serviceInstallers.ToList();
        _serviceInstaller = installers.FirstOrDefault(s => s.IsSupported) ?? new NullServiceInstaller();
        Debug.WriteLine($"[adam] AdminPanelViewModel: Selected service installer = {_serviceInstaller.GetType().Name} (IsSupported={_serviceInstaller.IsSupported}, ServiceName='{_serviceInstaller.ServiceName}')");
        Debug.WriteLine($"[adam] AdminPanelViewModel: Available installers count = {installers.Count}");
        foreach (var installer in installers)
        {
            Debug.WriteLine($"[adam]   Installer: {installer.GetType().Name}, IsSupported={installer.IsSupported}, ServiceName='{installer.ServiceName}'");
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

    private async Task SaveModeAsync()
    {
        if (SelectedMode == "Standalone")
            await _modeManager.InitializeAsync();
        else
            await _modeManager.InitializeMultiUserAsync(ServiceHost, ServicePort);

        // Persist settings
        var config = App.Config;
        config.Mode = SelectedMode;
        config.ServiceHost = ServiceHost;
        config.ServicePort = ServicePort;
        config.Save();

        StatusMessage = $"Mode switched to {SelectedMode}. Restart to apply.";
    }

    private async Task RefreshStatusAsync()
    {
        IsBusy = true;
        try
        {
            if (_modeManager.IsMultiUser && _modeManager.BrokerClient?.IsConnected == true)
            {
                var auth = _modeManager.AuthSession;
                var req = new Envelope
                {
                    AuthToken = auth?.Token ?? "",
                    CorrelationId = Guid.NewGuid().ToString(),
                    MessageType = MessageTypeCode.GetServiceStatusRequest,
                    Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new GetServiceStatusRequest()))
                };
                var resp = await _modeManager.BrokerClient.SendAsync(req);
                if (resp.StatusCode == 0)
                {
                    var status = ProtoHelper.Deserialize<GetServiceStatusResponse>(resp.Payload.ToByteArray());
                    ActiveConnections = status.ActiveConnections;
                    ServiceStatusText = status.ServiceState;
                    UptimeText = FormatUptime(status.UptimeSeconds);
                }
            }
            else
            {
                var svc = await _serviceInstaller.GetStatusAsync();
                ServiceStatusText = svc.ToString();
                IsServiceInstalled = svc != ServiceStatus.NotInstalled;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Status error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task InstallServiceAsync()
    {
        IsBusy = true;
        try
        {
            var brokerPath = Environment.ProcessPath ?? "";
            await _serviceInstaller.InstallAsync(brokerPath, ServicePort);

            // Persist port/host settings after successful install
            var config = App.Config;
            config.ServiceHost = ServiceHost;
            config.ServicePort = ServicePort;
            config.Mode = "MultiUser";
            config.Save();

            StatusMessage = "Service installed and started.";
            IsServiceInstalled = true;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already in use"))
        {
            StatusMessage = $"⚠ {ex.Message}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Install error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task UninstallServiceAsync()
    {
        IsBusy = true;
        try
        {
            await _serviceInstaller.UninstallAsync();
            StatusMessage = "Service stopped and removed.";
            IsServiceInstalled = false;
        }
        catch (Exception ex)
        {
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
    public string ServiceName => "None";
    public bool IsSupported => false;
    public Task InstallAsync(string brokerPath, int port, CancellationToken ct = default)
    {
        Debug.WriteLine("[adam] NullServiceInstaller.InstallAsync() — no platform installer found, throwing PlatformNotSupportedException");
        throw new PlatformNotSupportedException("No service installer available for this platform.");
    }
    public Task UninstallAsync(CancellationToken ct = default)
    {
        Debug.WriteLine("[adam] NullServiceInstaller.UninstallAsync() — no platform installer found, throwing PlatformNotSupportedException");
        throw new PlatformNotSupportedException("No service installer available for this platform.");
    }
    public Task<ServiceStatus> GetStatusAsync(CancellationToken ct = default)
    {
        Debug.WriteLine("[adam] NullServiceInstaller.GetStatusAsync() — returning NotInstalled");
        return Task.FromResult(ServiceStatus.NotInstalled);
    }
}
