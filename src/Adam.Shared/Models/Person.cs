namespace Adam.Shared.Models;

/// <summary>
/// Represents a known person identified through facial recognition.
/// Stores the averaged centroid embedding and a representative face thumbnail.
/// </summary>
public sealed class Person
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Notes { get; set; }

    /// <summary>64×64 JPEG thumbnail of the representative face for UI display.</summary>
    public byte[]? ThumbnailImage { get; set; }

    /// <summary>512-dim float32 centroid vector serialized as byte[2048] (512 × 4 bytes).</summary>
    public byte[]? CentroidEmbedding { get; set; }

    /// <summary>Model version that produced this centroid (e.g. "arcface-onnx-v1").</summary>
    public string? EmbeddingModelVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }

    public ICollection<AssetFace> Faces { get; set; } = [];
}
