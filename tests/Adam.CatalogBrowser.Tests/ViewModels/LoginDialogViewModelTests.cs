using System.Collections.ObjectModel;
using Adam.CatalogBrowser.ViewModels;
using FluentAssertions;

namespace Adam.CatalogBrowser.Tests.ViewModels;

/// <summary>
/// Tests for <see cref="LoginDialogViewModel"/> — covers default state, property
/// change notifications, <see cref="LoginDialogViewModel.CanLogin"/> guards,
/// authentication via <see cref="LoginDialogViewModel.AuthenticateAsync"/>,
/// connection testing via <see cref="LoginDialogViewModel.TestConnectionCommand"/>,
/// recent hosts parsing, and login command behavior.
/// </summary>
public sealed class LoginDialogViewModelTests
{
    // ──────────────────────────────────────────────
    //  Constructor & defaults
    // ──────────────────────────────────────────────

    [Fact]
    public void Constructor_DefaultProperties()
    {
        var vm = new LoginDialogViewModel();

        vm.Username.Should().BeEmpty();
        vm.Password.Should().BeEmpty();
        vm.ErrorMessage.Should().BeEmpty();
        vm.IsLoggingIn.Should().BeFalse();
        vm.IsTestingConnection.Should().BeFalse();
        vm.LoginSucceeded.Should().BeFalse();
        vm.CanLogin.Should().BeFalse();
        vm.CanTestConnection.Should().BeTrue("defaults to localhost:9100, which is valid");
        vm.ServiceHost.Should().Be("localhost");
        vm.ServicePort.Should().Be(9100);
        vm.ConnectionTestStatus.Should().BeEmpty();
        vm.HasConnectionTestResult.Should().BeFalse();
        vm.ConnectionTestSuccessful.Should().BeFalse();
        vm.AuthenticateAsync.Should().BeNull();
        vm.TestConnectionAsync.Should().BeNull();
        vm.RecentHosts.Should().BeEmpty();
        vm.SelectedRecentHost.Should().BeNull();
    }

    // ──────────────────────────────────────────────
    //  CanLogin guards
    // ──────────────────────────────────────────────

    [Fact]
    public void CanLogin_WhenUsernameEmpty_ReturnsFalse()
    {
        var vm = new LoginDialogViewModel
        {
            Password = "secret"
        };

        vm.CanLogin.Should().BeFalse();
    }

    [Fact]
    public void CanLogin_WhenPasswordEmpty_ReturnsFalse()
    {
        var vm = new LoginDialogViewModel
        {
            Username = "admin"
        };

        vm.CanLogin.Should().BeFalse();
    }

    [Fact]
    public void CanLogin_WhenBothFilled_ReturnsTrue()
    {
        var vm = new LoginDialogViewModel
        {
            Username = "admin",
            Password = "secret"
        };

        vm.CanLogin.Should().BeTrue();
    }

    [Fact]
    public void CanLogin_WhenIsLoggingIn_ReturnsFalse()
    {
        var vm = new LoginDialogViewModel
        {
            Username = "admin",
            Password = "secret",
            IsLoggingIn = true
        };

        vm.CanLogin.Should().BeFalse();
    }

    [Fact]
    public void CanLogin_WhenIsTestingConnection_ReturnsFalse()
    {
        var vm = new LoginDialogViewModel
        {
            Username = "admin",
            Password = "secret",
            IsTestingConnection = true
        };

        vm.CanLogin.Should().BeFalse();
    }

    [Fact]
    public void CanLogin_UsernameOnlyWhitespace_ReturnsFalse()
    {
        var vm = new LoginDialogViewModel
        {
            Username = "   ",
            Password = "secret"
        };

        vm.CanLogin.Should().BeFalse();
    }

    [Fact]
    public void CanLogin_PasswordOnlyWhitespace_ReturnsFalse()
    {
        var vm = new LoginDialogViewModel
        {
            Username = "admin",
            Password = "   "
        };

        vm.CanLogin.Should().BeFalse();
    }

    [Fact]
    public void CanLogin_WhenServiceHostEmpty_ReturnsFalse()
    {
        var vm = new LoginDialogViewModel
        {
            ServiceHost = "",
            Username = "admin",
            Password = "secret"
        };

        vm.CanLogin.Should().BeFalse();
    }

