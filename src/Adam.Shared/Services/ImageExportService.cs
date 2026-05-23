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
    /// Export a single image file.
    /// </summary>
    public async Task ExportAsync(
        string sourcePath,
        string destinationPath,
        ExportFormat format,
        int quality = 85,
        int? maxDimension = null,
        ImageOrientation orientation = ImageOrientation.Normal,
        TiffCompression tiffCompression = TiffCompression.Lzw,
        DigitalAsset? asset = null,
        CancellationToken ct = default)
    {
        using var image = await Image.LoadAsync(sourcePath, ct);

        // Apply orientation adjustment
        _adjustment.ApplyOrientation(image, orientation);

        // Resize if max dimension specified
        if (maxDimension.HasValue && maxDimension.Value > 0)
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
        TiffCompression tiffCompression = TiffCompression.Lzw,
        IProgress<(int Completed, int Total, string CurrentFile)>? progress = null,
        CancellationToken ct = default)
    {
        for (int i = 0; i < items.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var (sourcePath, destinationPath, asset) = items[i];
            var orientation = asset?.Orientation ?? ImageOrientation.Normal;

            await ExportAsync(sourcePath, destinationPath, format, quality, maxDimension, orientation, tiffCompression, asset, ct);

            progress?.Report((i + 1, items.Count, Path.GetFileName(sourcePath)));
        }
    }
}
