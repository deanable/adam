using System.Threading;
using System.Threading.Tasks;

namespace Adam.Shared.ThumbnailExtractors;

/// <summary>
/// Extracts a thumbnail preview from a file.
/// Each implementation handles one or more file types.
/// </summary>
public interface IThumbnailExtractor
{
    /// <summary>
    /// Lower values are tried first. Tier 1 = 100, Tier 2 = 200, Tier 3 = 300.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Whether this extractor can handle the given file.
    /// </summary>
    bool CanExtract(string filePath, string mimeType);

    /// <summary>
    /// Extract a thumbnail and save it to destPath as a JPEG (maxSize on long edge).
    /// Returns true on success. False allows the pipeline to try the next extractor.
    /// </summary>
    Task<bool> ExtractAsync(
        string sourcePath,
        string destPath,
        int maxSize,
        CancellationToken ct);
}
