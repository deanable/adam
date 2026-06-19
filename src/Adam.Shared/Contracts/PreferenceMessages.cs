using Google.Protobuf;

namespace Adam.Shared.Contracts;

// ═══════════════════════════════════════════════════════════════
//  Get Preferences
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Request all preferences for the current user.
/// The UserId is derived from the auth token on the broker side.
/// </summary>
public sealed class GetPreferencesRequest : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}

public sealed partial class GetPreferencesResponse : IProtoSerializable
{
    [ProtoField(1)] public List<PreferenceItem> Preferences { get; } = [];
}

public sealed partial class PreferenceItem : IProtoSerializable
{
    [ProtoField(1)] public string Key { get; set; } = string.Empty;
    [ProtoField(2)] public string ValueJson { get; set; } = string.Empty;
    [ProtoField(3)] public long UpdatedAt { get; set; }
    [ProtoField(4, DefaultValue = 0)] public int Version { get; set; }
}

// ═══════════════════════════════════════════════════════════════
//  Set Preference (create or update)
// ═══════════════════════════════════════════════════════════════

public sealed partial class SetPreferenceRequest : IProtoSerializable
{
    [ProtoField(1)] public string Key { get; set; } = string.Empty;
    [ProtoField(2)] public string ValueJson { get; set; } = string.Empty;
}

public sealed class SetPreferenceResponse : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}

// ═══════════════════════════════════════════════════════════════
//  Reset Preference (delete by key)
// ═══════════════════════════════════════════════════════════════

public sealed partial class ResetPreferenceRequest : IProtoSerializable
{
    [ProtoField(1)] public string Key { get; set; } = string.Empty;
}

public sealed class ResetPreferenceResponse : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}

// ═══════════════════════════════════════════════════════════════
//  Reset All Preferences
// ═══════════════════════════════════════════════════════════════

public sealed class ResetAllPreferencesRequest : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}

public sealed class ResetAllPreferencesResponse : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}
