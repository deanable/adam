using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;

namespace Adam.Shared.Services;

public class ThumbnailService
{
    public async Task<string> GenerateThumbnailAsync(string sourcePath, string thumbnailDirectory, CancellationToken ct = default)
    {
        var thumbnailPath = GetThumbnailPath(sourcePath, thumbnailDirectory);

        if (File.Exists(thumbnailPath))
            return thumbnailPath;

        Directory.CreateDirectory(thumbnailDirectory);

        using var image = await Image.LoadAsync(sourcePath, ct);
        var (w, h) = (image.Width, image.Height);

        if (w > h)
        {
            var ratio = 256.0 / w;
            image.Mutate(x => x.Resize(256, (int)(h * ratio)));
        }
        else
        {
            var ratio = 256.0 / h;
            image.Mutate(x => x.Resize((int)(w * ratio), 256));
        }

        var encoder = new JpegEncoder { Quality = 85 };
        await image.SaveAsync(thumbnailPath, encoder, ct);

        return thumbnailPath;
    }

    public string GetThumbnailPath(string sourcePath, string thumbnailDirectory)
    {
        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(sourcePath))
        )[..16];

        return Path.Combine(thumbnailDirectory, $"{hash}.jpg");
    }
}
