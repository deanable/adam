using Google.Protobuf;

namespace Adam.Shared.Contracts;

// ─── SavedSearch Wire DTO ─────────────────────────────────────

public sealed class SavedSearchWire : IProtoSerializable
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? QueryText { get; set; }
    public string FiltersJson { get; set; } = "{}";
    public bool IsPinned { get; set; }
    public long CreatedAt { get; set; }
    public long ModifiedAt { get; set; }

    public int CalculateSize()
    {
        int size = 0;
        size += ProtoHelper.FieldSize(1, Id);
        size += ProtoHelper.FieldSize(2, Name);
        if (!string.IsNullOrEmpty(QueryText)) size += ProtoHelper.FieldSize(3, QueryText);
        size += ProtoHelper.FieldSize(4, FiltersJson);
        if (IsPinned) size += ProtoHelper.FieldSize(5, IsPinned);
        if (CreatedAt != 0) size += ProtoHelper.FieldSize(6, CreatedAt);
        if (ModifiedAt != 0) size += ProtoHelper.FieldSize(7, ModifiedAt);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, Id);
        ProtoHelper.WriteField(output, 2, Name);
        if (!string.IsNullOrEmpty(QueryText)) ProtoHelper.WriteField(output, 3, QueryText);
        ProtoHelper.WriteField(output, 4, FiltersJson);
        if (IsPinned) ProtoHelper.WriteField(output, 5, IsPinned);
        if (CreatedAt != 0) ProtoHelper.WriteField(output, 6, CreatedAt);
        if (ModifiedAt != 0) ProtoHelper.WriteField(output, 7, ModifiedAt);
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
                case 3: QueryText = input.ReadString(); break;
                case 4: FiltersJson = input.ReadString(); break;
                case 5: IsPinned = input.ReadBool(); break;
                case 6: CreatedAt = input.ReadInt64(); break;
                case 7: ModifiedAt = input.ReadInt64(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}

// ─── CreateSavedSearch ────────────────────────────────────────

public sealed class CreateSavedSearchRequest : IProtoSerializable
{
    public string Name { get; set; } = string.Empty;
    public string? QueryText { get; set; }
    public string FiltersJson { get; set; } = "{}";
    public bool IsPinned { get; set; }

    public int CalculateSize()
    {
        int size = 0;
        size += ProtoHelper.FieldSize(1, Name);
        if (!string.IsNullOrEmpty(QueryText)) size += ProtoHelper.FieldSize(2, QueryText);
        size += ProtoHelper.FieldSize(3, FiltersJson);
        if (IsPinned) size += ProtoHelper.FieldSize(4, IsPinned);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, Name);
        if (!string.IsNullOrEmpty(QueryText)) ProtoHelper.WriteField(output, 2, QueryText);
        ProtoHelper.WriteField(output, 3, FiltersJson);
        if (IsPinned) ProtoHelper.WriteField(output, 4, IsPinned);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: Name = input.ReadString(); break;
                case 2: QueryText = input.ReadString(); break;
                case 3: FiltersJson = input.ReadString(); break;
                case 4: IsPinned = input.ReadBool(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}

public sealed class CreateSavedSearchResponse : IProtoSerializable
{
    public string Id { get; set; } = string.Empty;
    public long CreatedAt { get; set; }

    public int CalculateSize()
    {
        int size = ProtoHelper.FieldSize(1, Id);
        if (CreatedAt != 0) size += ProtoHelper.FieldSize(2, CreatedAt);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, Id);
        if (CreatedAt != 0) ProtoHelper.WriteField(output, 2, CreatedAt);
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

// ─── ListSavedSearches ────────────────────────────────────────

public sealed class ListSavedSearchesRequest : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}

public sealed class ListSavedSearchesResponse : IProtoSerializable
{
    public List<SavedSearchWire> Items { get; } = [];

