using Google.Protobuf;

namespace Adam.Shared.Contracts;

public sealed partial class WatchedFolderInfo : IProtoSerializable
{
    [ProtoField(1)] public string Id { get; set; } = string.Empty;
    [ProtoField(2)] public string Path { get; set; } = string.Empty;
    [ProtoField(3)] public bool IsEnabled { get; set; } = true;
}

// Empty request - keep manual
public sealed class ListWatchedFoldersRequest : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}

public sealed partial class ListWatchedFoldersResponse : IProtoSerializable
{
    [ProtoField(1)] public List<WatchedFolderInfo> Folders { get; } = new();
}

public sealed partial class CreateWatchedFolderRequest : IProtoSerializable
{
    [ProtoField(1)] public string Path { get; set; } = string.Empty;
}

public sealed partial class CreateWatchedFolderResponse : IProtoSerializable
{
    [ProtoField(1)] public string Id { get; set; } = string.Empty;
    [ProtoField(2)] public string Path { get; set; } = string.Empty;
}

public sealed partial class UpdateWatchedFolderRequest : IProtoSerializable
{
    [ProtoField(1)] public string Id { get; set; } = string.Empty;
    [ProtoField(2)] public string Path { get; set; } = string.Empty;
    [ProtoField(3)] public bool IsEnabled { get; set; } = true;
}

public sealed partial class DeleteWatchedFolderRequest : IProtoSerializable
{
    [ProtoField(1)] public string Id { get; set; } = string.Empty;
}

public sealed partial class DeleteWatchedFolderResponse : IProtoSerializable
{
    [ProtoField(1)] public bool Success { get; set; }
}
