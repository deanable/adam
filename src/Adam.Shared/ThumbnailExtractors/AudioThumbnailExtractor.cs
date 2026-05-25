using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using TagLib;

namespace Adam.Shared.ThumbnailExtractors;

/// <summary>
/// Extracts embedded album art from audio files using TagLibSharp.
/// </summary>
public class AudioThumbnailExtractor : IThumbnailExtractor
{
    public int Priority => 110;

    public bool CanExtract(string filePath, string mimeType)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext is ".mp3" or ".flac" or ".wav" or ".m4a" or ".ogg" or ".wma";
    }

    public async Task<bool> ExtractAsync(
        string sourcePath,
        string destPath,
        int maxSize,
        CancellationToken ct)
    {
        try
        {
            using var file = TagLib.File.Create(sourcePath);
            var picture = file.Tag.Pictures?.FirstOrDefault();
            if (picture == null)
                return false;

            using var image = Image.Load(picture.Data.Data);
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
