using Google.Protobuf;

namespace Adam.Shared.Contracts;

public sealed partial class ListAssetsRequest : IProtoSerializable
{
    [ProtoField(1)] public string Search { get; set; } = string.Empty;
    [ProtoField(2)] public string Type { get; set; } = string.Empty;
    [ProtoField(3)] public string CollectionId { get; set; } = string.Empty;
    [ProtoField(4)] public List<string> Tags { get; } = [];
    [ProtoField(5, DefaultValue = 1)] public int Page { get; set; } = 1;
    [ProtoField(6, DefaultValue = 50)] public int PageSize { get; set; } = 50;
    [ProtoField(7, DefaultValue = "FileName")] public string SortBy { get; set; } = "FileName";
    [ProtoField(8, DefaultValue = "asc")] public string SortDir { get; set; } = "asc";
    [ProtoField(9)] public long FromDate { get; set; }
    [ProtoField(10)] public long ToDate { get; set; }
    [ProtoField(11)] public string FolderPath { get; set; } = string.Empty;
    [ProtoField(12)] public List<string> KeywordIds { get; } = [];
    [ProtoField(13)] public List<string> CategoryIds { get; } = [];
}

public sealed partial class ListAssetsResponse : IProtoSerializable
{
    [ProtoField(1)] public List<AssetSummary> Items { get; } = [];
    [ProtoField(2)] public int TotalCount { get; set; }
    [ProtoField(3)] public int Page { get; set; }
    [ProtoField(4)] public int PageSize { get; set; }
}

public sealed partial class AssetSummary : IProtoSerializable
{
    [ProtoField(1)] public string Id { get; set; } = string.Empty;
    [ProtoField(2)] public string FileName { get; set; } = string.Empty;
    [ProtoField(3)] public string MimeType { get; set; } = string.Empty;
    [ProtoField(4)] public long FileSize { get; set; }
    [ProtoField(5)] public string Title { get; set; } = string.Empty;
    [ProtoField(6)] public string Type { get; set; } = string.Empty;
    [ProtoField(7)] public string CollectionId { get; set; } = string.Empty;
    [ProtoField(8)] public string UploadedBy { get; set; } = string.Empty;
    [ProtoField(9)] public long CreatedAt { get; set; }
    [ProtoField(10)] public int Rating { get; set; }
    [ProtoField(11)] public int Label { get; set; }
    [ProtoField(12)] public int Flag { get; set; }
    [ProtoField(13)] public long ModifiedAt { get; set; }
}

public sealed partial class GetAssetRequest : IProtoSerializable
{
    [ProtoField(1)] public string Id { get; set; } = string.Empty;
}

public sealed partial class AssetDetail : IProtoSerializable
{
    [ProtoField(1)] public string Id { get; set; } = string.Empty;
    [ProtoField(2)] public string FileName { get; set; } = string.Empty;
    [ProtoField(3)] public string FileExtension { get; set; } = string.Empty;
    [ProtoField(4)] public string MimeType { get; set; } = string.Empty;
    [ProtoField(5)] public long FileSize { get; set; }
    [ProtoField(6)] public string ChecksumSha256 { get; set; } = string.Empty;
    [ProtoField(7)] public string Title { get; set; } = string.Empty;
    [ProtoField(8)] public string Description { get; set; } = string.Empty;
    [ProtoField(9)] public List<string> Tags { get; } = [];
    [ProtoField(10)] public string Type { get; set; } = string.Empty;
    [ProtoField(11)] public int Width { get; set; }
    [ProtoField(12)] public int Height { get; set; }
    [ProtoField(13)] public double Duration { get; set; }
    [ProtoField(14)] public string CollectionId { get; set; } = string.Empty;
    [ProtoField(15)] public string CollectionName { get; set; } = string.Empty;
    [ProtoField(16)] public string UploadedBy { get; set; } = string.Empty;
    [ProtoField(17, DefaultValue = 1)] public int Version { get; set; } = 1;
    [ProtoField(18)] public long CreatedAt { get; set; }
    [ProtoField(19)] public long ModifiedAt { get; set; }
    [ProtoField(20)] public int Rating { get; set; }
    [ProtoField(21)] public int Label { get; set; }
    [ProtoField(22)] public int Flag { get; set; }
    [ProtoField(23)] public double GpsLatitude { get; set; }
    [ProtoField(24)] public double GpsLongitude { get; set; }
    [ProtoField(25)] public string Copyright { get; set; } = string.Empty;
    [ProtoField(26)] public int Orientation { get; set; }
    [ProtoField(27)] public List<bool> TagsAreAiGenerated { get; } = [];
}

public sealed partial class CreateAssetRequest : IProtoSerializable
{
    [ProtoField(1)] public string FileName { get; set; } = string.Empty;
    [ProtoField(2)] public ByteString Content { get; set; } = ByteString.Empty;
    [ProtoField(3)] public string Title { get; set; } = string.Empty;
    [ProtoField(4)] public string Description { get; set; } = string.Empty;
    [ProtoField(5)] public List<string> Tags { get; } = [];
    [ProtoField(6)] public string CollectionId { get; set; } = string.Empty;
    [ProtoField(7)] public int Rating { get; set; }
    [ProtoField(8)] public int Label { get; set; }
    [ProtoField(9)] public int Flag { get; set; }
    [ProtoField(10)] public double GpsLatitude { get; set; }
    [ProtoField(11)] public double GpsLongitude { get; set; }
    [ProtoField(12)] public string Copyright { get; set; } = string.Empty;
    [ProtoField(13)] public int Orientation { get; set; }
}

