using Adam.CatalogBrowser.ViewModels;
using Adam.Shared.Services;
using FluentAssertions;

namespace Adam.CatalogBrowser.Tests.ViewModels;

/// <summary>
/// Tests for <see cref="LoginDialogViewModel"/> — covers default state, property
/// change notifications, <see cref="LoginDialogViewModel.CanLogin"/> guards, and
/// login success/failure flows via a <see cref="FakeAuthSession"/>.
/// </summary>
public sealed class LoginDialogViewModelTests
{
    // ──────────────────────────────────────────────
    //  Constructor & defaults
    // ──────────────────────────────────────────────

    [Fact]
    public void Constructor_DefaultProperties()
    {
        var auth = new FakeAuthSession();
        var vm = new LoginDialogViewModel(auth);

        vm.Username.Should().BeEmpty();
        vm.Password.Should().BeEmpty();
        vm.ErrorMessage.Should().BeEmpty();
        vm.IsLoggingIn.Should().BeFalse();
        vm.LoginSucceeded.Should().BeFalse();
        vm.CanLogin.Should().BeFalse();
        vm.ServiceHost.Should().Be("localhost");
        vm.ServicePort.Should().Be(9100);
    }

    // ──────────────────────────────────────────────
    //  CanLogin guards
    // ──────────────────────────────────────────────

    [Fact]
    public void CanLogin_WhenUsernameEmpty_ReturnsFalse()
    {
        var vm = new LoginDialogViewModel(new FakeAuthSession())
        {
            Password = "secret"
        };

        vm.CanLogin.Should().BeFalse();
    }

    [Fact]
    public void CanLogin_WhenPasswordEmpty_ReturnsFalse()
    {
        var vm = new LoginDialogViewModel(new FakeAuthSession())
        {
            Username = "admin"
        };

        vm.CanLogin.Should().BeFalse();
    }

    [Fact]
    public void CanLogin_WhenBothFilled_ReturnsTrue()
    {
        var vm = new LoginDialogViewModel(new FakeAuthSession())
        {
            Username = "admin",
            Password = "secret"
        };

        vm.CanLogin.Should().BeTrue();
    }

    [Fact]
    public void CanLogin_WhenIsLoggingIn_ReturnsFalse()
    {
        var vm = new LoginDialogViewModel(new FakeAuthSession())
        {
            Username = "admin",
            Password = "secret",
            IsLoggingIn = true
        };

        vm.CanLogin.Should().BeFalse();
    }

    [Fact]
    public void CanLogin_UsernameOnlyWhitespace_ReturnsFalse()
    {
        var vm = new LoginDialogViewModel(new FakeAuthSession())
        {
            Username = "   ",
            Password = "secret"
        };

        vm.CanLogin.Should().BeFalse();
    }

    [Fact]
    public void CanLogin_PasswordOnlyWhitespace_ReturnsFalse()
    {
        var vm = new LoginDialogViewModel(new FakeAuthSession())
        {
            Username = "admin",
            Password = "   "
        };

        vm.CanLogin.Should().BeFalse();
    }

    // ──────────────────────────────────────────────
    //  PropertyChanged notifications
    // ──────────────────────────────────────────────

    [Fact]
    public void Username_Setter_RaisesPropertyChanged()
    {
        var vm = new LoginDialogViewModel(new FakeAuthSession());
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Username = "admin";

        raised.Should().Contain(nameof(vm.Username));
    }

    [Fact]
    public void Username_Setter_RaisesCanLoginChanged()
    {
        var vm = new LoginDialogViewModel(new FakeAuthSession())
        {
            Password = "secret"
        };
        vm.CanLogin.Should().BeFalse("username is empty");

        vm.Username = "admin";

        vm.CanLogin.Should().BeTrue();
    }

    [Fact]
    public void Password_Setter_RaisesPropertyChanged()
    {
        var vm = new LoginDialogViewModel(new FakeAuthSession());
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Password = "secret";

        raised.Should().Contain(nameof(vm.Password));
    }

    [Fact]
    public void Password_Setter_RaisesCanLoginChanged()
    {
        var vm = new LoginDialogViewModel(new FakeAuthSession())
        {
            Username = "admin"
        };
        vm.CanLogin.Should().BeFalse("password is empty");

        vm.Password = "secret";

        vm.CanLogin.Should().BeTrue();
    }

    [Fact]
    public void IsLoggingIn_Setter_RaisesPropertyChanged()
    {
        var vm = new LoginDialogViewModel(new FakeAuthSession());
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.IsLoggingIn = true;

        raised.Should().Contain(nameof(vm.IsLoggingIn));
    }

