using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LibVLCSharp.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.Shared.Services;

/// <summary>
/// Exports video files by transcoding to a target resolution using LibVLCSharp.
/// Only downscales — never upscales. Uses H.264 encoding.
/// </summary>
public class VideoExportService
{
    private readonly ILogger<VideoExportService> _logger;
    private static readonly bool _vlcInitialized = InitializeVlc();

    private static bool InitializeVlc()
    {
        try { Core.Initialize(); return true; }
        catch { return false; }
    }

    public VideoExportService(ILogger<VideoExportService>? logger = null)
    {
        _logger = logger ?? NullLogger<VideoExportService>.Instance;
    }

    /// <summary>
    /// Transcodes a video file to the specified target resolution and CRF quality.
    /// Only downscales — if the source is smaller than the target, it is copied as-is.
    /// </summary>
    /// <param name="sourcePath">Path to the source video file.</param>
    /// <param name="destinationPath">Path for the transcoded output.</param>
    /// <param name="targetWidth">Target width in pixels. Use 0 to keep source width.</param>
    /// <param name="targetHeight">Target height in pixels. Use 0 to keep source height.</param>
    /// <param name="crf">Constant Rate Factor (0-51). Lower = better quality, larger file. Default 23.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task ExportAsync(
        string sourcePath,
        string destinationPath,
        int targetWidth,
        int targetHeight,
        int crf = 23,
        CancellationToken ct = default)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Source video not found", sourcePath);

        // If no resize requested, just copy the file
        if (targetWidth <= 0 && targetHeight <= 0)
        {
            await CopyFileAsync(sourcePath, destinationPath, ct);
            return;
        }

        // Detect source dimensions via MediaPlayer
        var (srcWidth, srcHeight) = await GetVideoDimensionsAsync(sourcePath, ct);

        if (srcWidth <= 0 || srcHeight <= 0)
        {
            _logger.LogWarning("Could not detect dimensions for {Source}, copying as-is", sourcePath);
            await CopyFileAsync(sourcePath, destinationPath, ct);
            return;
        }

        // Compute scaled dimensions (fit within target, maintain aspect ratio, downscale only)
        var scaleW = targetWidth > 0 ? targetWidth : srcWidth;
        var scaleH = targetHeight > 0 ? targetHeight : srcHeight;
        var ratio = Math.Min((double)scaleW / srcWidth, (double)scaleH / srcHeight);

        // Only downscale — never upscale
        if (ratio >= 1.0)
        {
            _logger.LogInformation("Source {Source} is already smaller than target, copying as-is", sourcePath);
            await CopyFileAsync(sourcePath, destinationPath, ct);
            return;
        }

        var outWidth = (int)(srcWidth * ratio);
        var outHeight = (int)(srcHeight * ratio);

        // Ensure even dimensions (required by many codecs)
        if (outWidth % 2 != 0) outWidth++;
        if (outHeight % 2 != 0) outHeight++;

        _logger.LogInformation(
            "Transcoding {Source}: {SrcW}x{SrcH} → {OutW}x{OutH} (CRF={Crf})",
            sourcePath, srcWidth, srcHeight, outWidth, outHeight, crf);

        await TranscodeAsync(sourcePath, destinationPath, outWidth, outHeight, crf, ct);
    }

    /// <summary>
    /// Exports a batch of video files with progress reporting.
    /// </summary>
    public async Task ExportBatchAsync(
        IReadOnlyList<(string SourcePath, string DestinationPath)> items,
        int targetWidth,
        int targetHeight,
        int crf = 23,
        IProgress<(int Completed, int Total, string CurrentFile)>? progress = null,
        CancellationToken ct = default)
    {
        for (int i = 0; i < items.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var (sourcePath, destPath) = items[i];
            await ExportAsync(sourcePath, destPath, targetWidth, targetHeight, crf, ct);

            progress?.Report((i + 1, items.Count, Path.GetFileName(sourcePath)));
        }
    }

    /// <summary>
    /// Detects whether a file is a supported video format.
    /// </summary>
    public static bool IsVideoFile(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext is ".mp4" or ".avi" or ".mov" or ".mkv" or ".wmv" or ".webm" or ".flv" or ".m4v";
    }

    private static async Task CopyFileAsync(string source, string destination, CancellationToken ct)
    {
        await using var srcStream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
        await using var dstStream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true);
        await srcStream.CopyToAsync(dstStream, 65536, ct);
    }

    /// <summary>
    /// Detects video dimensions by parsing media track info.
    /// Uses Media.Tracks after Parse to read VideoTrack width/height via MediaTrackData.Video.
    /// </summary>
    private static Task<(int Width, int Height)> GetVideoDimensionsAsync(
        string filePath, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            try
            {
                using var libVlc = new LibVLC();
                using var media = new Media(libVlc, filePath);

                // Parse the media to populate track info
                media.Parse(MediaParseOptions.ParseLocal);
                Thread.Sleep(500);

                var tracks = media.Tracks;
                if (tracks != null)
                {
                    foreach (var track in tracks)
                    {
                        if (track.TrackType == TrackType.Video)
                        {
                            // VideoTrack.Width/Height are uint — cast to int
                            var vw = (int)track.Data.Video.Width;
                            var vh = (int)track.Data.Video.Height;
                            if (vw > 0 && vh > 0)
                                return (vw, vh);
                        }
                    }
                }

                return (0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VideoExportService] Failed to detect dimensions: {ex.Message}");
                return (0, 0);
            }
        }, ct);
    }

    /// <summary>
    /// Transcodes a video file to the specified output dimensions using LibVLC.
    /// </summary>
    private static async Task TranscodeAsync(
        string sourcePath,
        string destinationPath,
        int width,
        int height,
        int crf,
        CancellationToken ct)
    {
        await Task.Run(() =>
        {
            try
            {
                if (!_vlcInitialized)
                    throw new InvalidOperationException("LibVLC initialization failed");

                using var libVlc = new LibVLC();

                // Escape single quotes in the destination path for LibVLC MRL syntax
                var escapedPath = destinationPath.Replace("'", "'\\''");

                // Build transcoding options
                var transcodeOptions = $"--sout=#transcode{{vcodec=h264,width={width},height={height},venc=x264{{crf={crf}}},acodec=mpga,ab=128}}:std{{access=file,mux=mp4,dst='{escapedPath}'}}";

                using var media = new Media(libVlc, sourcePath);
                media.AddOption(transcodeOptions);

                using var mediaPlayer = new MediaPlayer(media);
                mediaPlayer.Play();

                // Poll for completion
                while (!ct.IsCancellationRequested)
                {
                    Thread.Sleep(100);

                    if (!mediaPlayer.IsPlaying)
                        break;
                }

                if (ct.IsCancellationRequested)
                {
                    mediaPlayer.Stop();
                    ct.ThrowIfCancellationRequested();
                }

                // Clean up partial output on failure
                if (!File.Exists(destinationPath) || new FileInfo(destinationPath).Length == 0)
                {
                    if (File.Exists(destinationPath))
                        File.Delete(destinationPath);
                    throw new InvalidOperationException($"Video transcoding failed for {sourcePath}");
                }
            }
            catch (OperationCanceledException)
            {
                if (File.Exists(destinationPath))
                    File.Delete(destinationPath);
                throw;
            }
        }, ct);
    }
}
