using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Adam.Shared.Models;
using Adam.Shared.Services;
using Avalonia.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.ServiceManager.ViewModels;

public class UserItem : INotifyPropertyChanged
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public class RoleItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class UserManagementViewModel : INotifyPropertyChanged
{
    private readonly ILogger<UserManagementViewModel> _logger;
    private readonly ModeManager _modeManager;
    private string _statusText = string.Empty;
    private bool _isLoading;
    private UserItem? _selectedUser;
    private string _editUsername = string.Empty;
    private string _editEmail = string.Empty;
    private string _editPassword = string.Empty;
    private string _editRoleId = string.Empty;
    private bool _editIsActive = true;
    private bool _isEditing;
    private RoleItem? _selectedRole;
    private ObservableCollection<string> _logMessages = [];
    private Guid _editUserId;

    public UserManagementViewModel(ModeManager modeManager, ILogger<UserManagementViewModel>? logger = null)
    {
        _logger = logger ?? NullLogger<UserManagementViewModel>.Instance;
        _modeManager = modeManager ?? throw new ArgumentNullException(nameof(modeManager));

        RefreshCommand = new RelayCommand(async _ => await LoadUsersAsync());
        AddUserCommand = new RelayCommand(_ => BeginAddUser());
        EditUserCommand = new RelayCommand(_ => BeginEditUser(), _ => SelectedUser != null);
        SaveUserCommand = new RelayCommand(async _ => await SaveUserAsync());
        DeleteUserCommand = new RelayCommand(async _ => await DeleteUserAsync(), _ => SelectedUser != null);
        CancelEditCommand = new RelayCommand(_ => CancelEdit());
        ClearLogCommand = new RelayCommand(_ => LogMessages.Clear());

        _logMessages.Add($"[{DateTime.Now:HH:mm:ss.fff}] User Management initialized");
    }

    public ObservableCollection<UserItem> Users { get; } = [];
    public ObservableCollection<RoleItem> Roles { get; } = [];

    public ICommand RefreshCommand { get; }
    public ICommand AddUserCommand { get; }
    public ICommand EditUserCommand { get; }
    public ICommand SaveUserCommand { get; }
    public ICommand DeleteUserCommand { get; }
    public ICommand CancelEditCommand { get; }
    public ICommand ClearLogCommand { get; }

    public ObservableCollection<string> LogMessages
    {
        get => _logMessages;
        set { _logMessages = value; OnPropertyChanged(); }
    }

    public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }
    public bool IsLoading { get => _isLoading; set { _isLoading = value; OnPropertyChanged(); } }

    public UserItem? SelectedUser
    {
        get => _selectedUser;
        set { _selectedUser = value; OnPropertyChanged(); }
    }

    public bool IsEditing { get => _isEditing; set { _isEditing = value; OnPropertyChanged(); } }
    public string EditUsername { get => _editUsername; set { _editUsername = value; OnPropertyChanged(); } }
    public string EditEmail { get => _editEmail; set { _editEmail = value; OnPropertyChanged(); } }
    public string EditPassword { get => _editPassword; set { _editPassword = value; OnPropertyChanged(); } }
    public string EditRoleId { get => _editRoleId; set { _editRoleId = value; OnPropertyChanged(); } }
    public bool EditIsActive { get => _editIsActive; set { _editIsActive = value; OnPropertyChanged(); } }

    public RoleItem? SelectedRole
    {
        get => _selectedRole;
        set { _selectedRole = value; OnPropertyChanged(); EditRoleId = value?.Id.ToString() ?? ""; }
    }

    private void AddLog(string message)
    {
        _logger.LogInformation("[UserManagement] {Message}", message);
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        var entry = $"[{timestamp}] {message}";

        if (Dispatcher.UIThread.CheckAccess())
        {
            AddLogEntry(entry);
        }
        else
        {
            Dispatcher.UIThread.Post(() => AddLogEntry(entry));
        }
    }

    private void AddLogEntry(string entry)
    {
        if (_logMessages.Count > 500)
            _logMessages.RemoveAt(0);
        _logMessages.Add(entry);
    }

    public async Task LoadUsersAsync()
    {
        AddLog("Loading users...");
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            IsLoading = true;
            Users.Clear();
            Roles.Clear();
        });

        try
        {
            AddLog("Querying database...");
            List<UserItem> userItems;
            List<RoleItem> roleItems;

            await using (var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false))
            {
                var users = await db.Users
                    .Include(u => u.Role)
                    .OrderBy(u => u.Username)
                    .ToListAsync().ConfigureAwait(false);
                userItems = users.Select(u => new UserItem
                {
                    Id = u.Id,
                    Username = u.Username,
                    Email = u.Email,
                    RoleName = u.Role?.Name ?? "",
                    IsActive = u.IsActive,
                    CreatedAt = u.CreatedAt
                }).ToList();

                var roles = await db.Roles.OrderBy(r => r.Name).ToListAsync().ConfigureAwait(false);
                roleItems = roles.Select(r => new RoleItem { Id = r.Id, Name = r.Name }).ToList();
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var u in userItems)
                    Users.Add(u);
                foreach (var r in roleItems)
                    Roles.Add(r);
            });
            AddLog($"Loaded {userItems.Count} user(s) and {roleItems.Count} role(s).");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load users");
            AddLog($"ERROR loading users: {ex.GetType().Name}: {ex.Message}");
            await Dispatcher.UIThread.InvokeAsync(() => StatusText = $"Error: {ex.Message}");
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsLoading = false);
        }
    }

    private void BeginAddUser()
    {
        AddLog("Opening Add User form...");
        _editUserId = Guid.Empty;
        EditUsername = "";
        EditEmail = "";
        EditPassword = "";
        EditRoleId = Roles.FirstOrDefault()?.Id.ToString() ?? "";
        EditIsActive = true;
        IsEditing = true;
    }

    private void BeginEditUser()
    {
        if (SelectedUser == null) return;
        AddLog($"Editing user '{SelectedUser.Username}' (ID={SelectedUser.Id})...");
        _editUserId = SelectedUser.Id;
        EditUsername = SelectedUser.Username;
        EditEmail = SelectedUser.Email;
        EditPassword = "";
        EditRoleId = Roles.FirstOrDefault(r => r.Name == SelectedUser.RoleName)?.Id.ToString() ?? "";
        EditIsActive = SelectedUser.IsActive;
        IsEditing = true;
    }

    private async Task SaveUserAsync()
    {
        if (string.IsNullOrWhiteSpace(EditUsername) || EditUsername.Length < 2)
        {
            StatusText = "Username is required (min 2 characters).";
            return;
        }

        if (string.IsNullOrWhiteSpace(EditEmail) || !EditEmail.Contains('@') || !EditEmail.Contains('.'))
        {
            StatusText = "A valid email address is required.";
            return;
        }

        if (_editUserId == Guid.Empty && string.IsNullOrWhiteSpace(EditPassword))
        {
            StatusText = "Password is required for new users.";
            return;
        }

        if (!string.IsNullOrEmpty(EditPassword) && EditPassword.Length < 4)
        {
            StatusText = "Password must be at least 4 characters.";
            return;
        }

        try
        {
            if (_editUserId == Guid.Empty)
                AddLog($"Saving new user '{EditUsername}' ({EditEmail})...");
            else
                AddLog($"Updating user '{EditUsername}' (ID={_editUserId})...");

            AddLog("Writing to database...");
            await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);
            if (_editUserId == Guid.Empty)
            {
                var roleId = Guid.Parse(EditRoleId);
                var user = new User
                {
                    Id = Guid.NewGuid(),
                    Username = EditUsername,
                    Email = EditEmail,
                    PasswordHash = HashPassword(EditPassword),
                    RoleId = roleId,
                    IsActive = true,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                db.Users.Add(user);
                AddLog($"Created user '{user.Username}' with ID={user.Id}");
            }
            else
            {
                var user = await db.Users.FindAsync(_editUserId).ConfigureAwait(false);
                if (user != null)
                {
                    user.Email = EditEmail;
                    if (!string.IsNullOrEmpty(EditPassword))
                        user.PasswordHash = HashPassword(EditPassword);
                    user.RoleId = Guid.Parse(EditRoleId);
                    user.IsActive = EditIsActive;
                    db.Users.Update(user);
                    AddLog($"Updated user '{user.Username}' (ID={user.Id})");
                }
            }
            await db.SaveChangesAsync().ConfigureAwait(false);

            IsEditing = false;
            await LoadUsersAsync();
            StatusText = "User saved.";
            AddLog("User saved successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save user");
            AddLog($"ERROR saving user: {ex.GetType().Name}: {ex.Message}");
            StatusText = $"Error saving user: {ex.Message}";
        }
    }

    private async Task DeleteUserAsync()
    {
        if (SelectedUser == null) return;
        AddLog($"Deactivating user '{SelectedUser.Username}' (ID={SelectedUser.Id})...");
        try
        {
            await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);
            var user = await db.Users.FindAsync(SelectedUser.Id).ConfigureAwait(false);
            if (user != null)
            {
                user.IsActive = false;
                db.Users.Update(user);
                await db.SaveChangesAsync().ConfigureAwait(false);
                AddLog($"User '{user.Username}' (ID={user.Id}) deactivated.");
            }

            await LoadUsersAsync();
            StatusText = "User deactivated.";
            AddLog("User deactivated successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete user");
            AddLog($"ERROR deleting user: {ex.GetType().Name}: {ex.Message}");
            StatusText = $"Error deleting user: {ex.Message}";
        }
    }

    private void CancelEdit() => IsEditing = false;

    private static string HashPassword(string password) => PasswordHelper.HashPassword(password);

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
