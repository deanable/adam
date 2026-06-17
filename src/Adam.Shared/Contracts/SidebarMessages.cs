using Google.Protobuf;

namespace Adam.Shared.Contracts;

// ─── Folders ───

// Empty request - keep manual
public sealed class ListFoldersRequest : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) != 0) input.SkipLastField(); }
}

public sealed partial class ListFoldersResponse : IProtoSerializable
{
    [ProtoField(1)] public List<FolderInfo> Folders { get; } = new();
}

public sealed partial class FolderInfo : IProtoSerializable
{
    [ProtoField(1)] public string Path { get; set; } = string.Empty;
    [ProtoField(2)] public int AssetCount { get; set; }
}

// ─── Keywords ───

// Empty request - keep manual
public sealed class ListKeywordsRequest : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}

public sealed partial class ListKeywordsResponse : IProtoSerializable
{
    [ProtoField(1)] public List<KeywordInfo> Keywords { get; } = new();
}

public sealed partial class KeywordInfo : IProtoSerializable
{
    [ProtoField(1)] public Guid Id { get; set; }
    [ProtoField(2)] public string Name { get; set; } = string.Empty;
    [ProtoField(3)] public Guid? ParentId { get; set; }
    [ProtoField(4)] public int AssetCount { get; set; }
    [ProtoField(5)] public bool IsAiGenerated { get; set; }
}

// ─── Media Format Counts ───

// Empty request - keep manual
public sealed class ListMediaFormatCountsRequest : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}

public sealed partial class ListMediaFormatCountsResponse : IProtoSerializable
{
    [ProtoField(1)] public int TotalCount { get; set; }
    [ProtoField(2)] public int ImageCount { get; set; }
    [ProtoField(3)] public int VideoCount { get; set; }
    [ProtoField(4)] public int DocumentCount { get; set; }
    [ProtoField(5)] public int AudioCount { get; set; }
}

// ─── Metadata Categories ───

// Empty request - keep manual
public sealed class ListMetadataCategoriesRequest : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}

public sealed partial class ListMetadataCategoriesResponse : IProtoSerializable
{
    [ProtoField(1)] public List<CategoryInfo> Categories { get; } = new();
}

public sealed partial class CategoryInfo : IProtoSerializable
{
    [ProtoField(1)] public Guid Id { get; set; }
    [ProtoField(2)] public string Name { get; set; } = string.Empty;
    [ProtoField(3)] public Guid? ParentId { get; set; }
    [ProtoField(4)] public int AssetCount { get; set; }
    [ProtoField(5)] public bool IsAiGenerated { get; set; }
}

// ─── Keyword CRUD ───

public sealed partial class CreateKeywordRequest : IProtoSerializable
{
    [ProtoField(1)] public string Name { get; set; } = string.Empty;
    [ProtoField(2)] public string ParentId { get; set; } = string.Empty;
}

public sealed partial class CreateKeywordResponse : IProtoSerializable
{
    [ProtoField(1)] public string Id { get; set; } = string.Empty;
}

public sealed partial class UpdateKeywordRequest : IProtoSerializable
{
    [ProtoField(1)] public string Id { get; set; } = string.Empty;
    [ProtoField(2)] public string Name { get; set; } = string.Empty;
}

public sealed partial class DeleteKeywordRequest : IProtoSerializable
{
    [ProtoField(1)] public string Id { get; set; } = string.Empty;
    [ProtoField(2)] public bool CascadeChildren { get; set; } = true;
}

// Empty response - keep manual
public sealed class DeleteKeywordResponse : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) != 0) input.SkipLastField(); }
}

// ─── Category CRUD ───

public sealed partial class CreateCategoryRequest : IProtoSerializable
{
    [ProtoField(1)] public string Name { get; set; } = string.Empty;
    [ProtoField(2)] public string ParentId { get; set; } = string.Empty;
}

public sealed partial class CreateCategoryResponse : IProtoSerializable
{
    [ProtoField(1)] public string Id { get; set; } = string.Empty;
}

public sealed partial class UpdateCategoryRequest : IProtoSerializable
{
    [ProtoField(1)] public string Id { get; set; } = string.Empty;
    [ProtoField(2)] public string Name { get; set; } = string.Empty;
}

public sealed partial class DeleteCategoryRequest : IProtoSerializable
{
    [ProtoField(1)] public string Id { get; set; } = string.Empty;
    [ProtoField(2)] public bool CascadeChildren { get; set; } = true;
}

// Empty response - keep manual
public sealed class DeleteCategoryResponse : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) != 0) input.SkipLastField(); }
}

// ─── Date Taken Tree ───

// Empty request - keep manual
public sealed class ListDateTakenTreeRequest : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}

public sealed partial class ListDateTakenTreeResponse : IProtoSerializable
{
    [ProtoField(1)] public List<DateTakenYearInfo> Years { get; } = new();
}

public sealed partial class DateTakenYearInfo : IProtoSerializable
{
    [ProtoField(1)] public int Year { get; set; }
    [ProtoField(2)] public int AssetCount { get; set; }
    [ProtoField(3)] public List<DateTakenMonthInfo> Months { get; } = new();
}

public sealed partial class DateTakenMonthInfo : IProtoSerializable
{
    [ProtoField(1)] public int Month { get; set; }
    [ProtoField(2)] public string MonthName { get; set; } = string.Empty;
    [ProtoField(3)] public int AssetCount { get; set; }
}
