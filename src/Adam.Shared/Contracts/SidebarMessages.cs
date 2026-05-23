using Google.Protobuf;

namespace Adam.Shared.Contracts;

// ─── Folders ───

public sealed class ListFoldersRequest : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}

public sealed class ListFoldersResponse : IProtoSerializable
{
    public List<FolderInfo> Folders { get; } = new();

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
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1:
                    {
                        var item = new FolderInfo();
                        var buf = input.ReadBytes().ToByteArray();
                        using var ms = new MemoryStream(buf);
                        using var cis = new CodedInputStream(ms);
                        item.MergeFrom(cis);
                        Folders.Add(item);
                        break;
                    }
                default: input.SkipLastField(); break;
            }
        }
    }
}

public sealed class FolderInfo : IProtoSerializable
{
    public string Path { get; set; } = string.Empty;
    public int AssetCount { get; set; }

    public int CalculateSize() => ProtoHelper.FieldSize(1, Path) + ProtoHelper.FieldSize(2, AssetCount);
    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, Path);
        ProtoHelper.WriteField(output, 2, AssetCount);
    }
    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: Path = input.ReadString(); break;
                case 2: AssetCount = input.ReadInt32(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}

// ─── Keywords ───

public sealed class ListKeywordsRequest : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}

public sealed class ListKeywordsResponse : IProtoSerializable
{
    public List<KeywordInfo> Keywords { get; } = new();

    public int CalculateSize()
    {
        int size = 0;
        foreach (var k in Keywords) size += ProtoHelper.FieldSize(1, k);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        foreach (var k in Keywords) ProtoHelper.WriteField(output, 1, k);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1:
                    {
                        var item = new KeywordInfo();
                        var buf = input.ReadBytes().ToByteArray();
                        using var ms = new MemoryStream(buf);
                        using var cis = new CodedInputStream(ms);
                        item.MergeFrom(cis);
                        Keywords.Add(item);
                        break;
                    }
                default: input.SkipLastField(); break;
            }
        }
    }
}

public sealed class KeywordInfo : IProtoSerializable
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
    public int AssetCount { get; set; }

    public int CalculateSize() =>
        ProtoHelper.FieldSize(1, Id.ToString()) +
        ProtoHelper.FieldSize(2, Name) +
        (ParentId.HasValue ? ProtoHelper.FieldSize(3, ParentId.Value.ToString()) : 0) +
        ProtoHelper.FieldSize(4, AssetCount);

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, Id.ToString());
        ProtoHelper.WriteField(output, 2, Name);
        if (ParentId.HasValue) ProtoHelper.WriteField(output, 3, ParentId.Value.ToString());
        ProtoHelper.WriteField(output, 4, AssetCount);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: Id = Guid.Parse(input.ReadString()); break;
                case 2: Name = input.ReadString(); break;
                case 3: ParentId = Guid.Parse(input.ReadString()); break;
                case 4: AssetCount = input.ReadInt32(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}

// ─── Media Format Counts ───

public sealed class ListMediaFormatCountsRequest : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}

public sealed class ListMediaFormatCountsResponse : IProtoSerializable
{
    public int TotalCount { get; set; }
    public int ImageCount { get; set; }
    public int VideoCount { get; set; }
    public int DocumentCount { get; set; }
    public int AudioCount { get; set; }

    public int CalculateSize() =>
        ProtoHelper.FieldSize(1, TotalCount) +
        ProtoHelper.FieldSize(2, ImageCount) +
        ProtoHelper.FieldSize(3, VideoCount) +
        ProtoHelper.FieldSize(4, DocumentCount) +
        ProtoHelper.FieldSize(5, AudioCount);

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, TotalCount);
        ProtoHelper.WriteField(output, 2, ImageCount);
        ProtoHelper.WriteField(output, 3, VideoCount);
        ProtoHelper.WriteField(output, 4, DocumentCount);
        ProtoHelper.WriteField(output, 5, AudioCount);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: TotalCount = input.ReadInt32(); break;
                case 2: ImageCount = input.ReadInt32(); break;
                case 3: VideoCount = input.ReadInt32(); break;
                case 4: DocumentCount = input.ReadInt32(); break;
                case 5: AudioCount = input.ReadInt32(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}

// ─── Metadata Categories ───

