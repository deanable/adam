namespace Adam.Shared.Models;

public class MetadataProfile
{
    public Guid Id { get; set; }
    public Guid DigitalAssetId { get; set; }

    public string? CameraMake { get; set; }
    public string? CameraModel { get; set; }
    public string? LensModel { get; set; }
    public double? FocalLength { get; set; }
    public double? Aperture { get; set; }
    public string? ExposureTime { get; set; }
    public int? Iso { get; set; }
    public bool? Flash { get; set; }
    public double? GpsLatitude { get; set; }
    public double? GpsLongitude { get; set; }
    public double? GpsAltitude { get; set; }
    public DateTime? DateTaken { get; set; }
    public string? Orientation { get; set; }
    public int? Rating { get; set; }

    public string? Creator { get; set; }
    public string? Copyright { get; set; }
    public string? UsageTerms { get; set; }
    public string? ContactInfo { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? Headline { get; set; }
    public string? Description { get; set; }
    public string? Title { get; set; }
    public string? Category { get; set; }

    public DigitalAsset? DigitalAsset { get; set; }
}
