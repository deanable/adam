using Google.Protobuf;

namespace Adam.Shared.Contracts;

public sealed class ListCollectionsRequest : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}

public sealed class ListCollectionsResponse : IProtoSerializable
{
    public List<CollectionNode> Items { get; } = [];

    public int CalculateSize() => ProtoHelper.RepeatedFieldSize(1, Items);
    public void WriteTo(CodedOutputStream output) => ProtoHelper.WriteRepeatedField(output, 1, Items);
    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            if (WireFormat.GetTagFieldNumber(tag) == 1)
            {
                var item = new CollectionNode();
                var buf = input.ReadBytes().ToByteArray();
                using var ms = new MemoryStream(buf);
                using var cis = new CodedInputStream(ms);
                item.MergeFrom(cis);
                Items.Add(item);
            }
            else input.SkipLastField();
        }
    }
}

public sealed class CollectionNode : IProtoSerializable
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ParentId { get; set; } = string.Empty;
    public int AssetCount { get; set; }
    public bool IsSmart { get; set; }
    public string SmartQueryJson { get; set; } = string.Empty;
    public long LastAutoRefreshedAt { get; set; }
    public List<CollectionNode> Children { get; } = [];

    public int CalculateSize()
    {
        int size = 0;
        size += ProtoHelper.FieldSize(1, Id); size += ProtoHelper.FieldSize(2, Name);
        size += ProtoHelper.FieldSize(3, Description); size += ProtoHelper.FieldSize(4, ParentId);
        size += ProtoHelper.FieldSize(5, AssetCount); size += ProtoHelper.RepeatedFieldSize(6, Children);
        if (IsSmart) size += ProtoHelper.FieldSize(7, IsSmart);
        if (!string.IsNullOrEmpty(SmartQueryJson)) size += ProtoHelper.FieldSize(8, SmartQueryJson);
        if (LastAutoRefreshedAt != 0) size += ProtoHelper.FieldSize(9, LastAutoRefreshedAt);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, Id); ProtoHelper.WriteField(output, 2, Name);
        ProtoHelper.WriteField(output, 3, Description); ProtoHelper.WriteField(output, 4, ParentId);
        ProtoHelper.WriteField(output, 5, AssetCount); ProtoHelper.WriteRepeatedField(output, 6, Children);
        if (IsSmart) ProtoHelper.WriteField(output, 7, IsSmart);
        if (!string.IsNullOrEmpty(SmartQueryJson)) ProtoHelper.WriteField(output, 8, SmartQueryJson);
        if (LastAutoRefreshedAt != 0) ProtoHelper.WriteField(output, 9, LastAutoRefreshedAt);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: Id = input.ReadString(); break;
                case 2: Name = input.ReadString(); break;
                case 3: Description = input.ReadString(); break;
                case 4: ParentId = input.ReadString(); break;
                case 5: AssetCount = input.ReadInt32(); break;
                case 6:
                    {
                        var child = new CollectionNode();
                        var buf = input.ReadBytes().ToByteArray();
                        using var ms = new MemoryStream(buf);
                        using var cis = new CodedInputStream(ms);
                        child.MergeFrom(cis);
                        Children.Add(child);
                        break;
                    }
                case 7: IsSmart = input.ReadBool(); break;
                case 8: SmartQueryJson = input.ReadString(); break;
                case 9: LastAutoRefreshedAt = input.ReadInt64(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}

public sealed class CreateCollectionRequest : IProtoSerializable
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ParentId { get; set; } = string.Empty;
    public bool IsSmart { get; set; }
    public string SmartQueryJson { get; set; } = string.Empty;

    public int CalculateSize()
    {
        int size = 0;
        size += ProtoHelper.FieldSize(1, Name); size += ProtoHelper.FieldSize(2, Description);
        size += ProtoHelper.FieldSize(3, ParentId);
        if (IsSmart) size += ProtoHelper.FieldSize(4, IsSmart);
        if (!string.IsNullOrEmpty(SmartQueryJson)) size += ProtoHelper.FieldSize(5, SmartQueryJson);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, Name); ProtoHelper.WriteField(output, 2, Description);
        ProtoHelper.WriteField(output, 3, ParentId);
        if (IsSmart) ProtoHelper.WriteField(output, 4, IsSmart);
        if (!string.IsNullOrEmpty(SmartQueryJson)) ProtoHelper.WriteField(output, 5, SmartQueryJson);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: Name = input.ReadString(); break;
                case 2: Description = input.ReadString(); break;
                case 3: ParentId = input.ReadString(); break;
                case 4: IsSmart = input.ReadBool(); break;
                case 5: SmartQueryJson = input.ReadString(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}

