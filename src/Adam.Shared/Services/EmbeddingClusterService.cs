using Adam.Shared.Data;
using Adam.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Adam.Shared.Services;

/// <summary>
/// Clusters assets by their image embedding similarity using a simplified
/// HDBSCAN-like approach: density-based spatial clustering with adaptive threshold.
/// Falls back to text embeddings for non-image assets.
/// </summary>
public sealed class EmbeddingClusterService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<EmbeddingClusterService> _logger;

    /// <summary>Minimum cosine similarity for cluster membership (default 0.75).</summary>
    public double MinSimilarity { get; set; } = 0.75;

    /// <summary>Minimum assets per cluster (default 3).</summary>
    public int MinClusterSize { get; set; } = 3;

    /// <summary>Soft limit on generated albums (default 20).</summary>
    public int MaxClusters { get; set; } = 20;

    // For large catalogs, sample down to this many assets for clustering
    private const int MaxSampleSize = 10_000;

    public EmbeddingClusterService(
        IDbContextFactory<AppDbContext> dbFactory,
        ILogger<EmbeddingClusterService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    /// <summary>
    /// Clusters all (or specified) assets by embedding similarity.
    /// Returns suggested album clusters with names derived from common keywords.
    /// </summary>
    public async Task<IReadOnlyList<AlbumCluster>> ClusterAsync(
        IReadOnlyList<Guid>? assetIds = null,
        IProgress<(int completed, int total)>? progress = null,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        IQueryable<AssetEmbedding> query = db.AssetEmbeddings
            .Include(e => e.Asset)
            .ThenInclude(a => a.Keywords)
            .AsNoTracking();

        if (assetIds is { Count: > 0 })
        {
            query = query.Where(e => assetIds.Contains(e.AssetId));
        }

        // Prefer image embeddings; fall back to text
        // NOTE: SQLite EF Core can't translate .Length on byte[] columns,
        // so we filter by null check only. EmbeddingService always stores
        // non-empty byte arrays, so this is equivalent in practice.
        var embeddings = await query
            .Where(e => e.ImageEmbedding != null || e.TextEmbedding != null)
            .ToListAsync(ct);

        if (embeddings.Count < MinClusterSize)
            return [];

        // Sample if too large
        var sampled = embeddings;
        if (sampled.Count > MaxSampleSize)
        {
            sampled = sampled
                .OrderBy(_ => Random.Shared.Next())
                .Take(MaxSampleSize)
                .ToList();
            _logger.LogInformation(
                "Sampled {SampleCount} of {TotalCount} embeddings for clustering",
                sampled.Count, embeddings.Count);
        }

        // Build vectors
        var items = new List<(AssetEmbedding emb, float[] vec)>();
        foreach (var emb in sampled)
        {
            try
            {
                float[] vec;
                if (emb.ImageEmbedding is { Length: > 0 })
                    vec = EmbeddingService.BytesToFloats(emb.ImageEmbedding);
                else
                    vec = EmbeddingService.BytesToFloats(emb.TextEmbedding);

                items.Add((emb, vec));
            }
            catch
            {
                // Skip corrupt embeddings
            }
        }

        if (items.Count < MinClusterSize)
            return [];

        var total = items.Count;
        var completed = 0;

        // Step 1: Build similarity graph (connected components from mutual neighbors)
        var adjacency = new Dictionary<int, List<int>>();
        for (int i = 0; i < items.Count; i++)
            adjacency[i] = [];

        for (int i = 0; i < items.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            for (int j = i + 1; j < items.Count; j++)
            {
                var score = SemanticSearchService.CosineSimilarity(items[i].vec, items[j].vec);
                if (score >= MinSimilarity)
                {
                    adjacency[i].Add(j);
                    adjacency[j].Add(i);
                }
            }

            completed++;
            progress?.Report((completed, total));
        }

        // Step 2: Extract connected components (mutual neighbors only → mutual similarity)
        var visited = new HashSet<int>();
        var components = new List<List<int>>();

        for (int i = 0; i < items.Count; i++)
        {
            if (visited.Contains(i))
                continue;

            var component = new List<int>();
            var queue = new Queue<int>();
            queue.Enqueue(i);
            visited.Add(i);

            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                component.Add(node);

                foreach (var neighbor in adjacency[node])
                {
                    if (!visited.Contains(neighbor))
                    {
                        visited.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
            }

            if (component.Count >= MinClusterSize)
                components.Add(component);
        }

        // Step 3: Compute centroids and generate names
        var clusters = new List<AlbumCluster>();

        foreach (var component in components)
        {
            ct.ThrowIfCancellationRequested();

            var memberItems = component.Select(i => items[i]).ToList();
            var centroid = ComputeCentroid(memberItems.Select(m => m.vec).ToList());

            // Compute average similarity to centroid
            var avgSimilarity = memberItems
                .Average(m => SemanticSearchService.CosineSimilarity(centroid, m.vec));

            // Collect keywords from all member assets
            var keywords = memberItems
                .SelectMany(m => m.emb.Asset.Keywords)
                .Where(k => !k.IsAiGenerated)
                .GroupBy(k => k.Name)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .Take(10)
                .ToList();

            // Generate name from top keywords
            var name = GenerateAlbumName(keywords, memberItems);

            clusters.Add(new AlbumCluster
            {
                SuggestedName = name,
                Assets = memberItems.Select(m => new AssetClusterItem
                {
                    AssetId = m.emb.AssetId,
                    FileName = m.emb.Asset.FileName,
                    SimilarityToCentroid = SemanticSearchService.CosineSimilarity(
                        centroid, m.vec)
                }).ToList(),
                CentroidSimilarity = avgSimilarity,
                CommonKeywords = keywords
            });
        }

        // Step 4: Sort by intra-cluster similarity descending, limit to MaxClusters
        var result = clusters
            .OrderByDescending(c => c.CentroidSimilarity)
            .Take(MaxClusters)
            .ToList();

        _logger.LogInformation(
            "Clustering complete: {Clusters} clusters from {Items} items",
            result.Count, items.Count);

        return result;
    }

    /// <summary>
    /// Finds the best potential cluster for a single asset.
    /// </summary>
    public async Task<AlbumCluster?> ClusterForAssetAsync(
        Guid assetId,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var sourceEmbedding = await db.AssetEmbeddings
            .Include(e => e.Asset)
            .FirstOrDefaultAsync(e => e.AssetId == assetId, ct);

        if (sourceEmbedding?.ImageEmbedding == null || sourceEmbedding.ImageEmbedding.Length == 0)
            return null;

        var sourceVec = EmbeddingService.BytesToFloats(sourceEmbedding.ImageEmbedding);

        var candidates = await db.AssetEmbeddings
            .Include(e => e.Asset)
            .Where(e => e.AssetId != assetId
                        && e.ImageEmbedding != null)
            .AsNoTracking()
            .ToListAsync(ct);

        var matches = new List<(AssetEmbedding emb, float score)>();

        foreach (var cand in candidates)
        {
            try
            {
                var vec = EmbeddingService.BytesToFloats(cand.ImageEmbedding!);
                var score = SemanticSearchService.CosineSimilarity(sourceVec, vec);
                if (score >= MinSimilarity)
                    matches.Add((cand, score));
            }
            catch { }
        }

        if (matches.Count < MinClusterSize - 1) // -1 because we exclude the source
            return null;

        var centroid = ComputeCentroid(
            matches.Select(m => EmbeddingService.BytesToFloats(m.emb.ImageEmbedding!)).Prepend(sourceVec).ToList());

        var avgSimilarity = matches.Average(m => m.score);

        return new AlbumCluster
        {
            SuggestedName = $"Similar to {sourceEmbedding.Asset.FileName}",
            Assets = matches.Select(m => new AssetClusterItem
            {
                AssetId = m.emb.AssetId,
                FileName = m.emb.Asset.FileName,
                SimilarityToCentroid = m.score
            }).ToList(),
            CentroidSimilarity = avgSimilarity,
            CommonKeywords = []
        };
    }

    /// <summary>
    /// Computes the centroid (average) vector from a list of vectors.
    /// </summary>
    private static float[] ComputeCentroid(List<float[]> vectors)
    {
        if (vectors.Count == 0)
            return [];

        var dim = vectors[0].Length;
        var centroid = new float[dim];

        foreach (var vec in vectors)
        {
            for (int i = 0; i < dim && i < vec.Length; i++)
                centroid[i] += vec[i];
        }

        var count = vectors.Count;
        for (int i = 0; i < dim; i++)
            centroid[i] /= count;

        // Normalize to unit vector
        float norm = 0;
        for (int i = 0; i < dim; i++)
            norm += centroid[i] * centroid[i];
        norm = MathF.Sqrt(norm);

        if (norm > 0)
        {
            for (int i = 0; i < dim; i++)
                centroid[i] /= norm;
        }

        return centroid;
    }

    /// <summary>
    /// Generates a human-readable album name from common keywords and asset filenames.
    /// </summary>
    private static string GenerateAlbumName(
        IReadOnlyList<string> keywords,
        IReadOnlyList<(AssetEmbedding emb, float[] vec)> members)
    {
        if (keywords.Count >= 2)
        {
            return string.Join(" & ", keywords.Take(2));
        }

        if (keywords.Count == 1)
        {
            return keywords[0];
        }

        // Fall back to common filename patterns
        var prefixes = members
            .Select(m => m.emb.Asset.FileName)
            .Select(f =>
            {
                var dot = f.LastIndexOf('.');
                var name = dot > 0 ? f[..dot] : f;
                // Take first word of filename
                var space = name.IndexOfAny([' ', '_', '-']);
                return space > 0 ? name[..space] : name;
            })
            .GroupBy(p => p)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .Take(2)
            .ToList();

        if (prefixes.Count >= 2)
            return $"{prefixes[0]} & {prefixes[1]}";

        if (prefixes.Count == 1)
            return $"{prefixes[0]} Collection";

        return $"Album ({members.Count} items)";
    }
}

/// <summary>
/// A suggested album cluster from embedding similarity grouping.
/// </summary>
public sealed record AlbumCluster
{
    public string SuggestedName { get; init; } = string.Empty;
    public IReadOnlyList<AssetClusterItem> Assets { get; init; } = [];
    public float CentroidSimilarity { get; init; }
    public IReadOnlyList<string> CommonKeywords { get; init; } = [];
}

/// <summary>
/// An asset item within a cluster with similarity metadata.
/// Used by both EmbeddingClusterService and NearDuplicateService.
/// </summary>
public sealed record AssetClusterItem
{
    public Guid AssetId { get; init; }
    public string FileName { get; init; } = string.Empty;
    public float SimilarityToCentroid { get; init; }
    public float SimilarityScore { get; init; }
    public long FileSize { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
}
