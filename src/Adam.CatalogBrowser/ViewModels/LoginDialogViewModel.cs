using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using RelayCommand = Adam.Shared.Services.RelayCommand;

namespace Adam.CatalogBrowser.ViewModels;

/// <summary>
/// ViewModel for the login dialog shown when connecting to a broker service.
/// Collects service address, port, username, and password from the user.
/// Uses an <see cref="AuthenticateAsync"/> delegate to attempt authentication;
/// on failure the dialog stays open so the user can retry.
/// A separate <see cref="TestConnectionAsync"/> delegate verifies host/port
/// reachability before attempting login.
/// </summary>
public class LoginDialogViewModel : INotifyPropertyChanged
{
    private string _serviceHost = "localhost";
    private int _servicePort = 9100;
    private string _username = string.Empty;
    private string _password = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isLoggingIn;
    private bool _loginSucceeded;
    private bool _isTestingConnection;
    private string _connectionTestStatus = string.Empty;

    public LoginDialogViewModel()
    {
        LoginCommand = new RelayCommand(_ => ExecuteLogin(), _ => CanLogin);
        TestConnectionCommand = new RelayCommand(_ => ExecuteTestConnection(), _ => CanTestConnection);
        ClearCredentialsCommand = new RelayCommand(_ => ExecuteClearCredentials());
    }

    /// <summary>
    /// Delegate that performs the actual authentication.
    /// Takes (host, port, username, password) and returns null on success,
    /// or an error message string on failure.
    /// If null, the dialog closes with success. If set, the error is shown inline.
    /// </summary>
    public Func<string, int, string, string, Task<string?>>? AuthenticateAsync { get; set; }

    /// <summary>
    /// Delegate that tests host/port reachability.
    /// Takes (host, port) and returns null if reachable,
    /// or an error message string if unreachable.
    /// </summary>
    public Func<string, int, Task<string?>>? TestConnectionAsync { get; set; }

    /// <summary>
    /// Delegate that clears persisted credentials (host, port, username, recent hosts)
    /// in the application config. Called by <see cref="ClearCredentialsCommand"/>.
    /// Expected to persist the changes to disk.
    /// </summary>
    public Func<Task>? ClearCredentialsAsync { get; set; }

    /// <summary>
    /// Recently used servers in "host:port" format, for the dropdown.
    /// </summary>
    public ObservableCollection<string> RecentHosts { get; set; } = [];

    private string? _selectedRecentHost;

    /// <summary>
    /// When set from the dropdown, parses "host:port" and updates
    /// <see cref="ServiceHost"/> and <see cref="ServicePort"/> accordingly.
    /// </summary>
    public string? SelectedRecentHost
    {
        get => _selectedRecentHost;
        set
        {
            if (value == null || value == _selectedRecentHost) return;
            _selectedRecentHost = value;
            OnPropertyChanged();

            var colonIdx = value.LastIndexOf(':');
            if (colonIdx > 0 && int.TryParse(value.AsSpan(colonIdx + 1), out var port))
            {
                ServiceHost = value[..colonIdx];
                ServicePort = port;
            }
            else
            {
                // No port separator — treat the whole value as the host
                ServiceHost = value;
            }
        }
    }

    public string ServiceHost
    {
        get => _serviceHost;
        set
        {
            _serviceHost = value;
            OnPropertyChanged();
            ((RelayCommand)LoginCommand).RaiseCanExecuteChanged();
            ((RelayCommand)TestConnectionCommand).RaiseCanExecuteChanged();
            // Clear previous test result when host changes
            ConnectionTestStatus = string.Empty;
        }
    }

    public int ServicePort
    {
        get => _servicePort;
        set
        {
            _servicePort = value;
            OnPropertyChanged();
            ((RelayCommand)TestConnectionCommand).RaiseCanExecuteChanged();
            // Clear previous test result when port changes
            ConnectionTestStatus = string.Empty;
        }
    }

    public string Username
    {
        get => _username;
        set
        {
            _username = value;
            OnPropertyChanged();
            ((RelayCommand)LoginCommand).RaiseCanExecuteChanged();
        }
    }

