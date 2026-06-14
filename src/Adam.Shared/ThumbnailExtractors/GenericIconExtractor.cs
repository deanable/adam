using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.Fonts;

namespace Adam.Shared.ThumbnailExtractors;

/// <summary>
/// Generates a generic file-type icon for unsupported formats.
/// Never fails — this is the pipeline's final fallback.
/// </summary>
public class GenericIconExtractor : IThumbnailExtractor
{
    public int Priority => 999;

    public bool CanExtract(string filePath, string mimeType) => true;

    public Task<bool> ExtractAsync(
        string sourcePath,
        string destPath,
        int maxSize,
        CancellationToken ct)
    {
        var ext = Path.GetExtension(sourcePath).TrimStart('.').ToUpperInvariant();
        if (string.IsNullOrEmpty(ext))
            ext = "FILE";

        // Deterministic background color based on extension hash
        var hash = ext.GetHashCode(StringComparison.OrdinalIgnoreCase);
        var hue = Math.Abs(hash % 360);
        var rgb = HsvToRgb(hue / 360f, 0.6f, 0.9f);

        using var image = new Image<Rgba32>(maxSize, maxSize, rgb);

        // Draw extension text centered
        // Fall back gracefully if Arial is not available on the system (e.g., CI runners)
        var font = ResolveFont(maxSize);
        if (font != null)
        {
            var textOptions = new RichTextOptions(font)
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Origin = new System.Numerics.Vector2(maxSize / 2f, maxSize / 2f)
            };

            image.Mutate(ctx => ctx.DrawText(textOptions, ext, Color.White));
        }

        var encoder = new JpegEncoder { Quality = 85 };
        image.Save(destPath, encoder);

        return Task.FromResult(true);
    }

    /// <summary>
    /// Resolves a font for rendering the extension text, falling back
    /// gracefully when Arial is not installed (e.g., on CI runners).
    /// Returns null if no font is available.
    /// </summary>
    private static Font? ResolveFont(int maxSize)
    {
        var preferredFamilies = new[] { "Arial", "Segoe UI", "Tahoma", "Verdana", "Liberation Sans", "DejaVu Sans" };

        foreach (var family in preferredFamilies)
        {
            try
            {
                return SystemFonts.Get(family).CreateFont(maxSize / 4f, FontStyle.Bold);
            }
            catch (FontFamilyNotFoundException)
            {
                continue;
            }
        }

        // Last resort: use the first available font family
        try
        {
            var first = SystemFonts.Families.FirstOrDefault();
            if (first.Name != null)
                return first.CreateFont(maxSize / 4f, FontStyle.Bold);
        }
        catch
        {
            // No fonts available at all
        }

        return null;
    }

    private static Rgba32 HsvToRgb(float h, float s, float v)
    {
        float r, g, b;

        if (s == 0)
        {
            r = g = b = v;
        }
        else
        {
            var i = (int)Math.Floor(h * 6);
            var f = h * 6 - i;
            var p = v * (1 - s);
            var q = v * (1 - f * s);
            var t = v * (1 - (1 - f) * s);

            switch (i % 6)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }
        }

        return new Rgba32((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }
}
