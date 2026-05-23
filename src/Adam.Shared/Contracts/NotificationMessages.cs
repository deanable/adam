using Google.Protobuf;

namespace Adam.Shared.Contracts;

/// <summary>
/// Server-initiated push notification sent to all connected clients
/// when an asset is created, updated, or deleted.
/// </summary>
public sealed class ChangeNotification : IProtoSerializable
{
    public string EntityId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty; // "created", "updated", "deleted"
    public long Timestamp { get; set; }
    public string ChangedByUserId { get; set; } = string.Empty;

    public int CalculateSize() =>
        ProtoHelper.FieldSize(1, EntityId) +
        ProtoHelper.FieldSize(2, Action) +
        ProtoHelper.FieldSize(3, Timestamp) +
        ProtoHelper.FieldSize(4, ChangedByUserId);

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, EntityId);
        ProtoHelper.WriteField(output, 2, Action);
        ProtoHelper.WriteField(output, 3, Timestamp);
        ProtoHelper.WriteField(output, 4, ChangedByUserId);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: EntityId = input.ReadString(); break;
                case 2: Action = input.ReadString(); break;
                case 3: Timestamp = input.ReadInt64(); break;
                case 4: ChangedByUserId = input.ReadString(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}

public sealed class SubscribeChangesRequest : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}

public sealed class SubscribeChangesResponse : IProtoSerializable
{
    public bool Success { get; set; }

    public int CalculateSize() => ProtoHelper.FieldSize(1, Success);
    public void WriteTo(CodedOutputStream output) => ProtoHelper.WriteField(output, 1, Success);
    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: Success = input.ReadBool(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}
