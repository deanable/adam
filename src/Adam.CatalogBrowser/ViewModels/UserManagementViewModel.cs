using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Adam.CatalogBrowser.Services;
using Adam.Shared.Contracts;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;

namespace Adam.CatalogBrowser.ViewModels;

public class UserManagementViewModel : INotifyPropertyChanged
{
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

    public UserManagementViewModel(ModeManager modeManager)
    {
        _modeManager = modeManager;
        RefreshCommand = new RelayCommand(async _ => await LoadUsersAsync());
        AddUserCommand = new RelayCommand(_ => BeginAddUser());
        EditUserCommand = new RelayCommand(_ => BeginEditUser(), _ => SelectedUser != null);
        SaveUserCommand = new RelayCommand(async _ => await SaveUserAsync());
        DeleteUserCommand = new RelayCommand(async _ => await DeleteUserAsync(), _ => SelectedUser != null);
        CancelEditCommand = new RelayCommand(_ => CancelEdit());
    }

    public ObservableCollection<UserItem> Users { get; } = [];
    public ObservableCollection<RoleItem> Roles { get; } = [];

    public ICommand RefreshCommand { get; }
    public ICommand AddUserCommand { get; }
    public ICommand EditUserCommand { get; }
    public ICommand SaveUserCommand { get; }
    public ICommand DeleteUserCommand { get; }
    public ICommand CancelEditCommand { get; }

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

    private Guid _editUserId;

    public async Task LoadUsersAsync()
    {
        IsLoading = true;
        Users.Clear();
        Roles.Clear();

        try
        {
            if (_modeManager.IsStandalone)
            {
                await using var db = _modeManager.CreateDbContext();
                var users = db.Users.Include(u => u.Role).OrderBy(u => u.Username).ToList();
                foreach (var u in users)
                {
                    Users.Add(new UserItem
                    {
                        Id = u.Id,
                        Username = u.Username,
                        Email = u.Email,
                        RoleName = u.Role?.Name ?? "",
                        IsActive = u.IsActive,
                        CreatedAt = u.CreatedAt
                    });
                }

                foreach (var r in db.Roles.OrderBy(r => r.Name))
                    Roles.Add(new RoleItem { Id = r.Id, Name = r.Name });
            }
            else if (_modeManager.IsMultiUser)
            {
                var broker = _modeManager.BrokerClient;
                var auth = _modeManager.AuthSession;
                if (broker == null || auth == null) return;

                if (!broker.IsConnected)
                    await broker.ConnectAsync();

                var corrId = Guid.NewGuid().ToString();
                var req = new Envelope
                {
                    AuthToken = auth.Token ?? "",
                    CorrelationId = corrId,
                    MessageType = nameof(ListUsersRequest),
                    Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new ListUsersRequest()))
                };

                var resp = await broker.SendAsync(req);
                if (resp.StatusCode == 0)
                {
                    var listResp = ProtoHelper.Deserialize<ListUsersResponse>(resp.Payload.ToByteArray());
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
                }

                var roleReq = new Envelope
                {
                    AuthToken = auth.Token ?? "",
                    CorrelationId = Guid.NewGuid().ToString(),
                    MessageType = nameof(ListRolesRequest),
                    Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new ListRolesRequest()))
                };
                var roleResp = await broker.SendAsync(roleReq);
                if (roleResp.StatusCode == 0)
                {
                    var listRoles = ProtoHelper.Deserialize<ListRolesResponse>(roleResp.Payload.ToByteArray());
                    foreach (var r in listRoles.Items)
                        Roles.Add(new RoleItem { Id = Guid.Parse(r.Id), Name = r.Name });
                }
            }

            StatusText = $"{Users.Count} user(s)";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void BeginAddUser()
    {
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
            if (_modeManager.IsStandalone)
            {
                await using var db = _modeManager.CreateDbContext();
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
                }
                else
                {
                    var user = await db.Users.FindAsync(_editUserId);
                    if (user != null)
                    {
                        user.Email = EditEmail;
                        if (!string.IsNullOrEmpty(EditPassword))
                            user.PasswordHash = HashPassword(EditPassword);
                        user.RoleId = Guid.Parse(EditRoleId);
                        user.IsActive = EditIsActive;
                        db.Users.Update(user);
                    }
                }
                await db.SaveChangesAsync();
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
                        MessageType = nameof(CreateUserRequest),
                        Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new CreateUserRequest
                        {
                            Username = EditUsername,
                            Email = EditEmail,
                            Password = EditPassword,
                            RoleId = EditRoleId
                        }))
                    };
                    await broker.SendAsync(req);
                }
                else
                {
                    var req = new Envelope
                    {
                        AuthToken = auth.Token ?? "",
                        CorrelationId = Guid.NewGuid().ToString(),
                        MessageType = nameof(UpdateUserRequest),
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
                }
            }

            IsEditing = false;
            await LoadUsersAsync();
            StatusText = "User saved.";
        }
        catch (Exception ex)
        {
            StatusText = $"Error saving user: {ex.Message}";
        }
    }

    private async Task DeleteUserAsync()
    {
        if (SelectedUser == null) return;
        try
        {
            if (_modeManager.IsStandalone)
            {
                await using var db = _modeManager.CreateDbContext();
                var user = await db.Users.FindAsync(SelectedUser.Id);
                if (user != null)
                {
                    user.IsActive = false;
                    db.Users.Update(user);
                    await db.SaveChangesAsync();
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
                    MessageType = nameof(DeleteUserRequest),
                    Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new DeleteUserRequest
                    {
                        UserId = SelectedUser.Id.ToString()
                    }))
                };
                await broker.SendAsync(req);
            }

            await LoadUsersAsync();
            StatusText = "User deactivated.";
        }
        catch (Exception ex)
        {
            StatusText = $"Error deleting user: {ex.Message}";
        }
    }

    private void CancelEdit() => IsEditing = false;

    private static string HashPassword(string password)
    {
        var salt = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var hash = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
            password, salt, 600_000, System.Security.Cryptography.HashAlgorithmName.SHA256, 32);
        return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

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
