using Google.Protobuf;

namespace Adam.Shared.Contracts;

// Empty request - keep manual
public sealed class ListCollectionsRequest : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}

public sealed partial class ListCollectionsResponse : IProtoSerializable
{
    [ProtoField(1)] public List<CollectionNode> Items { get; } = [];
}

public sealed partial class CollectionNode : IProtoSerializable
{
    [ProtoField(1)] public string Id { get; set; } = string.Empty;
    [ProtoField(2)] public string Name { get; set; } = string.Empty;
    [ProtoField(3)] public string Description { get; set; } = string.Empty;
    [ProtoField(4)] public string ParentId { get; set; } = string.Empty;
    [ProtoField(5)] public int AssetCount { get; set; }
    [ProtoField(6)] public List<CollectionNode> Children { get; } = [];
    [ProtoField(7)] public bool IsSmart { get; set; }
    [ProtoField(8)] public string SmartQueryJson { get; set; } = string.Empty;
    [ProtoField(9)] public long LastAutoRefreshedAt { get; set; }
}

public sealed partial class CreateCollectionRequest : IProtoSerializable
{
    [ProtoField(1)] public string Name { get; set; } = string.Empty;
    [ProtoField(2)] public string Description { get; set; } = string.Empty;
    [ProtoField(3)] public string ParentId { get; set; } = string.Empty;
    [ProtoField(4)] public bool IsSmart { get; set; }
    [ProtoField(5)] public string SmartQueryJson { get; set; } = string.Empty;
}

public sealed partial class UpdateCollectionRequest : IProtoSerializable
{
    [ProtoField(1)] public string Id { get; set; } = string.Empty;
    [ProtoField(2)] public string Name { get; set; } = string.Empty;
    [ProtoField(3)] public string Description { get; set; } = string.Empty;
    [ProtoField(4)] public string SmartQueryJson { get; set; } = string.Empty;
}

public sealed partial class DeleteCollectionRequest : IProtoSerializable
{
    [ProtoField(1)] public string Id { get; set; } = string.Empty;
    [ProtoField(2)] public bool CascadeChildren { get; set; } = true;
}

public sealed partial class CreateCollectionResponse : IProtoSerializable
{
    [ProtoField(1)] public string Id { get; set; } = string.Empty;
}

// Empty response - keep manual
public sealed class DeleteCollectionResponse : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}

public sealed partial class RefreshSmartCollectionRequest : IProtoSerializable
{
    [ProtoField(1)] public string Id { get; set; } = string.Empty;
}

public sealed partial class RefreshSmartCollectionResponse : IProtoSerializable
{
    [ProtoField(1)] public List<string> AssetIds { get; } = [];
    [ProtoField(2)] public long LastAutoRefreshedAt { get; set; }
    [ProtoField(3)] public int TotalCount { get; set; }
}

public sealed partial class AssetOrderEntry : IProtoSerializable
{
    [ProtoField(1)] public string AssetId { get; set; } = string.Empty;
    [ProtoField(2)] public int SortOrder { get; set; }
}

public sealed partial class ReorderCollectionAssetsRequest : IProtoSerializable
{
    [ProtoField(1)] public string CollectionId { get; set; } = string.Empty;
    [ProtoField(2)] public List<AssetOrderEntry> Entries { get; } = [];
}

// Empty response - keep manual
public sealed class ReorderCollectionAssetsResponse : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}