    [Fact]
    public void IsLoggingIn_True_DisablesCanLogin()
    {
        var vm = new LoginDialogViewModel(new FakeAuthSession())
        {
            Username = "admin",
            Password = "secret",
            IsLoggingIn = true
        };

        vm.CanLogin.Should().BeFalse();
    }

    [Fact]
    public void IsLoggingIn_False_ReenablesCanLogin()
    {
        var vm = new LoginDialogViewModel(new FakeAuthSession())
        {
            Username = "admin",
            Password = "secret",
            IsLoggingIn = true
        };

        vm.IsLoggingIn = false;

        vm.CanLogin.Should().BeTrue();
    }

    [Fact]
    public void LoginSucceeded_Setter_RaisesPropertyChanged()
    {
        var vm = new LoginDialogViewModel(new FakeAuthSession());
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.LoginSucceeded = true;

        raised.Should().Contain(nameof(vm.LoginSucceeded));
    }

    [Fact]
    public void ErrorMessage_Setter_RaisesPropertyChanged()
    {
        var vm = new LoginDialogViewModel(new FakeAuthSession());
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.ErrorMessage = "Something went wrong";

        raised.Should().Contain(nameof(vm.ErrorMessage));
    }

    // ──────────────────────────────────────────────
    //  Login command — success path
    // ──────────────────────────────────────────────

    [Fact]
    public async Task LoginCommand_WhenLoginSucceeds_SetsLoginSucceeded()
    {
        var auth = new FakeAuthSession { NextLoginResult = true };
        var vm = new LoginDialogViewModel(auth)
        {
            Username = "admin",
            Password = "secret"
        };

        vm.LoginCommand.Execute(null);

        // Wait for the async command to complete
        await WaitForAsyncCompletionAsync(() => vm.LoginSucceeded);

        vm.LoginSucceeded.Should().BeTrue();
        vm.ErrorMessage.Should().BeEmpty();
        vm.IsLoggingIn.Should().BeFalse();
    }

    [Fact]
    public async Task LoginCommand_WhenLoginSucceeds_UpdatesAuthSessionState()
    {
        var auth = new FakeAuthSession { NextLoginResult = true };
        var vm = new LoginDialogViewModel(auth)
        {
            Username = "admin",
            Password = "secret"
        };

        vm.LoginCommand.Execute(null);
        await WaitForAsyncCompletionAsync(() => vm.LoginSucceeded);

        auth.LastLoginUsername.Should().Be("admin");
        auth.LastLoginPassword.Should().Be("secret");
        auth.IsLoggedIn.Should().BeTrue();
    }

    // ──────────────────────────────────────────────
    //  Login command — failure path
    // ──────────────────────────────────────────────

    [Fact]
    public async Task LoginCommand_WhenLoginFails_SetsErrorMessage()
    {
        var auth = new FakeAuthSession { NextLoginResult = false };
        var vm = new LoginDialogViewModel(auth)
        {
            Username = "admin",
            Password = "wrong"
        };

        vm.LoginCommand.Execute(null);

        await WaitForAsyncCompletionAsync(() => !string.IsNullOrEmpty(vm.ErrorMessage));

        vm.LoginSucceeded.Should().BeFalse();
        vm.ErrorMessage.Should().Be("Login failed. Check your credentials and try again.");
        vm.IsLoggingIn.Should().BeFalse();
    }

    [Fact]
    public async Task LoginCommand_WhenLoginFails_DoesNotUpdateAuthToken()
    {
        var auth = new FakeAuthSession { NextLoginResult = false };
        var vm = new LoginDialogViewModel(auth)
        {
            Username = "admin",
            Password = "wrong"
        };

        vm.LoginCommand.Execute(null);
        await WaitForAsyncCompletionAsync(() => !string.IsNullOrEmpty(vm.ErrorMessage));

        auth.IsLoggedIn.Should().BeFalse();
    }

    // ──────────────────────────────────────────────
    //  Login command — exception path
    // ──────────────────────────────────────────────

    [Fact]
    public async Task LoginCommand_WhenAuthThrows_SetsConnectionErrorMessage()
    {
        var auth = new FakeAuthSession { NextException = new InvalidOperationException("Connection refused") };
        var vm = new LoginDialogViewModel(auth)
        {
            Username = "admin",
            Password = "secret"
        };

        vm.LoginCommand.Execute(null);

        await WaitForAsyncCompletionAsync(() => !string.IsNullOrEmpty(vm.ErrorMessage));

        vm.LoginSucceeded.Should().BeFalse();
        vm.ErrorMessage.Should().Contain("Connection refused");
        vm.IsLoggingIn.Should().BeFalse();
    }

