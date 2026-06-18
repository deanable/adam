using Adam.Shared.Data;
using Adam.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Adam.Shared.Services;

/// <summary>
/// Detects near-duplicate and similar images by comparing 4096-dim image embeddings
/// via cosine similarity with LSH-bucketed acceleration for bulk scans.
/// </summary>
public sealed class NearDuplicateService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<NearDuplicateService> _logger;

    /// <summary>Similarity threshold for near-identical images (default 0.92).</summary>
    public double NearDuplicateThreshold { get; set; } = 0.92;

    /// <summary>Similarity threshold for edited/similar images (default 0.85).</summary>
    public double SimilarThreshold { get; set; } = 0.85;

    // LSH parameters
    private const int LshBits = 32;        // 32-bit signature
    private const int LshBuckets = 3;       // consider 3 adjacent buckets

    public NearDuplicateService(
        IDbContextFactory<AppDbContext> dbFactory,
        ILogger<NearDuplicateService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    /// <summary>
    /// Finds near-duplicate groups for a specific asset by comparing its
    /// image embedding against all other image embeddings in the catalog.
    /// </summary>
    public async Task<IReadOnlyList<DuplicateGroup>> FindForAssetAsync(
        Guid assetId,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var sourceEmbedding = await db.AssetEmbeddings
            .Include(e => e.Asset)
            .FirstOrDefaultAsync(e => e.AssetId == assetId, ct);

        if (sourceEmbedding?.ImageEmbedding == null || sourceEmbedding.ImageEmbedding.Length == 0)
            return [];

        var sourceVec = EmbeddingService.BytesToFloats(sourceEmbedding.ImageEmbedding);

        var candidates = await db.AssetEmbeddings
            .Include(e => e.Asset)
            .Where(e => e.AssetId != assetId
                        && e.ImageEmbedding != null)
            .AsNoTracking()
            .ToListAsync(ct);

        if (candidates.Count == 0)
            return [];

        var similar = new List<AssetClusterItem>();

        foreach (var cand in candidates)
        {
            try
            {
                var vec = EmbeddingService.BytesToFloats(cand.ImageEmbedding!);
                var score = CosineSimilarity(sourceVec, vec);

                if (score >= SimilarThreshold)
                {
                    similar.Add(new AssetClusterItem
                    {
                        AssetId = cand.AssetId,
                        FileName = cand.Asset.FileName,
                        SimilarityScore = score,
                        FileSize = cand.Asset.FileSize,
                        Width = cand.Asset.Width,
                        Height = cand.Asset.Height
                    });
                }
            }
            catch
            {
                // Skip corrupt embeddings
            }
        }

        if (similar.Count == 0)
            return [];

        // Sort by similarity descending, take best match as primary
        similar = similar.OrderByDescending(s => s.SimilarityScore).ToList();
        var best = similar.First();

        var group = new DuplicateGroup
        {
            GroupId = Guid.NewGuid(),
            Primary = best,
            Duplicates = similar.Skip(1).ToList(),
            MaxScore = best.SimilarityScore,
            GroupType = ClassifyGroupType(best.SimilarityScore)
        };

        return [group];
    }

    /// <summary>
    /// Scans all image assets and groups near-duplicates using LSH-bucketed comparison.
    /// Reports progress via optional IProgress callback.
    /// </summary>
    public async Task<IReadOnlyList<DuplicateGroup>> ScanAllAsync(
        IProgress<(int completed, int total)>? progress = null,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var allEmbeddings = await db.AssetEmbeddings
            .Include(e => e.Asset)
            .Where(e => e.ImageEmbedding != null)
            .AsNoTracking()
            .ToListAsync(ct);

        if (allEmbeddings.Count < 2)
            return [];

        // Build embedding vectors and LSH signatures
        var items = new List<(AssetEmbedding emb, float[] vec, uint lsh)>();
        foreach (var emb in allEmbeddings)
        {
            try
            {
                var vec = EmbeddingService.BytesToFloats(emb.ImageEmbedding!);
                var lsh = ComputeLshSignature(vec);
                items.Add((emb, vec, lsh));
            }
            catch
            {
                // Skip corrupt embeddings
            }
        }

        // Group by LSH bucket (only compare within bucket to avoid O(n²))
        var buckets = items.GroupBy(i => i.lsh)
            .ToDictionary(g => g.Key, g => g.ToList());

        var groups = new List<DuplicateGroup>();
        var visited = new HashSet<Guid>();
        var total = items.Count;
        var completed = 0;

        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();

            if (visited.Contains(item.emb.AssetId))
            {
                completed++;
                progress?.Report((completed, total));
                continue;
            }

            // Check this bucket and adjacent buckets
            var bucketKeys = new HashSet<uint> { item.lsh };
            for (int offset = 1; offset <= LshBuckets; offset++)
            {
                bucketKeys.Add(item.lsh + (uint)offset);
                if ((uint)offset <= item.lsh)
                    bucketKeys.Add(item.lsh - (uint)offset);
            }

            var candidates = bucketKeys
                .Where(bk => buckets.ContainsKey(bk))
                .SelectMany(bk => buckets[bk])
                .Where(c => c.emb.AssetId != item.emb.AssetId && !visited.Contains(c.emb.AssetId))
            .ToList();

            // Compare against candidates
            var matches = new List<(AssetEmbedding emb, float[] vec, float score)>();

            foreach (var candidate in candidates)
            {
                var score = CosineSimilarity(item.vec, candidate.vec);
                if (score >= SimilarThreshold)
                {
                    matches.Add((candidate.emb, candidate.vec, score));
                    visited.Add(candidate.emb.AssetId);
                }
            }

            if (matches.Count > 0)
            {
                visited.Add(item.emb.AssetId);

                // Build group: best match is primary, rest are duplicates
                var allMatches = matches.OrderByDescending(m => m.score).ToList();
                var best = allMatches.First();

                var group = new DuplicateGroup
                {
                    GroupId = Guid.NewGuid(),
                    Primary = new AssetClusterItem
                    {
                        AssetId = best.emb.AssetId,
                        FileName = best.emb.Asset.FileName,
                        SimilarityScore = best.score,
                        FileSize = best.emb.Asset.FileSize,
                        Width = best.emb.Asset.Width,
                        Height = best.emb.Asset.Height
                    },
                    Duplicates = allMatches.Skip(1).Select(m => new AssetClusterItem
                    {
                        AssetId = m.emb.AssetId,
                        FileName = m.emb.Asset.FileName,
                        SimilarityScore = m.score,
                        FileSize = m.emb.Asset.FileSize,
                        Width = m.emb.Asset.Width,
                        Height = m.emb.Asset.Height
                    }).ToList(),
                    MaxScore = best.score,
                    GroupType = ClassifyGroupType(best.score)
                };

                groups.Add(group);
            }

            completed++;
            progress?.Report((completed, total));
        }

        _logger.LogInformation(
            "Near-duplicate scan complete: {Groups} groups found from {Total} assets",
            groups.Count, total);

        return groups;
    }

    /// <summary>
    /// Returns statistics about duplicate density in the catalog.
    /// </summary>
    public async Task<DuplicateStats> GetStatsAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var totalAssets = await db.AssetEmbeddings
            .CountAsync(e => e.ImageEmbedding != null, ct);

        // Run a full scan to get accurate stats
        var groups = await ScanAllAsync(progress: null, ct);

        var assetsWithDups = groups.Sum(g => 1 + g.Duplicates.Count);
        var potentialSavings = groups.Sum(g => g.Duplicates.Sum(d => d.FileSize));

        return new DuplicateStats
        {
            TotalAssets = totalAssets,
            AssetsWithDuplicates = assetsWithDups,
            DuplicateGroups = groups.Count,
            PotentialSavingsBytes = potentialSavings
        };
    }

    /// <summary>
    /// Computes a 32-bit LSH signature from a float embedding vector
    /// using random projections. Used for bucketing to avoid O(n²) comparisons.
    /// </summary>
    private static uint ComputeLshSignature(float[] vec)
    {
        // Simple random projection LSH: sign of dot product with random vectors
        // We use a deterministic hash based on the vector itself
        uint hash = 0;
        for (int i = 0; i < LshBits && i < vec.Length; i++)
        {
            if (vec[i] > 0)
                hash |= (1u << (i % 32));
        }
        return hash;
    }

    /// <summary>
    /// Classifies a duplicate group type based on similarity score.
    /// </summary>
    private string ClassifyGroupType(float score)
    {
        if (score >= 0.95)
            return "Near-identical";
        if (score >= NearDuplicateThreshold)
            return "Edited version";
        return "Similar";
    }

    /// <summary>
    /// Cosine similarity between two vectors using SIMD acceleration.
    /// </summary>
    private static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
        => SemanticSearchService.CosineSimilarity(a, b);
}

/// <summary>
/// A group of near-duplicate or similar assets.
/// </summary>
public sealed record DuplicateGroup
{
    public Guid GroupId { get; init; }
    public AssetClusterItem Primary { get; init; } = null!;
    public IReadOnlyList<AssetClusterItem> Duplicates { get; init; } = [];
    public float MaxScore { get; init; }
    public string GroupType { get; init; } = string.Empty;
}

/// <summary>
/// Statistics about duplicate density in the catalog.
/// </summary>
public sealed record DuplicateStats
{
    public int TotalAssets { get; init; }
    public int AssetsWithDuplicates { get; init; }
    public int DuplicateGroups { get; init; }
    public long PotentialSavingsBytes { get; init; }
}
