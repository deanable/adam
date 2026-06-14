using Adam.Shared.Models;
using DocumentFormat.OpenXml.Packaging;

namespace Adam.Shared.Services;

/// <summary>
/// Extracts metadata from Office Open XML documents (.docx, .xlsx, .pptx).
/// Reads built-in package properties (Title, Subject, Keywords, Category, etc.)
/// and checks for XMP sidecar files when present.
/// </summary>
public class OfficeDocumentExtractor
{
    private readonly XmpSidecarReader _xmpReader = new();

    /// <summary>
    /// Extracts text metadata from an Office document.
    /// First checks for an XMP sidecar file (.xmp next to the document),
    /// then falls back to reading the document's built-in package properties.
    /// Returns a populated <see cref="ExtractedTextMetadata"/> (never null).
    /// </summary>
    public ExtractedTextMetadata Extract(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext is not ".docx" and not ".xlsx" and not ".pptx")
            return new ExtractedTextMetadata();

        // Check for XMP sidecar first — fill gaps from package properties
        var result = ExtractFromPackage(filePath);

        if (XmpSidecarReader.SidecarExists(filePath))
        {
            var sidecarResult = _xmpReader.ReadSidecar(XmpSidecarReader.GetSidecarPath(filePath));
            if (sidecarResult != null)
            {
                // Sidecar fields take precedence over package properties
                if (!string.IsNullOrWhiteSpace(sidecarResult.Title))
                    result.Title = sidecarResult.Title;
                if (!string.IsNullOrWhiteSpace(sidecarResult.Description))
                    result.Description = sidecarResult.Description;
                if (sidecarResult.Keywords.Count > 0)
                {
                    result.Keywords.Clear();
                    result.Keywords.AddRange(sidecarResult.Keywords);
                    result.HasHierarchicalKeywords = sidecarResult.HasHierarchicalKeywords;
                }
                if (sidecarResult.Rating.HasValue)
                    result.Rating = sidecarResult.Rating;
            }
        }

        return result;
    }

    /// <summary>
    /// Extracts metadata from the Office Open XML package properties.
    /// </summary>
    private static ExtractedTextMetadata ExtractFromPackage(string filePath)
    {
        var result = new ExtractedTextMetadata();

        try
        {
            using var doc = WordprocessingDocument.Open(filePath, false);
            var props = doc.PackageProperties;

            if (props.Title != null)
                result.Title = props.Title;

            if (props.Subject != null)
                result.Description = props.Subject;

            // Keywords (semicolon or comma separated in Office)
            if (props.Keywords != null)
            {
                foreach (var kw in props.Keywords.Split([';', ','], StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmed = kw.Trim();
                    if (trimmed.Length > 0 && !result.Keywords.Contains(trimmed))
                        result.Keywords.Add(trimmed);
                }
            }

            // Category may map to our Categories
            if (props.Category != null)
            {
                foreach (var cat in props.Category.Split([';', ','], StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmed = cat.Trim();
                    if (trimmed.Length > 0 && !result.Categories.Contains(trimmed))
                        result.Categories.Add(trimmed);
                }
            }
        }
        catch
        {
            // If the file can't be opened as WordprocessingDocument, try SpreadsheetDocument
            try
            {
                using var doc = SpreadsheetDocument.Open(filePath, false);
                var props = doc.PackageProperties;

                if (props.Title != null) result.Title = props.Title;
                if (props.Subject != null) result.Description = props.Subject;

                if (props.Keywords != null)
                {
                    foreach (var kw in props.Keywords.Split([';', ','], StringSplitOptions.RemoveEmptyEntries))
                    {
                        var trimmed = kw.Trim();
                        if (trimmed.Length > 0 && !result.Keywords.Contains(trimmed))
                            result.Keywords.Add(trimmed);
                    }
                }

                if (props.Category != null)
                {
                    foreach (var cat in props.Category.Split([';', ','], StringSplitOptions.RemoveEmptyEntries))
                    {
                        var trimmed = cat.Trim();
                        if (trimmed.Length > 0 && !result.Categories.Contains(trimmed))
                            result.Categories.Add(trimmed);
                    }
                }
            }
            catch
            {
                try
                {
                    using var doc = PresentationDocument.Open(filePath, false);
                    var props = doc.PackageProperties;

                    if (props.Title != null) result.Title = props.Title;
                    if (props.Subject != null) result.Description = props.Subject;

                    if (props.Keywords != null)
                    {
                        foreach (var kw in props.Keywords.Split([';', ','], StringSplitOptions.RemoveEmptyEntries))
                        {
                            var trimmed = kw.Trim();
                            if (trimmed.Length > 0 && !result.Keywords.Contains(trimmed))
                                result.Keywords.Add(trimmed);
                        }
                    }

                    if (props.Category != null)
                    {
                        foreach (var cat in props.Category.Split([';', ','], StringSplitOptions.RemoveEmptyEntries))
                        {
                            var trimmed = cat.Trim();
                            if (trimmed.Length > 0 && !result.Categories.Contains(trimmed))
                                result.Categories.Add(trimmed);
                        }
                    }
                }
                catch
                {
                    // All attempts failed — return empty result
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Returns the set of Office document extensions supported by this extractor.
    /// </summary>
    public static readonly HashSet<string> SupportedExtensions =
    [
        ".docx", ".xlsx", ".pptx"
    ];
}
