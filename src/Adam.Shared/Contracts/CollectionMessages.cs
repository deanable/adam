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
    public List<CollectionNode> Children { get; } = [];

    public int CalculateSize()
    {
        int size = 0;
        size += ProtoHelper.FieldSize(1, Id); size += ProtoHelper.FieldSize(2, Name);
        size += ProtoHelper.FieldSize(3, Description); size += ProtoHelper.FieldSize(4, ParentId);
        size += ProtoHelper.FieldSize(5, AssetCount); size += ProtoHelper.RepeatedFieldSize(6, Children);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, Id); ProtoHelper.WriteField(output, 2, Name);
        ProtoHelper.WriteField(output, 3, Description); ProtoHelper.WriteField(output, 4, ParentId);
        ProtoHelper.WriteField(output, 5, AssetCount); ProtoHelper.WriteRepeatedField(output, 6, Children);
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

    public int CalculateSize()
    {
        int size = 0;
        size += ProtoHelper.FieldSize(1, Name); size += ProtoHelper.FieldSize(2, Description);
        size += ProtoHelper.FieldSize(3, ParentId);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, Name); ProtoHelper.WriteField(output, 2, Description);
        ProtoHelper.WriteField(output, 3, ParentId);
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

    public int CalculateSize()
    {
        int size = 0;
        size += ProtoHelper.FieldSize(1, Id); size += ProtoHelper.FieldSize(2, Name);
        size += ProtoHelper.FieldSize(3, Description);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, Id); ProtoHelper.WriteField(output, 2, Name);
        ProtoHelper.WriteField(output, 3, Description);
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
                default: input.SkipLastField(); break;
            }
        }
    }
}

public sealed class DeleteCollectionRequest : IProtoSerializable
{
    public string Id { get; set; } = string.Empty;
    public int CalculateSize() => ProtoHelper.FieldSize(1, Id);
    public void WriteTo(CodedOutputStream output) => ProtoHelper.WriteField(output, 1, Id);
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) { if (WireFormat.GetTagFieldNumber(tag) == 1) Id = input.ReadString(); else input.SkipLastField(); } }
}

public sealed class DeleteCollectionResponse : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}
