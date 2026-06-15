using System.Threading;
using System.Threading.Tasks;
using Adam.Shared.Models;
using Adam.Shared.Services;

namespace Adam.Shared.Extractors;

/// <summary>
/// Built-in adapter that wraps <see cref="OfficeDocumentExtractor"/> as an <see cref="IMetadataExtractor"/>.
/// Handles Office Open XML documents (.docx, .xlsx, .pptx).
/// Priority 200 — lower priority than ImageExtractor (100) but higher than plugins (1000+).
/// </summary>
public sealed class OfficeExtractor : IMetadataExtractor
{
    private readonly OfficeDocumentExtractor _service;

    public OfficeExtractor(OfficeDocumentExtractor service)
    {
        _service = service;
    }

    public int Priority => 200;

    public string Name => "Office Document Extractor";

    public bool CanExtract(string filePath, string mimeType) =>
        OfficeDocumentExtractor.SupportedExtensions.Contains(
            Path.GetExtension(filePath).ToLowerInvariant());

    public Task<ExtractedTextMetadata?> ExtractTextAsync(string filePath, CancellationToken ct)
    {
        var result = _service.Extract(filePath);
        return Task.FromResult(result.HasAnyContent ? result : null);
    }

    public Task<MetadataProfile?> ExtractAsync(string filePath, CancellationToken ct)
        => Task.FromResult<MetadataProfile?>(null); // Office docs don't have rich metadata profiles
}