public sealed partial class CreateAssetResponse : IProtoSerializable
{
    [ProtoField(1)] public string Id { get; set; } = string.Empty;
    [ProtoField(2)] public string Checksum { get; set; } = string.Empty;
    [ProtoField(3)] public bool Duplicate { get; set; }
    [ProtoField(4)] public string ExistingAssetId { get; set; } = string.Empty;
}

public sealed partial class UpdateAssetRequest : IProtoSerializable
{
    [ProtoField(1)] public string Id { get; set; } = string.Empty;
    [ProtoField(2)] public string Title { get; set; } = string.Empty;
    [ProtoField(3)] public string Description { get; set; } = string.Empty;
    [ProtoField(4)] public List<string> Tags { get; } = [];
    [ProtoField(5)] public string CollectionId { get; set; } = string.Empty;
    [ProtoField(6)] public int ExpectedVersion { get; set; }
    [ProtoField(7)] public int Rating { get; set; }
    [ProtoField(8)] public int Label { get; set; }
    [ProtoField(9)] public int Flag { get; set; }
    [ProtoField(10)] public double GpsLatitude { get; set; }
    [ProtoField(11)] public double GpsLongitude { get; set; }
    [ProtoField(12)] public string Copyright { get; set; } = string.Empty;
    [ProtoField(13)] public int Orientation { get; set; }
}

public sealed partial class UpdateAssetResponse : IProtoSerializable
{
    [ProtoField(1)] public string Id { get; set; } = string.Empty;
    [ProtoField(2)] public int NewVersion { get; set; }
    [ProtoField(3)] public long ModifiedAt { get; set; }
    [ProtoField(4)] public bool Conflict { get; set; }
}

public sealed partial class DeleteAssetRequest : IProtoSerializable
{
    [ProtoField(1)] public string Id { get; set; } = string.Empty;
}

// Empty response - keep manual
public sealed class DeleteAssetResponse : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}

public sealed partial class RestoreAssetRequest : IProtoSerializable
{
    [ProtoField(1)] public string Id { get; set; } = string.Empty;
}

// Empty response - keep manual
public sealed class RestoreAssetResponse : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}

public sealed partial class PermanentDeleteAssetRequest : IProtoSerializable
{
    [ProtoField(1)] public string Id { get; set; } = string.Empty;
}

// Empty response - keep manual
public sealed class PermanentDeleteAssetResponse : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}

public sealed partial class BulkPermanentDeleteAssetRequest : IProtoSerializable
{
    [ProtoField(1)] public List<string> Ids { get; } = [];
}

public sealed partial class BulkPermanentDeleteAssetResponse : IProtoSerializable
{
    [ProtoField(1)] public int DeletedCount { get; set; }
}

public sealed partial class GetFileRequest : IProtoSerializable
{
    [ProtoField(1)] public string Id { get; set; } = string.Empty;
}

public sealed partial class GetFileResponse : IProtoSerializable
{
    [ProtoField(1)] public string FileName { get; set; } = string.Empty;
    [ProtoField(2)] public string FileExtension { get; set; } = string.Empty;
    [ProtoField(3)] public string MimeType { get; set; } = string.Empty;
    [ProtoField(4)] public long FileSize { get; set; }
    [ProtoField(5)] public string ChecksumSha256 { get; set; } = string.Empty;
    [ProtoField(6)] public ByteString Content { get; set; } = ByteString.Empty;
}

public sealed partial class GetFileChunkRequest : IProtoSerializable
{
    [ProtoField(1)] public string Id { get; set; } = string.Empty;
    [ProtoField(2)] public int ChunkIndex { get; set; }
    [ProtoField(3, DefaultValue = 16777216)] public int ChunkSize { get; set; } = 16 * 1024 * 1024;
}

public sealed partial class GetFileChunkResponse : IProtoSerializable
{
    [ProtoField(1)] public string FileName { get; set; } = string.Empty;
    [ProtoField(2)] public string FileExtension { get; set; } = string.Empty;
    [ProtoField(3)] public string MimeType { get; set; } = string.Empty;
    [ProtoField(4)] public long FileSize { get; set; }
    [ProtoField(5)] public string ChecksumSha256 { get; set; } = string.Empty;
    [ProtoField(6)] public ByteString ChunkData { get; set; } = ByteString.Empty;
    [ProtoField(7)] public int ChunkIndex { get; set; }
    [ProtoField(8)] public bool IsLastChunk { get; set; }
    [ProtoField(9)] public int TotalChunks { get; set; }
}

public sealed partial class ListDeletedAssetsRequest : IProtoSerializable
{
    [ProtoField(1)] public string Search { get; set; } = string.Empty;
    [ProtoField(2, DefaultValue = 1)] public int Page { get; set; } = 1;
    [ProtoField(3, DefaultValue = 50)] public int PageSize { get; set; } = 50;
}

public sealed partial class ListDeletedAssetsResponse : IProtoSerializable
{
    [ProtoField(1)] public List<AssetSummary> Items { get; } = [];
    [ProtoField(2)] public int TotalCount { get; set; }
}

public sealed partial class GetChangesRequest : IProtoSerializable
{
    [ProtoField(1)] public long SinceTimestamp { get; set; }
}

public sealed partial class GetChangesResponse : IProtoSerializable
{
    [ProtoField(1)] public List<ChangeEvent> Changes { get; } = [];
}

public sealed partial class ChangeEvent : IProtoSerializable
{
    [ProtoField(1)] public string EntityId { get; set; } = string.Empty;
    [ProtoField(2)] public string Action { get; set; } = string.Empty;
    [ProtoField(3)] public long Timestamp { get; set; }
}
