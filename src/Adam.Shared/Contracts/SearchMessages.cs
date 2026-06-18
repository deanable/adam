using Google.Protobuf;

namespace Adam.Shared.Contracts;

// ─── SavedSearch Wire DTO ─────────────────────────────────────

public sealed partial class SavedSearchWire : IProtoSerializable
{
    [ProtoField(1)] public string Id { get; set; } = string.Empty;
    [ProtoField(2)] public string Name { get; set; } = string.Empty;
    [ProtoField(3)] public string? QueryText { get; set; }
    [ProtoField(4)] public string FiltersJson { get; set; } = "{}";
    [ProtoField(5)] public bool IsPinned { get; set; }
    [ProtoField(6)] public long CreatedAt { get; set; }
    [ProtoField(7)] public long ModifiedAt { get; set; }
}

// ─── CreateSavedSearch ────────────────────────────────────────

public sealed partial class CreateSavedSearchRequest : IProtoSerializable
{
    [ProtoField(1)] public string Name { get; set; } = string.Empty;
    [ProtoField(2)] public string? QueryText { get; set; }
    [ProtoField(3)] public string FiltersJson { get; set; } = "{}";
    [ProtoField(4)] public bool IsPinned { get; set; }
}

public sealed partial class CreateSavedSearchResponse : IProtoSerializable
{
    [ProtoField(1)] public string Id { get; set; } = string.Empty;
    [ProtoField(2)] public long CreatedAt { get; set; }
}

// ─── ListSavedSearches ────────────────────────────────────────

// Empty request - keep manual
public sealed class ListSavedSearchesRequest : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}

public sealed partial class ListSavedSearchesResponse : IProtoSerializable
{
    [ProtoField(1)] public List<SavedSearchWire> Items { get; } = [];
}

// ─── UpdateSavedSearch ────────────────────────────────────────

public sealed partial class UpdateSavedSearchRequest : IProtoSerializable
{
    [ProtoField(1)] public string Id { get; set; } = string.Empty;
    [ProtoField(2)] public string Name { get; set; } = string.Empty;
    [ProtoField(3)] public string? QueryText { get; set; }
    [ProtoField(4)] public string FiltersJson { get; set; } = "{}";
}

public sealed partial class UpdateSavedSearchResponse : IProtoSerializable
{
    [ProtoField(1)] public long ModifiedAt { get; set; }
}

// ─── DeleteSavedSearch ────────────────────────────────────────

public sealed partial class DeleteSavedSearchRequest : IProtoSerializable
{
    [ProtoField(1)] public string Id { get; set; } = string.Empty;
}

// Empty response - keep manual
public sealed class DeleteSavedSearchResponse : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}

// ─── PinSavedSearch ───────────────────────────────────────────

public sealed partial class PinSavedSearchRequest : IProtoSerializable
{
    [ProtoField(1)] public string Id { get; set; } = string.Empty;
    [ProtoField(2)] public bool IsPinned { get; set; }
}

// Empty response - keep manual
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

public sealed partial class SearchHistoryWire : IProtoSerializable
{
    [ProtoField(1)] public string Id { get; set; } = string.Empty;
    [ProtoField(2)] public string QueryText { get; set; } = string.Empty;
    [ProtoField(3)] public string FiltersJson { get; set; } = "{}";
    [ProtoField(4)] public bool IsSemantic { get; set; }
    [ProtoField(5)] public long ExecutedAt { get; set; }
}

// ─── RecordSearchHistory ──────────────────────────────────────

public sealed partial class RecordSearchHistoryRequest : IProtoSerializable
{
    [ProtoField(1)] public string QueryText { get; set; } = string.Empty;
    [ProtoField(2)] public string FiltersJson { get; set; } = "{}";
    [ProtoField(3)] public bool IsSemantic { get; set; }
}

public sealed partial class RecordSearchHistoryResponse : IProtoSerializable
{
    [ProtoField(1)] public string Id { get; set; } = string.Empty;
}

// ─── ListSearchHistory ────────────────────────────────────────

public sealed partial class ListSearchHistoryRequest : IProtoSerializable
{
    [ProtoField(1, DefaultValue = 200)] public int MaxResults { get; set; } = 200;
}

