using Google.Protobuf;

namespace Adam.Shared.Contracts;

public sealed class WatchedFolderInfo : IProtoSerializable
{
    public string Id { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;

    public int CalculateSize() =>
        ProtoHelper.FieldSize(1, Id) +
        ProtoHelper.FieldSize(2, Path) +
        ProtoHelper.FieldSize(3, IsEnabled);

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, Id);
        ProtoHelper.WriteField(output, 2, Path);
        ProtoHelper.WriteField(output, 3, IsEnabled);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: Id = input.ReadString(); break;
                case 2: Path = input.ReadString(); break;
                case 3: IsEnabled = input.ReadBool(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}

public sealed class ListWatchedFoldersRequest : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}

public sealed class ListWatchedFoldersResponse : IProtoSerializable
{
    public List<WatchedFolderInfo> Folders { get; } = new();

    public int CalculateSize()
    {
        int size = 0;
        foreach (var f in Folders) size += ProtoHelper.FieldSize(1, f);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        foreach (var f in Folders) ProtoHelper.WriteField(output, 1, f);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            if (WireFormat.GetTagFieldNumber(tag) == 1)
            {
                var item = new WatchedFolderInfo();
                var buf = input.ReadBytes().ToByteArray();
                using var ms = new MemoryStream(buf);
                using var cis = new CodedInputStream(ms);
                item.MergeFrom(cis);
                Folders.Add(item);
            }
            else
            {
                input.SkipLastField();
            }
        }
    }
}

public sealed class CreateWatchedFolderRequest : IProtoSerializable
{
    public string Path { get; set; } = string.Empty;

    public int CalculateSize() => ProtoHelper.FieldSize(1, Path);
    public void WriteTo(CodedOutputStream output) => ProtoHelper.WriteField(output, 1, Path);
    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: Path = input.ReadString(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}

public sealed class CreateWatchedFolderResponse : IProtoSerializable
{
    public string Id { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;

    public int CalculateSize() => ProtoHelper.FieldSize(1, Id) + ProtoHelper.FieldSize(2, Path);
    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, Id);
        ProtoHelper.WriteField(output, 2, Path);
    }
    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: Id = input.ReadString(); break;
                case 2: Path = input.ReadString(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}

public sealed class UpdateWatchedFolderRequest : IProtoSerializable
{
    public string Id { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;

    public int CalculateSize() =>
        ProtoHelper.FieldSize(1, Id) +
        ProtoHelper.FieldSize(2, Path) +
        ProtoHelper.FieldSize(3, IsEnabled);

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, Id);
        ProtoHelper.WriteField(output, 2, Path);
        ProtoHelper.WriteField(output, 3, IsEnabled);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: Id = input.ReadString(); break;
                case 2: Path = input.ReadString(); break;
                case 3: IsEnabled = input.ReadBool(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}

public sealed class DeleteWatchedFolderRequest : IProtoSerializable
{
    public string Id { get; set; } = string.Empty;

    public int CalculateSize() => ProtoHelper.FieldSize(1, Id);
    public void WriteTo(CodedOutputStream output) => ProtoHelper.WriteField(output, 1, Id);
    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: Id = input.ReadString(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}

public sealed class DeleteWatchedFolderResponse : IProtoSerializable
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