    public string Password
    {
        get => _password;
        set
        {
            _password = value;
            OnPropertyChanged();
            ((RelayCommand)LoginCommand).RaiseCanExecuteChanged();
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set { _errorMessage = value; OnPropertyChanged(); }
    }

    public bool IsLoggingIn
    {
        get => _isLoggingIn;
        set
        {
            _isLoggingIn = value;
            OnPropertyChanged();
            ((RelayCommand)LoginCommand).RaiseCanExecuteChanged();
        }
    }

    public bool IsTestingConnection
    {
        get => _isTestingConnection;
        set
        {
            _isTestingConnection = value;
            OnPropertyChanged();
            ((RelayCommand)TestConnectionCommand).RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// Result text from the connection test.
    /// Empty string means no test has been performed yet.
    /// Non-empty with a green indicator means reachable;
    /// Non-empty with an error prefix means unreachable.
    /// </summary>
    public string ConnectionTestStatus
    {
        get => _connectionTestStatus;
        set
        {
            _connectionTestStatus = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasConnectionTestResult));
            OnPropertyChanged(nameof(ConnectionTestSuccessful));
            OnPropertyChanged(nameof(ConnectionTestIcon));
            OnPropertyChanged(nameof(ConnectionTestForeground));
        }
    }

    /// <summary>
    /// True when a connection test result is available.
    /// </summary>
    public bool HasConnectionTestResult => !string.IsNullOrEmpty(_connectionTestStatus);

    /// <summary>
    /// True if the last connection test was successful.
    /// </summary>
    public bool ConnectionTestSuccessful =>
        !string.IsNullOrEmpty(_connectionTestStatus) &&
        !_connectionTestStatus.StartsWith("✗");

    /// <summary>
    /// Icon character ("✓" or "✗") based on the connection test result.
    /// Returns empty string when no test has been performed.
    /// </summary>
    public string ConnectionTestIcon =>
        string.IsNullOrEmpty(_connectionTestStatus) ? string.Empty :
        ConnectionTestSuccessful ? "✓" : "✗";

    /// <summary>
    /// Foreground color (green/red) based on <see cref="ConnectionTestSuccessful"/>.
    /// Returns empty string when no test result is available.
    /// </summary>
    public string ConnectionTestForeground =>
        string.IsNullOrEmpty(_connectionTestStatus) ? string.Empty :
        ConnectionTestSuccessful ? "#2E7D32" : "#D32F2F";

    /// <summary>
    /// True once authentication succeeds.
    /// The dialog code-behind watches this property and closes with <c>true</c>.
    /// </summary>
    public bool LoginSucceeded
    {
        get => _loginSucceeded;
        set { _loginSucceeded = value; OnPropertyChanged(); }
    }

    public bool CanLogin =>
        !string.IsNullOrWhiteSpace(ServiceHost) &&
        ServicePort > 0 &&
        ServicePort <= 65535 &&
        !string.IsNullOrWhiteSpace(Username) &&
        !string.IsNullOrWhiteSpace(Password) &&
        !IsLoggingIn &&
        !IsTestingConnection;

    public bool CanTestConnection =>
        !string.IsNullOrWhiteSpace(ServiceHost) &&
        ServicePort > 0 &&
        ServicePort <= 65535 &&
        !IsTestingConnection &&
        !IsLoggingIn;

    public ICommand LoginCommand { get; }
    public ICommand TestConnectionCommand { get; }
    public ICommand ClearCredentialsCommand { get; }

    private async void ExecuteLogin()
    {
        if (!CanLogin) return;

        IsLoggingIn = true;
        ErrorMessage = string.Empty;

        try
        {
            if (AuthenticateAsync == null)
            {
                // No authenticator configured — just close (legacy fallback)
                LoginSucceeded = true;
                return;
            }

            var error = await AuthenticateAsync(ServiceHost, ServicePort, Username, Password);

            if (error == null)
            {
                // Authentication succeeded — dialog will close
                LoginSucceeded = true;
            }
            else
            {
                // Authentication failed — show error inline, keep dialog open
                ErrorMessage = error;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Unexpected error: {ex.Message}";
        }
        finally
        {
            IsLoggingIn = false;
        }
    }

    private async void ExecuteTestConnection()
    {
        if (!CanTestConnection) return;

        IsTestingConnection = true;
        ConnectionTestStatus = string.Empty;
        ErrorMessage = string.Empty;

        try
        {
            if (TestConnectionAsync == null)
            {
                ConnectionTestStatus = "No test method configured.";
                return;
            }

            var error = await TestConnectionAsync(ServiceHost, ServicePort);

            ConnectionTestStatus = error == null
                ? "✓ Service is reachable"
                : $"✗ {error}";
        }
        catch (Exception ex)
        {
            ConnectionTestStatus = $"✗ Unexpected error: {ex.Message}";
        }
        finally
        {
            IsTestingConnection = false;
        }
    }

    private async void ExecuteClearCredentials()
    {
        try
        {
            if (ClearCredentialsAsync != null)
                await ClearCredentialsAsync();
        }
        catch
        {
            // Delegate failure won't prevent local field reset
        }

        // Reset local fields to defaults
        ServiceHost = "localhost";
        ServicePort = 9100;
        Username = string.Empty;
        Password = string.Empty;
        ErrorMessage = string.Empty;
        ConnectionTestStatus = string.Empty;
        RecentHosts.Clear();

        // Clear the selected recent host so it stays in sync with the empty collection
        if (_selectedRecentHost != null)
        {
            _selectedRecentHost = null;
            OnPropertyChanged(nameof(SelectedRecentHost));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
