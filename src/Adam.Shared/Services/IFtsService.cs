using Adam.Shared.Models;

namespace Adam.Shared.Services;

/// <summary>
/// Provider-agnostic full-text search abstraction.
/// Implementations: <c>SqliteFtsService</c> (FTS5), <c>PostgresFtsService</c> (tsvector), <c>SqlServerFtsService</c> (CONTAINS).
/// </summary>
public interface IFtsService
{
    /// <summary>
    /// Performs a full-text search across indexed fields (Title, Description, FileName, Keywords).
    /// Returns results ranked by relevance (best match first).
    /// </summary>
    /// <param name="query">Search query. Supports prefix matching with <c>*</c> and phrase queries with <c>"..."</c>.</param>
    /// <param name="maxResults">Maximum number of results to return. Default 100.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Ranked search results with matched field information.</returns>
    Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int maxResults = 100,
        CancellationToken ct = default);

    /// <summary>
    /// Returns top autocomplete suggestions from indexed fields.
    /// Used for search-as-you-type with 300ms debounce.
    /// </summary>
    /// <param name="prefix">Partial search term to autocomplete.</param>
    /// <param name="maxSuggestions">Maximum suggestions to return. Default 5.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<string>> GetSuggestionsAsync(
        string prefix,
        int maxSuggestions = 5,
        CancellationToken ct = default);

    /// <summary>
    /// Checks whether the FTS index is available and properly populated.
    /// Returns false if FTS5 is not compiled into SQLite or the virtual table doesn't exist.
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken ct = default);

    /// <summary>
    /// Ensures the FTS tables, triggers, and indexes exist and are populated.
    /// Safe to call multiple times. Called during app startup after DB migration.
    /// </summary>
    Task EnsureReadyAsync(CancellationToken ct = default);

    /// <summary>
    /// Rebuilds the FTS index from the source tables.
    /// Called after bulk imports or when the index becomes corrupted.
    /// </summary>
    Task RebuildIndexAsync(CancellationToken ct = default);
}
