using Google.Protobuf;

namespace Adam.Shared.Contracts;

/// <summary>
/// Server-initiated push notification sent to all connected clients
/// when an asset is created, updated, or deleted.
/// </summary>
public sealed partial class ChangeNotification : IProtoSerializable
{
    [ProtoField(1)] public string EntityId { get; set; } = string.Empty;
    [ProtoField(2)] public string Action { get; set; } = string.Empty;
    [ProtoField(3)] public long Timestamp { get; set; }
    [ProtoField(4)] public string ChangedByUserId { get; set; } = string.Empty;
}

// Empty request - keep manual
public sealed class SubscribeChangesRequest : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}

public sealed partial class SubscribeChangesResponse : IProtoSerializable
{
    [ProtoField(1)] public bool Success { get; set; }
}
