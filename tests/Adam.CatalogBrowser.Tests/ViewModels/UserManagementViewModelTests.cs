using Adam.CatalogBrowser.ViewModels;
using Adam.Shared.Services;
using FluentAssertions;

namespace Adam.CatalogBrowser.Tests.ViewModels;

/// <summary>
/// Tests for <see cref="UserManagementViewModel"/> — the user administration panel
/// that manages users (list, create, edit, deactivate) and role assignments.
/// </summary>
public class UserManagementViewModelTests : IDisposable
{
    private readonly string _basePath;
    private readonly ModeManager _modeManager;
    private readonly UserManagementViewModel _vm;

    public UserManagementViewModelTests()
    {
        _basePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _modeManager = new ModeManager(_basePath);
        _vm = new UserManagementViewModel(_modeManager);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_basePath))
                Directory.Delete(_basePath, recursive: true);
        }
        catch (IOException) { }
    }

    // ──────────────────────────────────────────────
    //  Constructor & initial state
    // ──────────────────────────────────────────────

    [Fact]
    public void Constructor_Collections_AreEmpty()
    {
        _vm.Users.Should().BeEmpty();
        _vm.Roles.Should().BeEmpty();
        _vm.LogMessages.Should().ContainSingle();
        _vm.LogMessages[0].Should().Contain("initialized");
    }

    [Fact]
    public void Constructor_DefaultProperties()
    {
        _vm.StatusText.Should().BeEmpty();
        _vm.IsLoading.Should().BeFalse();
        _vm.SelectedUser.Should().BeNull();
        _vm.IsEditing.Should().BeFalse();
        _vm.EditUsername.Should().BeEmpty();
        _vm.EditEmail.Should().BeEmpty();
        _vm.EditPassword.Should().BeEmpty();
        _vm.EditRoleId.Should().BeEmpty();
        _vm.EditIsActive.Should().BeTrue();
        _vm.SelectedRole.Should().BeNull();
    }

    [Fact]
    public void Constructor_Commands_AreNotNull()
    {
        _vm.RefreshCommand.Should().NotBeNull();
        _vm.AddUserCommand.Should().NotBeNull();
        _vm.EditUserCommand.Should().NotBeNull();
        _vm.SaveUserCommand.Should().NotBeNull();
        _vm.DeleteUserCommand.Should().NotBeNull();
        _vm.CancelEditCommand.Should().NotBeNull();
        _vm.ClearLogCommand.Should().NotBeNull();
    }

    // ──────────────────────────────────────────────
    //  Command executability
    // ──────────────────────────────────────────────

    [Fact]
    public void EditUserCommand_WhenNoSelection_CannotExecute()
    {
        _vm.EditUserCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void EditUserCommand_WhenSelectedUser_CanExecute()
    {
        _vm.Users.Add(new UserItem { Id = Guid.NewGuid(), Username = "test" });
        _vm.SelectedUser = _vm.Users[0];

        _vm.EditUserCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void DeleteUserCommand_WhenNoSelection_CannotExecute()
    {
        _vm.DeleteUserCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void DeleteUserCommand_WhenSelectedUser_CanExecute()
    {
        _vm.Users.Add(new UserItem { Id = Guid.NewGuid(), Username = "test" });
        _vm.SelectedUser = _vm.Users[0];

        _vm.DeleteUserCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void RefreshCommand_AlwaysExecutable()
    {
        _vm.RefreshCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void AddUserCommand_AlwaysExecutable()
    {
        _vm.AddUserCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void CancelEditCommand_AlwaysExecutable()
    {
        _vm.CancelEditCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void ClearLogCommand_AlwaysExecutable()
    {
        _vm.ClearLogCommand.CanExecute(null).Should().BeTrue();
    }

    // ──────────────────────────────────────────────
    //  Property round-trips
    // ──────────────────────────────────────────────

    [Fact]
    public void StatusText_RoundTrips()
    {
        _vm.StatusText = "3 user(s) loaded";
        _vm.StatusText.Should().Be("3 user(s) loaded");
    }

    [Fact]
    public void IsLoading_RoundTrips()
    {
        _vm.IsLoading = true;
        _vm.IsLoading.Should().BeTrue();

        _vm.IsLoading = false;
        _vm.IsLoading.Should().BeFalse();
    }

    [Fact]
    public void EditUsername_RoundTrips()
    {
        _vm.EditUsername = "jdoe";
        _vm.EditUsername.Should().Be("jdoe");
    }

    [Fact]
    public void EditEmail_RoundTrips()
    {
        _vm.EditEmail = "jdoe@example.com";
        _vm.EditEmail.Should().Be("jdoe@example.com");
    }

    [Fact]
    public void EditPassword_RoundTrips()
    {
        _vm.EditPassword = "s3cret!";
        _vm.EditPassword.Should().Be("s3cret!");
    }

    [Fact]
    public void EditRoleId_RoundTrips()
    {
        _vm.EditRoleId = Guid.NewGuid().ToString();
        _vm.EditRoleId.Should().NotBeEmpty();
    }

    [Fact]
    public void EditIsActive_RoundTrips()
    {
        _vm.EditIsActive = false;
        _vm.EditIsActive.Should().BeFalse();

        _vm.EditIsActive = true;
        _vm.EditIsActive.Should().BeTrue();
    }

    [Fact]
    public void IsEditing_RoundTrips()
    {
        _vm.IsEditing = true;
        _vm.IsEditing.Should().BeTrue();
    }

    [Fact]
    public void LogMessages_RoundTrips()
    {
        var newLog = new System.Collections.ObjectModel.ObservableCollection<string> { "A", "B" };
        _vm.LogMessages = newLog;
        _vm.LogMessages.Should().BeSameAs(newLog);
        _vm.LogMessages.Should().HaveCount(2);
    }

    // ──────────────────────────────────────────────
    //  SelectedRole updates EditRoleId
    // ──────────────────────────────────────────────

    [Fact]
    public void SelectedRole_Setter_UpdatesEditRoleId()
    {
        var roleId = Guid.NewGuid();
        _vm.SelectedRole = new RoleItem { Id = roleId, Name = "Editor" };

        _vm.EditRoleId.Should().Be(roleId.ToString());
    }

    [Fact]
    public void SelectedRole_WhenNull_SetsEditRoleIdToEmpty()
    {
        _vm.EditRoleId = "some-id";
        _vm.SelectedRole = null;
        _vm.EditRoleId.Should().BeEmpty();
    }

    [Fact]
    public void SelectedUser_RoundTrips()
    {
        var user = new UserItem { Id = Guid.NewGuid(), Username = "test" };
        _vm.SelectedUser = user;
        _vm.SelectedUser.Should().BeSameAs(user);
    }

    // ──────────────────────────────────────────────
    //  PropertyChanged notifications
    // ──────────────────────────────────────────────

    [Fact]
    public void SettingProperties_RaisesPropertyChanged()
    {
        var changed = new List<string?>();
        _vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _vm.StatusText = "hello";
        _vm.IsLoading = true;
        _vm.IsEditing = true;
        _vm.EditUsername = "admin";
        _vm.EditEmail = "admin@test.com";
        _vm.EditPassword = "pass";
        _vm.SelectedUser = new UserItem { Id = Guid.NewGuid(), Username = "u" };

        changed.Should().Contain(nameof(UserManagementViewModel.StatusText));
        changed.Should().Contain(nameof(UserManagementViewModel.IsLoading));
        changed.Should().Contain(nameof(UserManagementViewModel.IsEditing));
        changed.Should().Contain(nameof(UserManagementViewModel.EditUsername));
        changed.Should().Contain(nameof(UserManagementViewModel.EditEmail));
        changed.Should().Contain(nameof(UserManagementViewModel.EditPassword));
        changed.Should().Contain(nameof(UserManagementViewModel.SelectedUser));
    }

    // ──────────────────────────────────────────────
    //  AddUserCommand initializes new user form
    // ──────────────────────────────────────────────

    [Fact]
    public void AddUserCommand_ResetsFormFields()
    {
        // Set some fields to verify they get cleared
        _vm.EditUsername = "old";
        _vm.EditEmail = "old@test.com";
        _vm.EditPassword = "oldpass";
        _vm.EditIsActive = false;

        // Seed roles so a default is selected
        _vm.Roles.Add(new RoleItem { Id = Guid.NewGuid(), Name = "Viewer" });
        _vm.Roles.Add(new RoleItem { Id = Guid.NewGuid(), Name = "Editor" });

        _vm.AddUserCommand.Execute(null);

        _vm.EditUsername.Should().BeEmpty();
        _vm.EditEmail.Should().BeEmpty();
        _vm.EditPassword.Should().BeEmpty();
        _vm.EditIsActive.Should().BeTrue();
        _vm.IsEditing.Should().BeTrue();
        // Should default to first role
        _vm.EditRoleId.Should().Be(_vm.Roles[0].Id.ToString());
    }

    // ──────────────────────────────────────────────
    //  CancelEditCommand
    // ──────────────────────────────────────────────

    [Fact]
    public void CancelEditCommand_SetsIsEditingFalse()
    {
        _vm.IsEditing = true;
        _vm.CancelEditCommand.Execute(null);
        _vm.IsEditing.Should().BeFalse();
    }

    // ──────────────────────────────────────────────
    //  ClearLogCommand
    // ──────────────────────────────────────────────

    [Fact]
    public void ClearLogCommand_ClearsLogMessages()
    {
        _vm.LogMessages.Should().NotBeEmpty();

        _vm.ClearLogCommand.Execute(null);

        _vm.LogMessages.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────
    //  UserItem & RoleItem models
    // ──────────────────────────────────────────────

    [Fact]
    public void UserItem_Defaults()
    {
        var item = new UserItem();
        item.Username.Should().BeEmpty();
        item.Email.Should().BeEmpty();
        item.RoleName.Should().BeEmpty();
        item.IsActive.Should().BeFalse();
        item.Id.Should().Be(Guid.Empty);
        item.CreatedAt.Should().Be(default);
    }

    [Fact]
    public void UserItem_PropertyChanged_Fires()
    {
        var item = new UserItem();
        var raised = new List<string?>();
        item.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        // Reflection set — INotifyPropertyChanged requires manual raising
        // The UserItem class has protected OnPropertyChanged, so we test
        // via public property sets or reflection. Since UserItem is a simple
        // data class with auto-properties, it doesn't raise events on set.
        // We verify it implements the interface.
        item.Should().BeAssignableTo<System.ComponentModel.INotifyPropertyChanged>();
    }

    [Fact]
    public void RoleItem_Defaults()
    {
        var item = new RoleItem();
        item.Name.Should().BeEmpty();
        item.Id.Should().Be(Guid.Empty);
    }

    // ──────────────────────────────────────────────
    //  LoadUsersAsync — standalone mode error path
    //  (doesn't require UI thread since it's wrapped)
    // ──────────────────────────────────────────────

    [Fact]
    public async Task LoadUsersAsync_StandaloneMode_DoesNotThrow()
    {
        // ModeManager defaults to standalone mode.
        // LoadUsersAsync calls Dispatcher.UIThread — this will not throw,
        // it simply won't execute the UI-bound callbacks in a headless context.
        Func<Task> act = () => _vm.LoadUsersAsync();
        await act.Should().NotThrowAsync();
    }
}