public sealed partial class ListSearchHistoryResponse : IProtoSerializable
{
    [ProtoField(1)] public List<SearchHistoryWire> Items { get; } = [];
}

// ─── ClearSearchHistory ───────────────────────────────────────

// Empty request/response - keep manual
public sealed class ClearSearchHistoryRequest : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}

// Empty request/response - keep manual
public sealed class ClearSearchHistoryResponse : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}

// ═══════════════════════════════════════════════════════════════
//  Semantic Search
// ═══════════════════════════════════════════════════════════════

public sealed partial class SemanticSearchRequest : IProtoSerializable
{
    [ProtoField(1)] public string Query { get; set; } = string.Empty;
    [ProtoField(2, DefaultValue = 50)] public int MaxResults { get; set; } = 50;
    [ProtoField(3)] public double MinScore { get; set; } = 0.0;

    // Client-side only; not serialized on the wire
    public bool RecordHistory { get; set; } = true;
}

public sealed partial class SemanticSearchResultWire : IProtoSerializable
{
    [ProtoField(1)] public string AssetId { get; set; } = string.Empty;
    [ProtoField(2)] public string Title { get; set; } = string.Empty;
    [ProtoField(3)] public string FileName { get; set; } = string.Empty;
    [ProtoField(4)] public string MimeType { get; set; } = string.Empty;
    [ProtoField(5)] public long FileSize { get; set; }
    [ProtoField(6)] public long CreatedAt { get; set; }
    [ProtoField(7)] public float Score { get; set; }
    [ProtoField(8)] public int Rank { get; set; }
}

public sealed partial class SemanticSearchResponse : IProtoSerializable
{
    [ProtoField(1)] public List<SemanticSearchResultWire> Results { get; } = [];
}

public sealed partial class FindSimilarRequest : IProtoSerializable
{
    [ProtoField(1)] public string AssetId { get; set; } = string.Empty;
    [ProtoField(2, DefaultValue = 20)] public int MaxResults { get; set; } = 20;
    [ProtoField(3)] public double MinScore { get; set; } = 0.0;
}

public sealed partial class FindSimilarResponse : IProtoSerializable
{
    [ProtoField(1)] public List<SemanticSearchResultWire> Results { get; } = [];
}

// Empty request - keep manual
public sealed class RecomputeEmbeddingsRequest : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}

public sealed partial class RecomputeEmbeddingsResponse : IProtoSerializable
{
    [ProtoField(1)] public int TotalProcessed { get; set; }
}

// ═══════════════════════════════════════════════════════════════
//  Smart Search Ranking — Click Logging & Re-Ranking
// ═══════════════════════════════════════════════════════════════

public sealed partial class LogSearchClickRequest : IProtoSerializable
{
    [ProtoField(1)] public string AssetId { get; set; } = string.Empty;
    [ProtoField(2)] public string QueryText { get; set; } = string.Empty;
    [ProtoField(3)] public int RankPosition { get; set; }
    [ProtoField(4)] public int DwellTimeMs { get; set; }
}

public sealed partial class LogSearchClickResponse : IProtoSerializable
{
    [ProtoField(1)] public string Id { get; set; } = string.Empty;
}

public sealed partial class ReRankRequest : IProtoSerializable
{
    [ProtoField(1)] public string Query { get; set; } = string.Empty;
    [ProtoField(2)] public List<RankedAssetWire> Results { get; } = [];
}

public sealed partial class RankedAssetWire : IProtoSerializable
{
    [ProtoField(1)] public string AssetId { get; set; } = string.Empty;
    [ProtoField(2)] public float OriginalScore { get; set; }
}

public sealed partial class ReRankResponse : IProtoSerializable
{
    [ProtoField(1)] public List<RankedResultWire> Results { get; } = [];
}

public sealed partial class RankedResultWire : IProtoSerializable
{
    [ProtoField(1)] public string AssetId { get; set; } = string.Empty;
    [ProtoField(2)] public float CombinedScore { get; set; }
    [ProtoField(3)] public float ClickBoost { get; set; }
}
