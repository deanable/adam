using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Adam.Shared.Models;
using Adam.Shared.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace Adam.Shared.ThumbnailExtractors;

/// <summary>
/// Extracts thumbnails from image files using ImageSharp.
/// Handles resize and EXIF orientation.
/// </summary>
public class ImageThumbnailExtractor : IThumbnailExtractor
{
    private readonly ImageAdjustmentService _adjustment;

    public ImageThumbnailExtractor(ImageAdjustmentService? adjustment = null)
    {
        _adjustment = adjustment ?? new ImageAdjustmentService();
    }

    public int Priority => 100;

    public bool CanExtract(string filePath, string mimeType)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext is 
            ".jpg" or ".jpeg" or ".png" or ".webp" or ".tiff" or ".tif"
            or ".cr2" or ".nef" or ".arw" or ".dng" or ".gif" or ".bmp" or ".heic";
    }

    public async Task<bool> ExtractAsync(
        string sourcePath,
        string destPath,
        int maxSize,
        CancellationToken ct)
    {
        using var image = await Image.LoadAsync(sourcePath, ct);

        _adjustment.ApplyOrientation(image, ImageOrientation.Normal);
        var (w, h) = (image.Width, image.Height);

        if (w > h)
        {
            var ratio = (double)maxSize / w;
            image.Mutate(x => x.Resize(maxSize, (int)(h * ratio)));
        }
        else
        {
            var ratio = (double)maxSize / h;
            image.Mutate(x => x.Resize((int)(w * ratio), maxSize));
        }

        var encoder = new JpegEncoder { Quality = 85 };
        await image.SaveAsync(destPath, encoder, ct);
        return true;
    }
}
