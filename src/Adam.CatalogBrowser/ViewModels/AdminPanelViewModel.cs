using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
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

public class AdminPanelViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ILogger<AdminPanelViewModel> _logger;
    private readonly ModeManager _modeManager;
    private readonly MigrationWizardViewModel _migrationWizard;

    // Mode state
    private bool _isServiceMode;
    private string _serviceHost = "localhost";
    private int _servicePort = 9100;
    private bool _isConnectedToService;
    private string _connectionStatus = "Disconnected";

    // Service status
    private int _connectedClients;
    private string _uptime = "—";
    private string _serviceState = "Unknown";
    private bool _isServiceStatusAvailable;
    private string _statusError = string.Empty;

    // UI state
    private bool _isMigrationWizardVisible;
    private bool _isBusy;
    private string _statusText = string.Empty;

    // Auto-refresh
    private readonly DispatcherTimer _statusRefreshTimer;
    private readonly EventHandler _timerTickHandler;
    private volatile bool _disposed;

    /// <summary>
    /// Creates an AdminPanelViewModel. ModeManager is required and should be the shared singleton instance.
    /// </summary>
    public AdminPanelViewModel(ModeManager modeManager, ILogger<AdminPanelViewModel>? logger = null)
    {
        _logger = logger ?? NullLogger<AdminPanelViewModel>.Instance;
        _modeManager = modeManager ?? throw new ArgumentNullException(nameof(modeManager));
        _migrationWizard = new MigrationWizardViewModel(_modeManager);

        // Commands
        ToggleLocalCommand = new RelayCommand(_ => SwitchToLocal());
        ToggleServiceCommand = new RelayCommand(async _ => await ConnectToServiceAsync());
        ConnectCommand = new RelayCommand(async _ => await ConnectToServiceWithLoginAsync(), _ => !IsConnectedToService);
        DisconnectCommand = new RelayCommand(async _ => await DisconnectFromServiceAsync(), _ => IsConnectedToService);
        LaunchServiceManagerCommand = new RelayCommand(_ => LaunchServiceManager());
        RefreshServiceStatusCommand = new RelayCommand(async _ => await RefreshServiceStatusAsync());
        ToggleMigrationWizardCommand = new RelayCommand(_ => IsMigrationWizardVisible = !IsMigrationWizardVisible);

        // Auto-refresh timer: polls every 10 seconds when admin panel is active
        _statusRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _timerTickHandler = async (_, _) =>
        {
            if (!_disposed && IsServiceMode && IsConnectedToService)
                await RefreshServiceStatusAsync();
        };
        _statusRefreshTimer.Tick += _timerTickHandler;
        _statusRefreshTimer.Start();

        // Load initial state from ModeManager
        _isServiceMode = _modeManager.Mode == "MultiUser";
        _isConnectedToService = _modeManager.IsConnected;
        _connectionStatus = _isConnectedToService ? "Connected" : "Disconnected";
    }

    // ─── Mode Properties ────────────────────────────────────────────

    public bool IsServiceMode
    {
        get => _isServiceMode;
        set
        {
            if (_isServiceMode == value) return;
            _isServiceMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsLocalMode));
            OnPropertyChanged(nameof(ModeLabel));
        }
    }

    public bool IsLocalMode => !_isServiceMode;
    public string ModeLabel => IsServiceMode ? "Multi-User" : "Standalone";

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

    public bool IsConnectedToService
    {
        get => _isConnectedToService;
        set
        {
            _isConnectedToService = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ConnectionIndicatorColor));
            ((RelayCommand)ConnectCommand).RaiseCanExecuteChanged();
            ((RelayCommand)DisconnectCommand).RaiseCanExecuteChanged();
        }
    }

    public string ConnectionStatus
    {
        get => _connectionStatus;
        set { _connectionStatus = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Traffic-light color for connection indicator.
    /// </summary>
    public string ConnectionIndicatorColor => IsConnectedToService ? "#4CAF50" : "#F44336";

    // ─── Service Status Properties ───────────────────────────────────

    public int ConnectedClients
    {
        get => _connectedClients;
        set { _connectedClients = value; OnPropertyChanged(); }
    }

    public string Uptime
    {
        get => _uptime;
        set { _uptime = value; OnPropertyChanged(); }
    }

    public string ServiceState
    {
        get => _serviceState;
        set
        {
            _serviceState = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ServiceStateColor));
            OnPropertyChanged(nameof(ServiceStateLabel));
        }
    }

    /// <summary>
    /// Traffic-light color for service state.
    /// </summary>
    public string ServiceStateColor => ServiceState switch
    {
        "Running" => "#4CAF50",
        "Stopped" => "#F44336",
        "Unknown" => "#FF9800",
        _ => "#9E9E9E"
    };

    public string ServiceStateLabel => ServiceState switch
    {
        "Running" => "Running",
        "Stopped" => "Stopped",
        "Unknown" => "Unknown",
        _ => "Not Available"
    };

    public bool IsServiceStatusAvailable
    {
        get => _isServiceStatusAvailable;
        set { _isServiceStatusAvailable = value; OnPropertyChanged(); }
    }

    public string StatusError
    {
        get => _statusError;
        set { _statusError = value; OnPropertyChanged(); }
    }

    // ─── UI State ──────────────────────────────────────────────────

    public bool IsMigrationWizardVisible
    {
        get => _isMigrationWizardVisible;
        set { _isMigrationWizardVisible = value; OnPropertyChanged(); }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set { _isBusy = value; OnPropertyChanged(); }
    }

    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    // ─── Commands ──────────────────────────────────────────────────

    public ICommand ToggleLocalCommand { get; }
    public ICommand ToggleServiceCommand { get; }
    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand LaunchServiceManagerCommand { get; }
    public ICommand RefreshServiceStatusCommand { get; }
    public MigrationWizardViewModel MigrationWizard => _migrationWizard;

    public ICommand ToggleMigrationWizardCommand { get; }

    // ─── Mode Switching ─────────────────────────────────────────────

    private void SwitchToLocal()
    {
        if (IsLocalMode) return;

        try
        {
            StatusText = "Switching to local mode...";
            IsBusy = true;

            // Disconnect broker if connected
            if (_modeManager.BrokerClient?.IsConnected == true)
            {
                _modeManager.BrokerClient.DisconnectAsync().GetAwaiter().GetResult();
            }

            _modeManager.InitializeAsync().GetAwaiter().GetResult();

            IsServiceMode = false;
            IsConnectedToService = false;
            ConnectionStatus = "Disconnected";

            App.Config.Mode = "Standalone";
            App.Config.Save();

            StatusText = "Switched to local mode";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to switch to local mode");
            StatusText = $"Failed to switch: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ConnectToServiceAsync()
    {
        if (IsServiceMode) return;

        // Show login dialog for connecting
        await ConnectToServiceWithLoginAsync();
    }

    private async Task ConnectToServiceWithLoginAsync()
    {
        if (IsConnectedToService) return;

        try
        {
            StatusText = "Connecting to service...";
            IsBusy = true;

            await _modeManager.InitializeMultiUserAsync(_serviceHost, _servicePort);

            if (_modeManager.BrokerClient == null || _modeManager.AuthSession == null)
            {
                StatusText = "Broker client not available";
                return;
            }

            await _modeManager.BrokerClient.ConnectAsync();

            // Show login dialog on UI thread
            var authenticated = await Dispatcher.UIThread.InvokeAsync(() =>
                TryShowLoginDialogAsync(_modeManager.AuthSession, _serviceHost, _servicePort));

            if (authenticated)
            {
                IsServiceMode = true;
                IsConnectedToService = true;
                ConnectionStatus = $"Connected to {_serviceHost}:{_servicePort}";
                StatusText = "Connected to service";

                // Persist config
                App.Config.Mode = "MultiUser";
                App.Config.ServiceHost = _serviceHost;
                App.Config.ServicePort = _servicePort;
                App.Config.Save();

                // Start status polling
                await RefreshServiceStatusAsync();
            }
            else
            {
                // Login cancelled or failed — disconnect broker
                await _modeManager.BrokerClient.DisconnectAsync();
                ConnectionStatus = "Login cancelled";
                StatusText = "Connection cancelled";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to service");
            ConnectionStatus = $"Connection failed: {ex.Message}";
            StatusText = "Connection failed";

            try
            {
                if (_modeManager.BrokerClient?.IsConnected == true)
                    await _modeManager.BrokerClient.DisconnectAsync();
            }
            catch { /* best-effort cleanup */ }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DisconnectFromServiceAsync()
    {
        try
        {
            StatusText = "Disconnecting from service...";
            IsBusy = true;

            if (_modeManager.BrokerClient?.IsConnected == true)
            {
                await _modeManager.BrokerClient.DisconnectAsync();
            }

            IsConnectedToService = false;
            ConnectionStatus = "Disconnected";
            IsServiceStatusAvailable = false;
            StatusText = "Disconnected from service";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to disconnect from service");
            StatusText = $"Disconnect failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ─── Service Status ─────────────────────────────────────────────

    public async Task RefreshServiceStatusAsync()
    {
        if (!IsServiceMode || !IsConnectedToService)
        {
            IsServiceStatusAvailable = false;
            return;
        }

        try
        {
            var broker = _modeManager.BrokerClient;
            var auth = _modeManager.AuthSession;

            if (broker == null || auth == null || !broker.IsConnected)
            {
                IsServiceStatusAvailable = false;
                StatusError = "Not connected";
                return;
            }

            var corrId = Guid.NewGuid().ToString();
            var req = new Envelope
            {
                AuthToken = auth.Token ?? "",
                CorrelationId = corrId,
                MessageType = MessageTypeCode.GetServiceStatusRequest,
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new GetServiceStatusRequest()))
            };

            var resp = await broker.SendAsync(req);

            if (resp.StatusCode == 0)
            {
                var status = ProtoHelper.Deserialize<GetServiceStatusResponse>(resp.Payload.ToByteArray());

                ConnectedClients = status.ActiveConnections;
                ServiceState = status.ServiceState ?? "Unknown";
                Uptime = FormatUptime(status.UptimeSeconds);
                IsServiceStatusAvailable = true;
                StatusError = string.Empty;
            }
            else
            {
                StatusError = $"Error {resp.StatusCode}: {resp.ErrorMessage}";
                IsServiceStatusAvailable = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh service status");
            StatusError = $"Status unavailable: {ex.Message}";
            IsServiceStatusAvailable = false;
        }
    }

    private static string FormatUptime(long seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalDays >= 1
            ? $"{ts.Days}d {ts.Hours}h {ts.Minutes}m"
            : $"{ts.Hours}h {ts.Minutes}m {ts.Seconds}s";
    }

    // ─── Service Manager Launcher ───────────────────────────────────

    private void LaunchServiceManager()
    {
        try
        {
            var path = ResolveServiceManagerPath();
            if (string.IsNullOrEmpty(path))
            {
                StatusText = "Service Manager executable not found.";
                return;
            }

            var psi = new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(path)!
            };

            if (OperatingSystem.IsWindows())
            {
                psi.Verb = "runas";
            }

            Process.Start(psi);
            _logger.LogInformation("Launched Service Manager: {Path}", path);
            StatusText = "Service Manager opened";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch Service Manager");
            StatusText = $"Failed to launch Service Manager: {ex.Message}";
        }
    }

    private static string ResolveServiceManagerPath()
    {
        var baseDir = AppContext.BaseDirectory;
        var extensions = OperatingSystem.IsWindows() ? new[] { ".exe", ".dll" } : new[] { "", ".dll" };

        foreach (var ext in extensions)
        {
            var path = Path.Combine(baseDir, $"Adam.ServiceManager{ext}");
            if (File.Exists(path))
                return path;
        }

        var siblingDir = Path.GetFullPath(Path.Combine(baseDir, "..", "ServiceManager"));
        foreach (var ext in extensions)
        {
            var path = Path.Combine(siblingDir, $"Adam.ServiceManager{ext}");
            if (File.Exists(path))
                return path;
        }

        var dir = new DirectoryInfo(baseDir);
        while (dir != null)
        {
            var hasGit = Directory.Exists(Path.Combine(dir.FullName, ".git"));
            var hasSln = dir.GetFiles("*.sln*").Length > 0;

            if (hasGit || hasSln)
            {
                foreach (var config in new[] { "Release", "Debug" })
                {
                    var candidate = Path.Combine(
                        dir.FullName, "src", "Adam.ServiceManager",
                        "bin", config, "net10.0", "Adam.ServiceManager.exe");
                    if (File.Exists(candidate))
                        return candidate;

                    var dllCandidate = Path.Combine(
                        dir.FullName, "src", "Adam.ServiceManager",
                        "bin", config, "net10.0", "Adam.ServiceManager.dll");
                    if (File.Exists(dllCandidate))
                        return dllCandidate;
                }
                break;
            }
            dir = dir.Parent;
        }

        return string.Empty;
    }

    // ─── Login Dialog ──────────────────────────────────────────────

    private static bool TryShowLoginDialogAsync(IAuthSession authSession, string host, int port)
    {
        var cfg = App.Config;
        var recentHosts = new ObservableCollection<string>(cfg.RecentHosts);
        var loginVm = new LoginDialogViewModel
        {
            ServiceHost = host,
            ServicePort = port,
            Username = cfg.LastUsername,
            RecentHosts = recentHosts,
            TestConnectionAsync = async (h, p) =>
            {
                try
                {
                    using var client = new System.Net.Sockets.TcpClient();
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    await client.ConnectAsync(h, p, cts.Token);
                    return null;
                }
                catch (Exception ex)
                {
                    return ex.Message;
                }
            },
            ClearCredentialsAsync = () =>
            {
                cfg.ServiceHost = "localhost";
                cfg.ServicePort = 9100;
                cfg.LastUsername = string.Empty;
                cfg.RecentHosts.Clear();
                cfg.Save();
                return Task.CompletedTask;
            },
            AuthenticateAsync = async (_, _, username, password) =>
            {
                try
                {
                    var ok = await authSession.LoginAsync(username, password);
                    return ok ? null : "Authentication failed. Check your credentials.";
                }
                catch (Exception ex)
                {
                    return ex.Message;
                }
            }
        };

        var loginDialog = new Views.LoginDialog(loginVm);
        var mainWindow = App.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

        if (mainWindow == null)
            return false;

        var loginResult = loginDialog.ShowDialog<bool?>(mainWindow).GetAwaiter().GetResult();
        if (loginResult == true)
        {
            cfg.LastUsername = loginVm.Username;
            cfg.PushRecentHost(loginVm.ServiceHost, loginVm.ServicePort);
            cfg.Save();
        }
        return loginResult == true;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    // ─── IDisposable ──────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _statusRefreshTimer.Stop();
        if (_timerTickHandler != null)
            _statusRefreshTimer.Tick -= _timerTickHandler;

        GC.SuppressFinalize(this);
    }
}

