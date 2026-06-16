namespace Adam.Shared.Models;

/// <summary>
/// Stores text and image embeddings for semantic and visual similarity search.
/// One row per asset. TextEmbedding is always computed; ImageEmbedding is optional
/// (only for images with AI tagging enabled).
/// </summary>
public sealed class AssetEmbedding
{
    public Guid Id { get; set; }
    public Guid AssetId { get; set; }

    /// <summary>384-dim float32 vector serialized as byte[1536] (384 × 4 bytes).</summary>
    public byte[] TextEmbedding { get; set; } = [];

    /// <summary>4096-dim float32 vector serialized as byte[16384] (optional, images only).</summary>
    public byte[]? ImageEmbedding { get; set; }

    /// <summary>Model version that produced the embeddings (e.g. "all-MiniLM-L6-v2-v1").</summary>
    public string ModelVersion { get; set; } = string.Empty;

    public DateTimeOffset ComputedAt { get; set; }

    public DigitalAsset Asset { get; set; } = null!;
}
