using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using UglyToad.PdfPig;

namespace Adam.Shared.ThumbnailExtractors;

/// <summary>
/// Extracts embedded preview images from PDF documents using PdfPig.
/// </summary>
public class PdfPreviewExtractor : IThumbnailExtractor
{
    public int Priority => 130;

    public bool CanExtract(string filePath, string mimeType)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext == ".pdf";
    }

    public Task<bool> ExtractAsync(
        string sourcePath,
        string destPath,
        int maxSize,
        CancellationToken ct)
    {
        try
        {
            if (!File.Exists(sourcePath))
                return Task.FromResult(false);

            using var document = PdfDocument.Open(sourcePath);

            // Try to find an embedded image on the first page
            var firstPage = document.GetPage(1);
            var imageList = firstPage.GetImages().ToList();

            if (!imageList.Any())
                return Task.FromResult(false);

            var imageInfo = imageList.First();
            if (!imageInfo.TryGetPng(out var bytes) || bytes is null || bytes.Length == 0)
                return Task.FromResult(false);

            using var image = Image.Load(bytes);
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
            image.Save(destPath, encoder);
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }
}
