using Google.Protobuf;

namespace Adam.Shared.Contracts;

public sealed partial class LoginRequest : IProtoSerializable
{
    [ProtoField(1)] public string Username { get; set; } = string.Empty;
    [ProtoField(2)] public string Password { get; set; } = string.Empty;
}

public sealed partial class LoginResponse : IProtoSerializable
{
    [ProtoField(1)] public string Token { get; set; } = string.Empty;
    [ProtoField(2)] public long ExpiresAt { get; set; }
    [ProtoField(3)] public UserProfile? User { get; set; }
}

public sealed partial class UserProfile : IProtoSerializable
{
    [ProtoField(1)] public string Id { get; set; } = string.Empty;
    [ProtoField(2)] public string Username { get; set; } = string.Empty;
    [ProtoField(3)] public string Role { get; set; } = string.Empty;
}

// No fields - keep manual implementation
public sealed class ValidateTokenRequest : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}

public sealed partial class ValidateTokenResponse : IProtoSerializable
{
    [ProtoField(1)] public bool IsValid { get; set; }
    [ProtoField(2)] public UserProfile? User { get; set; }
}
