using System.Data.Common;
using System.Text.RegularExpressions;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Adam.Shared.Services;

/// <summary>
/// PostgreSQL full-text search implementation using tsvector/tsquery.
/// Adds a generated SearchVector column to DigitalAssets and indexes it with GIN.
/// Uses ts_rank for relevance ranking.
/// </summary>
public sealed class PostgresFtsService : FtsServiceBase, IFtsService
{
    public PostgresFtsService(IDbContextFactory<AppDbContext> dbFactory, ILogger<PostgresFtsService> logger)
        : base(dbFactory, logger)
    {
    }

    public async Task EnsureReadyAsync(CancellationToken ct = default)
    {
        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync(ct);

        // T11.6: Add SearchVector generated column if it doesn't exist
        await ExecAsync(conn, @"
            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_name = 'DigitalAssets' AND column_name = 'SearchVector'
                ) THEN
                    ALTER TABLE ""DigitalAssets""
                    ADD COLUMN ""SearchVector"" tsvector
                    GENERATED ALWAYS AS (
                        setweight(to_tsvector('english', coalesce(""Title"", '')), 'A') ||
                        setweight(to_tsvector('english', coalesce(""Description"", '')), 'B') ||
                        setweight(to_tsvector('english', coalesce(""FileName"", '')), 'C')
                    ) STORED;
                END IF;
            END
            $$;", ct);

        // T11.6: Create GIN index on SearchVector
        await ExecAsync(conn, @"
            CREATE INDEX IF NOT EXISTS IX_DigitalAssets_SearchVector
            ON ""DigitalAssets"" USING GIN(""SearchVector"");", ct);

        // Add Keywords to search vector via trigger (keywords are in a join table)
        await CreateKeywordSyncTriggerAsync(conn, ct);

        Logger.LogDebug("[FTS] PostgreSQL tsvector/tsquery index ensured");
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query, int maxResults = 100, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        if (!await IsAvailableAsync(ct))
            return [];

        var tsQuery = BuildTsQuery(query);

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync(ct);

        // Search with ts_rank ranking
        var ranked = new List<(Guid assetId, double rank, string title, string? desc, string fileName, string? kw)>();

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT da.""Id"",
                       ts_rank(da.""SearchVector"", to_tsquery('english', @q)) AS rank,
                       da.""Title"", da.""Description"", da.""FileName"",
                       COALESCE((
                           SELECT string_agg(k.""Name"", ' ')
                           FROM ""AssetKeywords"" ak
                           JOIN ""Keywords"" k ON ak.""KeywordsId"" = k.""Id""
                           WHERE ak.""DigitalAssetsId"" = da.""Id""
                       ), '') AS Keywords
                FROM ""DigitalAssets"" da
                WHERE da.""SearchVector"" @@ to_tsquery('english', @q)
                  AND da.""IsDeleted"" = false
                ORDER BY rank DESC
                LIMIT @lim";

            AddParam(cmd, "@q", tsQuery);
            AddParam(cmd, "@lim", maxResults);

            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                ranked.Add(
                    (r.GetGuid(0), r.GetDouble(1),
                     r.IsDBNull(2) ? "" : r.GetString(2),
                     r.IsDBNull(3) ? null : r.GetString(3),
                     r.IsDBNull(4) ? "" : r.GetString(4),
                     r.IsDBNull(5) ? null : r.GetString(5)));
        }

        if (ranked.Count == 0)
            return [];

        // Load full entities via EF Core (filter IsDeleted to match raw SQL)
        var assetIds = ranked.Select(r => r.assetId).Distinct().ToList();
        var assets = await db.DigitalAssets
            .Include(a => a.Keywords)
            .Include(a => a.Categories)
            .Include(a => a.Collection)
            .Include(a => a.MetadataProfile)
            .Where(a => assetIds.Contains(a.Id) && !a.IsDeleted)
            .ToListAsync(ct);

        var assetMap = assets.ToDictionary(a => a.Id);
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return ranked
            .Where(r => assetMap.ContainsKey(r.assetId))
            .Select(r =>
            {
                var matched = new List<string>();
                if (MatchesAny(r.title, terms)) matched.Add("Title");
                if (MatchesAny(r.desc, terms)) matched.Add("Description");
                if (MatchesAny(r.fileName, terms)) matched.Add("FileName");
                if (MatchesAny(r.kw, terms)) matched.Add("Keywords");

                return new SearchResult
                {
                    Asset = assetMap[r.assetId],
                    Rank = r.rank,
                    MatchedFields = matched
                };
            })
            .ToList();
    }

