using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Adam.Shared.ThumbnailExtractors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.Shared.Services;

/// <summary>
/// Orchestrates thumbnail extraction by trying registered extractors in priority order.
/// Falls back to the last extractor (typically GenericIconExtractor) which never fails.
/// </summary>
public class ThumbnailPipeline
{
    private readonly IReadOnlyList<IThumbnailExtractor> _extractors;
    private readonly ILogger<ThumbnailPipeline> _logger;

    public ThumbnailPipeline(IEnumerable<IThumbnailExtractor> extractors, ILogger<ThumbnailPipeline>? logger = null)
    {
        _extractors = extractors.OrderBy(e => e.Priority).ToList();
        _logger = logger ?? NullLogger<ThumbnailPipeline>.Instance;
    }

    /// <summary>
    /// Attempts to extract a thumbnail from sourcePath and save it to destPath.
    /// Returns true if any extractor succeeded (including the fallback).
    /// </summary>
    public async Task<bool> TryExtractAsync(
        string sourcePath,
        string destPath,
        string mimeType,
        int maxSize,
        CancellationToken ct)
    {
        foreach (var extractor in _extractors)
        {
            if (!extractor.CanExtract(sourcePath, mimeType))
                continue;

            try
            {
                var success = await extractor.ExtractAsync(sourcePath, destPath, maxSize, ct);
                if (success)
                {
                    _logger.LogDebug(
                        "Thumbnail extracted by {Extractor}: {SourcePath} -> {DestPath}",
                        extractor.GetType().Name,
                        sourcePath,
                        destPath);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Extractor {Extractor} failed for {SourcePath}",
                    extractor.GetType().Name,
                    sourcePath);
            }
        }

        return false;
    }
}