public sealed class UpdateCollectionRequest : IProtoSerializable
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SmartQueryJson { get; set; } = string.Empty;

    public int CalculateSize()
    {
        int size = 0;
        size += ProtoHelper.FieldSize(1, Id); size += ProtoHelper.FieldSize(2, Name);
        size += ProtoHelper.FieldSize(3, Description);
        if (!string.IsNullOrEmpty(SmartQueryJson)) size += ProtoHelper.FieldSize(4, SmartQueryJson);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, Id); ProtoHelper.WriteField(output, 2, Name);
        ProtoHelper.WriteField(output, 3, Description);
        if (!string.IsNullOrEmpty(SmartQueryJson)) ProtoHelper.WriteField(output, 4, SmartQueryJson);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: Id = input.ReadString(); break;
                case 2: Name = input.ReadString(); break;
                case 3: Description = input.ReadString(); break;
                case 4: SmartQueryJson = input.ReadString(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}

public sealed class DeleteCollectionRequest : IProtoSerializable
{
    public string Id { get; set; } = string.Empty;
    public bool CascadeChildren { get; set; } = true;

    public int CalculateSize()
    {
        int size = ProtoHelper.FieldSize(1, Id);
        size += ProtoHelper.FieldSize(2, CascadeChildren);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, Id);
        ProtoHelper.WriteField(output, 2, CascadeChildren);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: Id = input.ReadString(); break;
                case 2: CascadeChildren = input.ReadBool(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}

public sealed class CreateCollectionResponse : IProtoSerializable
{
    public string Id { get; set; } = string.Empty;

    public int CalculateSize() => ProtoHelper.FieldSize(1, Id);
    public void WriteTo(CodedOutputStream output) => ProtoHelper.WriteField(output, 1, Id);
    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            if (WireFormat.GetTagFieldNumber(tag) == 1)
                Id = input.ReadString();
            else
                input.SkipLastField();
        }
    }
}

public sealed class DeleteCollectionResponse : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}

// ─── RefreshSmartCollection ─────────────────────────────────────

public sealed class RefreshSmartCollectionRequest : IProtoSerializable
{
    public string Id { get; set; } = string.Empty;

    public int CalculateSize() => ProtoHelper.FieldSize(1, Id);
    public void WriteTo(CodedOutputStream output) => ProtoHelper.WriteField(output, 1, Id);
    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            if (WireFormat.GetTagFieldNumber(tag) == 1)
                Id = input.ReadString();
            else
                input.SkipLastField();
        }
    }
}

public sealed class RefreshSmartCollectionResponse : IProtoSerializable
{
    public List<string> AssetIds { get; } = [];
    public long LastAutoRefreshedAt { get; set; }
    public int TotalCount { get; set; }

    public int CalculateSize()
    {
        int size = 0;
        size += ProtoHelper.RepeatedFieldSize(1, AssetIds);
        if (LastAutoRefreshedAt != 0) size += ProtoHelper.FieldSize(2, LastAutoRefreshedAt);
        if (TotalCount != 0) size += ProtoHelper.FieldSize(3, TotalCount);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteRepeatedField(output, 1, AssetIds);
        if (LastAutoRefreshedAt != 0) ProtoHelper.WriteField(output, 2, LastAutoRefreshedAt);
        if (TotalCount != 0) ProtoHelper.WriteField(output, 3, TotalCount);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1:
                    AssetIds.Add(input.ReadString());
                    break;
                case 2: LastAutoRefreshedAt = input.ReadInt64(); break;
                case 3: TotalCount = input.ReadInt32(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}

// ─── ReorderCollectionAssets ─────────────────────────────

public sealed class AssetOrderEntry : IProtoSerializable
{
    public string AssetId { get; set; } = string.Empty;
    public int SortOrder { get; set; }

    public int CalculateSize()
    {
        int size = ProtoHelper.FieldSize(1, AssetId);
        size += ProtoHelper.FieldSize(2, SortOrder);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, AssetId);
        ProtoHelper.WriteField(output, 2, SortOrder);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: AssetId = input.ReadString(); break;
                case 2: SortOrder = input.ReadInt32(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}

public sealed class ReorderCollectionAssetsRequest : IProtoSerializable
{
    public string CollectionId { get; set; } = string.Empty;
    public List<AssetOrderEntry> Entries { get; } = [];

    public int CalculateSize()
    {
        int size = ProtoHelper.FieldSize(1, CollectionId);
        size += ProtoHelper.RepeatedFieldSize(2, Entries);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, CollectionId);
        ProtoHelper.WriteRepeatedField(output, 2, Entries);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: CollectionId = input.ReadString(); break;
                case 2:
                    {
                        var entry = new AssetOrderEntry();
                        var buf = input.ReadBytes().ToByteArray();
                        using var ms = new MemoryStream(buf);
                        using var cis = new CodedInputStream(ms);
                        entry.MergeFrom(cis);
                        Entries.Add(entry);
                        break;
                    }
                default: input.SkipLastField(); break;
            }
        }
    }
}

public sealed class ReorderCollectionAssetsResponse : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}