    [Fact]
    public async Task LoginCommand_WhenAuthThrows_DoesNotSetLoginSucceeded()
    {
        var auth = new FakeAuthSession { NextException = new TimeoutException("Timed out") };
        var vm = new LoginDialogViewModel(auth)
        {
            Username = "admin",
            Password = "secret"
        };

        vm.LoginCommand.Execute(null);
        await WaitForAsyncCompletionAsync(() => !string.IsNullOrEmpty(vm.ErrorMessage));

        vm.LoginSucceeded.Should().BeFalse();
    }

    // ──────────────────────────────────────────────
    //  Login command — CanExecute guard
    // ──────────────────────────────────────────────

    [Fact]
    public void LoginCommand_CanExecute_WhenCannotLogin_ReturnsFalse()
    {
        var auth = new FakeAuthSession();
        var vm = new LoginDialogViewModel(auth);

        // Username and password are both empty
        vm.LoginCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void LoginCommand_CanExecute_WhenCanLogin_ReturnsTrue()
    {
        var auth = new FakeAuthSession();
        var vm = new LoginDialogViewModel(auth)
        {
            Username = "admin",
            Password = "secret"
        };

        vm.LoginCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void LoginCommand_CanExecute_WhenLoggingIn_ReturnsFalse()
    {
        var auth = new FakeAuthSession();
        var vm = new LoginDialogViewModel(auth)
        {
            Username = "admin",
            Password = "secret",
            IsLoggingIn = true
        };

        vm.LoginCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void LoginCommand_Execute_WhenCannotLogin_DoesNothing()
    {
        // CanLogin is false (empty username/password).
        // The command's CanExecute returns false, so Execute is a synchronous no-op.
        var auth = new FakeAuthSession { NextLoginResult = true };
        var vm = new LoginDialogViewModel(auth);

        vm.LoginCommand.Execute(null);

        vm.LoginSucceeded.Should().BeFalse();
        auth.LoginWasCalled.Should().BeFalse();
    }

    // ──────────────────────────────────────────────
    //  ServiceHost / ServicePort
    // ──────────────────────────────────────────────

    [Fact]
    public void ServiceHost_CanBeSet()
    {
        var vm = new LoginDialogViewModel(new FakeAuthSession())
        {
            ServiceHost = "192.168.1.100"
        };

        vm.ServiceHost.Should().Be("192.168.1.100");
    }

    [Fact]
    public void ServicePort_CanBeSet()
    {
        var vm = new LoginDialogViewModel(new FakeAuthSession())
        {
            ServicePort = 9090
        };

        vm.ServicePort.Should().Be(9090);
    }

    // ──────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────

    /// <summary>
    /// Polls the predicate every 50ms until it returns true or a timeout
    /// elapses. Used to wait for async command completion without needing
    /// a pumping dispatcher.
    /// </summary>
    private static async Task WaitForAsyncCompletionAsync(Func<bool> predicate, int timeoutMs = 3000)
    {
        var start = DateTime.UtcNow;
        while (!predicate())
        {
            if ((DateTime.UtcNow - start).TotalMilliseconds > timeoutMs)
                return; // Don't fail the test — the assertion will catch it
            await Task.Delay(50);
        }
    }
}

// ──────────────────────────────────────────────
//  Fake auth session for testing
// ──────────────────────────────────────────────

/// <summary>
/// A test double for <see cref="IAuthSession"/> that returns configurable
/// results without requiring a network connection.
/// </summary>
public sealed class FakeAuthSession : IAuthSession
{
    /// <summary>
    /// The result returned by <see cref="LoginAsync"/>. Defaults to false.
    /// </summary>
    public bool NextLoginResult { get; set; }

    /// <summary>
    /// If set, <see cref="LoginAsync"/> will throw this exception.
    /// </summary>
    public Exception? NextException { get; set; }

    /// <summary>
    /// The username passed to the last <see cref="LoginAsync"/> call.
    /// </summary>
    public string? LastLoginUsername { get; private set; }

    /// <summary>
    /// The password passed to the last <see cref="LoginAsync"/> call.
    /// </summary>
    public string? LastLoginPassword { get; private set; }

    /// <summary>
    /// Whether <see cref="LoginAsync"/> has been called.
    /// </summary>
    public bool LoginWasCalled { get; private set; }

    public string? Token { get; set; }
    public bool IsLoggedIn => Token != null;

    public Task<bool> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        LoginWasCalled = true;
        LastLoginUsername = username;
        LastLoginPassword = password;

        if (NextException != null)
            throw NextException;

        if (NextLoginResult)
            Token = "fake-jwt-token";

        return Task.FromResult(NextLoginResult);
    }

    public void Logout()
    {
        Token = null;
    }
}
