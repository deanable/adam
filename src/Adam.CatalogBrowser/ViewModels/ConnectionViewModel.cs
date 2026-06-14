using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Adam.Shared.Data;
using Adam.Shared.Services;
using Microsoft.Extensions.Logging;
using Avalonia.Threading;
using Adam.Shared.Contracts;

namespace Adam.CatalogBrowser.ViewModels;

public class ConnectionViewModel : INotifyPropertyChanged
{
    private readonly ILogger<ConnectionViewModel> _logger;
    private readonly ModeManager _modeManager;
    private bool _isServiceMode;
    private string _serviceHost = "localhost";
    private int _servicePort = 9100;
    private bool _isConnectedToService;
    private string _serviceConnectionStatus = "Disconnected";
    private string _connectionStatusText = "Disconnected";
    private bool _showConnectionStatus;

    public ConnectionViewModel(ILogger<ConnectionViewModel> logger, ModeManager modeManager)
    {
        _logger = logger;
        _modeManager = modeManager;

        // Seed the connection fields from the saved/registry-published settings so
        // the UI shows the real endpoint instead of the hard-coded defaults.
        var cfg = App.Config;
        _serviceHost = cfg.ServiceHost;
        _servicePort = cfg.ServicePort;

        ToggleLocalModeCommand = new RelayCommand(_ => IsServiceMode = false);
        ToggleServiceModeCommand = new RelayCommand(async _ => await ShowLoginAndConnectAsync());
        ConnectToServiceCommand = new RelayCommand(async _ => await ConnectToServiceAsync(), _ => !IsConnectedToService);
        DisconnectFromServiceCommand = new RelayCommand(async _ => await DisconnectFromServiceAsync(), _ => IsConnectedToService);
        LogoutCommand = new RelayCommand(async _ => await LogoutAsync(), _ => IsConnectedToService && _modeManager.AuthSession?.IsLoggedIn == true);

        if (modeManager.BrokerClient != null)
        {
            modeManager.BrokerClient.StatusChanged += (_, status) =>
            {
                var (text, show) = status switch
                {
                    ConnectionStatus.Connected => ("Connected", true),
                    ConnectionStatus.Reconnecting => ("Reconnecting...", true),
                    ConnectionStatus.Connecting => ("Connecting...", true),
                    _ => ("Disconnected", true)
                };
                Dispatcher.UIThread.Post(() =>
                {
                    ConnectionStatusText = text;
                    ShowConnectionStatus = show;
                    ConnectToServiceCommand.RaiseCanExecuteChanged();
                    DisconnectFromServiceCommand.RaiseCanExecuteChanged();
                });
            };

            // Phase 7 T7.5: Handle server-initiated session invalidation
            // (role change, account deactivation — fired from UserHandler on the broker)
            modeManager.BrokerClient.SessionInvalidated += async (_, _) =>
            {
                _logger.LogInformation("Session invalidated by server — re-validating");
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    ServiceConnectionStatus = "Session invalidated — re-authenticating...";
                    // ValidateTokenAsync will detect role change or deactivation and fire
                    // ForceLogout if the account is no longer active (T7.4).
                    _ = ValidateSessionAsync();
                });
            };
        }
    }

    public bool IsServiceMode
    {
        get => _isServiceMode;
        set
        {
            if (_isServiceMode == value) return;
            _isServiceMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsLocalMode));
            if (!value) _ = SwitchToLocalAsync();
        }
    }

    public bool IsLocalMode => !IsServiceMode;

    public string ServiceHost
    {
        get => _serviceHost;
        set { _serviceHost = value; OnPropertyChanged(); }
    }

    public int ServicePort
    {
        get => _servicePort;
        set { _servicePort = value; OnPropertyChanged(); }
    }

    public bool IsConnectedToService
    {
        get => _isConnectedToService;
        set
        {
            _isConnectedToService = value;
            OnPropertyChanged();
            ConnectToServiceCommand.RaiseCanExecuteChanged();
            DisconnectFromServiceCommand.RaiseCanExecuteChanged();
            LogoutCommand.RaiseCanExecuteChanged();
        }
    }

    public string ServiceConnectionStatus
    {
        get => _serviceConnectionStatus;
        set { _serviceConnectionStatus = value; OnPropertyChanged(); }
    }

    public string ConnectionStatusText
    {
        get => _connectionStatusText;
        set { _connectionStatusText = value; OnPropertyChanged(); }
    }

    public bool ShowConnectionStatus
    {
        get => _showConnectionStatus;
        set { _showConnectionStatus = value; OnPropertyChanged(); }
    }

    public RelayCommand ToggleLocalModeCommand { get; }
    public RelayCommand ToggleServiceModeCommand { get; }
    public RelayCommand ConnectToServiceCommand { get; }
    public RelayCommand DisconnectFromServiceCommand { get; }
    public RelayCommand LogoutCommand { get; }

    public event Func<IAuthSession, string, int, Task<bool>>? RequestLogin;
    public event Func<Task>? RequestLocalSwitch;

    /// <summary>
    /// Raised after both stages succeed (connected AND authenticated), so the host
    /// can switch into multi-user mode and reload data from the service.
    /// </summary>
    public event Func<Task>? ServiceConnected;

    /// <summary>
    /// Raised when the server returns status code 7 (account deactivated) or
    /// the token expires, signalling the host to force a logout (T7.4).
    /// </summary>
    public event Func<Task>? ForceLogout;

    private async Task ShowLoginAndConnectAsync()
    {
        IsServiceMode = true;
        await ConnectToServiceAsync();
    }

    private async Task ConnectToServiceAsync()
    {
        var sw = Stopwatch.StartNew();

        if (_modeManager.BrokerClient == null || _modeManager.AuthSession == null)
        {
            _logger.LogError("BrokerClient or AuthSession is null");
            return;
        }

        var cfg = App.Config;

        // ── Stage 1: Connect to the server (establish the TCP/TLS session) ──
        try
        {
            ServiceConnectionStatus = $"Connecting to {ServiceHost}:{ServicePort}...";

            _modeManager.BrokerClient.Reconfigure(ServiceHost, ServicePort, cfg.UseTls, cfg.AllowSelfSigned);
            await _modeManager.BrokerClient.ConnectAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stage 1 (connect) failed for {Host}:{Port} after {ElapsedMs:F0}ms", ServiceHost, ServicePort, sw.Elapsed.TotalMilliseconds);
            ServiceConnectionStatus = $"Could not reach server at {ServiceHost}:{ServicePort} — {ex.Message}";
            return;
        }

        // Connection succeeded — remember this endpoint for next launch.
        cfg.ServiceHost = ServiceHost;
        cfg.ServicePort = ServicePort;
        cfg.Save();

        // ── Stage 2: Authenticate the user (username/password) ──
        try
        {
            ServiceConnectionStatus = $"Connected to {ServiceHost}:{ServicePort} — signing in...";

            bool authenticated = false;
            if (RequestLogin != null)
            {
                authenticated = await RequestLogin(_modeManager.AuthSession, ServiceHost, ServicePort);
            }
            else
            {
                _logger.LogWarning("RequestLogin event is null, cannot authenticate");
            }

            if (authenticated)
            {
                IsConnectedToService = true;
                ServiceConnectionStatus = $"Connected to {ServiceHost}:{ServicePort}";
                // Switch the app into multi-user mode and reload from the service.
                if (ServiceConnected != null)
                    await ServiceConnected();
            }
            else
            {
                _logger.LogWarning("Connected but not authenticated, disconnecting");
                await _modeManager.BrokerClient.DisconnectAsync();
                ServiceConnectionStatus = "Sign-in cancelled — click Connect to try again";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stage 2 (authenticate) failed after {ElapsedMs:F0}ms", sw.Elapsed.TotalMilliseconds);
            await _modeManager.BrokerClient.DisconnectAsync();
            ServiceConnectionStatus = $"Connected, but sign-in failed: {ex.Message}";
        }
    }

    private async Task DisconnectFromServiceAsync()
    {
        if (_modeManager.BrokerClient != null)
            await _modeManager.BrokerClient.DisconnectAsync();

        IsConnectedToService = false;
        ServiceConnectionStatus = "Disconnected";
    }

    public async Task LogoutAsync()
    {
        _modeManager.AuthSession?.Logout();
        await DisconnectFromServiceAsync();
    }

    /// <summary>
    /// Validates the current session with the broker.
    /// If the account was deactivated, fires ForceLogout (T7.4, T7.5).
    /// Returns the current user profile, or null if invalid.
    /// </summary>
    public async Task<UserProfile?> ValidateSessionAsync(CancellationToken ct = default)
    {
        var auth = _modeManager.AuthSession;
        if (auth == null || !auth.IsLoggedIn)
            return null;

        var profile = await auth.ValidateTokenAsync(ct);
        if (profile == null && auth.CurrentUser == null)
        {
            // Account was deactivated — fire forced logout
            _logger.LogWarning("Session validation failed — account may be deactivated");
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (ForceLogout != null)
                    await ForceLogout();
            });
        }

        return profile;
    }

    private async Task SwitchToLocalAsync()
    {
        await DisconnectFromServiceAsync();
        if (RequestLocalSwitch != null) await RequestLocalSwitch();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
