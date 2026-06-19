namespace Adam.CatalogBrowser.ViewModels;

/// <summary>
/// Represents a single extracted metadata property for the raw metadata viewer (Panel H).
/// </summary>
public sealed record MetadataRawItem
{
    /// <summary>
    /// The metadata namespace/directory (e.g. "EXIF IFD0", "IPTC", "XMP").
    /// </summary>
    public string Namespace { get; init; } = string.Empty;

    /// <summary>
    /// The tag name (e.g. "Make", "By-line", "dc:creator").
    /// </summary>
    public string Tag { get; init; } = string.Empty;

    /// <summary>
    /// The display string combining namespace and tag (e.g. "EXIF IFD0: Make = Canon").
    /// </summary>
    public string DisplayName => $"{Namespace}: {Tag}";

    /// <summary>
    /// The extracted value as a string.
    /// </summary>
    public string Value { get; init; } = string.Empty;
}
