using System.ComponentModel;
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

    public AdminPanelViewModel(ModeManager modeManager, IEnumerable<IServiceInstaller> serviceInstallers)
    {
        _modeManager = modeManager;
        _selectedMode = modeManager.Mode;
        _serviceInstaller = serviceInstallers.FirstOrDefault(s => s.IsSupported) ?? new NullServiceInstaller();

        SaveModeCommand = new RelayCommand(async _ => await SaveModeAsync());
        RefreshStatusCommand = new RelayCommand(async _ => await RefreshStatusAsync());
        InstallServiceCommand = new RelayCommand(async _ => await InstallServiceAsync(), _ => !_isServiceInstalled);
        UninstallServiceCommand = new RelayCommand(async _ => await UninstallServiceAsync(), _ => _isServiceInstalled);
        OpenMigrationWizardCommand = new RelayCommand(_ => OpenMigrationWizard());
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

    public event Action? NavigateToMigrationWizard;

    private async Task SaveModeAsync()
    {
        if (SelectedMode == "Standalone")
            await _modeManager.InitializeAsync();
        else
            await _modeManager.InitializeMultiUserAsync("localhost", 9100);

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
                    MessageType = nameof(GetServiceStatusRequest),
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
            await _serviceInstaller.InstallAsync(brokerPath);
            StatusMessage = "Service installed and started.";
            IsServiceInstalled = true;
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

public sealed class NullServiceInstaller : IServiceInstaller
{
    public string ServiceName => "None";
    public bool IsSupported => false;
    public Task InstallAsync(string brokerPath, CancellationToken ct = default)
        => throw new PlatformNotSupportedException("No service installer available for this platform.");
    public Task UninstallAsync(CancellationToken ct = default)
        => throw new PlatformNotSupportedException("No service installer available for this platform.");
    public Task<ServiceStatus> GetStatusAsync(CancellationToken ct = default)
        => Task.FromResult(ServiceStatus.NotInstalled);
}