    public int CalculateSize() => ProtoHelper.RepeatedFieldSize(1, Items);
    public void WriteTo(CodedOutputStream output) => ProtoHelper.WriteRepeatedField(output, 1, Items);
    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            if (WireFormat.GetTagFieldNumber(tag) == 1)
            {
                var item = new SavedSearchWire();
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

// ─── UpdateSavedSearch ────────────────────────────────────────

public sealed class UpdateSavedSearchRequest : IProtoSerializable
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? QueryText { get; set; }
    public string FiltersJson { get; set; } = "{}";

    public int CalculateSize()
    {
        int size = ProtoHelper.FieldSize(1, Id);
        size += ProtoHelper.FieldSize(2, Name);
        if (!string.IsNullOrEmpty(QueryText)) size += ProtoHelper.FieldSize(3, QueryText);
        size += ProtoHelper.FieldSize(4, FiltersJson);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, Id);
        ProtoHelper.WriteField(output, 2, Name);
        if (!string.IsNullOrEmpty(QueryText)) ProtoHelper.WriteField(output, 3, QueryText);
        ProtoHelper.WriteField(output, 4, FiltersJson);
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
                case 3: QueryText = input.ReadString(); break;
                case 4: FiltersJson = input.ReadString(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}

public sealed class UpdateSavedSearchResponse : IProtoSerializable
{
    public long ModifiedAt { get; set; }

    public int CalculateSize()
    {
        if (ModifiedAt != 0) return ProtoHelper.FieldSize(1, ModifiedAt);
        return 0;
    }

    public void WriteTo(CodedOutputStream output)
    {
        if (ModifiedAt != 0) ProtoHelper.WriteField(output, 1, ModifiedAt);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            if (WireFormat.GetTagFieldNumber(tag) == 1)
                ModifiedAt = input.ReadInt64();
            else
                input.SkipLastField();
        }
    }
}

// ─── DeleteSavedSearch ────────────────────────────────────────

public sealed class DeleteSavedSearchRequest : IProtoSerializable
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

public sealed class DeleteSavedSearchResponse : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}

// ─── PinSavedSearch ───────────────────────────────────────────

public sealed class PinSavedSearchRequest : IProtoSerializable
{
    public string Id { get; set; } = string.Empty;
    public bool IsPinned { get; set; }

    public int CalculateSize()
    {
        int size = ProtoHelper.FieldSize(1, Id);
        size += ProtoHelper.FieldSize(2, IsPinned);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, Id);
        ProtoHelper.WriteField(output, 2, IsPinned);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: Id = input.ReadString(); break;
                case 2: IsPinned = input.ReadBool(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}

public sealed class PinSavedSearchResponse : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}

// ═══════════════════════════════════════════════════════════════
//  SearchHistory
// ═══════════════════════════════════════════════════════════════

// ─── SearchHistory Wire DTO ───────────────────────────────────

public sealed class SearchHistoryWire : IProtoSerializable
{
    public string Id { get; set; } = string.Empty;
    public string QueryText { get; set; } = string.Empty;
    public string FiltersJson { get; set; } = "{}";
    public bool IsSemantic { get; set; }
    public long ExecutedAt { get; set; }

    public int CalculateSize()
    {
        int size = 0;
        size += ProtoHelper.FieldSize(1, Id);
        size += ProtoHelper.FieldSize(2, QueryText);
        size += ProtoHelper.FieldSize(3, FiltersJson);
        if (IsSemantic) size += ProtoHelper.FieldSize(4, IsSemantic);
        if (ExecutedAt != 0) size += ProtoHelper.FieldSize(5, ExecutedAt);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, Id);
        ProtoHelper.WriteField(output, 2, QueryText);
        ProtoHelper.WriteField(output, 3, FiltersJson);
        if (IsSemantic) ProtoHelper.WriteField(output, 4, IsSemantic);
        if (ExecutedAt != 0) ProtoHelper.WriteField(output, 5, ExecutedAt);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: Id = input.ReadString(); break;
                case 2: QueryText = input.ReadString(); break;
                case 3: FiltersJson = input.ReadString(); break;
                case 4: IsSemantic = input.ReadBool(); break;
                case 5: ExecutedAt = input.ReadInt64(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}

// ─── RecordSearchHistory ──────────────────────────────────────

public sealed class RecordSearchHistoryRequest : IProtoSerializable
{
    public string QueryText { get; set; } = string.Empty;
    public string FiltersJson { get; set; } = "{}";
    public bool IsSemantic { get; set; }