    public async Task<IReadOnlyList<string>> GetSuggestionsAsync(
        string prefix, int maxSuggestions = 5, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(prefix) || prefix.Length < 2)
            return [];
        if (!await IsAvailableAsync(ct))
            return [];

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync(ct);

        // Use prefix matching with plainto_tsquery + LIKE for autocomplete
        var suggestions = new List<string>();

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT DISTINCT ""Title""
                FROM ""DigitalAssets""
                WHERE ""Title"" ILIKE @prefix
                  AND ""IsDeleted"" = false
                ORDER BY ""Title""
                LIMIT @lim";

            AddParam(cmd, "@prefix", $"{prefix}%");
            AddParam(cmd, "@lim", maxSuggestions);

            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                if (!r.IsDBNull(0))
                    suggestions.Add(r.GetString(0));
        }

        return suggestions;
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync(ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COUNT(*) FROM information_schema.columns
                WHERE table_name = 'DigitalAssets' AND column_name = 'SearchVector'";

            return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) > 0;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[FTS] PostgreSQL FTS availability check failed");
            return false;
        }
    }

    public async Task RebuildIndexAsync(CancellationToken ct = default)
    {
        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync(ct);

        // Rebuild: drop and recreate the GIN index
        await ExecAsync(conn, @"DROP INDEX IF EXISTS IX_DigitalAssets_SearchVector", ct);
        await ExecAsync(conn, @"
            CREATE INDEX IX_DigitalAssets_SearchVector
            ON ""DigitalAssets"" USING GIN(""SearchVector"")", ct);

        Logger.LogInformation("[FTS] PostgreSQL GIN index rebuilt");
    }

    #region Private Helpers

    private async Task CreateKeywordSyncTriggerAsync(DbConnection conn, CancellationToken ct)
    {
        // Trigger to update SearchVector when keywords change
        await ExecAsync(conn, @"
            CREATE OR REPLACE FUNCTION update_asset_search_vector()
            RETURNS trigger AS $$
            BEGIN
                UPDATE ""DigitalAssets""
                SET ""SearchVector"" = ""SearchVector""
                WHERE ""Id"" = COALESCE(NEW.""DigitalAssetsId"", OLD.""DigitalAssetsId"");
                RETURN NULL;
            END;
            $$ LANGUAGE plpgsql;", ct);

        await ExecAsync(conn, @"
            DROP TRIGGER IF EXISTS trg_ak_search_vector ON ""AssetKeywords"";", ct);

        await ExecAsync(conn, @"
            CREATE TRIGGER trg_ak_search_vector
            AFTER INSERT OR DELETE ON ""AssetKeywords""
            FOR EACH ROW EXECUTE FUNCTION update_asset_search_vector();", ct);
    }

    /// <summary>
    /// Converts user query to PostgreSQL tsquery syntax.
    /// Uses &amp; (AND) between terms and :* for prefix matching.
    /// </summary>
    private static string BuildTsQuery(string query)
    {
        query = query.Trim();

        // If already tsquery syntax, use as-is
        if (query.Contains('&') || query.Contains('|') || query.Contains(':'))
            return query;

        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => EscapeTsTerm(t) + ":*")
            .ToList();

        return string.Join(" & ", terms);
    }

    private static string EscapeTsTerm(string term)
        => Regex.Replace(term, @"['&|!():*]", @"\$&");

    #endregion
}
