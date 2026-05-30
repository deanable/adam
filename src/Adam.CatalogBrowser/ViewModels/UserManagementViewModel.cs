using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Adam.CatalogBrowser.Services;
using Adam.Shared.Contracts;
using Adam.Shared.Services;
using Avalonia.Threading;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.CatalogBrowser.ViewModels;

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
        _modeManager = modeManager;
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
            if (_modeManager.IsStandalone)
            {
                AddLog("Standalone mode: querying SQLite database...");
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
                AddLog($"Loaded {userItems.Count} user(s) and {roleItems.Count} role(s) from database.");
            }
            else if (_modeManager.IsMultiUser)
            {
                var broker = _modeManager.BrokerClient;
                var auth = _modeManager.AuthSession;
                if (broker == null || auth == null) return;

                if (!broker.IsConnected)
                {
                    AddLog("Multi-user mode: connecting to broker...");
                    await broker.ConnectAsync().ConfigureAwait(false);
                }

                AddLog("Requesting user list from broker...");
                var corrId = Guid.NewGuid().ToString();
                var req = new Envelope
                {
                    AuthToken = auth.Token ?? "",
                    CorrelationId = corrId,
                    MessageType = MessageTypeCode.ListUsersRequest,
                    Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new ListUsersRequest()))
                };

                var resp = await broker.SendAsync(req).ConfigureAwait(false);
                if (resp.StatusCode == 0)
                {
                    var listResp = ProtoHelper.Deserialize<ListUsersResponse>(resp.Payload.ToByteArray());
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        foreach (var u in listResp.Items)
                        {
                            Users.Add(new UserItem
                            {
                                Id = Guid.Parse(u.Id),
                                Username = u.Username,
                                Email = u.Email,
                                RoleName = u.RoleName,
                                IsActive = u.IsActive,
                                CreatedAt = DateTimeOffset.FromUnixTimeSeconds(u.CreatedAt)
                            });
                        }
                    });
                    AddLog($"Loaded {listResp.Items.Count} user(s) from broker.");
                }
                else
                {
                    AddLog($"Broker returned error {resp.StatusCode}: {resp.ErrorMessage}");
                }

                AddLog("Requesting role list from broker...");
                var roleReq = new Envelope
                {
                    AuthToken = auth.Token ?? "",
                    CorrelationId = Guid.NewGuid().ToString(),
                    MessageType = MessageTypeCode.ListRolesRequest,
                    Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new ListRolesRequest()))
                };
                var roleResp = await broker.SendAsync(roleReq).ConfigureAwait(false);
                if (roleResp.StatusCode == 0)
                {
                    var listRoles = ProtoHelper.Deserialize<ListRolesResponse>(roleResp.Payload.ToByteArray());
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        foreach (var r in listRoles.Items)
                            Roles.Add(new RoleItem { Id = Guid.Parse(r.Id), Name = r.Name });
                    });
                    AddLog($"Loaded {listRoles.Items.Count} role(s) from broker.");
                }
            }

            await Dispatcher.UIThread.InvokeAsync(() => StatusText = $"{Users.Count} user(s)");
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
        try
        {
            if (_editUserId == Guid.Empty)
            {
                AddLog($"Saving new user '{EditUsername}' ({EditEmail})...");
            }
            else
            {
                AddLog($"Updating user '{EditUsername}' (ID={_editUserId})...");
            }

            if (_modeManager.IsStandalone)
            {
                AddLog("Standalone mode: writing to SQLite database...");
                await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);
                if (_editUserId == Guid.Empty)
                {
                    var roleId = Guid.Parse(EditRoleId);
                    var user = new Adam.Shared.Models.User
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
            }
            else if (_modeManager.IsMultiUser)
            {
                var broker = _modeManager.BrokerClient;
                var auth = _modeManager.AuthSession;
                if (broker == null || auth == null) return;

                if (_editUserId == Guid.Empty)
                {
                    var req = new Envelope
                    {
                        AuthToken = auth.Token ?? "",
                        CorrelationId = Guid.NewGuid().ToString(),
                        MessageType = MessageTypeCode.CreateUserRequest,
                        Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new CreateUserRequest
                        {
                            Username = EditUsername,
                            Email = EditEmail,
                            Password = EditPassword,
                            RoleId = EditRoleId
                        }))
                    };
                    await broker.SendAsync(req);
                    AddLog($"Create user request sent to broker for '{EditUsername}'.");
                }
                else
                {
                    var req = new Envelope
                    {
                        AuthToken = auth.Token ?? "",
                        CorrelationId = Guid.NewGuid().ToString(),
                        MessageType = MessageTypeCode.UpdateUserRequest,
                        Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new UpdateUserRequest
                        {
                            UserId = _editUserId.ToString(),
                            Email = EditEmail,
                            Password = string.IsNullOrEmpty(EditPassword) ? null : EditPassword,
                            RoleId = EditRoleId,
                            IsActive = EditIsActive
                        }))
                    };
                    await broker.SendAsync(req);
                    AddLog($"Update user request sent to broker for ID={_editUserId}.");
                }
            }

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
            if (_modeManager.IsStandalone)
            {
                await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);
                var user = await db.Users.FindAsync(SelectedUser.Id).ConfigureAwait(false);
                if (user != null)
                {
                    user.IsActive = false;
                    db.Users.Update(user);
                    await db.SaveChangesAsync().ConfigureAwait(false);
                    AddLog($"User '{user.Username}' (ID={user.Id}) deactivated in database.");
                }
            }
            else if (_modeManager.IsMultiUser)
            {
                var broker = _modeManager.BrokerClient;
                var auth = _modeManager.AuthSession;
                if (broker == null || auth == null) return;

                var req = new Envelope
                {
                    AuthToken = auth.Token ?? "",
                    CorrelationId = Guid.NewGuid().ToString(),
                    MessageType = MessageTypeCode.DeleteUserRequest,
                    Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new DeleteUserRequest
                    {
                        UserId = SelectedUser.Id.ToString()
                    }))
                };
                await broker.SendAsync(req);
                AddLog($"Deactivate user request sent to broker for ID={SelectedUser.Id}.");
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
