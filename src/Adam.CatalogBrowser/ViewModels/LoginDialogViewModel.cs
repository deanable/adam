using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Adam.Shared.Services;
using RelayCommand = Adam.Shared.Services.RelayCommand;

namespace Adam.CatalogBrowser.ViewModels;

/// <summary>
/// ViewModel for the login dialog shown when connecting to a broker service.
/// Validates credentials via <see cref="IAuthSession.LoginAsync"/> and reports
/// success/failure through <see cref="LoginResult"/>.
/// </summary>
public class LoginDialogViewModel : INotifyPropertyChanged
{
    private readonly IAuthSession _authSession;
    private string _username = string.Empty;
    private string _password = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isLoggingIn;
    private bool _loginSucceeded;

    public LoginDialogViewModel(IAuthSession authSession)
    {
        _authSession = authSession;
        LoginCommand = new RelayCommand(async _ => await LoginAsync(), _ => CanLogin);
    }

    /// <summary>
    /// The hostname or IP of the broker service (displayed for context).
    /// </summary>
    public string ServiceHost { get; set; } = "localhost";

    /// <summary>
    /// The port of the broker service (displayed for context).
    /// </summary>
    public int ServicePort { get; set; } = 9100;

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

    /// <summary>
    /// True once login succeeds. The dialog reads this before closing.
    /// </summary>
    public bool LoginSucceeded
    {
        get => _loginSucceeded;
        set { _loginSucceeded = value; OnPropertyChanged(); }
    }

    public bool CanLogin => !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password) && !IsLoggingIn;

    public ICommand LoginCommand { get; }

    private async Task LoginAsync()
    {
        if (!CanLogin) return;

        IsLoggingIn = true;
        ErrorMessage = string.Empty;

        try
        {
            var success = await _authSession.LoginAsync(Username, Password);
            if (success)
            {
                LoginSucceeded = true;
            }
            else
            {
                ErrorMessage = "Login failed. Check your credentials and try again.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Connection error: {ex.Message}";
        }
        finally
        {
            IsLoggingIn = false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
