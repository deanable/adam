using System.Data.Common;
using System.Text.RegularExpressions;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Adam.Shared.Services;

/// <summary>
/// SQLite FTS5 full-text search implementation.
/// Uses a standalone FTS5 virtual table + integer rowid mapping table to bridge Guid PKs.
/// Triggers on DigitalAssets and AssetKeywords keep the FTS index in sync.
/// Uses bm25() for relevance ranking.
/// </summary>
public sealed class SqliteFtsService : IFtsService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<SqliteFtsService> _logger;

    /// <summary>FTS5 virtual table storing indexed text content.</summary>
    private const string FtsTable = "digital_assets_fts";

    /// <summary>Mapping table: integer FTS rowid ↔ Guid asset Id.</summary>
    private const string MapTable = "digital_assets_fts_map";

    public SqliteFtsService(IDbContextFactory<AppDbContext> dbFactory, ILogger<SqliteFtsService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    /// <summary>
    /// Creates the FTS5 virtual table, rowid mapping table, sync triggers, and populates the index.
    /// Safe to call multiple times (uses IF NOT EXISTS).
    /// </summary>
    public async Task EnsureFtsReadyAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync(ct);

        // RISK-001: Check FTS5 is compiled into SQLite before creating tables
        if (!await IsFts5CompiledAsync(conn, ct))
        {
            _logger.LogWarning("[FTS] FTS5 not compiled into this SQLite build — search will use LIKE fallback");
            return;
        }

        // T11.2: Create rowid mapping table
        await ExecAsync(conn, $@"
            CREATE TABLE IF NOT EXISTS {MapTable} (
                fts_rowid INTEGER PRIMARY KEY AUTOINCREMENT,
                asset_id TEXT NOT NULL UNIQUE
            );", ct);

        // T11.2: Create FTS5 virtual table (standalone — not content-synced)
        await ExecAsync(conn, $@"
            CREATE VIRTUAL TABLE IF NOT EXISTS {FtsTable} USING fts5(
                Title,
                Description,
                FileName,
                Keywords,
                tokenize='porter unicode61'
            );", ct);

        // T11.3: Create sync triggers
        await CreateTriggersAsync(conn, ct);

        // T11.5: Populate FTS index if empty
        await PopulateIfEmptyAsync(conn, ct);

        _logger.LogDebug("[FTS] SQLite FTS5 tables and triggers ensured");
    }

    // ── IFtsService ──────────────────────────────────────────────

    public Task EnsureReadyAsync(CancellationToken ct = default)
        => EnsureFtsReadyAsync(ct);

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query, int maxResults = 100, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        if (!await IsAvailableAsync(ct))
            return [];

        var ftsQuery = BuildFtsQuery(query);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync(ct);

        // 1. FTS5 MATCH + bm25() ranking
        var ranked = new List<(long ftsRowid, double rank, string title, string? desc, string fileName, string? kw)>();

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
                SELECT fts.rowid, bm25({FtsTable}) AS rank,
                       fts.Title, fts.Description, fts.FileName, fts.Keywords
                FROM {FtsTable} fts
                WHERE {FtsTable} MATCH @q
                ORDER BY rank
                LIMIT @lim";

            AddParam(cmd, "@q", ftsQuery);
            AddParam(cmd, "@lim", maxResults);

            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                ranked.Add(
                    (r.GetInt64(0), r.GetDouble(1),
                     r.IsDBNull(2) ? "" : r.GetString(2),
                     r.IsDBNull(3) ? null : r.GetString(3),
                     r.IsDBNull(4) ? "" : r.GetString(4),
                     r.IsDBNull(5) ? null : r.GetString(5)));
        }

        if (ranked.Count == 0)
            return [];

        // 2. Map FTS rowids → Guid asset ids
        var rowidToAssetId = new Dictionary<long, Guid>();
        var rowidList = ranked.Select(r => r.ftsRowid).ToList();

        // Build a parameterized IN clause
        var inClause = string.Join(",", rowidList.Select((_, i) => $"@r{i}"));
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"SELECT fts_rowid, asset_id FROM {MapTable} WHERE fts_rowid IN ({inClause})";
            for (int i = 0; i < rowidList.Count; i++)
                AddParam(cmd, $"@r{i}", rowidList[i]);

            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                rowidToAssetId[r.GetInt64(0)] = Guid.Parse(r.GetString(1));
        }

        // 3. Load full entities via EF Core
        var assetIds = rowidToAssetId.Values.Distinct().ToList();
        var assets = await db.DigitalAssets
            .Include(a => a.Keywords)
            .Include(a => a.Categories)
            .Include(a => a.Collection)
            .Include(a => a.MetadataProfile)
            .Where(a => assetIds.Contains(a.Id))
            .ToListAsync(ct);

        var assetMap = assets.ToDictionary(a => a.Id);

        // 4. Assemble ranked results
        return ranked
            .Where(r => rowidToAssetId.TryGetValue(r.ftsRowid, out var aid) && assetMap.ContainsKey(aid))
            .Select(r =>
            {
                var aid = rowidToAssetId[r.ftsRowid];
                var matched = new List<string>();
                var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (MatchesAny(r.title, terms)) matched.Add("Title");
                if (MatchesAny(r.desc, terms)) matched.Add("Description");
                if (MatchesAny(r.fileName, terms)) matched.Add("FileName");
                if (MatchesAny(r.kw, terms)) matched.Add("Keywords");

                return new SearchResult
                {
                    Asset = assetMap[aid],
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

        var ftsQ = $"\"{EscapeTerm(prefix)}\"*";
        var suggestions = new List<string>();

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
                SELECT DISTINCT fts.Title
                FROM {FtsTable} fts
                WHERE {FtsTable} MATCH @q
                ORDER BY rank
                LIMIT @lim";

            AddParam(cmd, "@q", ftsQ);
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
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@t";
            AddParam(cmd, "@t", FtsTable);

            return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) > 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[FTS] FTS5 availability check failed");
            return false;
        }
    }

    public async Task RebuildIndexAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync(ct);

        await ExecAsync(conn, $"DELETE FROM {FtsTable}", ct);
        await ExecAsync(conn, $"DELETE FROM {MapTable}", ct);
        await PopulateIfEmptyAsync(conn, ct);
        _logger.LogInformation("[FTS] Index rebuilt");
    }

    // ── Trigger creation ─────────────────────────────────────────

    private async Task CreateTriggersAsync(DbConnection conn, CancellationToken ct)
    {
        // ── DigitalAssets INSERT ──
        await ExecAsync(conn, $@"
            CREATE TRIGGER IF NOT EXISTS trg_fts_da_insert
            AFTER INSERT ON DigitalAssets
            BEGIN
                INSERT INTO {MapTable}(asset_id) VALUES (new.Id);
                INSERT INTO {FtsTable}(rowid, Title, Description, FileName, Keywords)
                VALUES (
                    last_insert_rowid(),
                    new.Title,
                    new.Description,
                    new.FileName,
                    COALESCE((
                        SELECT GROUP_CONCAT(k.Name, ' ')
                        FROM AssetKeywords ak
                        JOIN Keywords k ON ak.KeywordsId = k.Id
                        WHERE ak.DigitalAssetsId = new.Id
                    ), '')
                );
            END;", ct);

        // ── DigitalAssets UPDATE ──
        await ExecAsync(conn, $@"
            CREATE TRIGGER IF NOT EXISTS trg_fts_da_update
            AFTER UPDATE ON DigitalAssets
            BEGIN
                DELETE FROM {FtsTable} WHERE rowid = (
                    SELECT fts_rowid FROM {MapTable} WHERE asset_id = old.Id
                );
                INSERT INTO {FtsTable}(rowid, Title, Description, FileName, Keywords)
                VALUES (
                    (SELECT fts_rowid FROM {MapTable} WHERE asset_id = new.Id),
                    new.Title,
                    new.Description,
                    new.FileName,
                    COALESCE((
                        SELECT GROUP_CONCAT(k.Name, ' ')
                        FROM AssetKeywords ak
                        JOIN Keywords k ON ak.KeywordsId = k.Id
                        WHERE ak.DigitalAssetsId = new.Id
                    ), '')
                );
            END;", ct);

        // ── DigitalAssets DELETE ──
        await ExecAsync(conn, $@"
            CREATE TRIGGER IF NOT EXISTS trg_fts_da_delete
            AFTER DELETE ON DigitalAssets
            BEGIN
                DELETE FROM {FtsTable} WHERE rowid = (
                    SELECT fts_rowid FROM {MapTable} WHERE asset_id = old.Id
                );
                DELETE FROM {MapTable} WHERE asset_id = old.Id;
            END;", ct);

        // ── AssetKeywords INSERT (keyword added to asset) ──
        await ExecAsync(conn, $@"
            CREATE TRIGGER IF NOT EXISTS trg_fts_ak_insert
            AFTER INSERT ON AssetKeywords
            BEGIN
                DELETE FROM {FtsTable} WHERE rowid = (
                    SELECT fts_rowid FROM {MapTable} WHERE asset_id = new.DigitalAssetsId
                );
                INSERT INTO {FtsTable}(rowid, Title, Description, FileName, Keywords)
                SELECT
                    (SELECT fts_rowid FROM {MapTable} WHERE asset_id = da.Id),
                    da.Title, da.Description, da.FileName,
                    COALESCE((
                        SELECT GROUP_CONCAT(k.Name, ' ')
                        FROM AssetKeywords ak
                        JOIN Keywords k ON ak.KeywordsId = k.Id
                        WHERE ak.DigitalAssetsId = da.Id
                    ), '')
                FROM DigitalAssets da WHERE da.Id = new.DigitalAssetsId;
            END;", ct);

        // ── AssetKeywords DELETE (keyword removed from asset) ──
        await ExecAsync(conn, $@"
            CREATE TRIGGER IF NOT EXISTS trg_fts_ak_delete
            AFTER DELETE ON AssetKeywords
            BEGIN
                DELETE FROM {FtsTable} WHERE rowid = (
                    SELECT fts_rowid FROM {MapTable} WHERE asset_id = old.DigitalAssetsId
                );
                INSERT INTO {FtsTable}(rowid, Title, Description, FileName, Keywords)
                SELECT
                    (SELECT fts_rowid FROM {MapTable} WHERE asset_id = da.Id),
                    da.Title, da.Description, da.FileName,
                    COALESCE((
                        SELECT GROUP_CONCAT(k.Name, ' ')
                        FROM AssetKeywords ak
                        JOIN Keywords k ON ak.KeywordsId = k.Id
                        WHERE ak.DigitalAssetsId = da.Id
                    ), '')
                FROM DigitalAssets da WHERE da.Id = old.DigitalAssetsId;
            END;", ct);
    }

    // ── Population ───────────────────────────────────────────────

    private async Task PopulateIfEmptyAsync(DbConnection conn, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {MapTable}";
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));

        if (count == 0)
        {
            _logger.LogInformation("[FTS] Populating FTS index from DigitalAssets...");
            await ExecAsync(conn, $@"
                INSERT INTO {MapTable}(asset_id)
                SELECT Id FROM DigitalAssets WHERE Id NOT IN (SELECT asset_id FROM {MapTable});", ct);

            await ExecAsync(conn, $@"
                INSERT INTO {FtsTable}(rowid, Title, Description, FileName, Keywords)
                SELECT
                    m.fts_rowid,
                    da.Title, da.Description, da.FileName,
                    COALESCE((
                        SELECT GROUP_CONCAT(k.Name, ' ')
                        FROM AssetKeywords ak
                        JOIN Keywords k ON ak.KeywordsId = k.Id
                        WHERE ak.DigitalAssetsId = da.Id
                    ), '')
                FROM DigitalAssets da
                JOIN {MapTable} m ON m.asset_id = da.Id;", ct);

            _logger.LogInformation("[FTS] Initial population complete");
        }
    }

    // ── Query helpers ────────────────────────────────────────────

    /// <summary>
    /// Checks if FTS5 is compiled into the SQLite library.
    /// Uses sqlite3_compileoption_used() to detect FTS5 support.
    /// </summary>
    private static async Task<bool> IsFts5CompiledAsync(DbConnection conn, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT sqlite_compileoption_used('ENABLE_FTS5')";
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is long l && l == 1;
    }

    /// <summary>
    /// Converts a user search query into FTS5 syntax.
    /// Each term gets prefix-matching (*) for incremental search.
    /// </summary>
    private static string BuildFtsQuery(string query)
    {
        query = query.Trim();
        if (query.StartsWith('"') && query.EndsWith('"'))
            return query; // already a phrase query

        return string.Join(" ",
            query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => $"{EscapeTerm(t)}*"));
    }

    private static string EscapeTerm(string term)
        => Regex.Replace(term, @"[""*\\\-\+\(\):]", @"\$&");

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
}
