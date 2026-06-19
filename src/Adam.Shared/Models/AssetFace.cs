namespace Adam.Shared.Models;

/// <summary>
/// Represents a single detected face within a digital asset.
/// Stores the 512-dim ArcFace embedding, bounding box, matching metadata, and thumbnail.
/// </summary>
public sealed class AssetFace
{
    public Guid Id { get; set; }
    public Guid AssetId { get; set; }
    public Guid? PersonId { get; set; }

    /// <summary>512-dim float32 embedding serialized as byte[2048] (512 × 4 bytes).</summary>
    public byte[] FaceEmbedding { get; set; } = [];

    /// <summary>Bounding box and landmarks as JSON: {"x":100,"y":50,"w":80,"h":80,"landmarks":[{...}]}</summary>
    public string BoundingBoxJson { get; set; } = "{}";

    /// <summary>Detection confidence from YuNet (0.0–1.0).</summary>
    public float DetectionConfidence { get; set; }

    /// <summary>Matching confidence against known person centroid (0.0–1.0).</summary>
    public float MatchingConfidence { get; set; }

    /// <summary>True when auto-assigned (>0.85), false when suggested or unknown.</summary>
    public bool IsAutoAssigned { get; set; }

    /// <summary>64×64 JPEG thumbnail of the aligned face for UI display.</summary>
    public byte[]? ThumbnailImage { get; set; }

    public DigitalAsset Asset { get; set; } = null!;
    public Person? Person { get; set; }
}
