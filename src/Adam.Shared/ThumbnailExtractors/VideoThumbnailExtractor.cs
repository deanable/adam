using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LibVLCSharp.Shared;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace Adam.Shared.ThumbnailExtractors;

/// <summary>
/// Extracts a frame from video files using LibVLCSharp.
/// Requires the VLC native libraries to be present on the system or bundled with the app.
/// </summary>
public class VideoThumbnailExtractor : IThumbnailExtractor
{
    public int Priority => 200;

    public bool CanExtract(string filePath, string mimeType)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext is ".mp4" or ".avi" or ".mov" or ".mkv" or ".wmv" or ".webm" or ".flv";
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

            // Initialize LibVLC (lazy — only when needed)
            Core.Initialize();
            using var libVlc = new LibVLC();
            using var media = new Media(libVlc, sourcePath);
            using var mediaPlayer = new MediaPlayer(media);

            // Take snapshot at approximately 1 second
            mediaPlayer.Play();

            // Wait briefly for playback to start, then seek
            Thread.Sleep(500);
            mediaPlayer.Time = 1000; // 1 second in ms
            Thread.Sleep(200);

            var snapshotDir = Path.GetDirectoryName(destPath)!;
            Directory.CreateDirectory(snapshotDir);

            // LibVLC TakeSnapshot saves to a directory with auto-generated filename
            // We then move it to our desired destPath
            var tempDir = Path.Combine(snapshotDir, $".vlcsnap_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            mediaPlayer.TakeSnapshot(0, tempDir, 0, 0);

            // Find the generated snapshot
            var files = Directory.GetFiles(tempDir, "*.png");
            if (files.Length == 0)
            {
                Directory.Delete(tempDir, true);
                return Task.FromResult(false);
            }

            // Convert PNG snapshot to JPEG and resize
            var snapshotPath = files[0];
            using var image = Image.Load(snapshotPath);
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

            // Cleanup
            Directory.Delete(tempDir, true);

            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }
}
