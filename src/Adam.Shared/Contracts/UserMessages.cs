using Google.Protobuf;

namespace Adam.Shared.Contracts;

// --- ListUsers ---
public sealed class ListUsersRequest : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}

public sealed class ListUsersResponse : IProtoSerializable
{
    public List<UserInfo> Items { get; set; } = [];

    public int CalculateSize()
    {
        int size = 0;
        size += ProtoHelper.RepeatedFieldSize(1, Items);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteRepeatedField(output, 1, Items);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1:
                    {
                        var item = new UserInfo();
                        var buf = input.ReadBytes().ToByteArray();
                        using var ms = new MemoryStream(buf);
                        using var cis = new CodedInputStream(ms);
                        item.MergeFrom(cis);
                        Items.Add(item);
                        break;
                    }
                default: input.SkipLastField(); break;
            }
        }
    }
}

// --- GetUser ---
public sealed class GetUserRequest : IProtoSerializable
{
    public string UserId { get; set; } = string.Empty;

    public int CalculateSize()
    {
        int size = 0;
        size += ProtoHelper.FieldSize(1, UserId);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, UserId);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: UserId = input.ReadString(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}

// --- CreateUser ---
public sealed class CreateUserRequest : IProtoSerializable
{
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string RoleId { get; set; } = string.Empty;

    public int CalculateSize()
    {
        int size = 0;
        size += ProtoHelper.FieldSize(1, Username);
        size += ProtoHelper.FieldSize(2, Email);
        size += ProtoHelper.FieldSize(3, Password);
        size += ProtoHelper.FieldSize(4, RoleId);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, Username);
        ProtoHelper.WriteField(output, 2, Email);
        ProtoHelper.WriteField(output, 3, Password);
        ProtoHelper.WriteField(output, 4, RoleId);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: Username = input.ReadString(); break;
                case 2: Email = input.ReadString(); break;
                case 3: Password = input.ReadString(); break;
                case 4: RoleId = input.ReadString(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}

public sealed class CreateUserResponse : IProtoSerializable
{
    public UserInfo? User { get; set; }

    public int CalculateSize()
    {
        int size = 0;
        if (User != null) size += ProtoHelper.FieldSize(1, User);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        if (User != null) ProtoHelper.WriteField(output, 1, User);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1:
                    {
                        User = new UserInfo();
                        var buf = input.ReadBytes().ToByteArray();
                        using var ms = new MemoryStream(buf);
                        using var cis = new CodedInputStream(ms);
                        User.MergeFrom(cis);
                        break;
                    }
                default: input.SkipLastField(); break;
            }
        }
    }
}

// --- UpdateUser ---
public sealed class UpdateUserRequest : IProtoSerializable
{
    public string UserId { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Password { get; set; }
    public string? RoleId { get; set; }
    public bool IsActive { get; set; } = true;

    public int CalculateSize()
    {
        int size = 0;
        size += ProtoHelper.FieldSize(1, UserId);
        if (Email != null) size += ProtoHelper.FieldSize(2, Email);
        if (Password != null) size += ProtoHelper.FieldSize(3, Password);
        if (RoleId != null) size += ProtoHelper.FieldSize(4, RoleId);
        size += ProtoHelper.FieldSize(5, IsActive);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, UserId);
        if (Email != null) ProtoHelper.WriteField(output, 2, Email);
        if (Password != null) ProtoHelper.WriteField(output, 3, Password);
        if (RoleId != null) ProtoHelper.WriteField(output, 4, RoleId);
        ProtoHelper.WriteField(output, 5, IsActive);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: UserId = input.ReadString(); break;
                case 2: Email = input.ReadString(); break;
                case 3: Password = input.ReadString(); break;
                case 4: RoleId = input.ReadString(); break;
                case 5: IsActive = input.ReadBool(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}

// --- DeleteUser ---
public sealed class DeleteUserRequest : IProtoSerializable
{
    public string UserId { get; set; } = string.Empty;

    public int CalculateSize()
    {
        int size = 0;
        size += ProtoHelper.FieldSize(1, UserId);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, UserId);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: UserId = input.ReadString(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}

// --- UserInfo (reusable) ---
public sealed class UserInfo : IProtoSerializable
{
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string RoleId { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public long CreatedAt { get; set; }
    public long? LastLoginAt { get; set; }

    public int CalculateSize()
    {
        int size = 0;
        size += ProtoHelper.FieldSize(1, Id);
        size += ProtoHelper.FieldSize(2, Username);
        size += ProtoHelper.FieldSize(3, Email);
        size += ProtoHelper.FieldSize(4, RoleId);
        size += ProtoHelper.FieldSize(5, RoleName);
        size += ProtoHelper.FieldSize(6, IsActive);
        size += ProtoHelper.FieldSize(7, CreatedAt);
        if (LastLoginAt.HasValue) size += ProtoHelper.FieldSize(8, LastLoginAt.Value);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, Id);
        ProtoHelper.WriteField(output, 2, Username);
        ProtoHelper.WriteField(output, 3, Email);
        ProtoHelper.WriteField(output, 4, RoleId);
        ProtoHelper.WriteField(output, 5, RoleName);
        ProtoHelper.WriteField(output, 6, IsActive);
        ProtoHelper.WriteField(output, 7, CreatedAt);
        if (LastLoginAt.HasValue) ProtoHelper.WriteField(output, 8, LastLoginAt.Value);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: Id = input.ReadString(); break;
                case 2: Username = input.ReadString(); break;
                case 3: Email = input.ReadString(); break;
                case 4: RoleId = input.ReadString(); break;
                case 5: RoleName = input.ReadString(); break;
                case 6: IsActive = input.ReadBool(); break;
                case 7: CreatedAt = input.ReadInt64(); break;
                case 8: LastLoginAt = input.ReadInt64(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}

// --- RoleInfo ---
public sealed class RoleInfo : IProtoSerializable
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<string> Permissions { get; set; } = [];

    public int CalculateSize()
    {
        int size = 0;
        size += ProtoHelper.FieldSize(1, Id);
        size += ProtoHelper.FieldSize(2, Name);
        size += ProtoHelper.RepeatedFieldSize(3, Permissions);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, Id);
        ProtoHelper.WriteField(output, 2, Name);
        ProtoHelper.WriteRepeatedField(output, 3, Permissions);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: Id = input.ReadString(); break;
                case 2: Name = input.ReadString(); break;
                case 3: Permissions.Add(input.ReadString()); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}

// --- ListRoles ---
public sealed class ListRolesRequest : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}

public sealed class ListRolesResponse : IProtoSerializable
{
    public List<RoleInfo> Items { get; set; } = [];

    public int CalculateSize()
    {
        int size = 0;
        size += ProtoHelper.RepeatedFieldSize(1, Items);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteRepeatedField(output, 1, Items);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1:
                    {
                        var item = new RoleInfo();
                        var buf = input.ReadBytes().ToByteArray();
                        using var ms = new MemoryStream(buf);
                        using var cis = new CodedInputStream(ms);
                        item.MergeFrom(cis);
                        Items.Add(item);
                        break;
                    }
                default: input.SkipLastField(); break;
            }
        }
    }
}

// --- AuditLog messages ---
public sealed class ListAuditLogsRequest : IProtoSerializable
{
    public string? UserId { get; set; }
    public string? Action { get; set; }
    public string? EntityType { get; set; }
    public long? FromDate { get; set; }
    public long? ToDate { get; set; }

