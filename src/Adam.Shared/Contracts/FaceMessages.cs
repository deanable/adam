using Google.Protobuf;

namespace Adam.Shared.Contracts;

// ═══════════════════════════════════════════════════════════════
//  Face Detection
// ═══════════════════════════════════════════════════════════════

public sealed partial class DetectFacesRequest : IProtoSerializable
{
    [ProtoField(1)] public string AssetId { get; set; } = string.Empty;
}

public sealed partial class DetectFacesResponse : IProtoSerializable
{
    [ProtoField(1)] public string AssetId { get; set; } = string.Empty;
    [ProtoField(2, DefaultValue = 0)] public int FaceCount { get; set; }
    [ProtoField(3)] public string? ErrorMessage { get; set; }
}

// ═══════════════════════════════════════════════════════════════
//  Name Person (assign a name to a face, creates or links person)
// ═══════════════════════════════════════════════════════════════

public sealed partial class NamePersonRequest : IProtoSerializable
{
    [ProtoField(1)] public string AssetFaceId { get; set; } = string.Empty;
    [ProtoField(2)] public string PersonName { get; set; } = string.Empty;
}

public sealed partial class NamePersonResponse : IProtoSerializable
{
    [ProtoField(1)] public string PersonId { get; set; } = string.Empty;
    [ProtoField(2)] public string? ErrorMessage { get; set; }
}

// ═══════════════════════════════════════════════════════════════
//  List Persons
// ═══════════════════════════════════════════════════════════════

// Empty request
public sealed class ListPersonsRequest : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}

public sealed partial class ListPersonsResponse : IProtoSerializable
{
    [ProtoField(1)] public List<PersonWire> Persons { get; } = [];
}

public sealed partial class PersonWire : IProtoSerializable
{
    [ProtoField(1)] public string Id { get; set; } = string.Empty;
    [ProtoField(2)] public string Name { get; set; } = string.Empty;
    [ProtoField(3, DefaultValue = 0)] public int FaceCount { get; set; }
    [ProtoField(4)] public long CreatedAt { get; set; }
    [ProtoField(5)] public long ModifiedAt { get; set; }
    [ProtoField(6)] public float AvgConfidence { get; set; }
}

// ═══════════════════════════════════════════════════════════════
//  Merge Persons
// ═══════════════════════════════════════════════════════════════

public sealed partial class MergePersonsRequest : IProtoSerializable
{
    [ProtoField(1)] public string SourcePersonId { get; set; } = string.Empty;
    [ProtoField(2)] public string TargetPersonId { get; set; } = string.Empty;
}

public sealed partial class MergePersonsResponse : IProtoSerializable
{
    [ProtoField(1)] public string? ErrorMessage { get; set; }
}

// ═══════════════════════════════════════════════════════════════
//  Delete Person
// ═══════════════════════════════════════════════════════════════

public sealed partial class DeletePersonRequest : IProtoSerializable
{
    [ProtoField(1)] public string PersonId { get; set; } = string.Empty;
}

// Empty response
public sealed class DeletePersonResponse : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}
