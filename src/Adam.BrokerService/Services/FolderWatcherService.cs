using Adam.Shared.Data;
using Adam.Shared.Models;
using Adam.Shared.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Adam.BrokerService.Services;

public class FolderWatcherService : IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<FolderWatcherService> _logger;
    private readonly FileIndexer _fileIndexer;
    private readonly ChecksumService _checksumService;
    private readonly MetadataExtractorService _metadataExtractor = new();
    private FileSystemWatcher? _watcher;
    private readonly HashSet<string> _pendingEvents = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer? _debounceTimer;
    private bool _disposed;

    public FolderWatcherService(
        IServiceProvider serviceProvider,
        ILogger<FolderWatcherService> logger,
        FileIndexer fileIndexer,
        ChecksumService checksumService)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _fileIndexer = fileIndexer;
        _checksumService = checksumService;
        _debounceTimer = new Timer(_ => ProcessPendingEvents(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public bool IsWatching => _watcher != null;

    public void Start(string watchPath)
    {
        if (!Directory.Exists(watchPath))
        {
            _logger.LogWarning("Watch path does not exist: {Path}", watchPath);
            return;
        }

        _watcher = new FileSystemWatcher(watchPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        _watcher.Created += OnFileEvent;
        _watcher.Changed += OnFileEvent;
        _watcher.Renamed += OnRenamed;
        _watcher.Error += OnWatcherError;

        _logger.LogInformation("Folder watcher started on: {Path}", watchPath);
    }

    public void Stop()
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
            _logger.LogInformation("Folder watcher stopped");
        }
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        if (!FileTypeHelper.IsSupported(e.Name ?? "")) return;

        lock (_pendingEvents)
        {
            _pendingEvents.Add(e.FullPath);
        }
        _debounceTimer?.Change(2000, Timeout.Infinite);
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        if (!FileTypeHelper.IsSupported(e.Name ?? "")) return;

        lock (_pendingEvents)
        {
            _pendingEvents.Remove(e.OldFullPath);
            _pendingEvents.Add(e.FullPath);
        }
        _debounceTimer?.Change(2000, Timeout.Infinite);
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        _logger.LogError(e.GetException(), "Folder watcher error");
    }

    private async void ProcessPendingEvents()
    {
        string[] paths;
        lock (_pendingEvents)
        {
            paths = _pendingEvents.ToArray();
            _pendingEvents.Clear();
        }

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        foreach (var path in paths)
        {
            try
            {
                if (!File.Exists(path)) continue;

                var hash = await _checksumService.ComputeSha256Async(path);
                var exists = await db.DigitalAssets.AnyAsync(a => a.ChecksumSha256 == hash);
                if (exists) continue;

                var asset = await _fileIndexer.IndexFileAsync(path, Path.GetDirectoryName(path) ?? "", CancellationToken.None);

                db.DigitalAssets.Add(asset);

                var assetType = _fileIndexer.GetAssetType(path);
                if (assetType == AssetType.Image)
                {
                    var textMetadata = _metadataExtractor.ExtractTextMetadata(path);
                    if (textMetadata.Keywords.Count > 0)
                    {
                        await db.AssociateKeywordsAsync(asset, textMetadata.Keywords);
                    }
                    if (textMetadata.Categories.Count > 0)
                    {
                        await db.AssociateCategoriesAsync(asset, textMetadata.Categories);
                    }
                }

                await db.SaveChangesAsync();

                _logger.LogInformation("Auto-indexed: {Path}", path);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to index: {Path}", path);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _debounceTimer?.Dispose();
    }
}
