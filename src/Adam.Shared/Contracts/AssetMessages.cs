using Google.Protobuf;

namespace Adam.Shared.Contracts;

public sealed class ListAssetsRequest : IProtoSerializable
{
    public string Search { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string CollectionId { get; set; } = string.Empty;
    public List<string> Tags { get; } = [];
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public string SortBy { get; set; } = "FileName";
    public string SortDir { get; set; } = "asc";

    public int CalculateSize()
    {
        int size = 0;
        size += ProtoHelper.FieldSize(1, Search);
        size += ProtoHelper.FieldSize(2, Type);
        size += ProtoHelper.FieldSize(3, CollectionId);
        size += ProtoHelper.RepeatedFieldSize(4, Tags);
        if (Page != 1) size += ProtoHelper.FieldSize(5, Page);
        if (PageSize != 50) size += ProtoHelper.FieldSize(6, PageSize);
        if (SortBy != "FileName") size += ProtoHelper.FieldSize(7, SortBy);
        if (SortDir != "asc") size += ProtoHelper.FieldSize(8, SortDir);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, Search);
        ProtoHelper.WriteField(output, 2, Type);
        ProtoHelper.WriteField(output, 3, CollectionId);
        ProtoHelper.WriteRepeatedField(output, 4, Tags);
        if (Page != 1) ProtoHelper.WriteField(output, 5, Page);
        if (PageSize != 50) ProtoHelper.WriteField(output, 6, PageSize);
        if (SortBy != "FileName") ProtoHelper.WriteField(output, 7, SortBy);
        if (SortDir != "asc") ProtoHelper.WriteField(output, 8, SortDir);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: Search = input.ReadString(); break;
                case 2: Type = input.ReadString(); break;
                case 3: CollectionId = input.ReadString(); break;
                case 4: Tags.Add(input.ReadString()); break;
                case 5: Page = input.ReadInt32(); break;
                case 6: PageSize = input.ReadInt32(); break;
                case 7: SortBy = input.ReadString(); break;
                case 8: SortDir = input.ReadString(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}

public sealed class ListAssetsResponse : IProtoSerializable
{
    public List<AssetSummary> Items { get; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }

    public int CalculateSize()
    {
        int size = 0;
        size += ProtoHelper.RepeatedFieldSize(1, Items);
        size += ProtoHelper.FieldSize(2, TotalCount);
        size += ProtoHelper.FieldSize(3, Page);
        size += ProtoHelper.FieldSize(4, PageSize);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteRepeatedField(output, 1, Items);
        ProtoHelper.WriteField(output, 2, TotalCount);
        ProtoHelper.WriteField(output, 3, Page);
        ProtoHelper.WriteField(output, 4, PageSize);
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
                        var item = new AssetSummary();
                        var buf1 = input.ReadBytes().ToByteArray();
                        using var ms1 = new MemoryStream(buf1);
                        using var cis1 = new CodedInputStream(ms1);
                        item.MergeFrom(cis1);
                        Items.Add(item);
                        break;
                    }
                case 2: TotalCount = input.ReadInt32(); break;
                case 3: Page = input.ReadInt32(); break;
                case 4: PageSize = input.ReadInt32(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}

public sealed class AssetSummary : IProtoSerializable
{
    public string Id { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string CollectionId { get; set; } = string.Empty;
    public string UploadedBy { get; set; } = string.Empty;
    public long CreatedAt { get; set; }

    public int CalculateSize()
    {
        int size = 0;
        size += ProtoHelper.FieldSize(1, Id);
        size += ProtoHelper.FieldSize(2, FileName);
        size += ProtoHelper.FieldSize(3, MimeType);
        size += ProtoHelper.FieldSize(4, FileSize);
        size += ProtoHelper.FieldSize(5, Title);
        size += ProtoHelper.FieldSize(6, Type);
        size += ProtoHelper.FieldSize(7, CollectionId);
        size += ProtoHelper.FieldSize(8, UploadedBy);
        size += ProtoHelper.FieldSize(9, CreatedAt);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, Id);
        ProtoHelper.WriteField(output, 2, FileName);
        ProtoHelper.WriteField(output, 3, MimeType);
        ProtoHelper.WriteField(output, 4, FileSize);
        ProtoHelper.WriteField(output, 5, Title);
        ProtoHelper.WriteField(output, 6, Type);
        ProtoHelper.WriteField(output, 7, CollectionId);
        ProtoHelper.WriteField(output, 8, UploadedBy);
        ProtoHelper.WriteField(output, 9, CreatedAt);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: Id = input.ReadString(); break;
                case 2: FileName = input.ReadString(); break;
                case 3: MimeType = input.ReadString(); break;
                case 4: FileSize = input.ReadInt64(); break;
                case 5: Title = input.ReadString(); break;
                case 6: Type = input.ReadString(); break;
                case 7: CollectionId = input.ReadString(); break;
                case 8: UploadedBy = input.ReadString(); break;
                case 9: CreatedAt = input.ReadInt64(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}

public sealed class GetAssetRequest : IProtoSerializable
{
    public string Id { get; set; } = string.Empty;
    public int CalculateSize() => ProtoHelper.FieldSize(1, Id);
    public void WriteTo(CodedOutputStream output) => ProtoHelper.WriteField(output, 1, Id);
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) { if (WireFormat.GetTagFieldNumber(tag) == 1) Id = input.ReadString(); else input.SkipLastField(); } }
}

public sealed class AssetDetail : IProtoSerializable
{
    public string Id { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FileExtension { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string ChecksumSha256 { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Tags { get; } = [];
    public string Type { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public double Duration { get; set; }
    public string CollectionId { get; set; } = string.Empty;
    public string CollectionName { get; set; } = string.Empty;
    public string UploadedBy { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public long CreatedAt { get; set; }
    public long ModifiedAt { get; set; }

    public int CalculateSize()
    {
        int size = 0;
        size += ProtoHelper.FieldSize(1, Id); size += ProtoHelper.FieldSize(2, FileName);
        size += ProtoHelper.FieldSize(3, FileExtension); size += ProtoHelper.FieldSize(4, MimeType);
        size += ProtoHelper.FieldSize(5, FileSize); size += ProtoHelper.FieldSize(6, ChecksumSha256);
        size += ProtoHelper.FieldSize(7, Title); size += ProtoHelper.FieldSize(8, Description);
        size += ProtoHelper.RepeatedFieldSize(9, Tags); size += ProtoHelper.FieldSize(10, Type);
        size += ProtoHelper.FieldSize(11, Width); size += ProtoHelper.FieldSize(12, Height);
        size += ProtoHelper.FieldSize(13, Duration); size += ProtoHelper.FieldSize(14, CollectionId);
        size += ProtoHelper.FieldSize(15, CollectionName); size += ProtoHelper.FieldSize(16, UploadedBy);
        if (Version != 1) size += ProtoHelper.FieldSize(17, Version);
        size += ProtoHelper.FieldSize(18, CreatedAt); size += ProtoHelper.FieldSize(19, ModifiedAt);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, Id); ProtoHelper.WriteField(output, 2, FileName);
        ProtoHelper.WriteField(output, 3, FileExtension); ProtoHelper.WriteField(output, 4, MimeType);
        ProtoHelper.WriteField(output, 5, FileSize); ProtoHelper.WriteField(output, 6, ChecksumSha256);
        ProtoHelper.WriteField(output, 7, Title); ProtoHelper.WriteField(output, 8, Description);
        ProtoHelper.WriteRepeatedField(output, 9, Tags); ProtoHelper.WriteField(output, 10, Type);
        ProtoHelper.WriteField(output, 11, Width); ProtoHelper.WriteField(output, 12, Height);
        ProtoHelper.WriteField(output, 13, Duration); ProtoHelper.WriteField(output, 14, CollectionId);
        ProtoHelper.WriteField(output, 15, CollectionName); ProtoHelper.WriteField(output, 16, UploadedBy);
        if (Version != 1) ProtoHelper.WriteField(output, 17, Version);
        ProtoHelper.WriteField(output, 18, CreatedAt); ProtoHelper.WriteField(output, 19, ModifiedAt);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: Id = input.ReadString(); break;
                case 2: FileName = input.ReadString(); break;
                case 3: FileExtension = input.ReadString(); break;
                case 4: MimeType = input.ReadString(); break;
                case 5: FileSize = input.ReadInt64(); break;
                case 6: ChecksumSha256 = input.ReadString(); break;
                case 7: Title = input.ReadString(); break;
                case 8: Description = input.ReadString(); break;
                case 9: Tags.Add(input.ReadString()); break;
                case 10: Type = input.ReadString(); break;
                case 11: Width = input.ReadInt32(); break;
                case 12: Height = input.ReadInt32(); break;
                case 13: Duration = input.ReadDouble(); break;
                case 14: CollectionId = input.ReadString(); break;
                case 15: CollectionName = input.ReadString(); break;
                case 16: UploadedBy = input.ReadString(); break;
                case 17: Version = input.ReadInt32(); break;
                case 18: CreatedAt = input.ReadInt64(); break;
                case 19: ModifiedAt = input.ReadInt64(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}

public sealed class CreateAssetRequest : IProtoSerializable
{
    public string FileName { get; set; } = string.Empty;
    public ByteString Content { get; set; } = ByteString.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Tags { get; } = [];
    public string CollectionId { get; set; } = string.Empty;

    public int CalculateSize()
    {
        int size = 0;
        size += ProtoHelper.FieldSize(1, FileName); size += ProtoHelper.FieldSize(2, Content);
        size += ProtoHelper.FieldSize(3, Title); size += ProtoHelper.FieldSize(4, Description);
        size += ProtoHelper.RepeatedFieldSize(5, Tags); size += ProtoHelper.FieldSize(6, CollectionId);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, FileName); ProtoHelper.WriteField(output, 2, Content);
        ProtoHelper.WriteField(output, 3, Title); ProtoHelper.WriteField(output, 4, Description);
        ProtoHelper.WriteRepeatedField(output, 5, Tags); ProtoHelper.WriteField(output, 6, CollectionId);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: FileName = input.ReadString(); break;
                case 2: Content = input.ReadBytes(); break;
                case 3: Title = input.ReadString(); break;
                case 4: Description = input.ReadString(); break;
                case 5: Tags.Add(input.ReadString()); break;
                case 6: CollectionId = input.ReadString(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}

public sealed class CreateAssetResponse : IProtoSerializable
{
    public string Id { get; set; } = string.Empty;
    public string Checksum { get; set; } = string.Empty;
    public bool Duplicate { get; set; }
    public string ExistingAssetId { get; set; } = string.Empty;

    public int CalculateSize()
    {
        int size = 0;
        size += ProtoHelper.FieldSize(1, Id); size += ProtoHelper.FieldSize(2, Checksum);
        size += ProtoHelper.FieldSize(3, Duplicate); size += ProtoHelper.FieldSize(4, ExistingAssetId);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, Id); ProtoHelper.WriteField(output, 2, Checksum);
        ProtoHelper.WriteField(output, 3, Duplicate); ProtoHelper.WriteField(output, 4, ExistingAssetId);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: Id = input.ReadString(); break;
                case 2: Checksum = input.ReadString(); break;
                case 3: Duplicate = input.ReadBool(); break;
                case 4: ExistingAssetId = input.ReadString(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}

public sealed class UpdateAssetRequest : IProtoSerializable
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Tags { get; } = [];
    public string CollectionId { get; set; } = string.Empty;
    public int ExpectedVersion { get; set; }

    public int CalculateSize()
    {
        int size = 0;
        size += ProtoHelper.FieldSize(1, Id); size += ProtoHelper.FieldSize(2, Title);
        size += ProtoHelper.FieldSize(3, Description); size += ProtoHelper.RepeatedFieldSize(4, Tags);
        size += ProtoHelper.FieldSize(5, CollectionId); size += ProtoHelper.FieldSize(6, ExpectedVersion);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, Id); ProtoHelper.WriteField(output, 2, Title);
        ProtoHelper.WriteField(output, 3, Description); ProtoHelper.WriteRepeatedField(output, 4, Tags);
        ProtoHelper.WriteField(output, 5, CollectionId); ProtoHelper.WriteField(output, 6, ExpectedVersion);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: Id = input.ReadString(); break;
                case 2: Title = input.ReadString(); break;
                case 3: Description = input.ReadString(); break;
                case 4: Tags.Add(input.ReadString()); break;
                case 5: CollectionId = input.ReadString(); break;
                case 6: ExpectedVersion = input.ReadInt32(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}

public sealed class UpdateAssetResponse : IProtoSerializable
{
    public string Id { get; set; } = string.Empty;
    public int NewVersion { get; set; }
    public long ModifiedAt { get; set; }
    public bool Conflict { get; set; }

    public int CalculateSize()
    {
        int size = 0;
        size += ProtoHelper.FieldSize(1, Id); size += ProtoHelper.FieldSize(2, NewVersion);
        size += ProtoHelper.FieldSize(3, ModifiedAt); size += ProtoHelper.FieldSize(4, Conflict);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, Id); ProtoHelper.WriteField(output, 2, NewVersion);
        ProtoHelper.WriteField(output, 3, ModifiedAt); ProtoHelper.WriteField(output, 4, Conflict);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: Id = input.ReadString(); break;
                case 2: NewVersion = input.ReadInt32(); break;
                case 3: ModifiedAt = input.ReadInt64(); break;
                case 4: Conflict = input.ReadBool(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}

public sealed class DeleteAssetRequest : IProtoSerializable
{
    public string Id { get; set; } = string.Empty;
    public int CalculateSize() => ProtoHelper.FieldSize(1, Id);
    public void WriteTo(CodedOutputStream output) => ProtoHelper.WriteField(output, 1, Id);
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) { if (WireFormat.GetTagFieldNumber(tag) == 1) Id = input.ReadString(); else input.SkipLastField(); } }
}

public sealed class DeleteAssetResponse : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}

public sealed class GetChangesRequest : IProtoSerializable
{
    public long SinceTimestamp { get; set; }
    public int CalculateSize() => ProtoHelper.FieldSize(1, SinceTimestamp);
    public void WriteTo(CodedOutputStream output) => ProtoHelper.WriteField(output, 1, SinceTimestamp);
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) { if (WireFormat.GetTagFieldNumber(tag) == 1) SinceTimestamp = input.ReadInt64(); else input.SkipLastField(); } }
}

public sealed class GetChangesResponse : IProtoSerializable
{
    public List<ChangeEvent> Changes { get; } = [];

    public int CalculateSize() => ProtoHelper.RepeatedFieldSize(1, Changes);
    public void WriteTo(CodedOutputStream output) => ProtoHelper.WriteRepeatedField(output, 1, Changes);
    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            if (WireFormat.GetTagFieldNumber(tag) == 1)
            {
                var c = new ChangeEvent();
                var buf = input.ReadBytes().ToByteArray();
                using var ms = new MemoryStream(buf);
                using var cis = new CodedInputStream(ms);
                c.MergeFrom(cis);
                Changes.Add(c);
            }
            else input.SkipLastField();
        }
    }
}

public sealed class ChangeEvent : IProtoSerializable
{
    public string EntityId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public long Timestamp { get; set; }

    public int CalculateSize()
    {
        int size = 0;
        size += ProtoHelper.FieldSize(1, EntityId);
        size += ProtoHelper.FieldSize(2, Action);
        size += ProtoHelper.FieldSize(3, Timestamp);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, EntityId);
        ProtoHelper.WriteField(output, 2, Action);
        ProtoHelper.WriteField(output, 3, Timestamp);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: EntityId = input.ReadString(); break;
                case 2: Action = input.ReadString(); break;
                case 3: Timestamp = input.ReadInt64(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}