public sealed class ListMetadataCategoriesRequest : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}

public sealed class ListMetadataCategoriesResponse : IProtoSerializable
{
    public List<CategoryInfo> Categories { get; } = new();

    public int CalculateSize()
    {
        int size = 0;
        foreach (var c in Categories) size += ProtoHelper.FieldSize(1, c);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        foreach (var c in Categories) ProtoHelper.WriteField(output, 1, c);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1:
                    {
                        var item = new CategoryInfo();
                        var buf = input.ReadBytes().ToByteArray();
                        using var ms = new MemoryStream(buf);
                        using var cis = new CodedInputStream(ms);
                        item.MergeFrom(cis);
                        Categories.Add(item);
                        break;
                    }
                default: input.SkipLastField(); break;
            }
        }
    }
}

public sealed class CategoryInfo : IProtoSerializable
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
    public int AssetCount { get; set; }

    public int CalculateSize() =>
        ProtoHelper.FieldSize(1, Id.ToString()) +
        ProtoHelper.FieldSize(2, Name) +
        (ParentId.HasValue ? ProtoHelper.FieldSize(3, ParentId.Value.ToString()) : 0) +
        ProtoHelper.FieldSize(4, AssetCount);

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, Id.ToString());
        ProtoHelper.WriteField(output, 2, Name);
        if (ParentId.HasValue) ProtoHelper.WriteField(output, 3, ParentId.Value.ToString());
        ProtoHelper.WriteField(output, 4, AssetCount);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: Id = Guid.Parse(input.ReadString()); break;
                case 2: Name = input.ReadString(); break;
                case 3: ParentId = Guid.Parse(input.ReadString()); break;
                case 4: AssetCount = input.ReadInt32(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}

// ─── Date Taken Tree ───

public sealed class ListDateTakenTreeRequest : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}

public sealed class ListDateTakenTreeResponse : IProtoSerializable
{
    public List<DateTakenYearInfo> Years { get; } = new();

    public int CalculateSize()
    {
        int size = 0;
        foreach (var y in Years) size += ProtoHelper.FieldSize(1, y);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        foreach (var y in Years) ProtoHelper.WriteField(output, 1, y);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1:
                    {
                        var item = new DateTakenYearInfo();
                        var buf = input.ReadBytes().ToByteArray();
                        using var ms = new MemoryStream(buf);
                        using var cis = new CodedInputStream(ms);
                        item.MergeFrom(cis);
                        Years.Add(item);
                        break;
                    }
                default: input.SkipLastField(); break;
            }
        }
    }
}

public sealed class DateTakenYearInfo : IProtoSerializable
{
    public int Year { get; set; }
    public int AssetCount { get; set; }
    public List<DateTakenMonthInfo> Months { get; } = new();

    public int CalculateSize()
    {
        int size = ProtoHelper.FieldSize(1, Year) + ProtoHelper.FieldSize(2, AssetCount);
        foreach (var m in Months) size += ProtoHelper.FieldSize(3, m);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, Year);
        ProtoHelper.WriteField(output, 2, AssetCount);
        foreach (var m in Months) ProtoHelper.WriteField(output, 3, m);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: Year = input.ReadInt32(); break;
                case 2: AssetCount = input.ReadInt32(); break;
                case 3:
                    {
                        var item = new DateTakenMonthInfo();
                        var buf = input.ReadBytes().ToByteArray();
                        using var ms = new MemoryStream(buf);
                        using var cis = new CodedInputStream(ms);
                        item.MergeFrom(cis);
                        Months.Add(item);
                        break;
                    }
                default: input.SkipLastField(); break;
            }
        }
    }
}

public sealed class DateTakenMonthInfo : IProtoSerializable
{
    public int Month { get; set; }
    public string MonthName { get; set; } = string.Empty;
    public int AssetCount { get; set; }

    public int CalculateSize() =>
        ProtoHelper.FieldSize(1, Month) +
        ProtoHelper.FieldSize(2, MonthName) +
        ProtoHelper.FieldSize(3, AssetCount);

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, Month);
        ProtoHelper.WriteField(output, 2, MonthName);
        ProtoHelper.WriteField(output, 3, AssetCount);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: Month = input.ReadInt32(); break;
                case 2: MonthName = input.ReadString(); break;
                case 3: AssetCount = input.ReadInt32(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}