    [Fact]
    public void CanLogin_WhenServicePortZero_ReturnsFalse()
    {
        var vm = new LoginDialogViewModel
        {
            ServicePort = 0,
            Username = "admin",
            Password = "secret"
        };

        vm.CanLogin.Should().BeFalse();
    }

    [Fact]
    public void CanLogin_WhenServicePortTooHigh_ReturnsFalse()
    {
        var vm = new LoginDialogViewModel
        {
            ServicePort = 65536,
            Username = "admin",
            Password = "secret"
        };

        vm.CanLogin.Should().BeFalse();
    }

    // ──────────────────────────────────────────────
    //  CanTestConnection guards
    // ──────────────────────────────────────────────

    [Fact]
    public void CanTestConnection_WhenHostEmpty_ReturnsFalse()
    {
        var vm = new LoginDialogViewModel { ServiceHost = "" };

        vm.CanTestConnection.Should().BeFalse();
    }

    [Fact]
    public void CanTestConnection_WhenPortZero_ReturnsFalse()
    {
        var vm = new LoginDialogViewModel { ServicePort = 0 };

        vm.CanTestConnection.Should().BeFalse();
    }

    [Fact]
    public void CanTestConnection_WhenPortTooHigh_ReturnsFalse()
    {
        var vm = new LoginDialogViewModel { ServicePort = 65536 };

        vm.CanTestConnection.Should().BeFalse();
    }

    [Fact]
    public void CanTestConnection_WhenIsTestingConnection_ReturnsFalse()
    {
        var vm = new LoginDialogViewModel
        {
            IsTestingConnection = true
        };

        vm.CanTestConnection.Should().BeFalse();
    }

    [Fact]
    public void CanTestConnection_WhenIsLoggingIn_ReturnsFalse()
    {
        var vm = new LoginDialogViewModel
        {
            Username = "admin",
            Password = "secret",
            IsLoggingIn = true
        };

        vm.CanTestConnection.Should().BeFalse();
    }

    [Fact]
    public void CanTestConnection_WhenDefaults_ReturnsTrue()
    {
        var vm = new LoginDialogViewModel();

        // localhost:9100 is valid
        vm.CanTestConnection.Should().BeTrue();
    }

    // ──────────────────────────────────────────────
    //  PropertyChanged notifications
    // ──────────────────────────────────────────────

    [Fact]
    public void Username_Setter_RaisesPropertyChanged()
    {
        var vm = new LoginDialogViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Username = "admin";

        raised.Should().Contain(nameof(vm.Username));
    }

    [Fact]
    public void Username_Setter_RaisesCanLoginChanged()
    {
        var vm = new LoginDialogViewModel
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
        var vm = new LoginDialogViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Password = "secret";

        raised.Should().Contain(nameof(vm.Password));
    }

    [Fact]
    public void Password_Setter_RaisesCanLoginChanged()
    {
        var vm = new LoginDialogViewModel
        {
            Username = "admin"
        };
        vm.CanLogin.Should().BeFalse("password is empty");

        vm.Password = "secret";

        vm.CanLogin.Should().BeTrue();
    }

    [Fact]
    public void ServiceHost_Setter_RaisesPropertyChanged()
    {
        var vm = new LoginDialogViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.ServiceHost = "192.168.1.100";

        raised.Should().Contain(nameof(vm.ServiceHost));
    }

    [Fact]
    public void ServiceHost_Setter_RaisesCanLoginChanged()
    {
        var vm = new LoginDialogViewModel
        {
            ServiceHost = "",
            Username = "admin",
            Password = "secret"
        };
        vm.CanLogin.Should().BeFalse("ServiceHost is empty");

        vm.ServiceHost = "localhost";

        vm.CanLogin.Should().BeTrue();
    }

    [Fact]
    public void ServiceHost_Setter_UpdatesCanTestConnection()
    {
        var vm = new LoginDialogViewModel { ServiceHost = "" };
        vm.CanTestConnection.Should().BeFalse("ServiceHost is empty");

        vm.ServiceHost = "myserver";

        vm.CanTestConnection.Should().BeTrue();
    }

