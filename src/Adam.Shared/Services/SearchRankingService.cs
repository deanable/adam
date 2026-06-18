using Adam.Shared.Data;
using Adam.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Adam.Shared.Services;

/// <summary>
/// Tracks click-through events on search results and re-ranks results
/// using implicit feedback signals (click count, recency, dwell time).
/// </summary>
public sealed class SearchRankingService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<SearchRankingService> _logger;

    // Weight multipliers
    private const double RecencyWeight = 3.0;       // clicks in last 7 days weighted 3×
    private const double DwellWeight = 2.0;           // dwell > 5s weighted 2×
    private const int RecencyDays = 7;
    private const int DwellThresholdMs = 5000;
    private const int PurgeAfterDays = 90;
    private const double ClickAffinityWeight = 0.3;  // 30% of combined score
    private const double CosineWeight = 0.7;          // 70% of combined score

    public SearchRankingService(
        IDbContextFactory<AppDbContext> dbFactory,
        ILogger<SearchRankingService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    /// <summary>
    /// Logs a click event when a user opens an asset from search results.
    /// </summary>
    public async Task<Guid> LogClickAsync(
        Guid assetId,
        string query,
        int rankPosition,
        int dwellTimeMs,
        CancellationToken ct = default)
    {
        var normalized = NormalizeQuery(query);

        var log = new SearchClickLog
        {
            Id = Guid.NewGuid(),
            AssetId = assetId,
            QueryText = query,
            NormalizedQuery = normalized,
            ClickedAt = DateTimeOffset.UtcNow,
            DwellTimeMs = dwellTimeMs,
            RankPosition = rankPosition
        };

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.SearchClickLogs.Add(log);
        await db.SaveChangesAsync(ct);

        _logger.LogDebug(
            "Logged click: asset={AssetId}, query={Query}, rank={Rank}, dwell={Dwell}ms",
            assetId, query, rankPosition, dwellTimeMs);

        return log.Id;
    }

    /// <summary>
    /// Applies ranking boost based on click history affinity.
    /// Score = 0.7 × cosine_similarity + 0.3 × click_affinity.
    /// </summary>
    public async Task<IReadOnlyList<ReRankedResult>> ReRankAsync(
        IReadOnlyList<SemanticSearchResult> results,
        string query,
        CancellationToken ct = default)
    {
        if (results.Count == 0)
            return [];

        var normalized = NormalizeQuery(query);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Get click counts for each asset+query pair
        // Load logs first, then aggregate in memory (SQLite can't translate
        // DateTimeOffset arithmetic inside GroupBy aggregates)
        var assetIds = results.Select(r => r.Asset.Id).ToList();
        var clickLogs = await db.SearchClickLogs
            .Where(l => l.NormalizedQuery == normalized
                        && assetIds.Contains(l.AssetId))
            .ToListAsync(ct);

        var clickData = clickLogs
            .GroupBy(l => l.AssetId)
            .Select(g => new
            {
                AssetId = g.Key,
                TotalClicks = g.Count(),
                RecentClicks = g.Count(l => l.ClickedAt >= DateTimeOffset.UtcNow.AddDays(-RecencyDays)),
                LongDwellClicks = g.Count(l => l.DwellTimeMs >= DwellThresholdMs)
            })
            .ToList();

        var clickMap = clickData.ToDictionary(c => c.AssetId);
        var maxClicks = clickData.Count > 0 ? clickData.Max(c => c.TotalClicks) : 1;

        var ranked = new List<ReRankedResult>();

        foreach (var result in results)
        {
            double clickAffinity = 0;

            if (clickMap.TryGetValue(result.Asset.Id, out var data) && maxClicks > 0)
            {
                // Base click affinity: proportion of max clicks for this query
                clickAffinity = (double)data.TotalClicks / maxClicks;

                // Recency bonus: recent clicks weighted 3×
                if (data.RecentClicks > 0)
                {
                    var recencyRatio = (double)data.RecentClicks / Math.Max(data.TotalClicks, 1);
                    clickAffinity += recencyRatio * (RecencyWeight - 1) * (double)data.TotalClicks / maxClicks;
                }

                // Dwell bonus: long dwells weighted 2×
                if (data.LongDwellClicks > 0)
                {
                    var dwellRatio = (double)data.LongDwellClicks / Math.Max(data.TotalClicks, 1);
                    clickAffinity += dwellRatio * (DwellWeight - 1) * (double)data.TotalClicks / maxClicks;
                }

                // Clamp to reasonable range
                clickAffinity = Math.Min(clickAffinity, 2.0);
            }

            var combinedScore = (float)(CosineWeight * result.Score + ClickAffinityWeight * clickAffinity);

            ranked.Add(new ReRankedResult
            {
                AssetId = result.Asset.Id,
                OriginalScore = result.Score,
                CombinedScore = Math.Clamp(combinedScore, 0f, 1f),
                ClickBoost = (float)clickAffinity,
                Asset = result.Asset,
                Rank = result.Rank
            });
        }

        return ranked
            .OrderByDescending(r => r.CombinedScore)
            .Select((r, i) =>
            {
                r.Rank = i + 1;
                return r;
            })
            .ToList();
    }

    /// <summary>
    /// Purges click logs older than 90 days to prevent unbounded growth.
    /// </summary>
    public async Task<int> PurgeOldLogsAsync(CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-PurgeAfterDays);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Load all logs and filter in memory (SQLite can't translate
        // DateTimeOffset comparisons in LINQ)
        var allLogs = await db.SearchClickLogs.ToListAsync(ct);
        var oldLogs = allLogs.Where(l => l.ClickedAt < cutoff).ToList();

        if (oldLogs.Count == 0)
            return 0;

        db.SearchClickLogs.RemoveRange(oldLogs);
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Purged {Count} old search click logs", oldLogs.Count);
        return oldLogs.Count;
    }

    /// <summary>
    /// Gets the click count for a specific asset+query pair.
    /// </summary>
    public async Task<int> GetClickCountAsync(
        Guid assetId,
        string normalizedQuery,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        return await db.SearchClickLogs
            .CountAsync(l => l.AssetId == assetId
                             && l.NormalizedQuery == normalizedQuery, ct);
    }

    /// <summary>
    /// Normalizes a query string for matching: lowercase, trim, collapse whitespace.
    /// </summary>
    private static string NormalizeQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return string.Empty;

        var trimmed = query.Trim().ToLowerInvariant();

        // Collapse multiple whitespace characters into single space
        var collapsed = System.Text.RegularExpressions.Regex.Replace(trimmed, @"\s+", " ");
        return collapsed;
    }
}

/// <summary>
/// Result of re-ranking with combined score from cosine similarity and click affinity.
/// </summary>
public sealed class ReRankedResult
{
    public Guid AssetId { get; set; }
    public float OriginalScore { get; set; }
    public float CombinedScore { get; set; }
    public float ClickBoost { get; set; }
    public DigitalAsset Asset { get; set; } = null!;
    public int Rank { get; set; }
}
