using Adam.Shared.Data;
using Adam.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Adam.Shared.Services;

/// <summary>
/// Orchestrates semantic and visual similarity search by computing embeddings
/// and ranking assets by cosine similarity against stored embedding vectors.
/// </summary>
public sealed class SemanticSearchService
{
    private const int EmbeddingDimension = 384;
    private const int EmbeddingByteSize = EmbeddingDimension * 4; // float32
    private readonly EmbeddingService _embeddings;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<SemanticSearchService> _logger;

    public SemanticSearchService(
        EmbeddingService embeddings,
        IDbContextFactory<AppDbContext> dbFactory,
        ILogger<SemanticSearchService> logger)
    {
        _embeddings = embeddings;
        _dbFactory = dbFactory;
        _logger = logger;
    }

    /// <summary>
    /// Searches assets by natural language text query using cosine similarity
    /// against stored text embeddings.
    /// </summary>
    public async Task<IReadOnlyList<SemanticSearchResult>> SearchByTextAsync(
        string query,
        int maxResults = 50,
        double minScore = 0.0,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var queryVec = await _embeddings.GetTextEmbeddingAsync(query, ct);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var embeddings = await db.AssetEmbeddings
            .Include(e => e.Asset)
            .ThenInclude(a => a.Keywords)
            .AsNoTracking()
            .ToListAsync(ct);

        if (embeddings.Count == 0)
            return [];

        var results = new List<(AssetEmbedding embedding, float score, float[] vec)>();

        foreach (var emb in embeddings)
        {
            if (emb.TextEmbedding.Length != EmbeddingDimension * 4)
                continue; // malformed or empty

            try
            {
                var vec = EmbeddingService.BytesToFloats(emb.TextEmbedding);
                if (vec.Length != EmbeddingDimension)
                    continue;

                var score = CosineSimilarity(queryVec, vec);
                if (score >= minScore)
                    results.Add((emb, score, vec));
            }
            catch
            {
                // Skip corrupt embeddings
            }
        }

        return results
            .OrderByDescending(r => r.score)
            .Take(maxResults)
            .Select((r, i) => new SemanticSearchResult
            {
                Asset = r.embedding.Asset,
                Score = r.score,
                Rank = i + 1
            })
            .ToList();
    }

    /// <summary>
    /// Finds visually similar assets by comparing image embeddings.
    /// Only returns image assets with stored image embeddings.
    /// </summary>
    public async Task<IReadOnlyList<SemanticSearchResult>> FindSimilarAsync(
        Guid assetId,
        int maxResults = 20,
        double minScore = 0.0,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var sourceEmbedding = await db.AssetEmbeddings
            .Include(e => e.Asset)
            .FirstOrDefaultAsync(e => e.AssetId == assetId, ct);

        if (sourceEmbedding?.ImageEmbedding == null || sourceEmbedding.ImageEmbedding.Length == 0)
        {
            // Fall back to text embedding comparison
            if (sourceEmbedding?.TextEmbedding.Length > 0)
                return await FindSimilarByTextAsync(db, sourceEmbedding, maxResults, minScore, ct);

            return [];
        }

        var sourceVec = EmbeddingService.BytesToFloats(sourceEmbedding.ImageEmbedding);
        var dimension = sourceVec.Length; // 4096 for image embeddings

        var candidates = await db.AssetEmbeddings
            .Include(e => e.Asset)
            .Where(e => e.AssetId != assetId && e.ImageEmbedding != null && e.ImageEmbedding.Length > 0)
            .AsNoTracking()
            .ToListAsync(ct);

        if (candidates.Count == 0)
            return [];

        var results = new List<(AssetEmbedding emb, float score)>();

        foreach (var cand in candidates)
        {
            try
            {
                var vec = EmbeddingService.BytesToFloats(cand.ImageEmbedding!);
                if (vec.Length != dimension) continue;
                var score = CosineSimilarity(sourceVec, vec);
                if (score >= minScore)
                    results.Add((cand, score));
            }
            catch
            {
                // Skip corrupt embeddings
            }
        }

        return results
            .OrderByDescending(r => r.score)
            .Take(maxResults)
            .Select((r, i) => new SemanticSearchResult
            {
                Asset = r.emb.Asset,
                Score = r.score,
                Rank = i + 1
            })
            .ToList();
    }

    /// <summary>
    /// Fallback for FindSimilarAsync when no image embedding exists: compares text embeddings.
    /// </summary>
    private async Task<IReadOnlyList<SemanticSearchResult>> FindSimilarByTextAsync(
        AppDbContext db,
        AssetEmbedding source,
        int maxResults,
        double minScore,
        CancellationToken ct)
    {
        var sourceVec = EmbeddingService.BytesToFloats(source.TextEmbedding);

        var candidates = await db.AssetEmbeddings
            .Include(e => e.Asset)
            .Where(e => e.AssetId != source.AssetId && e.TextEmbedding.Length > 0)
            .AsNoTracking()
            .ToListAsync(ct);

        var results = new List<(AssetEmbedding emb, float score)>();

        foreach (var cand in candidates)
        {
            try
            {
                var vec = EmbeddingService.BytesToFloats(cand.TextEmbedding);
                var score = CosineSimilarity(sourceVec, vec);
                if (score >= minScore)
                    results.Add((cand, score));
            }
            catch { }
        }

        return results
            .OrderByDescending(r => r.score)
            .Take(maxResults)
            .Select((r, i) => new SemanticSearchResult
            {
                Asset = r.emb.Asset,
                Score = r.score,
                Rank = i + 1
            })
            .ToList();
    }

    /// <summary>
    /// Computes cosine similarity between two vectors.
    /// </summary>
    public static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length)
            return 0f;

        float dot = 0, normA = 0, normB = 0;

        // Use Vector<T> for SIMD acceleration if available
        int i = 0;
#if NET8_0_OR_GREATER
        if (System.Numerics.Vector.IsHardwareAccelerated && a.Length >= System.Numerics.Vector<float>.Count)
        {
            var vecA = System.Numerics.Vector<float>.Zero;
            var vecB = System.Numerics.Vector<float>.Zero;
            var vecSize = System.Numerics.Vector<float>.Count;

            for (; i <= a.Length - vecSize; i += vecSize)
            {
                var va = new System.Numerics.Vector<float>(a.Slice(i));
                var vb = new System.Numerics.Vector<float>(b.Slice(i));
                vecA += va * va;
                vecB += vb * vb;
                dot += System.Numerics.Vector.Dot(va, vb);
            }

            normA = System.Numerics.Vector.Sum(vecA);
            normB = System.Numerics.Vector.Sum(vecB);
        }
#endif

        for (; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denom = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denom > 0 ? dot / denom : 0f;
    }

    /// <summary>
    /// Returns the count of assets that have embeddings computed.
    /// </summary>
    public async Task<int> GetEmbeddedCountAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.AssetEmbeddings.CountAsync(ct);
    }

    /// <summary>
    /// Returns the count of assets that still need embeddings.
    /// </summary>
    public async Task<int> GetPendingCountAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.DigitalAssets
            .Where(a => !db.AssetEmbeddings.Any(e => e.AssetId == a.Id))
            .CountAsync(ct);
    }
}

/// <summary>
/// Result of a semantic or visual similarity search.
/// </summary>
public sealed class SemanticSearchResult
{
    public DigitalAsset Asset { get; set; } = null!;
    public float Score { get; set; }
    public int Rank { get; set; }
}