    [Fact]
    public void ServiceHost_Setter_ClearsConnectionTestStatus()
    {
        var vm = new LoginDialogViewModel { ConnectionTestStatus = "✓ Reachable" };

        vm.ServiceHost = "otherhost";

        vm.ConnectionTestStatus.Should().BeEmpty();
    }

    [Fact]
    public void ServicePort_Setter_UpdatesCanTestConnection()
    {
        var vm = new LoginDialogViewModel { ServicePort = 0 };
        vm.CanTestConnection.Should().BeFalse("ServicePort is 0");

        vm.ServicePort = 9090;

        vm.CanTestConnection.Should().BeTrue();
    }

    [Fact]
    public void ServicePort_Setter_ClearsConnectionTestStatus()
    {
        var vm = new LoginDialogViewModel { ConnectionTestStatus = "✓ Reachable" };

        vm.ServicePort = 9200;

        vm.ConnectionTestStatus.Should().BeEmpty();
    }

    [Fact]
    public void IsLoggingIn_Setter_RaisesPropertyChanged()
    {
        var vm = new LoginDialogViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.IsLoggingIn = true;

        raised.Should().Contain(nameof(vm.IsLoggingIn));
    }

    [Fact]
    public void IsLoggingIn_True_DisablesCanLogin()
    {
        var vm = new LoginDialogViewModel
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
        var vm = new LoginDialogViewModel
        {
            Username = "admin",
            Password = "secret",
            IsLoggingIn = true
        };

        vm.IsLoggingIn = false;

        vm.CanLogin.Should().BeTrue();
    }

    [Fact]
    public void IsTestingConnection_Setter_RaisesPropertyChanged()
    {
        var vm = new LoginDialogViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.IsTestingConnection = true;

        raised.Should().Contain(nameof(vm.IsTestingConnection));
    }

    [Fact]
    public void IsTestingConnection_True_DisablesCanTestConnection()
    {
        var vm = new LoginDialogViewModel { IsTestingConnection = true };

        vm.CanTestConnection.Should().BeFalse();
    }

    [Fact]
    public void IsTestingConnection_True_DisablesCanLogin()
    {
        var vm = new LoginDialogViewModel
        {
            Username = "admin",
            Password = "secret",
            IsTestingConnection = true
        };

        vm.CanLogin.Should().BeFalse();
    }

    [Fact]
    public void IsTestingConnection_False_ReenablesCanTestConnection()
    {
        var vm = new LoginDialogViewModel { IsTestingConnection = true };

        vm.IsTestingConnection = false;

        vm.CanTestConnection.Should().BeTrue();
    }

    [Fact]
    public void LoginSucceeded_Setter_RaisesPropertyChanged()
    {
        var vm = new LoginDialogViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.LoginSucceeded = true;

        raised.Should().Contain(nameof(vm.LoginSucceeded));
    }

    [Fact]
    public void ErrorMessage_Setter_RaisesPropertyChanged()
    {
        var vm = new LoginDialogViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.ErrorMessage = "Something went wrong";

        raised.Should().Contain(nameof(vm.ErrorMessage));
    }

    // ──────────────────────────────────────────────
    //  ConnectionTestStatus / HasConnectionTestResult / ConnectionTestSuccessful
    // ──────────────────────────────────────────────

    [Fact]
    public void ConnectionTestStatus_Setter_RaisesPropertyChanged()
    {
        var vm = new LoginDialogViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.ConnectionTestStatus = "✓ OK";

        raised.Should().Contain(nameof(vm.ConnectionTestStatus));
    }

    [Fact]
    public void ConnectionTestStatus_Setter_RaisesHasConnectionTestResult()
    {
        var vm = new LoginDialogViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.ConnectionTestStatus = "✓ OK";

        raised.Should().Contain(nameof(vm.HasConnectionTestResult));
    }

    [Fact]
    public void HasConnectionTestResult_WhenNonEmpty_ReturnsTrue()
    {
        var vm = new LoginDialogViewModel { ConnectionTestStatus = "✓ OK" };

        vm.HasConnectionTestResult.Should().BeTrue();
    }

    [Fact]
    public void HasConnectionTestResult_WhenEmpty_ReturnsFalse()
    {
        var vm = new LoginDialogViewModel { ConnectionTestStatus = "" };

        vm.HasConnectionTestResult.Should().BeFalse();
    }

