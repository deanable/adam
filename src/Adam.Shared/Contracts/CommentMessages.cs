using Google.Protobuf;

namespace Adam.Shared.Contracts;

/// <summary>
/// Reusable wire DTO for a single comment (top-level or reply).
/// Used in ListCommentsResponse and CommentNotification.
/// </summary>
public sealed class CommentWire : IProtoSerializable
{
    public string Id { get; set; } = string.Empty;
    public string AssetId { get; set; } = string.Empty;
    public string? ParentCommentId { get; set; }
    public string Body { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public long CreatedAt { get; set; }
    public long EditedAt { get; set; }
    public bool IsDeleted { get; set; }
    public bool CanEdit { get; set; }
    public bool CanDelete { get; set; }
    public List<CommentWire> Replies { get; } = [];

    public int CalculateSize()
    {
        int size = 0;
        size += ProtoHelper.FieldSize(1, Id);
        size += ProtoHelper.FieldSize(2, AssetId);
        if (!string.IsNullOrEmpty(ParentCommentId)) size += ProtoHelper.FieldSize(3, ParentCommentId);
        size += ProtoHelper.FieldSize(4, Body);
        size += ProtoHelper.FieldSize(5, UserName);
        size += ProtoHelper.FieldSize(6, UserId);
        size += ProtoHelper.FieldSize(7, CreatedAt);
        if (EditedAt != 0) size += ProtoHelper.FieldSize(8, EditedAt);
        if (IsDeleted) size += ProtoHelper.FieldSize(9, IsDeleted);
        if (CanEdit) size += ProtoHelper.FieldSize(10, CanEdit);
        if (CanDelete) size += ProtoHelper.FieldSize(11, CanDelete);
        size += ProtoHelper.RepeatedFieldSize(12, Replies);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, Id);
        ProtoHelper.WriteField(output, 2, AssetId);
        if (!string.IsNullOrEmpty(ParentCommentId)) ProtoHelper.WriteField(output, 3, ParentCommentId);
        ProtoHelper.WriteField(output, 4, Body);
        ProtoHelper.WriteField(output, 5, UserName);
        ProtoHelper.WriteField(output, 6, UserId);
        ProtoHelper.WriteField(output, 7, CreatedAt);
        if (EditedAt != 0) ProtoHelper.WriteField(output, 8, EditedAt);
        if (IsDeleted) ProtoHelper.WriteField(output, 9, IsDeleted);
        if (CanEdit) ProtoHelper.WriteField(output, 10, CanEdit);
        if (CanDelete) ProtoHelper.WriteField(output, 11, CanDelete);
        ProtoHelper.WriteRepeatedField(output, 12, Replies);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: Id = input.ReadString(); break;
                case 2: AssetId = input.ReadString(); break;
                case 3: ParentCommentId = input.ReadString(); break;
                case 4: Body = input.ReadString(); break;
                case 5: UserName = input.ReadString(); break;
                case 6: UserId = input.ReadString(); break;
                case 7: CreatedAt = input.ReadInt64(); break;
                case 8: EditedAt = input.ReadInt64(); break;
                case 9: IsDeleted = input.ReadBool(); break;
                case 10: CanEdit = input.ReadBool(); break;
                case 11: CanDelete = input.ReadBool(); break;
                case 12:
                    {
                        var reply = new CommentWire();
                        var buf = input.ReadBytes().ToByteArray();
                        using var ms = new MemoryStream(buf);
                        using var cis = new CodedInputStream(ms);
                        reply.MergeFrom(cis);
                        Replies.Add(reply);
                        break;
                    }
                default: input.SkipLastField(); break;
            }
        }
    }
}

// ─── ListComments ─────────────────────────────────────────────

public sealed class ListCommentsRequest : IProtoSerializable
{
    public string AssetId { get; set; } = string.Empty;

    public int CalculateSize() => ProtoHelper.FieldSize(1, AssetId);
    public void WriteTo(CodedOutputStream output) => ProtoHelper.WriteField(output, 1, AssetId);
    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            if (WireFormat.GetTagFieldNumber(tag) == 1)
                AssetId = input.ReadString();
            else
                input.SkipLastField();
        }
    }
}

public sealed class ListCommentsResponse : IProtoSerializable
{
    public List<CommentWire> Comments { get; } = [];
    public int TotalCount { get; set; }