    public int CalculateSize()
    {
        int size = 0;
        if (UserId != null) size += ProtoHelper.FieldSize(1, UserId);
        if (Action != null) size += ProtoHelper.FieldSize(2, Action);
        if (EntityType != null) size += ProtoHelper.FieldSize(3, EntityType);
        if (FromDate.HasValue) size += ProtoHelper.FieldSize(4, FromDate.Value);
        if (ToDate.HasValue) size += ProtoHelper.FieldSize(5, ToDate.Value);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        if (UserId != null) ProtoHelper.WriteField(output, 1, UserId);
        if (Action != null) ProtoHelper.WriteField(output, 2, Action);
        if (EntityType != null) ProtoHelper.WriteField(output, 3, EntityType);
        if (FromDate.HasValue) ProtoHelper.WriteField(output, 4, FromDate.Value);
        if (ToDate.HasValue) ProtoHelper.WriteField(output, 5, ToDate.Value);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: UserId = input.ReadString(); break;
                case 2: Action = input.ReadString(); break;
                case 3: EntityType = input.ReadString(); break;
                case 4: FromDate = input.ReadInt64(); break;
                case 5: ToDate = input.ReadInt64(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}

public sealed class ListAuditLogsResponse : IProtoSerializable
{
    public List<AuditLogEntry> Items { get; set; } = [];

    public int CalculateSize()
    {
        int size = 0;
        size += ProtoHelper.RepeatedFieldSize(1, Items);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteRepeatedField(output, 1, Items);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1:
                    {
                        var item = new AuditLogEntry();
                        var buf = input.ReadBytes().ToByteArray();
                        using var ms = new MemoryStream(buf);
                        using var cis = new CodedInputStream(ms);
                        item.MergeFrom(cis);
                        Items.Add(item);
                        break;
                    }
                default: input.SkipLastField(); break;
            }
        }
    }
}

public sealed class AuditLogEntry : IProtoSerializable
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string? Details { get; set; }
    public long Timestamp { get; set; }

    public int CalculateSize()
    {
        int size = 0;
        size += ProtoHelper.FieldSize(1, Id);
        size += ProtoHelper.FieldSize(2, UserId);
        size += ProtoHelper.FieldSize(3, Username);
        size += ProtoHelper.FieldSize(4, Action);
        size += ProtoHelper.FieldSize(5, EntityType);
        if (EntityId != null) size += ProtoHelper.FieldSize(6, EntityId);
        if (Details != null) size += ProtoHelper.FieldSize(7, Details);
        size += ProtoHelper.FieldSize(8, Timestamp);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, Id);
        ProtoHelper.WriteField(output, 2, UserId);
        ProtoHelper.WriteField(output, 3, Username);
        ProtoHelper.WriteField(output, 4, Action);
        ProtoHelper.WriteField(output, 5, EntityType);
        if (EntityId != null) ProtoHelper.WriteField(output, 6, EntityId);
        if (Details != null) ProtoHelper.WriteField(output, 7, Details);
        ProtoHelper.WriteField(output, 8, Timestamp);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: Id = input.ReadString(); break;
                case 2: UserId = input.ReadString(); break;
                case 3: Username = input.ReadString(); break;
                case 4: Action = input.ReadString(); break;
                case 5: EntityType = input.ReadString(); break;
                case 6: EntityId = input.ReadString(); break;
                case 7: Details = input.ReadString(); break;
                case 8: Timestamp = input.ReadInt64(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}
