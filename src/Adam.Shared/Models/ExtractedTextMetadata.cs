namespace Adam.Shared.Models;

public class ExtractedTextMetadata
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public List<string> Keywords { get; set; } = [];
    public List<string> Categories { get; set; } = [];
    public int? Rating { get; set; }

    /// <summary>
    /// True if keywords came from HierarchicalSubject (pipe-delimited paths).
    /// False if they came from flat dc:subject or IPTC keywords.
    /// </summary>
    public bool HasHierarchicalKeywords { get; set; }

    /// <summary>
    /// Returns true when any content field (Title, Description, Keywords, Categories, or Rating) is populated.
    /// Used by <see cref="Extractors.IMetadataExtractor"/> adapters to determine whether to return null
    /// (allowing the priority chain to fall through to the next extractor).
    /// </summary>
    public bool HasAnyContent =>
        !string.IsNullOrWhiteSpace(Title) ||
        !string.IsNullOrWhiteSpace(Description) ||
        Keywords.Count > 0 ||
        Categories.Count > 0 ||
        Rating.HasValue;
}
