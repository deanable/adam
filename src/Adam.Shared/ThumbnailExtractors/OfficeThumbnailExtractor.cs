using System;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace Adam.Shared.ThumbnailExtractors;

/// <summary>
/// Extracts embedded preview thumbnails from Office documents (DOCX, XLSX, PPTX).
/// These files are ZIP packages that may contain docProps/thumbnail.jpeg.
/// </summary>
public class OfficeThumbnailExtractor : IThumbnailExtractor
{
    public int Priority => 120;

    public bool CanExtract(string filePath, string mimeType)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext is ".docx" or ".xlsx" or ".pptx";
    }

    public async Task<bool> ExtractAsync(
        string sourcePath,
        string destPath,
        int maxSize,
        CancellationToken ct)
    {
        try
        {
            if (!File.Exists(sourcePath))
                return false;

            using var package = Package.Open(sourcePath, FileMode.Open, FileAccess.Read);
            var thumbnailPart = package.GetParts()
                .FirstOrDefault(p => p.Uri.OriginalString.Contains("thumbnail", StringComparison.OrdinalIgnoreCase));

            if (thumbnailPart == null)
                return false;

            using var stream = thumbnailPart.GetStream();
            using var image = Image.Load(stream);
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
        catch
        {
            return false;
        }
    }
}
