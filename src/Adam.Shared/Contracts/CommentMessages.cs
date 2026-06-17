using Google.Protobuf;

namespace Adam.Shared.Contracts;

/// <summary>
/// Reusable wire DTO for a single comment (top-level or reply).
/// Used in ListCommentsResponse and CommentNotification.
/// </summary>
public sealed partial class CommentWire : IProtoSerializable
{
    [ProtoField(1)] public string Id { get; set; } = string.Empty;
    [ProtoField(2)] public string AssetId { get; set; } = string.Empty;
    [ProtoField(3)] public string? ParentCommentId { get; set; }
    [ProtoField(4)] public string Body { get; set; } = string.Empty;
    [ProtoField(5)] public string UserName { get; set; } = string.Empty;
    [ProtoField(6)] public string UserId { get; set; } = string.Empty;
    [ProtoField(7)] public long CreatedAt { get; set; }
    [ProtoField(8)] public long EditedAt { get; set; }
    [ProtoField(9)] public bool IsDeleted { get; set; }
    [ProtoField(10)] public bool CanEdit { get; set; }
    [ProtoField(11)] public bool CanDelete { get; set; }
    [ProtoField(12)] public List<CommentWire> Replies { get; } = [];
}

// ─── ListComments ─────────────────────────────────────────────

public sealed partial class ListCommentsRequest : IProtoSerializable
{
    [ProtoField(1)] public string AssetId { get; set; } = string.Empty;
}

public sealed partial class ListCommentsResponse : IProtoSerializable
{
    [ProtoField(1)] public List<CommentWire> Comments { get; } = [];
    [ProtoField(2)] public int TotalCount { get; set; }
}

// ─── CreateComment ────────────────────────────────────────────

public sealed partial class CreateCommentRequest : IProtoSerializable
{
    [ProtoField(1)] public string AssetId { get; set; } = string.Empty;
    [ProtoField(2)] public string? ParentCommentId { get; set; }
    [ProtoField(3)] public string Body { get; set; } = string.Empty;
}

public sealed partial class CreateCommentResponse : IProtoSerializable
{
    [ProtoField(1)] public string Id { get; set; } = string.Empty;
    [ProtoField(2)] public long CreatedAt { get; set; }
}

// ─── UpdateComment ────────────────────────────────────────────

public sealed partial class UpdateCommentRequest : IProtoSerializable
{
    [ProtoField(1)] public string CommentId { get; set; } = string.Empty;
    [ProtoField(2)] public string Body { get; set; } = string.Empty;
}

public sealed partial class UpdateCommentResponse : IProtoSerializable
{
    [ProtoField(1)] public string Id { get; set; } = string.Empty;
    [ProtoField(2)] public long EditedAt { get; set; }
}

// ─── DeleteComment ────────────────────────────────────────────

public sealed partial class DeleteCommentRequest : IProtoSerializable
{
    [ProtoField(1)] public string CommentId { get; set; } = string.Empty;
}

// Empty response - keep manual
public sealed class DeleteCommentResponse : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}

// ─── CommentNotification (opcode 128) ─────────────────────────

public sealed partial class CommentNotification : IProtoSerializable
{
    [ProtoField(1)] public string AssetId { get; set; } = string.Empty;
    [ProtoField(2)] public string CommentId { get; set; } = string.Empty;
    [ProtoField(3)] public string Action { get; set; } = string.Empty;
    [ProtoField(4)] public long Timestamp { get; set; }
}
