using System.Threading;
using System.Threading.Tasks;
using Adam.Shared.Models;
using Adam.Shared.Services;

namespace Adam.Shared.Extractors;

/// <summary>
/// Built-in adapter that wraps <see cref="MetadataExtractorService"/> as an <see cref="IMetadataExtractor"/>.
/// Handles image files (mime type starts with "image/").
/// Priority 100 — higher than OfficeExtractor (200) and third-party plugins (1000+).
/// </summary>
public sealed class ImageExtractor : IMetadataExtractor
{
    private readonly MetadataExtractorService _service;

    public ImageExtractor(MetadataExtractorService service)
    {
        _service = service;
    }

    public int Priority => 100;

    public string Name => "Image EXIF/XMP Extractor";

    public bool CanExtract(string filePath, string mimeType) =>
        mimeType.StartsWith("image/");

    public Task<ExtractedTextMetadata?> ExtractTextAsync(string filePath, CancellationToken ct)
    {
        var result = _service.ExtractTextMetadata(filePath);
        return Task.FromResult(result.HasAnyContent ? result : null);
    }

    public Task<MetadataProfile?> ExtractAsync(string filePath, CancellationToken ct)
    {
        var result = _service.Extract(filePath);
        return Task.FromResult(result.HasAnyContent ? result : null);
    }
}
