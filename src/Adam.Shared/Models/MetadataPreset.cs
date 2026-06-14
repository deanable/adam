namespace Adam.Shared.Models;

/// <summary>
/// Represents a saved metadata preset that can be applied to assets.
/// All fields are nullable so that a preset can capture only a subset of metadata.
/// Null fields are skipped when applying.
/// </summary>
public class MetadataPreset
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Title { get; set; }

    /// <summary>Pipe-separated keyword names.</summary>
    public string? Keywords { get; set; }

    /// <summary>Pipe-separated category names.</summary>
    public string? Categories { get; set; }

    public int? Rating { get; set; }

    /// <summary>Serialized AssetLabel name (e.g. "Red", "Blue", "None").</summary>
    public string? Label { get; set; }

    /// <summary>Serialized AssetFlag name (e.g. "Pick", "Reject", "Unflagged").</summary>
    public string? Flag { get; set; }

    public string? Copyright { get; set; }
    public double? GpsLatitude { get; set; }
    public double? GpsLongitude { get; set; }
    public string? CameraMake { get; set; }
    public string? CameraModel { get; set; }
    public DateTime? DateTaken { get; set; }

    /// <summary>UTC timestamp when this preset was created or last saved.</summary>
    public DateTime SavedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Returns a human-readable summary of the fields set in this preset.
    /// </summary>
    public string FieldSummary
    {
        get
        {
            var fields = new List<string>();
            if (Title != null) fields.Add("Title");
            if (Description != null) fields.Add("Description");
            if (!string.IsNullOrWhiteSpace(Keywords)) fields.Add("Keywords");
            if (!string.IsNullOrWhiteSpace(Categories)) fields.Add("Categories");
            if (Rating.HasValue) fields.Add("Rating");
            if (!string.IsNullOrWhiteSpace(Label) && !string.Equals(Label, "None", StringComparison.OrdinalIgnoreCase)) fields.Add("Label");
            if (!string.IsNullOrWhiteSpace(Flag) && !string.Equals(Flag, "Unflagged", StringComparison.OrdinalIgnoreCase)) fields.Add("Flag");
            if (Copyright != null) fields.Add("Copyright");
            if (GpsLatitude.HasValue || GpsLongitude.HasValue) fields.Add("GPS");
            if (CameraMake != null || CameraModel != null) fields.Add("Camera");
            if (DateTaken.HasValue) fields.Add("DateTaken");
            return fields.Count > 0 ? string.Join(", ", fields) : "(empty)";
        }
    }

    /// <summary>Formatted save date for display.</summary>
    public string SavedAtFormatted => SavedAt.ToLocalTime().ToString("g");
}