    [Fact]
    public void ConnectionTestSuccessful_WhenCheckmark_ReturnsTrue()
    {
        var vm = new LoginDialogViewModel { ConnectionTestStatus = "✓ Service is reachable" };

        vm.ConnectionTestSuccessful.Should().BeTrue();
    }

    [Fact]
    public void ConnectionTestSuccessful_WhenCross_ReturnsFalse()
    {
        var vm = new LoginDialogViewModel { ConnectionTestStatus = "✗ Connection refused" };

        vm.ConnectionTestSuccessful.Should().BeFalse();
    }

    [Fact]
    public void ConnectionTestSuccessful_WhenEmpty_ReturnsFalse()
    {
        var vm = new LoginDialogViewModel { ConnectionTestStatus = "" };

        vm.ConnectionTestSuccessful.Should().BeFalse();
    }

    [Fact]
    public void ConnectionTestIcon_WhenCheckmark_ReturnsCheckmark()
    {
        var vm = new LoginDialogViewModel { ConnectionTestStatus = "✓ Service is reachable" };

        vm.ConnectionTestIcon.Should().Be("✓");
    }

    [Fact]
    public void ConnectionTestIcon_WhenCross_ReturnsCross()
    {
        var vm = new LoginDialogViewModel { ConnectionTestStatus = "✗ Connection refused" };

        vm.ConnectionTestIcon.Should().Be("✗");
    }

    [Fact]
    public void ConnectionTestIcon_WhenEmpty_ReturnsEmpty()
    {
        var vm = new LoginDialogViewModel { ConnectionTestStatus = "" };

        vm.ConnectionTestIcon.Should().BeEmpty();
    }

    [Fact]
    public void ConnectionTestIcon_Setter_RaisesPropertyChanged()
    {
        var vm = new LoginDialogViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.ConnectionTestStatus = "✓ OK";

        raised.Should().Contain(nameof(vm.ConnectionTestIcon));
    }

    [Fact]
    public void ConnectionTestForeground_WhenCheckmark_ReturnsGreen()
    {
        var vm = new LoginDialogViewModel { ConnectionTestStatus = "✓ Service is reachable" };

        vm.ConnectionTestForeground.Should().Be("#2E7D32");
    }

    [Fact]
    public void ConnectionTestForeground_WhenCross_ReturnsRed()
    {
        var vm = new LoginDialogViewModel { ConnectionTestStatus = "✗ Connection refused" };

        vm.ConnectionTestForeground.Should().Be("#D32F2F");
    }

    [Fact]
    public void ConnectionTestForeground_WhenEmpty_ReturnsEmpty()
    {
        var vm = new LoginDialogViewModel { ConnectionTestStatus = "" };

        vm.ConnectionTestForeground.Should().BeEmpty();
    }

    [Fact]
    public void ConnectionTestForeground_Setter_RaisesPropertyChanged()
    {
        var vm = new LoginDialogViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.ConnectionTestStatus = "✓ OK";

        raised.Should().Contain(nameof(vm.ConnectionTestForeground));
    }

    // ──────────────────────────────────────────────
    //  Login command — AuthenticateAsync delegate (success)
    // ──────────────────────────────────────────────

    [Fact]
    public void LoginCommand_WithAuthenticateDelegate_Success_SetsLoginSucceeded()
    {
        var vm = new LoginDialogViewModel
        {
            Username = "admin",
            Password = "secret",
            AuthenticateAsync = (_, _, _, _) => Task.FromResult<string?>(null)
        };

        vm.LoginCommand.Execute(null);

        vm.LoginSucceeded.Should().BeTrue();
        vm.ErrorMessage.Should().BeEmpty();
        vm.IsLoggingIn.Should().BeFalse();
    }

    [Fact]
    public void LoginCommand_WithAuthenticateDelegate_Failure_SetsErrorMessage()
    {
        var vm = new LoginDialogViewModel
        {
            Username = "admin",
            Password = "secret",
            AuthenticateAsync = (_, _, _, _) => Task.FromResult<string?>("Invalid credentials")
        };

        vm.LoginCommand.Execute(null);

        vm.LoginSucceeded.Should().BeFalse();
        vm.ErrorMessage.Should().Be("Invalid credentials");
        vm.IsLoggingIn.Should().BeFalse();
    }

