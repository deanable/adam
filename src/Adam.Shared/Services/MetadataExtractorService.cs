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
    }

    private static void MapXmp(XmpDirectory dir, MetadataProfile profile)
    {
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