    public int CalculateSize()
    {
        int size = 0;
        size += ProtoHelper.RepeatedFieldSize(1, Comments);
        size += ProtoHelper.FieldSize(2, TotalCount);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteRepeatedField(output, 1, Comments);
        ProtoHelper.WriteField(output, 2, TotalCount);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1:
                    {
                        var c = new CommentWire();
                        var buf = input.ReadBytes().ToByteArray();
                        using var ms = new MemoryStream(buf);
                        using var cis = new CodedInputStream(ms);
                        c.MergeFrom(cis);
                        Comments.Add(c);
                        break;
                    }
                case 2: TotalCount = input.ReadInt32(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}

// ─── CreateComment ────────────────────────────────────────────

public sealed class CreateCommentRequest : IProtoSerializable
{
    public string AssetId { get; set; } = string.Empty;
    public string? ParentCommentId { get; set; }
    public string Body { get; set; } = string.Empty;

    public int CalculateSize()
    {
        int size = 0;
        size += ProtoHelper.FieldSize(1, AssetId);
        if (!string.IsNullOrEmpty(ParentCommentId)) size += ProtoHelper.FieldSize(2, ParentCommentId);
        size += ProtoHelper.FieldSize(3, Body);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, AssetId);
        if (!string.IsNullOrEmpty(ParentCommentId)) ProtoHelper.WriteField(output, 2, ParentCommentId);
        ProtoHelper.WriteField(output, 3, Body);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: AssetId = input.ReadString(); break;
                case 2: ParentCommentId = input.ReadString(); break;
                case 3: Body = input.ReadString(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}

public sealed class CreateCommentResponse : IProtoSerializable
{
    public string Id { get; set; } = string.Empty;
    public long CreatedAt { get; set; }

    public int CalculateSize()
    {
        int size = 0;
        size += ProtoHelper.FieldSize(1, Id);
        size += ProtoHelper.FieldSize(2, CreatedAt);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, Id);
        ProtoHelper.WriteField(output, 2, CreatedAt);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: Id = input.ReadString(); break;
                case 2: CreatedAt = input.ReadInt64(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}

// ─── UpdateComment ────────────────────────────────────────────

public sealed class UpdateCommentRequest : IProtoSerializable
{
    public string CommentId { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;

    public int CalculateSize()
    {
        int size = 0;
        size += ProtoHelper.FieldSize(1, CommentId);
        size += ProtoHelper.FieldSize(2, Body);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, CommentId);
        ProtoHelper.WriteField(output, 2, Body);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: CommentId = input.ReadString(); break;
                case 2: Body = input.ReadString(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}

public sealed class UpdateCommentResponse : IProtoSerializable
{
    public string Id { get; set; } = string.Empty;
    public long EditedAt { get; set; }

    public int CalculateSize()
    {
        int size = 0;
        size += ProtoHelper.FieldSize(1, Id);
        size += ProtoHelper.FieldSize(2, EditedAt);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, Id);
        ProtoHelper.WriteField(output, 2, EditedAt);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: Id = input.ReadString(); break;
                case 2: EditedAt = input.ReadInt64(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}

// ─── DeleteComment ────────────────────────────────────────────

public sealed class DeleteCommentRequest : IProtoSerializable
{
    public string CommentId { get; set; } = string.Empty;

    public int CalculateSize() => ProtoHelper.FieldSize(1, CommentId);
    public void WriteTo(CodedOutputStream output) => ProtoHelper.WriteField(output, 1, CommentId);
    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            if (WireFormat.GetTagFieldNumber(tag) == 1)
                CommentId = input.ReadString();
            else
                input.SkipLastField();
        }
    }
}

public sealed class DeleteCommentResponse : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}

// ─── CommentNotification (opcode 128) ─────────────────────────

public sealed class CommentNotification : IProtoSerializable
{
    public string AssetId { get; set; } = string.Empty;
    public string CommentId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty; // "created", "updated", "deleted"
    public long Timestamp { get; set; }

    public int CalculateSize()
    {
        int size = 0;
        size += ProtoHelper.FieldSize(1, AssetId);
        size += ProtoHelper.FieldSize(2, CommentId);
        size += ProtoHelper.FieldSize(3, Action);
        size += ProtoHelper.FieldSize(4, Timestamp);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, AssetId);
        ProtoHelper.WriteField(output, 2, CommentId);
        ProtoHelper.WriteField(output, 3, Action);
        ProtoHelper.WriteField(output, 4, Timestamp);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: AssetId = input.ReadString(); break;
                case 2: CommentId = input.ReadString(); break;
                case 3: Action = input.ReadString(); break;
                case 4: Timestamp = input.ReadInt64(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}
