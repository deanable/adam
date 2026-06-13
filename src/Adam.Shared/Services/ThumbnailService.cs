using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Adam.Shared.Models;
using Adam.Shared.ThumbnailExtractors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.Shared.Services;

public class ThumbnailService
{
    private readonly ThumbnailPipeline _pipeline;
    private readonly ILogger<ThumbnailService> _logger;

    public ThumbnailService(ILogger<ThumbnailService>? logger = null)
    {
        _logger = logger ?? NullLogger<ThumbnailService>.Instance;

        var adjustment = new ImageAdjustmentService();
        _pipeline = new ThumbnailPipeline([
            new ImageThumbnailExtractor(adjustment),
            new AudioThumbnailExtractor(),
            new OfficeThumbnailExtractor(),
            new PdfPreviewExtractor(),
            new VideoThumbnailExtractor(),
            new GenericIconExtractor()
        ], null);
    }

    /// <summary>
    /// For testing / advanced use: create with a custom pipeline.
    /// </summary>
    public ThumbnailService(ThumbnailPipeline pipeline, ILogger<ThumbnailService>? logger = null)
    {
        _pipeline = pipeline;
        _logger = logger ?? NullLogger<ThumbnailService>.Instance;
    }

    public Task<string> GenerateThumbnailAsync(
        string sourcePath,
        string thumbnailDirectory,
        CancellationToken ct = default)
        => GenerateThumbnailAsync(sourcePath, thumbnailDirectory, ImageOrientation.Normal, ct);

    public async Task<string> GenerateThumbnailAsync(
        string sourcePath,
        string thumbnailDirectory,
        ImageOrientation orientation,
        CancellationToken ct = default)
    {
        var thumbnailPath = GetThumbnailPath(sourcePath, thumbnailDirectory);

        // T12.4: Check if the existing thumbnail is still fresh by comparing
        // LastWriteTimeUtc against the source file. This avoids regenerating
        // thumbnails for unchanged files even when orientation is applied.
        if (File.Exists(thumbnailPath))
        {
            try
            {
                var sourceInfo = new FileInfo(sourcePath);
                var thumbInfo = new FileInfo(thumbnailPath);
                if (thumbInfo.LastWriteTimeUtc >= sourceInfo.LastWriteTimeUtc &&
                    orientation == ImageOrientation.Normal)
                {
                    return thumbnailPath;
                }
            }
            catch
            {
                // If we can't stat the source (e.g. permission, network path),
                // fall through to regenerate the thumbnail.
            }
        }

        Directory.CreateDirectory(thumbnailDirectory);

        var mimeType = ResolveMimeType(sourcePath);

        var success = await _pipeline.TryExtractAsync(
            sourcePath, thumbnailPath, mimeType, maxSize: 256, ct);

        if (!success)
            _logger.LogWarning("All extractors failed for {SourcePath}; no thumbnail generated", sourcePath);

        return thumbnailPath;
    }

    public string GetThumbnailPath(string sourcePath, string thumbnailDirectory)
    {
        var normalized = sourcePath.Replace('\\', '/');
        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(normalized))
        )[..16];
        return Path.Combine(thumbnailDirectory, $"{hash}.jpg");
    }

    private static string ResolveMimeType(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".tiff" or ".tif" => "image/tiff",
            ".cr2" or ".nef" or ".arw" or ".dng" => "image/x-raw",
            ".mp4" => "video/mp4",
            ".avi" => "video/x-msvideo",
            ".mov" => "video/quicktime",
            ".mkv" => "video/x-matroska",
            ".wmv" => "video/x-ms-wmv",
            ".webm" => "video/webm",
            ".mp3" => "audio/mpeg",
            ".flac" => "audio/flac",
            ".wav" => "audio/wav",
            ".m4a" => "audio/mp4",
            ".ogg" => "audio/ogg",
            ".wma" => "audio/x-ms-wma",
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            _ => "application/octet-stream"
        };
    }
}
