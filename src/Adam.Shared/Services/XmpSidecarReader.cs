using System.Xml.Linq;
using Adam.Shared.Models;

namespace Adam.Shared.Services;

/// <summary>
/// Reads XMP sidecar (.xmp) files and extracts metadata into <see cref="ExtractedTextMetadata"/>.
/// Supports standard XMP namespaces: dc, xmp, photoshop, MicrosoftPhoto, Iptc4xmpCore.
/// </summary>
public class XmpSidecarReader
{
    private static readonly XNamespace Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Xmp = "http://ns.adobe.com/xap/1.0/";
    private static readonly XNamespace Photoshop = "http://ns.adobe.com/photoshop/1.0/";
    private static readonly XNamespace Iptc = "http://iptc.org/std/Iptc4xmpCore/1.0/xmlns/";
    private static readonly XNamespace MicrosoftPhoto = "http://ns.microsoft.com/photo/1.2/";

    /// <summary>
    /// Reads an XMP sidecar file and extracts metadata.
    /// Returns null if the file doesn't exist or can't be parsed.
    /// </summary>
    public ExtractedTextMetadata? ReadSidecar(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        try
        {
            var doc = XDocument.Load(filePath);
            var result = new ExtractedTextMetadata();

            var rdfElement = doc.Descendants(Rdf + "RDF").FirstOrDefault();
            var description = rdfElement?.Descendants(Rdf + "Description").FirstOrDefault();

            if (description == null)
                return result;

            // dc:title (rdf:Alt > rdf:li)
            var titleAlt = description.Element(Dc + "title");
            if (titleAlt != null)
            {
                var li = titleAlt.Descendants(Rdf + "li").FirstOrDefault();
                result.Title = li?.Value;
            }

            // dc:description (rdf:Alt > rdf:li)
            var descAlt = description.Element(Dc + "description");
            if (descAlt != null)
            {
                var li = descAlt.Descendants(Rdf + "li").FirstOrDefault();
                result.Description = li?.Value;
            }

            // dc:subject (rdf:Bag > rdf:li) — flat keywords
            var subjectBag = description.Element(Dc + "subject");
            if (subjectBag != null)
            {
                foreach (var li in subjectBag.Descendants(Rdf + "li"))
                {
                    var val = li.Value?.Trim();
                    if (!string.IsNullOrWhiteSpace(val) && !result.Keywords.Contains(val))
                        result.Keywords.Add(val);
                }
            }

            // HierarchicalSubject (from Lightroom) — takes precedence over flat keywords
            // These appear as elements in the description, not standard namespaces
            // Look for any descendant elements with HierarchicalSubject in the name
            var hierElements = description.Descendants()
                .Where(e => e.Name.LocalName == "HierarchicalSubject")
                .ToList();

            if (hierElements.Count > 0)
            {
                result.Keywords.Clear();
                foreach (var elem in hierElements)
                {
                    var val = elem.Value?.Trim();
                    if (!string.IsNullOrWhiteSpace(val))
                    {
                        result.Keywords.Add(val);
                        result.HasHierarchicalKeywords = true;
                    }
                }
            }

            // photoshop:Headline → use as fallback description
            if (string.IsNullOrEmpty(result.Description))
            {
                var headline = (string?)description.Element(Photoshop + "Headline");
                if (!string.IsNullOrWhiteSpace(headline))
                    result.Description = headline;
            }

            // xmp:Rating (attribute or element)
            var ratingAttr = description.Attribute(Xmp + "Rating");
            if (ratingAttr != null && int.TryParse(ratingAttr.Value, out var rating))
                result.Rating = rating;

            if (!result.Rating.HasValue)
            {
                var ratingElem = (string?)description.Element(Xmp + "Rating");
                if (ratingElem != null && int.TryParse(ratingElem, out var r))
                    result.Rating = r;
            }

            // MicrosoftPhoto:Rating (element)
            if (!result.Rating.HasValue)
            {
                var msRating = (string?)description.Element(MicrosoftPhoto + "Rating");
                if (msRating != null && int.TryParse(msRating, out var msR))
                    result.Rating = msR;
            }

            return result;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Returns the expected path for an XMP sidecar file next to the given file.
    /// </summary>
    public static string GetSidecarPath(string filePath)
        => Path.ChangeExtension(filePath, ".xmp");

    /// <summary>
    /// Returns true if an XMP sidecar file exists next to the given file.
    /// </summary>
    public static bool SidecarExists(string filePath)
        => File.Exists(GetSidecarPath(filePath));
}
