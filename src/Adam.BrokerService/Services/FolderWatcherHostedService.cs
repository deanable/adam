using System.Collections.Concurrent;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Adam.Shared.Services;
using Adam.Shared.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Adam.BrokerService.Services;

/// <summary>
/// Hosted service that watches configured folders for new or modified files
/// and auto-indexes them into the catalog database.
/// </summary>
public sealed class FolderWatcherHostedService : IHostedService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<FolderWatcherHostedService> _logger;
    private readonly IConfiguration _configuration;
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly ConcurrentDictionary<string, System.Threading.Timer> _debounceTimers = new();
    private readonly TimeSpan _debounceWindow = TimeSpan.FromMilliseconds(500);
    private System.Threading.Timer? _refreshTimer;

    public FolderWatcherHostedService(IServiceProvider serviceProvider, ILogger<FolderWatcherHostedService> logger, IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        await RefreshWatchersAsync(ct);
        _refreshTimer = new System.Threading.Timer(async _ =>
        {
            try
            {
                await RefreshWatchersAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing folder watchers");
            }
        }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    private async Task RefreshWatchersAsync(CancellationToken ct)
    {
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Config-based folders
        var configFolders = _configuration.GetSection("Broker:WatchedFolders").Get<string[]>() ?? Array.Empty<string>();
        foreach (var f in configFolders)
            folders.Add(f);

        // DB-based folders
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var dbFolders = await db.WatchedFolders
                .AsNoTracking()
                .Where(w => w.IsEnabled)
                .Select(w => w.Path)
                .ToListAsync(ct);
            foreach (var f in dbFolders)
                folders.Add(f);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load watched folders from database");
        }

        // Stop watchers for removed folders
        var toRemove = _watchers.Where(w => !folders.Contains(w.Path)).ToList();
        foreach (var watcher in toRemove)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
            _watchers.Remove(watcher);
            _logger.LogInformation("Stopped watching folder: {Folder}", watcher.Path);
        }

        // Start watchers for new folders
        foreach (var folder in folders)
        {
            if (!_watchers.Any(w => string.Equals(w.Path, folder, StringComparison.OrdinalIgnoreCase)))
            {
                if (Directory.Exists(folder))
                {
                    StartWatcher(folder);
                }
                else
                {
                    _logger.LogWarning("Watched folder does not exist: {Folder}", folder);
                }
            }
        }

        _logger.LogDebug("Folder watcher refresh: {Count} active folder(s)", _watchers.Count);
    }

    public Task StopAsync(CancellationToken ct)
    {
        _refreshTimer?.Dispose();
        _refreshTimer = null;

        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        _watchers.Clear();

        foreach (var timer in _debounceTimers.Values)
        {
            timer.Dispose();
        }
        _debounceTimers.Clear();

        _logger.LogInformation("Folder watcher stopped");
        return Task.CompletedTask;
    }

    private void StartWatcher(string folder)
    {
        var watcher = new FileSystemWatcher(folder)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        watcher.Created += (_, e) => Debounce(e.FullPath, () => HandleCreatedAsync(e.FullPath));
        watcher.Changed += (_, e) => Debounce(e.FullPath, () => HandleChangedAsync(e.FullPath));
        watcher.Renamed += (_, e) => Debounce(e.FullPath, () => HandleRenamedAsync(e.OldFullPath, e.FullPath));
        watcher.Deleted += (_, e) => Debounce(e.FullPath, () => HandleDeletedAsync(e.FullPath));

        _watchers.Add(watcher);
        _logger.LogInformation("Watching folder: {Folder}", folder);
    }

    private void Debounce(string path, Func<Task> action)
    {
        if (_debounceTimers.TryGetValue(path, out System.Threading.Timer? existingTimer))
        {
            existingTimer?.Dispose();
            _debounceTimers.TryRemove(path, out System.Threading.Timer? _);
        }

        var timer = new System.Threading.Timer(async _ =>
        {
            _debounceTimers.TryRemove(path, out System.Threading.Timer? _);
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing watched file: {Path}", path);
            }
        }, null, _debounceWindow, Timeout.InfiniteTimeSpan);

        _debounceTimers[path] = timer;
    }

    private async Task HandleCreatedAsync(string path)
    {
        await IngestFileAsync(path, isNew: true);
    }

    private async Task HandleChangedAsync(string path)
    {
        await IngestFileAsync(path, isNew: false);
    }

    private async Task IngestFileAsync(string path, bool isNew)
    {
        if (!IsSupportedFile(path)) return;

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var validator = new AssetValidator();
        var checksumService = new ChecksumService();
        var thumbnailService = new ThumbnailService();
        var metadataExtractor = new MetadataExtractorService();

        try
        {
            // Compute checksum to detect duplicates
            var sha256 = await checksumService.ComputeSha256Async(path);
            var existing = await db.DigitalAssets
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(a => a.ChecksumSha256 == sha256);
            if (existing != null)
            {
                _logger.LogDebug("Skipping duplicate file: {Path} (matches asset {AssetId})", path, existing.Id);
                return;
            }

            var fileInfo = new FileInfo(path);

            // Validate
            var validation = validator.ValidateForIngestion(path, fileInfo.Length, fileInfo.Name, [], null);
            if (!validation.IsValid)
            {
                _logger.LogWarning("Validation failed for {Path}: {Errors}", path, string.Join(", ", validation.Errors));
                return;
            }

            var asset = new DigitalAsset
            {
                Id = Guid.NewGuid(),
                FileName = fileInfo.Name,
                FileExtension = fileInfo.Extension.TrimStart('.').ToLowerInvariant(),
                MimeType = GetMimeType(path),
                FileSize = fileInfo.Length,
                ChecksumSha256 = sha256,
                StoragePath = path,
                OriginalPath = path,
                Title = Path.GetFileNameWithoutExtension(fileInfo.Name),
                Description = "",
                Type = AssetType.Other,
                IsDeleted = false,
                Version = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                ModifiedAt = DateTimeOffset.UtcNow
            };

            // Extract metadata
            try
            {
                var metadata = metadataExtractor.Extract(path);
                if (metadata != null)
                {
                    asset.MetadataProfile = metadata;
                    if (!string.IsNullOrEmpty(metadata.Title))
                        asset.Title = metadata.Title;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Metadata extraction failed for {Path}", path);
            }

            // Determine asset type from mime type
            asset.Type = GetAssetType(asset.MimeType);

            // Generate thumbnail
            try
            {
                var thumbDir = Path.Combine(Path.GetTempPath(), "adam", "thumbnails");
                var thumbnailPath = await thumbnailService.GenerateThumbnailAsync(path, thumbDir);
                _logger.LogDebug("Generated thumbnail for {Path}: {ThumbnailPath}", path, thumbnailPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Thumbnail generation failed for {Path}", path);
            }

            if (isNew)
            {
                db.DigitalAssets.Add(asset);
                _logger.LogInformation("Auto-indexed new file: {Path} -> {AssetId}", path, asset.Id);
            }
            else
            {
                var toUpdate = await db.DigitalAssets.FirstOrDefaultAsync(a => a.StoragePath == path && !a.IsDeleted);
                if (toUpdate != null)
                {
                    toUpdate.FileSize = asset.FileSize;
                    toUpdate.ChecksumSha256 = asset.ChecksumSha256;
                    toUpdate.ModifiedAt = DateTimeOffset.UtcNow;
                    toUpdate.Version++;
                    _logger.LogInformation("Updated existing file: {Path} -> {AssetId}", path, toUpdate.Id);
                }
                else
                {
                    db.DigitalAssets.Add(asset);
                    _logger.LogInformation("Auto-indexed changed file: {Path} -> {AssetId}", path, asset.Id);
                }
            }

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to auto-index file: {Path}", path);
        }
    }

    private async Task HandleRenamedAsync(string oldPath, string newPath)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var asset = await db.DigitalAssets.FirstOrDefaultAsync(a => a.StoragePath == oldPath && !a.IsDeleted);
        if (asset != null)
        {
            asset.StoragePath = newPath;
            asset.OriginalPath = newPath;
            asset.FileName = Path.GetFileName(newPath);
            await db.SaveChangesAsync();
            _logger.LogInformation("Updated storage path for asset {AssetId}: {OldPath} -> {NewPath}", asset.Id, oldPath, newPath);
        }
    }

    private async Task HandleDeletedAsync(string path)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var asset = await db.DigitalAssets.FirstOrDefaultAsync(a => a.StoragePath == path && !a.IsDeleted);
        if (asset != null)
        {
            asset.IsDeleted = true;
            asset.ModifiedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            _logger.LogInformation("Soft-deleted asset {AssetId} (file deleted: {Path})", asset.Id, path);
        }
    }

    private static bool IsSupportedFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".tiff" or ".tif"
            or ".mp4" or ".mov" or ".avi" or ".mkv" or ".wmv" or ".webm" or ".m4v"
            or ".mp3" or ".wav" or ".flac" or ".aac" or ".ogg" or ".m4a" or ".wma"
            or ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx";
    }

    private static string GetMimeType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            ".tiff" or ".tif" => "image/tiff",
            ".mp4" => "video/mp4",
            ".mov" => "video/quicktime",
            ".avi" => "video/x-msvideo",
            ".mkv" => "video/x-matroska",
            ".wmv" => "video/x-ms-wmv",
            ".webm" => "video/webm",
            ".m4v" => "video/x-m4v",
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".flac" => "audio/flac",
            ".aac" => "audio/aac",
            ".ogg" => "audio/ogg",
            ".m4a" => "audio/mp4",
            ".wma" => "audio/x-ms-wma",
            ".pdf" => "application/pdf",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".ppt" => "application/vnd.ms-powerpoint",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            _ => "application/octet-stream"
        };
    }

    private static AssetType GetAssetType(string mimeType)
    {
        if (mimeType.StartsWith("image/")) return AssetType.Image;
        if (mimeType.StartsWith("video/")) return AssetType.Video;
        if (mimeType.StartsWith("audio/")) return AssetType.Audio;
        if (mimeType == "application/pdf") return AssetType.Document;
        return AssetType.Other;
    }

    public void Dispose()
    {
        StopAsync(CancellationToken.None).Wait();
    }
}
