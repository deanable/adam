using System.Collections.ObjectModel;
using Adam.CatalogBrowser.Models.Sidebar;
using Adam.Shared.Contracts;
using Adam.Shared.Data;
using Adam.Shared.Services;
using Avalonia.Threading;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Adam.CatalogBrowser.Services;

/// <summary>
/// Loads folder tree from DB/broker, handles reveal and rescan operations.
/// </summary>
public sealed class FolderTreeService
{
    private readonly ModeManager _modeManager;
    private readonly ILogger<FolderTreeService> _logger;
    private readonly FolderScanService _folderScanService;

    public FolderTreeService(ModeManager modeManager, ILogger<FolderTreeService> logger, FolderScanService? folderScanService = null)
    {
        _modeManager = modeManager;
        _logger = logger;
        _folderScanService = folderScanService ?? new FolderScanService(modeManager, new PluginLoaderService(
            Microsoft.Extensions.Options.Options.Create(new Adam.Shared.Configuration.PluginConfig()),
            logger as ILogger<PluginLoaderService> ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<PluginLoaderService>.Instance));
    }

    public ObservableCollection<FolderNode> Roots { get; } = [];

    public async Task LoadAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("[FolderTreeService] Starting folder load. IsStandalone={IsStandalone}", _modeManager.IsStandalone);

        var paths = new HashSet<string>();
        var folderCounts = new Dictionary<string, int>();

        if (_modeManager.IsStandalone)
        {
            await using var db = await _modeManager.CreateDbContextAsync(ct).ConfigureAwait(false);
            _logger.LogInformation("[FolderTreeService] Querying directories from database...");

            var storagePaths = await db.DigitalAssets
                .Select(a => a.StoragePath)
                .Where(p => p != null)
                .ToListAsync(ct).ConfigureAwait(false);

            paths = storagePaths
                .Select(p => GetDirectoryName(p))
                .Where(d => d.Length > 0)
                .ToHashSet();

            var allPaths = await db.DigitalAssets
                .Select(a => a.StoragePath)
                .ToListAsync(ct).ConfigureAwait(false);

            folderCounts = allPaths
                .GroupBy(p => GetDirectoryName(p))
                .ToDictionary(g => g.Key, g => g.Count());

            _logger.LogInformation("[FolderTreeService] Retrieved {Count} distinct directories", paths.Count);
        }
        else if (_modeManager.BrokerClient != null)
        {
            var req = new Envelope
            {
                MessageType = MessageTypeCode.ListFoldersRequest,
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new ListFoldersRequest()))
            };
            var resp = await _modeManager.BrokerClient.SendAsync(req, ct).ConfigureAwait(false);
            if (resp.StatusCode == 0)
            {
                var data = ProtoHelper.Deserialize<ListFoldersResponse>(resp.Payload.ToByteArray());
                foreach (var f in data.Folders)
                {
                    paths.Add(f.Path);
                    folderCounts[f.Path] = f.AssetCount;
                }
                _logger.LogInformation("[FolderTreeService] Retrieved {Count} folders from broker", data.Folders.Count);
            }
        }

        // Build tree on background thread
        var root = new FolderNode { Name = "All Folders", Path = "", IsExpanded = true };
        foreach (var dir in paths.OrderBy(p => p))
        {
            var normalizedDir = dir.Replace('\\', '/');
            var isUnc = normalizedDir.StartsWith("//");
            var parts = normalizedDir.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var current = root;
            var cumulative = "";

            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (i == 0 && isUnc)
                    cumulative = "//" + part;
                else if (i == 0)
                    cumulative = part;
                else
                    cumulative = cumulative + "/" + part;

                var existing = current.Children.FirstOrDefault(c => c.Name == part);
                if (existing == null)
                {
                    existing = new FolderNode { Name = part, Path = cumulative };
                    current.Children.Add(existing);
                }
                current = existing;
            }
        }

        // Populate asset counts
        foreach (var (dir, count) in folderCounts)
        {
            var node = FindFolderNode(root, dir);
            if (node != null)
                node.AssetCount = count;
        }

        _logger.LogInformation("[FolderTreeService] Applied counts for {Count} folders", folderCounts.Count);

        // Propagate counts upward so parents show totals
        root.PropagateCounts();

        _logger.LogInformation("[FolderTreeService] Assigning Roots collection on UI thread");
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Roots.Clear();
            Roots.Add(root);
        });
        _logger.LogInformation("[FolderTreeService] Completed");
    }

    /// <summary>
    /// Opens the selected folder in the system file explorer.
    /// </summary>
    public void RevealFolder(FolderNode? folder)
    {
        if (folder == null || string.IsNullOrEmpty(folder.Path)) return;

        try
        {
            if (OperatingSystem.IsWindows())
                System.Diagnostics.Process.Start("explorer.exe", folder.Path);
            else if (OperatingSystem.IsMacOS())
                System.Diagnostics.Process.Start("open", folder.Path);
            else if (OperatingSystem.IsLinux())
                System.Diagnostics.Process.Start("xdg-open", folder.Path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reveal folder: {Path}", folder.Path);
        }
    }

    /// <summary>
    /// Triggers a re-scan/ingest of the selected folder.
    /// </summary>
    public async Task RescanFolderAsync(FolderNode? folder)
    {
        if (folder == null || string.IsNullOrEmpty(folder.Path)) return;

        try
        {
            _logger.LogInformation("Re-scanning folder: {Path}", folder.Path);
            var ingested = await _folderScanService.ScanFolderAsync(folder.Path, recursive: true);
            _logger.LogInformation("Re-scan complete for folder: {Path} — {Count} new asset(s) ingested", folder.Path, ingested);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rescan folder: {Path}", folder.Path);
        }
    }

    internal static FolderNode? FindFolderNode(FolderNode root, string path)
    {
        if (string.IsNullOrEmpty(path))
            return root;

        var normalizedPath = path.Replace('\\', '/');
        var parts = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = root;
        foreach (var part in parts)
        {
            current = current.Children.FirstOrDefault(c => c.Name == part);
            if (current == null)
                return null;
        }
        return current;
    }

    private static string GetDirectoryName(string path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        var lastSep = path.LastIndexOfAny(['/', '\\']);
        return lastSep > 0 ? path[..lastSep] : "";
    }
}
