using System.Data.Common;
using System.Text.RegularExpressions;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Adam.Shared.Services;

/// <summary>
/// SQL Server full-text search implementation using CONTAINS.
/// Creates a full-text catalog and index on DigitalAssets.
/// Uses CONTAINSTABLE for ranked results.
/// </summary>
public sealed class SqlServerFtsService : IFtsService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<SqlServerFtsService> _logger;

    private const string FtCatalog = "AdamFtCatalog";

    public SqlServerFtsService(IDbContextFactory<AppDbContext> dbFactory, ILogger<SqlServerFtsService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task EnsureReadyAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync(ct);

        // T11.7: Create full-text catalog if it doesn't exist
        await ExecAsync(conn, $@"
            IF NOT EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = '{FtCatalog}')
                CREATE FULLTEXT CATALOG {FtCatalog} AS DEFAULT;", ct);

        // T11.7: Create full-text index on DigitalAssets
        // Include Title, Description, FileName for search
        // Keywords are searched via a separate approach (subquery on AssetKeywords)
        await ExecAsync(conn, $@"
            IF NOT EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('DigitalAssets'))
                CREATE FULLTEXT INDEX ON DigitalAssets(
                    Title LANGUAGE English,
                    Description LANGUAGE English,
                    FileName LANGUAGE English
                )
                KEY INDEX PK_DigitalAssets
                ON {FtCatalog}
                WITH CHANGE_TRACKING AUTO;", ct);

        _logger.LogDebug("[FTS] SQL Server full-text index ensured");
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query, int maxResults = 100, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        if (!await IsAvailableAsync(ct))
            return [];

        var containsQuery = BuildContainsQuery(query);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync(ct);

        // Use CONTAINSTABLE for ranked results
        var ranked = new List<(Guid assetId, double rank, string title, string? desc, string fileName, string? kw)>();

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT da.Id,
                       ft.RANK AS rank,
                       da.Title, da.Description, da.FileName,
                       COALESCE((
                           SELECT STRING_AGG(k.Name, ' ')
                           FROM AssetKeywords ak
                           JOIN Keywords k ON ak.KeywordsId = k.Id
                           WHERE ak.DigitalAssetsId = da.Id
                       ), '') AS Keywords
                FROM DigitalAssets da
                INNER JOIN CONTAINSTABLE(DigitalAssets, (Title, Description, FileName), @q, LANGUAGE 'English', TOP @lim) ft
                    ON da.Id = ft.[KEY]
                WHERE da.IsDeleted = 0
                ORDER BY ft.RANK DESC";

            AddParam(cmd, "@q", containsQuery);
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

        // Also search keywords via LIKE since they're in a join table
        var keywordMatches = await SearchKeywordsAsync(conn, query, ct);

        // Merge keyword matches into results
        var assetIdToRank = ranked.ToDictionary(r => r.assetId, r => r);
        foreach (var km in keywordMatches)
        {
            if (!assetIdToRank.ContainsKey(km))
            {
                ranked.Add((km, 0.0, "", null, "", null));
            }
        }

        // Load full entities via EF Core
        var allAssetIds = ranked.Select(r => r.assetId).Distinct().ToList();
        var assets = await db.DigitalAssets
            .Include(a => a.Keywords)
            .Include(a => a.Categories)
            .Include(a => a.Collection)
            .Include(a => a.MetadataProfile)
            .Where(a => allAssetIds.Contains(a.Id) && !a.IsDeleted)
            .ToListAsync(ct);

        var assetMap = assets.ToDictionary(a => a.Id);
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return ranked
            .Where(r => assetMap.ContainsKey(r.assetId))
            .GroupBy(r => r.assetId)
            .Select(g => g.First())
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

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync(ct);

        var suggestions = new List<string>();

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT DISTINCT TOP (@lim) Title
                FROM DigitalAssets
                WHERE Title LIKE @prefix AND IsDeleted = 0
                ORDER BY Title";

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
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var conn = db.Database.GetDbConnection();
            await conn.OpenAsync(ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COUNT(*) FROM sys.fulltext_indexes
                WHERE object_id = OBJECT_ID('DigitalAssets')";

            return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) > 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[FTS] SQL Server full-text availability check failed");
            return false;
        }
    }

    public async Task RebuildIndexAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync(ct);

        await ExecAsync(conn, $@"
            ALTER FULLTEXT INDEX ON DigitalAssets
            START UPDATE POPULATION", ct);

        _logger.LogInformation("[FTS] SQL Server full-text index rebuild started");
    }

    #region Private Helpers

    private async Task<List<Guid>> SearchKeywordsAsync(DbConnection conn, string query, CancellationToken ct)
    {
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length == 0) return [];

        // Batch all terms into a single query with OR conditions
        var conditions = terms.Select((_, i) => $"k.Name LIKE @p{i}");
        var sql = $@"
            SELECT DISTINCT ak.DigitalAssetsId
            FROM AssetKeywords ak
            JOIN Keywords k ON ak.KeywordsId = k.Id
            WHERE ({string.Join(" OR ", conditions)})
              AND EXISTS (SELECT 1 FROM DigitalAssets da WHERE da.Id = ak.DigitalAssetsId AND da.IsDeleted = 0)";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        for (int i = 0; i < terms.Length; i++)
            AddParam(cmd, $"@p{i}", $"%{terms[i]}%");

        var results = new List<Guid>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            if (!r.IsDBNull(0))
                results.Add(r.GetGuid(0));

        return results;
    }

    /// <summary>
    /// Converts user query to SQL Server CONTAINS syntax.
    /// Prefix matching with * and phrase queries with "".
    /// </summary>
    private static string BuildContainsQuery(string query)
    {
        query = query.Trim();

        // If already contains syntax, use as-is
        if (query.Contains('"'))
            return query;

        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => $"\"{EscapeContainsTerm(t)}*\"")
            .ToList();

        return string.Join(" AND ", terms);
    }

    private static string EscapeContainsTerm(string term)
        => Regex.Replace(term, @"[""\\]", @"$&");

    private static bool MatchesAny(string? text, string[] terms)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var lower = text.ToLowerInvariant();
        return terms.Any(t => lower.Contains(t.ToLowerInvariant()));
    }

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    private static async Task ExecAsync(DbConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    #endregion
}
