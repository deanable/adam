using Adam.Shared.Models;

namespace Adam.Shared.Services;

/// <summary>
/// Central registry of supported file extensions, MIME types, and asset type mappings.
/// Single source of truth to eliminate duplicate mappings across the codebase.
/// </summary>
public static class FileTypeHelper
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".tiff", ".tif",
        ".cr2", ".nef", ".arw", ".dng",
        ".mp4", ".mov",
        ".pdf", ".docx", ".txt",
        ".mp3", ".wav"
    };

    private static readonly Dictionary<string, AssetType> ExtensionTypeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = AssetType.Image, [".jpeg"] = AssetType.Image,
        [".png"] = AssetType.Image, [".webp"] = AssetType.Image,
        [".tiff"] = AssetType.Image, [".tif"] = AssetType.Image,
        [".cr2"] = AssetType.Image, [".nef"] = AssetType.Image,
        [".arw"] = AssetType.Image, [".dng"] = AssetType.Image,
        [".mp4"] = AssetType.Video, [".mov"] = AssetType.Video,
        [".pdf"] = AssetType.Document, [".docx"] = AssetType.Document,
        [".txt"] = AssetType.Document,
        [".mp3"] = AssetType.Audio, [".wav"] = AssetType.Audio
    };

    /// <summary>
    /// Returns the read-only set of supported file extensions.
    /// </summary>
    public static IReadOnlySet<string> AllSupportedExtensions => SupportedExtensions;

    public static bool IsSupported(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return SupportedExtensions.Contains(ext);
    }

    public static AssetType GetAssetType(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return ExtensionTypeMap.TryGetValue(ext, out var type) ? type : AssetType.Other;
    }

    public static string GetMimeType(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".tiff" or ".tif" => "image/tiff",
            ".cr2" => "image/x-canon-cr2",
            ".nef" => "image/x-nikon-nef",
            ".arw" => "image/x-sony-arw",
            ".dng" => "image/x-adobe-dng",
            ".mp4" => "video/mp4",
            ".mov" => "video/quicktime",
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".txt" => "text/plain",
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            _ => "application/octet-stream"
        };
    }
}
