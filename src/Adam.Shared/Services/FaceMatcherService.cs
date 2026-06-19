using Adam.Shared.Data;
using Adam.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Adam.Shared.Services;

/// <summary>
/// Core face matching logic: cosine similarity comparison against known person centroids,
/// HDBSCAN-style clustering for unknown faces, centroid computation, and confidence-gated
/// auto-assignment.
/// </summary>
public sealed class FaceMatcherService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<FaceMatcherService> _logger;

    /// <summary>Confidence threshold for automatic assignment (default 0.85).</summary>
    public double AutoAssignThreshold { get; set; } = 0.85;

    /// <summary>Confidence threshold for suggesting a match (default 0.70).</summary>
    public double SuggestThreshold { get; set; } = 0.70;

    /// <summary>Minimum faces to form a cluster (default 3).</summary>
    public int MinClusterSize { get; set; } = 3;

    /// <summary>Similarity threshold for cluster membership (default 0.75).</summary>
    public double ClusterSimilarityThreshold { get; set; } = 0.75;

    public FaceMatcherService(
        IDbContextFactory<AppDbContext> dbFactory,
        ILogger<FaceMatcherService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    /// <summary>
    /// Matches a detected face against known persons and returns the best match.
    /// </summary>
    public async Task<FaceMatchResult> MatchAsync(Guid assetFaceId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var face = await db.AssetFaces
            .FirstOrDefaultAsync(f => f.Id == assetFaceId, ct);

        if (face == null || face.FaceEmbedding.Length == 0)
            return new FaceMatchResult
            {
                AssetFaceId = assetFaceId,
                MatchType = FaceMatchType.Unknown,
                Confidence = 0
            };

        var persons = (await db.Persons
            .AsNoTracking()
            .ToListAsync(ct))
            .Where(p => p.CentroidEmbedding != null && p.CentroidEmbedding.Length > 0)
            .ToList();

        if (persons.Count == 0)
            return new FaceMatchResult
            {
                AssetFaceId = assetFaceId,
                MatchType = FaceMatchType.Unknown,
                Confidence = 0
            };

        var faceVec = EmbeddingService.BytesToFloats(face.FaceEmbedding);
        Person? bestMatch = null;
        float bestScore = 0;

        foreach (var person in persons)
        {
            var centroidVec = EmbeddingService.BytesToFloats(person.CentroidEmbedding!);
            var score = CosineSimilarity(faceVec, centroidVec);
            if (score > bestScore)
            {
                bestScore = score;
                bestMatch = person;
            }
        }

        if (bestScore >= AutoAssignThreshold)
        {
            return new FaceMatchResult
            {
                AssetFaceId = assetFaceId,
                MatchedPersonId = bestMatch!.Id,
                MatchedPersonName = bestMatch.Name,
                Confidence = bestScore,
                MatchType = FaceMatchType.AutoAssigned
            };
        }

        if (bestScore >= SuggestThreshold)
        {
            return new FaceMatchResult
            {
                AssetFaceId = assetFaceId,
                MatchedPersonId = bestMatch!.Id,
                MatchedPersonName = bestMatch.Name,
                Confidence = bestScore,
                MatchType = FaceMatchType.Suggested
            };
        }

        return new FaceMatchResult
        {
            AssetFaceId = assetFaceId,
            MatchType = FaceMatchType.Unknown,
            Confidence = bestScore
        };
    }

    /// <summary>
    /// Batch: for all unmatched faces, run matching against known person centroids.
    /// </summary>
    public async Task BatchMatchAsync(
        IProgress<(int completed, int total)>? progress = null,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var unmatchedFaces = await db.AssetFaces
            .Where(f => f.PersonId == null)
            .ToListAsync(ct);

        if (unmatchedFaces.Count == 0)
            return;

        var persons = (await db.Persons
            .AsNoTracking()
            .ToListAsync(ct))
            .Where(p => p.CentroidEmbedding != null && p.CentroidEmbedding.Length > 0)
            .ToList();

        if (persons.Count == 0)
            return;

        var total = unmatchedFaces.Count;
        var completed = 0;

        foreach (var face in unmatchedFaces)
        {
            ct.ThrowIfCancellationRequested();

            if (face.FaceEmbedding.Length == 0)
            {
                completed++;
                progress?.Report((completed, total));
                continue;
            }

            var faceVec = EmbeddingService.BytesToFloats(face.FaceEmbedding);
            Person? bestMatch = null;
            float bestScore = 0;

            foreach (var person in persons)
            {
                var centroidVec = EmbeddingService.BytesToFloats(person.CentroidEmbedding!);
                var score = CosineSimilarity(faceVec, centroidVec);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestMatch = person;
                }
            }

            if (bestScore >= SuggestThreshold && bestMatch != null)
            {
                face.PersonId = bestMatch.Id;
                face.MatchingConfidence = bestScore;
                face.IsAutoAssigned = bestScore >= AutoAssignThreshold;
            }

            completed++;
            progress?.Report((completed, total));
        }

        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "BatchMatchAsync completed: {Completed}/{Total} faces processed",
            completed, total);
    }

    /// <summary>
    /// Clusters unknown faces into candidate person groups using HDBSCAN-style
    /// connected components based on mutual similarity.
    /// </summary>
    public async Task<IReadOnlyList<PersonCluster>> ClusterUnknownFacesAsync(
        IProgress<(int completed, int total)>? progress = null,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var unknownFaces = (await db.AssetFaces
            .Include(f => f.Asset)
            .ThenInclude(a => a.Keywords)
            .Where(f => f.PersonId == null)
            .AsNoTracking()
            .ToListAsync(ct))
            .Where(f => f.FaceEmbedding.Length > 0)
            .ToList();

        if (unknownFaces.Count < MinClusterSize)
            return [];

        // Build vectors
        var items = new List<(AssetFace face, float[] vec)>();
        foreach (var face in unknownFaces)
        {
            try
            {
                var vec = EmbeddingService.BytesToFloats(face.FaceEmbedding);
                items.Add((face, vec));
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

        // Step 1: Build similarity graph
        var adjacency = new Dictionary<int, List<int>>();
        for (int i = 0; i < items.Count; i++)
            adjacency[i] = [];

        for (int i = 0; i < items.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            for (int j = i + 1; j < items.Count; j++)
            {
                var score = CosineSimilarity(items[i].vec, items[j].vec);
                if (score >= ClusterSimilarityThreshold)
                {
                    adjacency[i].Add(j);
                    adjacency[j].Add(i);
                }
            }
            completed++;
            progress?.Report((completed, total));
        }

        // Step 2: Extract connected components
        var visited = new HashSet<int>();
        var components = new List<List<int>>();

        for (int i = 0; i < items.Count; i++)
        {
            if (visited.Contains(i)) continue;

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

        // Step 3: Build clusters
        var clusters = new List<PersonCluster>();

        foreach (var component in components)
        {
            ct.ThrowIfCancellationRequested();
            var memberItems = component.Select(i => items[i]).ToList();

            var centroid = ComputeCentroid(memberItems.Select(m => m.vec).ToList());

            var avgConfidence = memberItems.Average(m => CosineSimilarity(centroid, m.vec));

            var keywords = memberItems
                .SelectMany(m => m.face.Asset.Keywords)
                .Where(k => !k.IsAiGenerated)
                .GroupBy(k => k.Name)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .Take(5)
                .ToList();

            var suggestedName = keywords.Count > 0
                ? string.Join(" & ", keywords.Take(2))
                : $"Cluster ({memberItems.Count} faces)";

            clusters.Add(new PersonCluster
            {
                SuggestedName = suggestedName,
                FaceCount = memberItems.Count,
                CentroidEmbedding = FloatsToBytes(centroid),
                AssetFaceIds = memberItems.Select(m => m.face.Id).ToList(),
                AvgConfidence = avgConfidence,
                CommonAssetKeywords = keywords
            });
        }

        _logger.LogInformation(
            "Face clustering complete: {Clusters} clusters from {Items} faces",
            clusters.Count, items.Count);

        return clusters;
    }

    /// <summary>
    /// Computes the centroid embedding for a person from all their known faces.
    /// </summary>
    public async Task<byte[]> ComputeCentroidAsync(Guid personId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var faces = (await db.AssetFaces
            .Where(f => f.PersonId == personId)
            .AsNoTracking()
            .ToListAsync(ct))
            .Where(f => f.FaceEmbedding.Length > 0)
            .ToList();

        if (faces.Count == 0)
            return [];

        var vectors = new List<float[]>();
        foreach (var face in faces)
        {
            try
            {
                vectors.Add(EmbeddingService.BytesToFloats(face.FaceEmbedding));
            }
            catch { }
        }

        if (vectors.Count == 0)
            return [];

        var centroid = ComputeCentroid(vectors);
        return FloatsToBytes(centroid);
    }

    /// <summary>
    /// Cosine similarity between two face embeddings.
    /// </summary>
    public static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
        => SemanticSearchService.CosineSimilarity(a, b);

    /// <summary>
    /// Computes the centroid (average) vector from a list of vectors, normalized to unit length.
    /// </summary>
    private static float[] ComputeCentroid(List<float[]> vectors)
    {
        if (vectors.Count == 0) return [];

        var dim = vectors[0].Length;
        var centroid = new float[dim];

        for (int i = 0; i < dim; i++)
        {
            double sum = 0;
            for (int j = 0; j < vectors.Count; j++)
                sum += vectors[j][i];
            centroid[i] = (float)(sum / vectors.Count);
        }

        // Normalize to unit vector
        float norm = 0;
        for (int i = 0; i < dim; i++)
            norm += centroid[i] * centroid[i];
        norm = MathF.Sqrt(norm);

        if (norm > 0)
            for (int i = 0; i < dim; i++)
                centroid[i] /= norm;

        return centroid;
    }

    /// <summary>Converts float array to byte array (float32 serialization).</summary>
    private static byte[] FloatsToBytes(float[] floats)
    {
        var bytes = new byte[floats.Length * 4];
        Buffer.BlockCopy(floats, 0, bytes, 0, bytes.Length);
        return bytes;
    }
}

/// <summary>
/// Result of matching a face against known person centroids.
/// </summary>
public sealed record FaceMatchResult
{
    public Guid AssetFaceId { get; init; }
    public Guid? MatchedPersonId { get; init; }
    public string? MatchedPersonName { get; init; }
    public float Confidence { get; init; }
    public FaceMatchType MatchType { get; init; }
}

/// <summary>Classification of a face match confidence level.</summary>
public enum FaceMatchType { AutoAssigned, Suggested, Unknown }

/// <summary>
/// A suggested person cluster from grouping unknown faces by similarity.
/// </summary>
public sealed record PersonCluster
{
    public string SuggestedName { get; init; } = string.Empty;
    public int FaceCount { get; init; }
    public byte[] CentroidEmbedding { get; init; } = [];
    public IReadOnlyList<Guid> AssetFaceIds { get; init; } = [];
    public float AvgConfidence { get; init; }
    public IReadOnlyList<string> CommonAssetKeywords { get; init; } = [];
}