    [Fact]
    public void LoginCommand_WithAuthenticateDelegate_Exception_SetsErrorMessage()
    {
        var vm = new LoginDialogViewModel
        {
            Username = "admin",
            Password = "secret",
            AuthenticateAsync = (_, _, _, _) => throw new InvalidOperationException("Connection lost")
        };

        vm.LoginCommand.Execute(null);

        vm.LoginSucceeded.Should().BeFalse();
        vm.ErrorMessage.Should().Contain("Connection lost");
        vm.IsLoggingIn.Should().BeFalse();
    }

    [Fact]
    public void LoginCommand_WithoutDelegate_SetsLoginSucceeded()
    {
        var vm = new LoginDialogViewModel
        {
            Username = "admin",
            Password = "secret",
            AuthenticateAsync = null
        };

        vm.LoginCommand.Execute(null);

        vm.LoginSucceeded.Should().BeTrue();
        vm.IsLoggingIn.Should().BeFalse();
    }

    [Fact]
    public void LoginCommand_CanExecute_WhenCannotLogin_ReturnsFalse()
    {
        var vm = new LoginDialogViewModel();

        vm.LoginCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void LoginCommand_CanExecute_WhenCanLogin_ReturnsTrue()
    {
        var vm = new LoginDialogViewModel
        {
            Username = "admin",
            Password = "secret"
        };

        vm.LoginCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void LoginCommand_CanExecute_WhenLoggingIn_ReturnsFalse()
    {
        var vm = new LoginDialogViewModel
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
        var vm = new LoginDialogViewModel();

        vm.LoginCommand.Execute(null);

        vm.LoginSucceeded.Should().BeFalse();
    }

    // ──────────────────────────────────────────────
    //  TestConnection command
    // ──────────────────────────────────────────────

    [Fact]
    public void TestConnectionCommand_WithoutDelegate_SetsNoTestMethod()
    {
        var vm = new LoginDialogViewModel { TestConnectionAsync = null };

        vm.TestConnectionCommand.Execute(null);

        vm.ConnectionTestStatus.Should().Be("No test method configured.");
        vm.IsTestingConnection.Should().BeFalse();
    }

    [Fact]
    public void TestConnectionCommand_WithDelegate_Success_SetsCheckmark()
    {
        var vm = new LoginDialogViewModel
        {
            TestConnectionAsync = (_, _) => Task.FromResult<string?>(null)
        };

        vm.TestConnectionCommand.Execute(null);

        vm.ConnectionTestStatus.Should().StartWith("✓");
        vm.IsTestingConnection.Should().BeFalse();
    }

    [Fact]
    public void TestConnectionCommand_WithDelegate_Failure_SetsCross()
    {
        var vm = new LoginDialogViewModel
        {
            TestConnectionAsync = (_, _) => Task.FromResult<string?>("Connection refused")
        };

        vm.TestConnectionCommand.Execute(null);

        vm.ConnectionTestStatus.Should().StartWith("✗");
        vm.ConnectionTestStatus.Should().Contain("Connection refused");
        vm.IsTestingConnection.Should().BeFalse();
    }

    [Fact]
    public void TestConnectionCommand_WithDelegate_Exception_SetsCross()
    {
        var vm = new LoginDialogViewModel
        {
            TestConnectionAsync = (_, _) => throw new TimeoutException("Timed out")
        };

        vm.TestConnectionCommand.Execute(null);

        vm.ConnectionTestStatus.Should().StartWith("✗");
        vm.ConnectionTestStatus.Should().Contain("Timed out");
        vm.IsTestingConnection.Should().BeFalse();
    }

    [Fact]
    public void TestConnectionCommand_CanExecute_WhenCannotTest_ReturnsFalse()
    {
        var vm = new LoginDialogViewModel { ServiceHost = "" };

        vm.TestConnectionCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void TestConnectionCommand_CanExecute_WhenCanTest_ReturnsTrue()
    {
        var vm = new LoginDialogViewModel();

        vm.TestConnectionCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void TestConnectionCommand_CanExecute_WhenIsTestingConnection_ReturnsFalse()
    {
        var vm = new LoginDialogViewModel { IsTestingConnection = true };

        vm.TestConnectionCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void TestConnectionCommand_CanExecute_WhenIsLoggingIn_ReturnsFalse()
    {
        var vm = new LoginDialogViewModel
        {
            Username = "admin",
            Password = "secret",
            IsLoggingIn = true
        };

        vm.TestConnectionCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void TestConnectionCommand_Execute_WhenCannotTest_DoesNothing()
    {
        var vm = new LoginDialogViewModel { ServiceHost = "" };

        vm.TestConnectionCommand.Execute(null);

        vm.ConnectionTestStatus.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────
    //  SelectedRecentHost parsing
    // ──────────────────────────────────────────────

    [Fact]
    public void SelectedRecentHost_WithHostPort_ParsesBoth()
    {
        var vm = new LoginDialogViewModel();

        vm.SelectedRecentHost = "myserver:9200";

        vm.ServiceHost.Should().Be("myserver");
        vm.ServicePort.Should().Be(9200);
        vm.SelectedRecentHost.Should().Be("myserver:9200");
    }

    [Fact]
    public void SelectedRecentHost_WithIPv4Port_ParsesBoth()
    {
        var vm = new LoginDialogViewModel();

        vm.SelectedRecentHost = "192.168.1.100:9090";

        vm.ServiceHost.Should().Be("192.168.1.100");
        vm.ServicePort.Should().Be(9090);
    }

    [Fact]
    public void SelectedRecentHost_WithBracketedIPv6Port_ParsesBoth()
    {
        var vm = new LoginDialogViewModel();

        vm.SelectedRecentHost = "[::1]:9100";

        vm.ServiceHost.Should().Be("[::1]");
        vm.ServicePort.Should().Be(9100);
    }

    [Fact]
    public void SelectedRecentHost_WithHostOnly_SetsHostAndDoesNotChangePort()
    {
        var vm = new LoginDialogViewModel { ServicePort = 7777 };

        vm.SelectedRecentHost = "myserver";

        // No colon found, so the whole value becomes the host
        vm.ServiceHost.Should().Be("myserver");
        vm.ServicePort.Should().Be(7777); // unchanged
    }

    [Fact]
    public void SelectedRecentHost_SettingNull_DoesNothing()
    {
        var vm = new LoginDialogViewModel
        {
            ServiceHost = "original",
            ServicePort = 1111
        };

        vm.SelectedRecentHost = null;

        vm.ServiceHost.Should().Be("original");
        vm.ServicePort.Should().Be(1111);
    }

    [Fact]
    public void SelectedRecentHost_SettingSameValue_DoesNothing()
    {
        var vm = new LoginDialogViewModel();
        vm.SelectedRecentHost = "host:9100";
        vm.ServiceHost = "changed";

        vm.SelectedRecentHost = "host:9100"; // same value, setter skips

        vm.ServiceHost.Should().Be("changed"); // unchanged because setter skipped
    }

    [Fact]
    public void SelectedRecentHost_Setter_RaisesPropertyChanged()
    {
        var vm = new LoginDialogViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.SelectedRecentHost = "server:9000";

        raised.Should().Contain(nameof(vm.SelectedRecentHost));
    }

    [Fact]
    public void SelectedRecentHost_WithPortColonInHostname_ParsesLastColon()
    {
        var vm = new LoginDialogViewModel();

        vm.SelectedRecentHost = "some:weird:host:8080";

        vm.ServiceHost.Should().Be("some:weird:host");
        vm.ServicePort.Should().Be(8080);
    }

    // ──────────────────────────────────────────────
    //  RecentHosts collection
    // ──────────────────────────────────────────────

    [Fact]
    public void RecentHosts_CanBeSetViaObjectInitializer()
    {
        var hosts = new ObservableCollection<string> { "a:1", "b:2" };

        var vm = new LoginDialogViewModel { RecentHosts = hosts };

        vm.RecentHosts.Should().BeSameAs(hosts);
        vm.RecentHosts.Should().HaveCount(2);
    }

    [Fact]
    public void RecentHosts_DefaultIsEmpty()
    {
        var vm = new LoginDialogViewModel();

        vm.RecentHosts.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────
    //  ServiceHost / ServicePort
    // ──────────────────────────────────────────────

    [Fact]
    public void ServiceHost_CanBeSet()
    {
        var vm = new LoginDialogViewModel
        {
            ServiceHost = "192.168.1.100"
        };

        vm.ServiceHost.Should().Be("192.168.1.100");
    }

    [Fact]
    public void ServicePort_CanBeSet()
    {
        var vm = new LoginDialogViewModel
        {
            ServicePort = 9090
        };

        vm.ServicePort.Should().Be(9090);
    }

    // ──────────────────────────────────────────────
    //  ClearCredentialsCommand
    // ──────────────────────────────────────────────

    [Fact]
    public void ClearCredentialsCommand_AlwaysEnabled()
    {
        var vm = new LoginDialogViewModel();

        vm.ClearCredentialsCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void ClearCredentialsCommand_ResetsHostPortAndUsername()
    {
        var vm = new LoginDialogViewModel
        {
            ServiceHost = "myserver",
            ServicePort = 9200,
            Username = "admin",
            Password = "secret"
        };

        vm.ClearCredentialsCommand.Execute(null);

        vm.ServiceHost.Should().Be("localhost");
        vm.ServicePort.Should().Be(9100);
        vm.Username.Should().BeEmpty();
        vm.Password.Should().BeEmpty();
    }

    [Fact]
    public void ClearCredentialsCommand_ClearsRecentHosts()
    {
        var vm = new LoginDialogViewModel();
        vm.RecentHosts.Add("a:1");
        vm.RecentHosts.Add("b:2");

        vm.ClearCredentialsCommand.Execute(null);

        vm.RecentHosts.Should().BeEmpty();
    }

    [Fact]
    public void ClearCredentialsCommand_ClearsSelectedRecentHost()
    {
        var vm = new LoginDialogViewModel();
        vm.RecentHosts.Add("s:1");
        vm.SelectedRecentHost = "s:1";
        vm.SelectedRecentHost.Should().NotBeNull();

        vm.ClearCredentialsCommand.Execute(null);

        vm.SelectedRecentHost.Should().BeNull();
    }

    [Fact]
    public void ClearCredentialsCommand_ClearsErrorMessage()
    {
        var vm = new LoginDialogViewModel { ErrorMessage = "Some error" };

        vm.ClearCredentialsCommand.Execute(null);

        vm.ErrorMessage.Should().BeEmpty();
    }

    [Fact]
    public void ClearCredentialsCommand_ClearsConnectionTestStatus()
    {
        var vm = new LoginDialogViewModel { ConnectionTestStatus = "✓ OK" };

        vm.ClearCredentialsCommand.Execute(null);

        vm.ConnectionTestStatus.Should().BeEmpty();
    }

    [Fact]
    public void ClearCredentialsCommand_CallsDelegate()
    {
        var called = false;
        var vm = new LoginDialogViewModel
        {
            ClearCredentialsAsync = () =>
            {
                called = true;
                return Task.CompletedTask;
            }
        };

        vm.ClearCredentialsCommand.Execute(null);

        called.Should().BeTrue();
    }

    [Fact]
    public void ClearCredentialsCommand_WithoutDelegate_DoesNotThrow()
    {
        var vm = new LoginDialogViewModel
        {
            ServiceHost = "myserver",
            ClearCredentialsAsync = null
        };

        vm.ClearCredentialsCommand.Execute(null);

        // Should still reset fields even without a delegate
        vm.ServiceHost.Should().Be("localhost");
    }

    // ──────────────────────────────────────────────
    //  ErrorMessage clearing on login / test
    // ──────────────────────────────────────────────

    [Fact]
    public void LoginCommand_ClearsPreviousErrorMessage()
    {
        var vm = new LoginDialogViewModel
        {
            Username = "admin",
            Password = "secret",
            ErrorMessage = "Previous error",
            AuthenticateAsync = (_, _, _, _) => Task.FromResult<string?>(null)
        };

        vm.LoginCommand.Execute(null);

        vm.ErrorMessage.Should().BeEmpty();
    }

    [Fact]
    public void TestConnectionCommand_ClearsPreviousErrorMessage()
    {
        var vm = new LoginDialogViewModel
        {
            ErrorMessage = "Previous error",
            TestConnectionAsync = (_, _) => Task.FromResult<string?>(null)
        };

        vm.TestConnectionCommand.Execute(null);

        vm.ErrorMessage.Should().BeEmpty();
    }
}
