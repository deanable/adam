using Adam.Shared.Models;

namespace Adam.Shared.Services;

/// <summary>
/// Represents a single full-text search result with relevance ranking.
/// </summary>
public sealed class SearchResult
{
    /// <summary>
    /// The matched digital asset (includes all navigation properties needed by callers).
    /// </summary>
    public required DigitalAsset Asset { get; init; }

    /// <summary>
    /// BM25 relevance score (lower is more relevant when using SQLite FTS5 bm25()).
    /// </summary>
    public double Rank { get; init; }

    /// <summary>
    /// Which fields matched the query (e.g., "Title", "Description", "Keywords").
    /// Used for highlighting and search-result display.
    /// </summary>
    public IReadOnlyList<string> MatchedFields { get; init; } = [];
}
