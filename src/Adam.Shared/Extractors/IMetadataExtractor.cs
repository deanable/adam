using System.Threading;
using System.Threading.Tasks;
using Adam.Shared.Models;

namespace Adam.Shared.Extractors;

/// <summary>
/// Extracts metadata from a file. Each implementation handles one or more file types.
/// Registered extractors are discovered via PluginLoaderService.
/// Modeled after the existing <see cref="Adam.Shared.ThumbnailExtractors.IThumbnailExtractor"/> pattern.
/// </summary>
public interface IMetadataExtractor
{
    /// <summary>
    /// Lower values are tried first. Built-in: 100 (image), 200 (office).
    /// Third-party plugins should use 1000+ to allow future built-in tiers.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Display name shown in plugin management UI.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Whether this extractor can handle the given file.
    /// </summary>
    bool CanExtract(string filePath, string mimeType);

    /// <summary>
    /// Extracts text metadata (Title, Description, Keywords, Categories, Rating).
    /// Returns null if no relevant metadata was found (allows pipeline to continue).
    /// </summary>
    Task<ExtractedTextMetadata?> ExtractTextAsync(string filePath, CancellationToken ct);

    /// <summary>
    /// Extracts rich metadata profile (camera settings, GPS, EXIF, XMP).
    /// Returns null if no rich metadata was found.
    /// </summary>
    Task<MetadataProfile?> ExtractAsync(string filePath, CancellationToken ct);
}