    public int CalculateSize()
    {
        int size = ProtoHelper.FieldSize(1, QueryText);
        size += ProtoHelper.FieldSize(2, FiltersJson);
        if (IsSemantic) size += ProtoHelper.FieldSize(3, IsSemantic);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, QueryText);
        ProtoHelper.WriteField(output, 2, FiltersJson);
        if (IsSemantic) ProtoHelper.WriteField(output, 3, IsSemantic);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: QueryText = input.ReadString(); break;
                case 2: FiltersJson = input.ReadString(); break;
                case 3: IsSemantic = input.ReadBool(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}

public sealed class RecordSearchHistoryResponse : IProtoSerializable
{
    public string Id { get; set; } = string.Empty;

    public int CalculateSize()
    {
        if (!string.IsNullOrEmpty(Id)) return ProtoHelper.FieldSize(1, Id);
        return 0;
    }

    public void WriteTo(CodedOutputStream output)
    {
        if (!string.IsNullOrEmpty(Id)) ProtoHelper.WriteField(output, 1, Id);
    }

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

// ─── ListSearchHistory ────────────────────────────────────────

public sealed class ListSearchHistoryRequest : IProtoSerializable
{
    public int MaxResults { get; set; } = 200;

    public int CalculateSize()
    {
        if (MaxResults != 200) return ProtoHelper.FieldSize(1, MaxResults);
        return 0;
    }

    public void WriteTo(CodedOutputStream output)
    {
        if (MaxResults != 200) ProtoHelper.WriteField(output, 1, MaxResults);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            if (WireFormat.GetTagFieldNumber(tag) == 1)
                MaxResults = input.ReadInt32();
            else
                input.SkipLastField();
        }
    }
}

public sealed class ListSearchHistoryResponse : IProtoSerializable
{
    public List<SearchHistoryWire> Items { get; } = [];

    public int CalculateSize() => ProtoHelper.RepeatedFieldSize(1, Items);
    public void WriteTo(CodedOutputStream output) => ProtoHelper.WriteRepeatedField(output, 1, Items);
    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            if (WireFormat.GetTagFieldNumber(tag) == 1)
            {
                var item = new SearchHistoryWire();
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

// ─── ClearSearchHistory ───────────────────────────────────────

public sealed class ClearSearchHistoryRequest : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}

public sealed class ClearSearchHistoryResponse : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}

// ═══════════════════════════════════════════════════════════════
//  Semantic Search
// ═══════════════════════════════════════════════════════════════

// ─── SemanticSearchRequest / Response ─────────────────────────

public sealed class SemanticSearchRequest : IProtoSerializable
{
    public string Query { get; set; } = string.Empty;
    public int MaxResults { get; set; } = 50;
    public double MinScore { get; set; } = 0.0;
    public bool RecordHistory { get; set; } = true;

    public int CalculateSize()
    {
        int size = ProtoHelper.FieldSize(1, Query);
        if (MaxResults != 50) size += ProtoHelper.FieldSize(2, MaxResults);
        if (MinScore != 0.0) size += ProtoHelper.FieldSize(3, MinScore);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, Query);
        if (MaxResults != 50) ProtoHelper.WriteField(output, 2, MaxResults);
        if (MinScore != 0.0) ProtoHelper.WriteField(output, 3, MinScore);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: Query = input.ReadString(); break;
                case 2: MaxResults = input.ReadInt32(); break;
                case 3: MinScore = input.ReadDouble(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}

public sealed class SemanticSearchResultWire : IProtoSerializable
{
    public string AssetId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public long CreatedAt { get; set; }
    public float Score { get; set; }
    public int Rank { get; set; }

