using Google.Protobuf;

namespace Adam.Shared.Contracts;

public sealed class LoginRequest : IProtoSerializable
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    public int CalculateSize()
    {
        int size = 0;
        size += ProtoHelper.FieldSize(1, Username);
        size += ProtoHelper.FieldSize(2, Password);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, Username);
        ProtoHelper.WriteField(output, 2, Password);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: Username = input.ReadString(); break;
                case 2: Password = input.ReadString(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}

public sealed class LoginResponse : IProtoSerializable
{
    public string Token { get; set; } = string.Empty;
    public long ExpiresAt { get; set; }
    public UserProfile? User { get; set; }

    public int CalculateSize()
    {
        int size = 0;
        size += ProtoHelper.FieldSize(1, Token);
        size += ProtoHelper.FieldSize(2, ExpiresAt);
        if (User != null) size += ProtoHelper.FieldSize(3, User);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, Token);
        ProtoHelper.WriteField(output, 2, ExpiresAt);
        if (User != null) ProtoHelper.WriteField(output, 3, User);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: Token = input.ReadString(); break;
                case 2: ExpiresAt = input.ReadInt64(); break;
                case 3:
                    {
                        User = new UserProfile();
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

public sealed class UserProfile : IProtoSerializable
{
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;

    public int CalculateSize()
    {
        int size = 0;
        size += ProtoHelper.FieldSize(1, Id);
        size += ProtoHelper.FieldSize(2, Username);
        size += ProtoHelper.FieldSize(3, Role);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, Id);
        ProtoHelper.WriteField(output, 2, Username);
        ProtoHelper.WriteField(output, 3, Role);
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
                case 3: Role = input.ReadString(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}

public sealed class ValidateTokenRequest : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}

public sealed class ValidateTokenResponse : IProtoSerializable
{
    public bool IsValid { get; set; }
    public UserProfile? User { get; set; }

    public int CalculateSize()
    {
        int size = 0;
        size += ProtoHelper.FieldSize(1, IsValid);
        if (User != null) size += ProtoHelper.FieldSize(2, User);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, IsValid);
        if (User != null) ProtoHelper.WriteField(output, 2, User);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: IsValid = input.ReadBool(); break;
                case 2:
                    {
                        User = new UserProfile();
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
