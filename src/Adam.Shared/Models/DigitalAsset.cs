namespace Adam.Shared.Models;

public class DigitalAsset
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FileExtension { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string ChecksumSha256 { get; set; } = string.Empty;
    public string StoragePath { get; set; } = string.Empty;
    public string OriginalPath { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public AssetType Type { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public double? Duration { get; set; }
    public Guid? CollectionId { get; set; }
    public Guid? UploadedByUserId { get; set; }
    public bool IsDeleted { get; set; }
    public int Version { get; set; } = 1;

    // Metadata round-trip fields (Phase 4)
    public int Rating { get; set; }
    public AssetLabel Label { get; set; }
    public AssetFlag Flag { get; set; }
    public double? GpsLatitude { get; set; }
    public double? GpsLongitude { get; set; }
    public string? Copyright { get; set; }
    public ImageOrientation Orientation { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }

    public Collection? Collection { get; set; }
    public MetadataProfile? MetadataProfile { get; set; }
    public ICollection<Keyword> Keywords { get; set; } = [];
    public ICollection<Category> Categories { get; set; } = [];
}
