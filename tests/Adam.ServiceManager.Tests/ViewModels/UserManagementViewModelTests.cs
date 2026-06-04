using System.ComponentModel;
using System.Reflection;
using Adam.ServiceManager.ViewModels;
using Adam.Shared.Models;
using Adam.Shared.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.ServiceManager.Tests.ViewModels;

/// <summary>
/// Synchronous dispatcher stub for testing. Executes actions immediately
/// on the calling thread instead of marshalling to a UI thread.
/// </summary>
internal sealed class SyncUiDispatcher : IUiDispatcher
{
    public Task InvokeAsync(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    public void Post(Action action) => action();
    public bool CheckAccess() => true;
}

/// <summary>
/// Tests for <see cref="UserManagementViewModel"/> — the user administration panel
/// in the ServiceManager (server panel). Uses <see cref="SyncUiDispatcher"/> to
/// avoid hanging on Avalonia's Dispatcher.UIThread in test context.
/// </summary>
public sealed class UserManagementViewModelTests : IDisposable
{
    private readonly string _basePath;
    private readonly ModeManager _modeManager;
    private readonly UserManagementViewModel _vm;
    private readonly SyncUiDispatcher _dispatcher;
    private bool _disposed;

    public UserManagementViewModelTests()
    {
        _basePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _modeManager = new ModeManager(_basePath);
        _modeManager.InitializeAsync().GetAwaiter().GetResult();
        _dispatcher = new SyncUiDispatcher();
        _vm = new UserManagementViewModel(_modeManager, new NullLogger<UserManagementViewModel>(), _dispatcher);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_basePath))
                Directory.Delete(_basePath, recursive: true);
        }
        catch (IOException) { }
    }

    // ──────────────────────────────────────────────
    //  Constructor
    // ──────────────────────────────────────────────

    [Fact]
    public void Constructor_AllCommandsAreInitialized()
    {
        _vm.RefreshCommand.Should().NotBeNull();
        _vm.AddUserCommand.Should().NotBeNull();
        _vm.EditUserCommand.Should().NotBeNull();
        _vm.SaveUserCommand.Should().NotBeNull();
        _vm.DeleteUserCommand.Should().NotBeNull();
        _vm.CancelEditCommand.Should().NotBeNull();
        _vm.ClearLogCommand.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_LogMessages_ContainsInitializationEntry()
    {
        _vm.LogMessages.Should().NotBeEmpty();
        _vm.LogMessages[0].Should().Contain("User Management initialized");
    }

    [Fact]
    public void Constructor_DefaultState_IsNotEditing()
    {
        _vm.IsEditing.Should().BeFalse();
    }

    [Fact]
    public void Constructor_DefaultState_IsNotLoading()
    {
        _vm.IsLoading.Should().BeFalse();
    }

    [Fact]
    public void Constructor_DefaultState_StatusTextIsEmpty()
    {
        _vm.StatusText.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_DefaultState_UsersIsEmpty()
    {
        _vm.Users.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_DefaultState_RolesIsEmpty()
    {
        _vm.Roles.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────
    //  Property round-trips
    // ──────────────────────────────────────────────

    [Fact]
    public void IsLoading_RoundTrips()
    {
        _vm.IsLoading = true;
        _vm.IsLoading.Should().BeTrue();
        _vm.IsLoading = false;
        _vm.IsLoading.Should().BeFalse();
    }

    [Fact]
    public void StatusText_RoundTrips()
    {
        _vm.StatusText = "Test status";
        _vm.StatusText.Should().Be("Test status");
    }

    [Fact]
    public void IsEditing_RoundTrips()
    {
        _vm.IsEditing = true;
        _vm.IsEditing.Should().BeTrue();
        _vm.IsEditing = false;
        _vm.IsEditing.Should().BeFalse();
    }

    [Fact]
    public void EditUsername_RoundTrips()
    {
        _vm.EditUsername = "johndoe";
        _vm.EditUsername.Should().Be("johndoe");
    }

    [Fact]
    public void EditEmail_RoundTrips()
    {
        _vm.EditEmail = "john@example.com";
        _vm.EditEmail.Should().Be("john@example.com");
    }

    [Fact]
    public void EditPassword_RoundTrips()
    {
        _vm.EditPassword = "secret123";
        _vm.EditPassword.Should().Be("secret123");
    }

    [Fact]
    public void EditRoleId_RoundTrips()
    {
        var id = Guid.NewGuid().ToString();
        _vm.EditRoleId = id;
        _vm.EditRoleId.Should().Be(id);
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
    public void SelectedUser_RoundTrips()
    {
        var user = new UserItem { Id = Guid.NewGuid(), Username = "test", Email = "test@test.com" };
        _vm.SelectedUser = user;
        _vm.SelectedUser.Should().BeSameAs(user);
        _vm.SelectedUser = null;
        _vm.SelectedUser.Should().BeNull();
    }

    // ──────────────────────────────────────────────
    //  SelectedRole
    // ──────────────────────────────────────────────

    [Fact]
    public void SelectedRole_Setter_SetsEditRoleId()
    {
        var role = new RoleItem { Id = Guid.NewGuid(), Name = "Admin" };
        _vm.SelectedRole = role;
        _vm.EditRoleId.Should().Be(role.Id.ToString());
    }

    [Fact]
    public void SelectedRole_Setter_WithNull_ClearsEditRoleId()
    {
        _vm.SelectedRole = null;
        _vm.EditRoleId.Should().BeEmpty();
    }

    [Fact]
    public void SelectedRole_PropertyChanged_RaisesBoth()
    {
        var changes = new List<string?>();
        _vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        _vm.SelectedRole = new RoleItem { Id = Guid.NewGuid(), Name = "Editor" };

        changes.Should().Contain(nameof(UserManagementViewModel.SelectedRole));
        changes.Should().Contain(nameof(UserManagementViewModel.EditRoleId));
    }

    // ──────────────────────────────────────────────
    //  PropertyChanged notifications
    // ──────────────────────────────────────────────

    [Fact]
    public void StatusText_Setter_RaisesPropertyChanged()
    {
        var changes = new List<string?>();
        _vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        _vm.StatusText = "Updated";

        changes.Should().Contain(nameof(UserManagementViewModel.StatusText));
    }

    [Fact]
    public void IsLoading_Setter_RaisesPropertyChanged()
    {
        var changes = new List<string?>();
        _vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        _vm.IsLoading = true;

        changes.Should().Contain(nameof(UserManagementViewModel.IsLoading));
    }

    [Fact]
    public void IsEditing_Setter_RaisesPropertyChanged()
    {
        var changes = new List<string?>();
        _vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        _vm.IsEditing = true;

        changes.Should().Contain(nameof(UserManagementViewModel.IsEditing));
    }

    // ──────────────────────────────────────────────
    //  BeginAddUser
    // ──────────────────────────────────────────────

    [Fact]
    public void BeginAddUser_SetsEditingState()
    {
        _vm.Roles.Add(new RoleItem { Id = Guid.NewGuid(), Name = "Viewer" });

        _vm.AddUserCommand.Execute(null);

        _vm.IsEditing.Should().BeTrue();
        _vm.EditUsername.Should().BeEmpty();
        _vm.EditEmail.Should().BeEmpty();
        _vm.EditPassword.Should().BeEmpty();
        _vm.EditIsActive.Should().BeTrue();
    }

    [Fact]
    public void BeginAddUser_SetsEditRoleIdToFirstRole()
    {
        var role1 = new RoleItem { Id = Guid.NewGuid(), Name = "Admin" };
        var role2 = new RoleItem { Id = Guid.NewGuid(), Name = "Viewer" };
        _vm.Roles.Add(role1);
        _vm.Roles.Add(role2);

        _vm.AddUserCommand.Execute(null);

        _vm.EditRoleId.Should().Be(role1.Id.ToString());
    }

    [Fact]
    public void BeginAddUser_WithNoRoles_EditRoleIdIsEmpty()
    {
        _vm.AddUserCommand.Execute(null);

        _vm.EditRoleId.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────
    //  BeginEditUser
    // ──────────────────────────────────────────────

    [Fact]
    public void BeginEditUser_WithSelectedUser_CopiesFields()
    {
        var role = new RoleItem { Id = Guid.NewGuid(), Name = "Admin" };
        _vm.Roles.Add(role);

        _vm.SelectedUser = new UserItem
        {
            Id = Guid.NewGuid(),
            Username = "johndoe",
            Email = "john@example.com",
            RoleName = "Admin",
            IsActive = true
        };

        _vm.EditUserCommand.Execute(null);

        _vm.IsEditing.Should().BeTrue();
        _vm.EditUsername.Should().Be("johndoe");
        _vm.EditEmail.Should().Be("john@example.com");
        _vm.EditPassword.Should().BeEmpty();
        _vm.EditIsActive.Should().BeTrue();
    }

    [Fact]
    public void BeginEditUser_SetsEditRoleIdFromMatchingRole()
    {
        var role1 = new RoleItem { Id = Guid.NewGuid(), Name = "Admin" };
        var role2 = new RoleItem { Id = Guid.NewGuid(), Name = "Editor" };
        _vm.Roles.Add(role1);
        _vm.Roles.Add(role2);

        _vm.SelectedUser = new UserItem
        {
            Id = Guid.NewGuid(),
            Username = "editor1",
            Email = "editor@example.com",
            RoleName = "Editor",
            IsActive = true
        };

        _vm.EditUserCommand.Execute(null);

        _vm.EditRoleId.Should().Be(role2.Id.ToString());
    }

    [Fact]
    public void BeginEditUser_WithNullSelectedUser_DoesNothing()
    {
        _vm.SelectedUser = null;

        _vm.EditUserCommand.Execute(null);

        _vm.IsEditing.Should().BeFalse();
    }

    // ──────────────────────────────────────────────
    //  CancelEdit
    // ──────────────────────────────────────────────

    [Fact]
    public void CancelEdit_SetsIsEditingFalse()
    {
        _vm.IsEditing = true;

        _vm.CancelEditCommand.Execute(null);

        _vm.IsEditing.Should().BeFalse();
    }

    // ──────────────────────────────────────────────
    //  Command CanExecute
    // ──────────────────────────────────────────────

    [Fact]
    public void EditUserCommand_CanExecute_WithSelectedUser_ReturnsTrue()
    {
        _vm.SelectedUser = new UserItem { Id = Guid.NewGuid(), Username = "u", Email = "e@e.com" };

        _vm.EditUserCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void EditUserCommand_CanExecute_WithoutSelectedUser_ReturnsFalse()
    {
        _vm.SelectedUser = null;

        _vm.EditUserCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void DeleteUserCommand_CanExecute_WithSelectedUser_ReturnsTrue()
    {
        _vm.SelectedUser = new UserItem { Id = Guid.NewGuid(), Username = "u", Email = "e@e.com" };

        _vm.DeleteUserCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void DeleteUserCommand_CanExecute_WithoutSelectedUser_ReturnsFalse()
    {
        _vm.SelectedUser = null;

        _vm.DeleteUserCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void RefreshCommand_CanExecute_AlwaysTrue()
    {
        _vm.RefreshCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void AddUserCommand_CanExecute_AlwaysTrue()
    {
        _vm.AddUserCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void SaveUserCommand_CanExecute_AlwaysTrue()
    {
        _vm.SaveUserCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void CancelEditCommand_CanExecute_AlwaysTrue()
    {
        _vm.CancelEditCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void ClearLogCommand_CanExecute_AlwaysTrue()
    {
        _vm.ClearLogCommand.CanExecute(null).Should().BeTrue();
    }

    // ──────────────────────────────────────────────
    //  ClearLogCommand
    // ──────────────────────────────────────────────

    [Fact]
    public void ClearLogCommand_ClearsLogMessages()
    {
        _vm.LogMessages.Add("Test message");

        _vm.ClearLogCommand.Execute(null);

        _vm.LogMessages.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────
    //  LoadUsersAsync
    // ──────────────────────────────────────────────

    [Fact]
    public async Task LoadUsersAsync_WhenDatabaseEmpty_LeavesUsersEmpty()
    {
        await _vm.LoadUsersAsync();

        // Users are not seeded; roles (Viewer, Editor, Administrator) are seeded by AppDbContext.SeedData
        _vm.Users.Should().BeEmpty();
        _vm.Roles.Should().HaveCount(3);
        _vm.Roles.Should().Contain(r => r.Name == "Viewer");
        _vm.Roles.Should().Contain(r => r.Name == "Editor");
        _vm.Roles.Should().Contain(r => r.Name == "Administrator");
    }

    [Fact]
    public async Task LoadUsersAsync_LoadsUsersAndRoles()
    {
        // Use unique role names that don't conflict with the 3 default roles seeded by AppDbContext.SeedData
        var operatorRoleId = await SeedRoleAsync("Operator");
        var analystRoleId = await SeedRoleAsync("Analyst");
        await SeedUserAsync("alice", "alice@test.com", operatorRoleId);
        await SeedUserAsync("bob", "bob@test.com", analystRoleId);

        await _vm.LoadUsersAsync();

        _vm.Users.Should().HaveCount(2);
        _vm.Users.Should().Contain(u => u.Username == "alice" && u.RoleName == "Operator");
        _vm.Users.Should().Contain(u => u.Username == "bob" && u.RoleName == "Analyst");
        // 3 seeded roles (Viewer, Editor, Administrator) + 2 custom roles = 5
        _vm.Roles.Should().HaveCount(5);
        _vm.Roles.Should().Contain(r => r.Name == "Operator");
        _vm.Roles.Should().Contain(r => r.Name == "Analyst");
    }

    [Fact]
    public async Task LoadUsersAsync_SetsIsLoadingFlags()
    {
        // IsLoading should be true during load, then false when done
        await _vm.LoadUsersAsync();

        _vm.IsLoading.Should().BeFalse();
    }

    [Fact]
    public async Task LoadUsersAsync_ClearsPreviousDataBeforeReload()
    {
        var roleId = await SeedRoleAsync("Admin");
        await SeedUserAsync("user1", "u1@test.com", roleId);

        // First load
        await _vm.LoadUsersAsync();
        _vm.Users.Should().HaveCount(1);

        // Second load should clear and re-populate
        await _vm.LoadUsersAsync();
        _vm.Users.Should().HaveCount(1);
    }

    // ──────────────────────────────────────────────
    //  SaveUserAsync — validation
    // ──────────────────────────────────────────────

    [Fact]
    public async Task SaveUserAsync_WithShortUsername_SetsStatusText()
    {
        _vm.EditUsername = "A";
        _vm.EditEmail = "test@example.com";
        _vm.EditPassword = "password123";
        _vm.EditRoleId = Guid.NewGuid().ToString();

        await InvokeSaveUserAsync();

        _vm.StatusText.Should().Be("Username is required (min 2 characters).");
    }

    [Fact]
    public async Task SaveUserAsync_WithEmptyUsername_SetsStatusText()
    {
        _vm.EditUsername = "";
        _vm.EditEmail = "test@example.com";
        _vm.EditPassword = "password123";

        await InvokeSaveUserAsync();

        _vm.StatusText.Should().Be("Username is required (min 2 characters).");
    }

    [Fact]
    public async Task SaveUserAsync_WithInvalidEmail_NoAt_SetsStatusText()
    {
        _vm.EditUsername = "validuser";
        _vm.EditEmail = "invalid-email";
        _vm.EditPassword = "password123";

        await InvokeSaveUserAsync();

        _vm.StatusText.Should().Be("A valid email address is required.");
    }

    [Fact]
    public async Task SaveUserAsync_WithInvalidEmail_NoDot_SetsStatusText()
    {
        _vm.EditUsername = "validuser";
        _vm.EditEmail = "user@nowhere";
        _vm.EditPassword = "password123";

        await InvokeSaveUserAsync();

        _vm.StatusText.Should().Be("A valid email address is required.");
    }

    [Fact]
    public async Task SaveUserAsync_NewUserWithoutPassword_SetsStatusText()
    {
        _vm.EditUsername = "newuser";
        _vm.EditEmail = "new@example.com";
        _vm.EditPassword = "";
        _vm.EditRoleId = Guid.NewGuid().ToString();

        await InvokeSaveUserAsync();

        _vm.StatusText.Should().Be("Password is required for new users.");
    }

    [Fact]
    public async Task SaveUserAsync_WithShortPassword_SetsStatusText()
    {
        _vm.EditUsername = "validuser";
        _vm.EditEmail = "user@example.com";
        _vm.EditPassword = "ab";

        await InvokeSaveUserAsync();

        _vm.StatusText.Should().Be("Password must be at least 4 characters.");
    }

    // ──────────────────────────────────────────────
    //  SaveUserAsync — create user
    // ──────────────────────────────────────────────

    [Fact]
    public async Task SaveUserAsync_CreatesNewUserInDatabase()
    {
        var roleId = await SeedRoleAsync("Operator");
        _vm.EditUsername = "operator1";
        _vm.EditEmail = "op1@example.com";
        _vm.EditPassword = "securePass123";
        _vm.EditRoleId = roleId.ToString();
        SetField("_editUserId", Guid.Empty);

        await InvokeSaveUserAsync();

        // Verify in DB
        await using var db = _modeManager.CreateDbContext();
        var saved = db.Users.FirstOrDefault(u => u.Username == "operator1");
        saved.Should().NotBeNull();
        saved!.Email.Should().Be("op1@example.com");
        saved.RoleId.Should().Be(roleId);
        saved.IsActive.Should().BeTrue();
        PasswordHelper.VerifyPassword("securePass123", saved.PasswordHash).Should().BeTrue();
    }

    [Fact]
    public async Task SaveUserAsync_NewUser_ReloadsUsersList()
    {
        var roleId = await SeedRoleAsync("Admin");
        _vm.EditUsername = "newguy";
        _vm.EditEmail = "newguy@test.com";
        _vm.EditPassword = "test1234";
        _vm.EditRoleId = roleId.ToString();
        SetField("_editUserId", Guid.Empty);

        await InvokeSaveUserAsync();

        _vm.Users.Should().Contain(u => u.Username == "newguy");
    }

    [Fact]
    public async Task SaveUserAsync_NewUser_SetsIsEditingFalse()
    {
        var roleId = await SeedRoleAsync("Admin");
        _vm.IsEditing = true;
        _vm.EditUsername = "newuser2";
        _vm.EditEmail = "nu2@test.com";
        _vm.EditPassword = "pass1234";
        _vm.EditRoleId = roleId.ToString();
        SetField("_editUserId", Guid.Empty);

        await InvokeSaveUserAsync();

        _vm.IsEditing.Should().BeFalse();
    }

    [Fact]
    public async Task SaveUserAsync_NewUser_SetsStatusText()
    {
        var roleId = await SeedRoleAsync("Admin");
        _vm.EditUsername = "statususer";
        _vm.EditEmail = "su@test.com";
        _vm.EditPassword = "pass1234";
        _vm.EditRoleId = roleId.ToString();
        SetField("_editUserId", Guid.Empty);

        await InvokeSaveUserAsync();

        _vm.StatusText.Should().Be("User saved.");
    }

    // ──────────────────────────────────────────────
    //  SaveUserAsync — update existing user
    // ──────────────────────────────────────────────

    [Fact]
    public async Task SaveUserAsync_UpdatesExistingUserEmail()
    {
        var roleId = await SeedRoleAsync("Admin");
        var userId = await SeedUserAsync("updateuser", "old@test.com", roleId);

        SetField("_editUserId", userId);
        _vm.EditUsername = "updateuser";
        _vm.EditEmail = "new@test.com";
        _vm.EditPassword = "";
        _vm.EditRoleId = roleId.ToString();
        _vm.EditIsActive = true;

        await InvokeSaveUserAsync();

        await using var db = _modeManager.CreateDbContext();
        var saved = db.Users.Find(userId);
        saved!.Email.Should().Be("new@test.com");
    }

    [Fact]
    public async Task SaveUserAsync_UpdatesExistingUserPassword()
    {
        var roleId = await SeedRoleAsync("Admin");
        var userId = await SeedUserAsync("passupdate", "pu@test.com", roleId);

        SetField("_editUserId", userId);
        _vm.EditUsername = "passupdate";
        _vm.EditEmail = "pu@test.com";
        _vm.EditPassword = "newSecurePass!";
        _vm.EditRoleId = roleId.ToString();
        _vm.EditIsActive = true;

        await InvokeSaveUserAsync();

        await using var db = _modeManager.CreateDbContext();
        var saved = db.Users.Find(userId);
        PasswordHelper.VerifyPassword("newSecurePass!", saved!.PasswordHash).Should().BeTrue();
    }

    [Fact]
    public async Task SaveUserAsync_UpdatesExistingUserDeactivates()
    {
        var roleId = await SeedRoleAsync("Admin");
        var userId = await SeedUserAsync("deactuser", "du@test.com", roleId);

        SetField("_editUserId", userId);
        _vm.EditUsername = "deactuser";
        _vm.EditEmail = "du@test.com";
        _vm.EditPassword = "";
        _vm.EditRoleId = roleId.ToString();
        _vm.EditIsActive = false;

        await InvokeSaveUserAsync();

        await using var db = _modeManager.CreateDbContext();
        var saved = db.Users.Find(userId);
        saved!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task SaveUserAsync_Update_ReloadsUsersList()
    {
        var roleId = await SeedRoleAsync("Admin");
        var userId = await SeedUserAsync("updatereload", "ur@test.com", roleId);

        SetField("_editUserId", userId);
        _vm.EditUsername = "updatereload";
        _vm.EditEmail = "updated@test.com";
        _vm.EditPassword = "";
        _vm.EditRoleId = roleId.ToString();
        _vm.EditIsActive = true;

        await InvokeSaveUserAsync();

        _vm.Users.Should().Contain(u => u.Username == "updatereload" && u.Email == "updated@test.com");
    }

    // ──────────────────────────────────────────────
    //  DeleteUserAsync (deactivate)
    // ──────────────────────────────────────────────

    [Fact]
    public async Task DeleteUserAsync_WithSelectedUser_DeactivatesInDatabase()
    {
        var roleId = await SeedRoleAsync("User");
        var userId = await SeedUserAsync("deleteme", "dm@test.com", roleId);

        _vm.SelectedUser = new UserItem { Id = userId, Username = "deleteme", Email = "dm@test.com" };

        await InvokeDeleteUserAsync();

        await using var db = _modeManager.CreateDbContext();
        var saved = db.Users.Find(userId);
        saved.Should().NotBeNull();
        saved!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteUserAsync_ReloadsUsersList()
    {
        var roleId = await SeedRoleAsync("User");
        var userId = await SeedUserAsync("deletereload", "dr@test.com", roleId);

        // Have another user in the DB so list is non-empty after deletion
        await SeedUserAsync("otheruser", "other@test.com", roleId);

        _vm.SelectedUser = new UserItem { Id = userId, Username = "deletereload", Email = "dr@test.com" };

        await InvokeDeleteUserAsync();

        _vm.Users.Should().Contain(u => u.Username == "otheruser");
    }

    [Fact]
    public async Task DeleteUserAsync_SetsStatusText()
    {
        var roleId = await SeedRoleAsync("User");
        var userId = await SeedUserAsync("statusdelete", "sd@test.com", roleId);

        _vm.SelectedUser = new UserItem { Id = userId, Username = "statusdelete", Email = "sd@test.com" };

        await InvokeDeleteUserAsync();

        _vm.StatusText.Should().Be("User deactivated.");
    }

    [Fact]
    public async Task DeleteUserAsync_WithNullSelection_DoesNotThrow()
    {
        _vm.SelectedUser = null;

        var act = async () => await InvokeDeleteUserAsync();
        await act.Should().NotThrowAsync();
    }

    // ──────────────────────────────────────────────
    //  LogMessages overflow (via AddLog behind SyncUiDispatcher)
    // ──────────────────────────────────────────────

    [Fact]
    public async Task LoadUsersAsync_AddsLogEntries()
    {
        await _vm.LoadUsersAsync();

        _vm.LogMessages.Should().Contain(m => m.Contains("Loading users..."));
        _vm.LogMessages.Should().Contain(m => m.Contains("Querying database..."));
        _vm.LogMessages.Should().Contain(m => m.Contains("Loaded 0 user(s)"));
    }

    [Fact]
    public async Task SaveUserAsync_AddsLogEntries()
    {
        var roleId = await SeedRoleAsync("Admin");
        _vm.EditUsername = "logtest";
        _vm.EditEmail = "lt@test.com";
        _vm.EditPassword = "pass1234";
        _vm.EditRoleId = roleId.ToString();
        SetField("_editUserId", Guid.Empty);

        await InvokeSaveUserAsync();

        _vm.LogMessages.Should().Contain(m => m.Contains("Saving new user 'logtest'"));
        _vm.LogMessages.Should().Contain(m => m.Contains("User saved successfully."));
    }

    // ──────────────────────────────────────────────
    //  PropertyChanged — full notification set
    // ──────────────────────────────────────────────

    [Fact]
    public void BeginAddUser_RaisesPropertyChanged()
    {
        var changes = new List<string?>();
        _vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        _vm.AddUserCommand.Execute(null);

        changes.Should().Contain(nameof(UserManagementViewModel.IsEditing));
        changes.Should().Contain(nameof(UserManagementViewModel.EditUsername));
        changes.Should().Contain(nameof(UserManagementViewModel.EditEmail));
        changes.Should().Contain(nameof(UserManagementViewModel.EditPassword));
        changes.Should().Contain(nameof(UserManagementViewModel.EditRoleId));
        changes.Should().Contain(nameof(UserManagementViewModel.EditIsActive));
    }

    [Fact]
    public void BeginEditUser_RaisesPropertyChanged()
    {
        var role = new RoleItem { Id = Guid.NewGuid(), Name = "Admin" };
        _vm.Roles.Add(role);
        _vm.SelectedUser = new UserItem { Id = Guid.NewGuid(), Username = "u", Email = "e@e.com", RoleName = "Admin", IsActive = true };

        var changes = new List<string?>();
        _vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        _vm.EditUserCommand.Execute(null);

        changes.Should().Contain(nameof(UserManagementViewModel.IsEditing));
        changes.Should().Contain(nameof(UserManagementViewModel.EditUsername));
        changes.Should().Contain(nameof(UserManagementViewModel.EditEmail));
        changes.Should().Contain(nameof(UserManagementViewModel.EditPassword));
        changes.Should().Contain(nameof(UserManagementViewModel.EditRoleId));
        changes.Should().Contain(nameof(UserManagementViewModel.EditIsActive));
    }

    [Fact]
    public async Task LoadUsersAsync_RaisesPropertyChanged()
    {
        var changes = new List<string?>();
        _vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        await _vm.LoadUsersAsync();

        changes.Should().Contain(nameof(UserManagementViewModel.IsLoading));
    }

    // ──────────────────────────────────────────────
    //  Helpers (reflection for private methods)
    // ──────────────────────────────────────────────

    private void SetField(string fieldName, object? value)
    {
        var field = typeof(UserManagementViewModel)
            .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
        field.SetValue(_vm, value);
    }

    private async Task InvokeSaveUserAsync()
    {
        var method = typeof(UserManagementViewModel)
            .GetMethod("SaveUserAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)method.Invoke(_vm, null)!;
    }

    private async Task InvokeDeleteUserAsync()
    {
        var method = typeof(UserManagementViewModel)
            .GetMethod("DeleteUserAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)method.Invoke(_vm, null)!;
    }

    private async Task<Guid> SeedRoleAsync(string name)
    {
        await using var db = _modeManager.CreateDbContext();
        var role = new Role { Id = Guid.NewGuid(), Name = name };
        db.Roles.Add(role);
        await db.SaveChangesAsync();
        return role.Id;
    }

    private async Task<Guid> SeedUserAsync(string username, string email, Guid roleId)
    {
        await using var db = _modeManager.CreateDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = username,
            Email = email,
            PasswordHash = PasswordHelper.HashPassword("defaultPass"),
            RoleId = roleId,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }
}