    public int CalculateSize()
    {
        int size = 0;
        size += ProtoHelper.FieldSize(1, AssetId);
        size += ProtoHelper.FieldSize(2, Title);
        size += ProtoHelper.FieldSize(3, FileName);
        size += ProtoHelper.FieldSize(4, MimeType);
        if (FileSize != 0) size += ProtoHelper.FieldSize(5, FileSize);
        if (CreatedAt != 0) size += ProtoHelper.FieldSize(6, CreatedAt);
        if (Score != 0) size += ProtoHelper.FieldSize(7, Score);
        if (Rank != 0) size += ProtoHelper.FieldSize(8, Rank);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, AssetId);
        ProtoHelper.WriteField(output, 2, Title);
        ProtoHelper.WriteField(output, 3, FileName);
        ProtoHelper.WriteField(output, 4, MimeType);
        if (FileSize != 0) ProtoHelper.WriteField(output, 5, FileSize);
        if (CreatedAt != 0) ProtoHelper.WriteField(output, 6, CreatedAt);
        if (Score != 0) ProtoHelper.WriteField(output, 7, Score);
        if (Rank != 0) ProtoHelper.WriteField(output, 8, Rank);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: AssetId = input.ReadString(); break;
                case 2: Title = input.ReadString(); break;
                case 3: FileName = input.ReadString(); break;
                case 4: MimeType = input.ReadString(); break;
                case 5: FileSize = input.ReadInt64(); break;
                case 6: CreatedAt = input.ReadInt64(); break;
                case 7: Score = input.ReadFloat(); break;
                case 8: Rank = input.ReadInt32(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}

public sealed class SemanticSearchResponse : IProtoSerializable
{
    public List<SemanticSearchResultWire> Results { get; } = [];

    public int CalculateSize() => ProtoHelper.RepeatedFieldSize(1, Results);
    public void WriteTo(CodedOutputStream output) => ProtoHelper.WriteRepeatedField(output, 1, Results);
    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            if (WireFormat.GetTagFieldNumber(tag) == 1)
            {
                var item = new SemanticSearchResultWire();
                var buf = input.ReadBytes().ToByteArray();
                using var ms = new MemoryStream(buf);
                using var cis = new CodedInputStream(ms);
                item.MergeFrom(cis);
                Results.Add(item);
            }
            else input.SkipLastField();
        }
    }
}

// ─── FindSimilarRequest / Response ────────────────────────────

public sealed class FindSimilarRequest : IProtoSerializable
{
    public string AssetId { get; set; } = string.Empty;
    public int MaxResults { get; set; } = 20;
    public double MinScore { get; set; } = 0.0;

    public int CalculateSize()
    {
        int size = ProtoHelper.FieldSize(1, AssetId);
        if (MaxResults != 20) size += ProtoHelper.FieldSize(2, MaxResults);
        if (MinScore != 0.0) size += ProtoHelper.FieldSize(3, MinScore);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, AssetId);
        if (MaxResults != 20) ProtoHelper.WriteField(output, 2, MaxResults);
        if (MinScore != 0.0) ProtoHelper.WriteField(output, 3, MinScore);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: AssetId = input.ReadString(); break;
                case 2: MaxResults = input.ReadInt32(); break;
                case 3: MinScore = input.ReadDouble(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}

public sealed class FindSimilarResponse : IProtoSerializable
{
    public List<SemanticSearchResultWire> Results { get; } = [];

    public int CalculateSize() => ProtoHelper.RepeatedFieldSize(1, Results);
    public void WriteTo(CodedOutputStream output) => ProtoHelper.WriteRepeatedField(output, 1, Results);
    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            if (WireFormat.GetTagFieldNumber(tag) == 1)
            {
                var item = new SemanticSearchResultWire();
                var buf = input.ReadBytes().ToByteArray();
                using var ms = new MemoryStream(buf);
                using var cis = new CodedInputStream(ms);
                item.MergeFrom(cis);
                Results.Add(item);
            }
            else input.SkipLastField();
        }
    }
}

// ─── RecomputeEmbeddingsRequest / Response ────────────────────

public sealed class RecomputeEmbeddingsRequest : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}

public sealed class RecomputeEmbeddingsResponse : IProtoSerializable
{
    public int TotalProcessed { get; set; }

    public int CalculateSize()
    {
        if (TotalProcessed != 0) return ProtoHelper.FieldSize(1, TotalProcessed);
        return 0;
    }

    public void WriteTo(CodedOutputStream output)
    {
        if (TotalProcessed != 0) ProtoHelper.WriteField(output, 1, TotalProcessed);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            if (WireFormat.GetTagFieldNumber(tag) == 1)
                TotalProcessed = input.ReadInt32();
            else
                input.SkipLastField();
        }
    }
}
