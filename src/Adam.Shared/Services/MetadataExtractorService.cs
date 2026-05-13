using Adam.Shared.Models;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Iptc;
using MetadataExtractor.Formats.Xmp;
using Directory = MetadataExtractor.Directory;

namespace Adam.Shared.Services;

public class MetadataExtractorService
{
    public MetadataProfile Extract(string filePath)
    {
        var directories = ImageMetadataReader.ReadMetadata(filePath);
        var profile = new MetadataProfile();

        foreach (var dir in directories)
        {
            switch (dir)
            {
                case ExifIfd0Directory ifd0:
                    MapExifIfd0(ifd0, profile);
                    break;
                case ExifSubIfdDirectory subIfd:
                    MapExifSubIfd(subIfd, profile);
                    break;
                case GpsDirectory gps:
                    MapGps(gps, profile);
                    break;
                case IptcDirectory iptc:
                    MapIptc(iptc, profile);
                    break;
                case XmpDirectory xmp:
                    MapXmp(xmp, profile);
                    break;
            }
        }

        return profile;
    }

    public ExtractedTextMetadata ExtractTextMetadata(string filePath)
    {
        var directories = ImageMetadataReader.ReadMetadata(filePath);
        var result = new ExtractedTextMetadata();

        foreach (var dir in directories)
        {
            switch (dir)
            {
                case IptcDirectory iptc:
                    ExtractIptcText(iptc, result);
                    break;
                case XmpDirectory xmp:
                    ExtractXmpText(xmp, result);
                    break;
            }
        }

        return result;
    }

    private static void MapExifIfd0(ExifIfd0Directory dir, MetadataProfile profile)
    {
        profile.CameraMake = GetString(dir, ExifIfd0Directory.TagMake);
        profile.CameraModel = GetString(dir, ExifIfd0Directory.TagModel);
        profile.Orientation = GetString(dir, ExifIfd0Directory.TagOrientation);
    }

    private static void MapExifSubIfd(ExifSubIfdDirectory dir, MetadataProfile profile)
    {
        profile.LensModel = GetString(dir, ExifSubIfdDirectory.TagLensModel);
        profile.FocalLength = GetDouble(dir, ExifSubIfdDirectory.TagFocalLength);
        profile.Aperture = GetDouble(dir, ExifSubIfdDirectory.TagFNumber);
        profile.ExposureTime = GetString(dir, ExifSubIfdDirectory.TagExposureTime);
        profile.Iso = GetInt(dir, ExifSubIfdDirectory.TagIsoEquivalent);
        profile.Flash = GetBool(dir, ExifSubIfdDirectory.TagFlash);
        profile.DateTaken = GetDateTime(dir, ExifSubIfdDirectory.TagDateTimeOriginal);
    }

    private static void MapGps(GpsDirectory dir, MetadataProfile profile)
    {
        var location = dir.GetGeoLocation();
        if (location != null)
        {
            profile.GpsLatitude = location.Latitude;
            profile.GpsLongitude = location.Longitude;
        }
        profile.GpsAltitude = GetDouble(dir, GpsDirectory.TagAltitude);
    }

    private static void MapIptc(IptcDirectory dir, MetadataProfile profile)
    {
        profile.Creator = GetString(dir, IptcDirectory.TagByLine);
        profile.Copyright = GetString(dir, IptcDirectory.TagCopyrightNotice);
        profile.Headline = GetString(dir, IptcDirectory.TagHeadline);
        profile.City = GetString(dir, IptcDirectory.TagCity);
        profile.State = GetString(dir, IptcDirectory.TagProvinceOrState);
        profile.Country = GetString(dir, IptcDirectory.TagCountryOrPrimaryLocationName);

        var categories = new List<string>();
        var cat = GetString(dir, IptcDirectory.TagCategory);
        if (!string.IsNullOrWhiteSpace(cat)) categories.Add(cat.Trim());

        var suppCats = GetString(dir, IptcDirectory.TagSupplementalCategories);
        if (!string.IsNullOrWhiteSpace(suppCats))
        {
            foreach (var sc in suppCats.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = sc.Trim();
                if (trimmed.Length > 0) categories.Add(trimmed);
            }
        }

        profile.Category = categories.Count > 0 ? string.Join(";", categories) : null;
    }

