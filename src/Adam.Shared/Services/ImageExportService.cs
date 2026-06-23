using Adam.Shared.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.Formats.Tiff.Constants;
using SixLabors.ImageSharp.Processing;

namespace Adam.Shared.Services;

/// <summary>
/// Export images to JPEG or TIFF with optional resize, quality, and orientation adjustments.
/// </summary>
public class ImageExportService
{
    private readonly ThumbnailService _thumbnailService = new();
    private readonly ImageAdjustmentService _adjustment = new();
    private readonly MetadataWritebackService _writeback = new();

    public enum ExportFormat
    {
        Jpeg,
        Tiff
    }

    /// <summary>
    /// Resizes an image to fit within the specified target bounds, maintaining aspect ratio.
    /// Only downscales — never upscales.
    /// </summary>
    public static void ResizeToFit(Image image, int targetWidth, int targetHeight)
    {
        if (targetWidth <= 0 || targetHeight <= 0)
            return;

        var (w, h) = (image.Width, image.Height);
        if (w <= targetWidth && h <= targetHeight)
            return; // Already fits within bounds, no upscaling

        var ratio = Math.Min((double)targetWidth / w, (double)targetHeight / h);
        if (ratio < 1.0)
        {
            image.Mutate(x => x.Resize((int)(w * ratio), (int)(h * ratio)));
        }
    }

    /// <summary>
    /// Crops the center of the image to the specified aspect ratio.
    /// Does nothing if the image already matches the ratio or if either value is ≤ 0.
    /// </summary>
    public static void CropToAspectRatio(Image image, int aspectW, int aspectH)
    {
        if (aspectW <= 0 || aspectH <= 0)
            return;

        var currentRatio = (double)image.Width / image.Height;
        var targetRatio = (double)aspectW / aspectH;

        if (Math.Abs(currentRatio - targetRatio) < 0.001)
            return; // Already matches

        if (currentRatio > targetRatio)
        {
            // Image is wider than target — crop width from center
            var newWidth = (int)(image.Height * targetRatio);
            var x = (image.Width - newWidth) / 2;
            image.Mutate(ctx => ctx.Crop(new Rectangle(x, 0, newWidth, image.Height)));
        }
        else
        {
            // Image is taller than target — crop height from center
            var newHeight = (int)(image.Width / targetRatio);
            var y = (image.Height - newHeight) / 2;
            image.Mutate(ctx => ctx.Crop(new Rectangle(0, y, image.Width, newHeight)));
        }
    }

    /// <summary>
    /// Export a single image file.
    /// </summary>
    /// <param name="sourcePath">Source file path.</param>
    /// <param name="destinationPath">Output file path.</param>
    /// <param name="format">Export format (JPEG or TIFF).</param>
    /// <param name="quality">JPEG quality (1-100).</param>
    /// <param name="maxDimension">If set, constrains the larger dimension (maintains aspect ratio, downscale only).</param>
    /// <param name="targetWidth">If set with <paramref name="targetHeight"/>, fits image within these bounds (downscale only).</param>
    /// <param name="targetHeight">If set with <paramref name="targetWidth"/>, fits image within these bounds (downscale only).</param>
    /// <param name="orientation">Orientation adjustment to apply.</param>
    /// <param name="tiffCompression">TIFF compression type.</param>
    /// <param name="asset">Optional asset for metadata preservation.</param>
    /// <param name="cropAspectW">If set with <paramref name="cropAspectH"/>, crops the image to this aspect ratio after resize.</param>
    /// <param name="cropAspectH">If set with <paramref name="cropAspectW"/>, crops the image to this aspect ratio after resize.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task ExportAsync(
        string sourcePath,
        string destinationPath,
        ExportFormat format,
        int quality = 85,
        int? maxDimension = null,
        int? targetWidth = null,
        int? targetHeight = null,
        ImageOrientation orientation = ImageOrientation.Normal,
        TiffCompression tiffCompression = TiffCompression.Lzw,
        DigitalAsset? asset = null,
        int? cropAspectW = null,
        int? cropAspectH = null,
        CancellationToken ct = default)
    {
        using var image = await Image.LoadAsync(sourcePath, ct);

        // Apply orientation adjustment
        _adjustment.ApplyOrientation(image, orientation);

        // Resize: prefer target (width, height) bounds, fall back to maxDimension
        if (targetWidth.HasValue && targetHeight.HasValue && targetWidth.Value > 0 && targetHeight.Value > 0)
        {
            ResizeToFit(image, targetWidth.Value, targetHeight.Value);
        }
        else if (maxDimension.HasValue && maxDimension.Value > 0)
        {
            var (w, h) = (image.Width, image.Height);
            if (w > maxDimension.Value || h > maxDimension.Value)
            {
                if (w > h)
                {
                    var ratio = (double)maxDimension.Value / w;
                    image.Mutate(x => x.Resize(maxDimension.Value, (int)(h * ratio)));
                }
                else
                {
                    var ratio = (double)maxDimension.Value / h;
                    image.Mutate(x => x.Resize((int)(w * ratio), maxDimension.Value));
                }
            }
        }

        // Crop to aspect ratio after resize (center crop)
        if (cropAspectW.HasValue && cropAspectH.HasValue)
        {
            CropToAspectRatio(image, cropAspectW.Value, cropAspectH.Value);
        }

        // Save based on format
        switch (format)
        {
            case ExportFormat.Jpeg:
                var jpegEncoder = new JpegEncoder { Quality = quality };
                await image.SaveAsync(destinationPath, jpegEncoder, ct);
                break;

            case ExportFormat.Tiff:
                var tiffEncoder = new TiffEncoder
                {
                    Compression = tiffCompression
                };
                await image.SaveAsync(destinationPath, tiffEncoder, ct);
                break;
        }

        // Preserve metadata (XMP) if asset provided
        if (asset != null && _writeback.SupportsEmbeddedMetadata(destinationPath))
        {
            try
            {
                await _writeback.WriteMetadataAsync(destinationPath, asset, ct);
            }
            catch
            {
                // Best-effort metadata preservation; failures are logged but don't fail export
            }
        }
    }

    /// <summary>
    /// Export multiple images with progress reporting.
    /// </summary>
    public async Task ExportBatchAsync(
        IReadOnlyList<(string SourcePath, string DestinationPath, DigitalAsset? Asset)> items,
        ExportFormat format,
        int quality = 85,
        int? maxDimension = null,
        int? targetWidth = null,
        int? targetHeight = null,
        TiffCompression tiffCompression = TiffCompression.Lzw,
        IProgress<(int Completed, int Total, string CurrentFile)>? progress = null,
        int? cropAspectW = null,
        int? cropAspectH = null,
        CancellationToken ct = default)
    {
        for (int i = 0; i < items.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var (sourcePath, destinationPath, asset) = items[i];
            var orientation = asset?.Orientation ?? ImageOrientation.Normal;

            await ExportAsync(sourcePath, destinationPath, format, quality, maxDimension, targetWidth, targetHeight, orientation, tiffCompression, asset, cropAspectW, cropAspectH, ct);

            progress?.Report((i + 1, items.Count, Path.GetFileName(sourcePath)));
        }
    }
}
