using Google.Protobuf;

namespace Adam.Shared.Contracts;

// --- ListUsers ---
// Empty request - keep manual
public sealed class ListUsersRequest : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}

public sealed partial class ListUsersResponse : IProtoSerializable
{
    [ProtoField(1)] public List<UserInfo> Items { get; set; } = [];
}

// --- GetUser ---
public sealed partial class GetUserRequest : IProtoSerializable
{
    [ProtoField(1)] public string UserId { get; set; } = string.Empty;
}

// --- CreateUser ---
public sealed partial class CreateUserRequest : IProtoSerializable
{
    [ProtoField(1)] public string Username { get; set; } = string.Empty;
    [ProtoField(2)] public string Email { get; set; } = string.Empty;
    [ProtoField(3)] public string Password { get; set; } = string.Empty;
    [ProtoField(4)] public string RoleId { get; set; } = string.Empty;
}

public sealed partial class CreateUserResponse : IProtoSerializable
{
    [ProtoField(1)] public UserInfo? User { get; set; }
}

// --- UpdateUser ---
public sealed partial class UpdateUserRequest : IProtoSerializable
{
    [ProtoField(1)] public string UserId { get; set; } = string.Empty;
    [ProtoField(2)] public string? Email { get; set; }
    [ProtoField(3)] public string? Password { get; set; }
    [ProtoField(4)] public string? RoleId { get; set; }
    [ProtoField(5)] public bool IsActive { get; set; } = true;
}

// --- DeleteUser ---
public sealed partial class DeleteUserRequest : IProtoSerializable
{
    [ProtoField(1)] public string UserId { get; set; } = string.Empty;
}

// --- UserInfo (reusable) ---
public sealed partial class UserInfo : IProtoSerializable
{
    [ProtoField(1)] public string Id { get; set; } = string.Empty;
    [ProtoField(2)] public string Username { get; set; } = string.Empty;
    [ProtoField(3)] public string Email { get; set; } = string.Empty;
    [ProtoField(4)] public string RoleId { get; set; } = string.Empty;
    [ProtoField(5)] public string RoleName { get; set; } = string.Empty;
    [ProtoField(6)] public bool IsActive { get; set; }
    [ProtoField(7)] public long CreatedAt { get; set; }
    [ProtoField(8)] public long? LastLoginAt { get; set; }
}

// --- RoleInfo ---
public sealed partial class RoleInfo : IProtoSerializable
{
    [ProtoField(1)] public string Id { get; set; } = string.Empty;
    [ProtoField(2)] public string Name { get; set; } = string.Empty;
    [ProtoField(3)] public List<string> Permissions { get; set; } = [];
}

// --- ListRoles ---
// Empty request - keep manual
public sealed class ListRolesRequest : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}

public sealed partial class ListRolesResponse : IProtoSerializable
{
    [ProtoField(1)] public List<RoleInfo> Items { get; set; } = [];
}

// --- AuditLog messages ---
public sealed partial class ListAuditLogsRequest : IProtoSerializable
{
    [ProtoField(1)] public string? UserId { get; set; }
    [ProtoField(2)] public string? Action { get; set; }
    [ProtoField(3)] public string? EntityType { get; set; }
    [ProtoField(4)] public long? FromDate { get; set; }
    [ProtoField(5)] public long? ToDate { get; set; }
}

public sealed partial class ListAuditLogsResponse : IProtoSerializable
{
    [ProtoField(1)] public List<AuditLogEntry> Items { get; set; } = [];
}

public sealed partial class AuditLogEntry : IProtoSerializable
{
    [ProtoField(1)] public string Id { get; set; } = string.Empty;
    [ProtoField(2)] public string UserId { get; set; } = string.Empty;
    [ProtoField(3)] public string Username { get; set; } = string.Empty;
    [ProtoField(4)] public string Action { get; set; } = string.Empty;
    [ProtoField(5)] public string EntityType { get; set; } = string.Empty;
    [ProtoField(6)] public string? EntityId { get; set; }
    [ProtoField(7)] public string? Details { get; set; }
    [ProtoField(8)] public long Timestamp { get; set; }
}