    private static void ExtractIptcText(IptcDirectory dir, ExtractedTextMetadata result)
    {
        if (string.IsNullOrEmpty(result.Title))
            result.Title = GetString(dir, IptcDirectory.TagObjectName);

        if (string.IsNullOrEmpty(result.Description))
            result.Description = GetString(dir, IptcDirectory.TagCaption);

        if (result.Keywords.Count == 0)
        {
            var keywords = dir.GetKeywords();
            if (keywords != null)
            {
                foreach (var kw in keywords.Where(k => !string.IsNullOrWhiteSpace(k)))
                    result.Keywords.Add(kw.Trim());
            }
        }
    }

    private static void MapXmp(XmpDirectory dir, MetadataProfile profile)
    {
        if (dir.XmpMeta == null) return;
        var props = dir.GetXmpProperties();

        var xmpCats = new List<string>();

        var hierKeys = props.Keys.Where(k =>
            k.StartsWith("Hierarchical Subject", StringComparison.OrdinalIgnoreCase) ||
            k.EndsWith(":HierarchicalSubject", StringComparison.OrdinalIgnoreCase));
        foreach (var key in hierKeys)
        {
            var val = props[key];
            if (!string.IsNullOrWhiteSpace(val))
            {
                foreach (var part in val.Split('|', StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmed = part.Trim();
                    if (trimmed.Length > 0) xmpCats.Add(trimmed);
                }
            }
        }

        if (xmpCats.Count > 0)
        {
            var existing = profile.Category;
            profile.Category = existing != null
                ? $"{existing};{string.Join(";", xmpCats)}"
                : string.Join(";", xmpCats);
        }
    }

    private static void ExtractXmpText(XmpDirectory dir, ExtractedTextMetadata result)
    {
        if (dir.XmpMeta == null) return;

        var props = dir.GetXmpProperties();

        // Title: dc:title or dc:title[1] or dc:title[1]/xml:lang
        if (string.IsNullOrEmpty(result.Title))
        {
            var titleKey = props.Keys.FirstOrDefault(k =>
                k.Equals("dc:title", StringComparison.OrdinalIgnoreCase) ||
                k.StartsWith("dc:title[", StringComparison.OrdinalIgnoreCase));
            if (titleKey != null)
                result.Title = props[titleKey];
        }

        // Description: dc:description or dc:description[1]
        if (string.IsNullOrEmpty(result.Description))
        {
            var descKey = props.Keys.FirstOrDefault(k =>
                k.Equals("dc:description", StringComparison.OrdinalIgnoreCase) ||
                k.StartsWith("dc:description[", StringComparison.OrdinalIgnoreCase));
            if (descKey != null)
                result.Description = props[descKey];
        }

        // Keywords: dc:subject[*]
        var subjectKeys = props.Keys.Where(k =>
            k.StartsWith("dc:subject", StringComparison.OrdinalIgnoreCase));
        foreach (var key in subjectKeys)
        {
            var val = props[key];
            if (!string.IsNullOrWhiteSpace(val) && !result.Keywords.Contains(val.Trim()))
                result.Keywords.Add(val.Trim());
        }

        // Rating: xmp:Rating or MicrosoftPhoto:Rating
        if (!result.Rating.HasValue)
        {
            var ratingKey = props.Keys.FirstOrDefault(k =>
                k.Equals("xmp:Rating", StringComparison.OrdinalIgnoreCase) ||
                k.Equals("MicrosoftPhoto:Rating", StringComparison.OrdinalIgnoreCase));
            if (ratingKey != null && int.TryParse(props[ratingKey], out var r))
                result.Rating = r;
        }
    }

    private static string? GetString(Directory dir, int tag)
    {
        return dir.GetString(tag);
    }

    private static int? GetInt(Directory dir, int tag)
    {
        return dir.TryGetInt32(tag, out var value) ? value : null;
    }

    private static double? GetDouble(Directory dir, int tag)
    {
        return dir.TryGetRational(tag, out var rational) ? rational.ToDouble() : null;
    }

    private static bool? GetBool(Directory dir, int tag)
    {
        return dir.TryGetBoolean(tag, out var value) ? value : null;
    }

    private static DateTime? GetDateTime(Directory dir, int tag)
    {
        return dir.TryGetDateTime(tag, out var value) ? value : null;
    }
}
