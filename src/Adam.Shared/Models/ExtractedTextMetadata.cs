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
}
