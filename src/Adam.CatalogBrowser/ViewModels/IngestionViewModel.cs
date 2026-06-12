using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Adam.CatalogBrowser.Services;
using Adam.Shared.Models;
using Adam.Shared.Services;
using Adam.Shared.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Avalonia.Threading;

namespace Adam.CatalogBrowser.ViewModels;

public class IngestionViewModel : INotifyPropertyChanged
{
    private readonly ModeManager _modeManager;
    private readonly AssetValidator _validator = new();
    private readonly ThumbnailService _thumbnailService = new();
    private readonly ChecksumService _checksumService = new();
    private readonly MetadataExtractorService _metadataExtractor = new();
    private readonly ILogger<IngestionViewModel> _logger;
    private readonly AiTaggingService? _aiTaggingService;
    private int _progressValue;
    private string _progressText = string.Empty;
    private bool _isIngesting;
    private string _ingestionStatus = string.Empty;
    private CancellationTokenSource? _cts;
    private System.Diagnostics.Stopwatch? _ingestStopwatch;
    private bool _enableAiTagging;

    public event Action? IngestionCompleted;

    public IngestionViewModel(ModeManager modeManager, ILogger<IngestionViewModel> logger, AiTaggingService? aiTaggingService = null)
    {
        _modeManager = modeManager;
        _logger = logger;
        _aiTaggingService = aiTaggingService;
        StartIngestionCommand = new RelayCommand(async _ => await StartIngestionAsync(), _ => !IsIngesting && PendingFiles.Count > 0);
        ClearCommand = new RelayCommand(_ => ClearFiles());
        RefreshFolderBrowserCommand = new RelayCommand(async _ => await LoadIngestedFoldersAsync());
    }

    public ObservableCollection<string> PendingFiles { get; } = [];
    public ObservableCollection<FolderNode> IngestedFolders { get; } = [];

    public bool HasPendingFiles => PendingFiles.Count > 0;
    public bool HasIngestedFolders => IngestedFolders.Count > 0;

    public int ProgressValue
    {
        get => _progressValue;
        set { _progressValue = value; OnPropertyChanged(); }
    }

    public string ProgressText
    {
        get => _progressText;
        set { _progressText = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// When enabled, AI tagging runs as a sequential post-pass after the parallel ingest completes.
    /// (D-10: opt-in during ingest)
    /// </summary>
    public bool EnableAiTagging
    {
        get => _enableAiTagging;
        set { _enableAiTagging = value; OnPropertyChanged(); }
    }

    public bool IsIngesting
    {
        get => _isIngesting;
        set { _isIngesting = value; OnPropertyChanged(); }
    }

    public string IngestionStatus
    {
        get => _ingestionStatus;
        set { _ingestionStatus = value; OnPropertyChanged(); }
    }

    public RelayCommand StartIngestionCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand RefreshFolderBrowserCommand { get; }

    public void AddFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (!PendingFiles.Contains(path))
                PendingFiles.Add(path);
        }
        OnPropertyChanged(nameof(PendingFiles));
        OnPropertyChanged(nameof(HasPendingFiles));
        StartIngestionCommand.RaiseCanExecuteChanged();
    }

    public void ClearFiles()
    {
        PendingFiles.Clear();
        IngestionStatus = string.Empty;
        OnPropertyChanged(nameof(HasPendingFiles));
        StartIngestionCommand.RaiseCanExecuteChanged();
    }

    public async Task LoadIngestedFoldersAsync()
    {
        IngestedFolders.Clear();

        if (_modeManager.IsStandalone)
        {
            await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);
            var storagePaths = await db.DigitalAssets
                .Select(a => a.StoragePath)
                .Where(p => p != null && p.Length > 0)
                .Distinct()
                .ToListAsync().ConfigureAwait(false);

            var dirs = storagePaths
                .Select(p => Path.GetDirectoryName(p.Replace('\\', '/')) ?? "")
                .Where(d => d.Length > 0)
                .Distinct()
                .ToHashSet();

            var root = new FolderNode { Name = "All Ingested", Path = "", IsExpanded = true };
            foreach (var dir in dirs.OrderBy(p => p))
            {
                var parts = dir.Split('/');
                var current = root;
                var cumulative = "";
                foreach (var part in parts)
                {
                    if (part.Length == 0) continue;
                    cumulative = cumulative.Length == 0 ? part : $"{cumulative}/{part}";
                    var existing = current.Children.FirstOrDefault(c => c.Name == part);
                    if (existing == null)
                    {
                        existing = new FolderNode { Name = part, Path = cumulative };
                        current.Children.Add(existing);
                    }
                    current = existing;
                }
            }

            IngestedFolders.Add(root);
        }

        OnPropertyChanged(nameof(HasIngestedFolders));
    }

    public async Task StartIngestionAsync()
    {
        var ingestId = Guid.NewGuid().ToString("N")[..8];
        IsIngesting = true;
        IngestionStatus = string.Empty;
        var total = PendingFiles.Count;
        var files = PendingFiles.ToArray();

        _logger.LogInformation("[{IngestId}] Starting ingestion of {Total} file(s)", ingestId, total);

        _ingestStopwatch = System.Diagnostics.Stopwatch.StartNew();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        int ingested = 0, skipped = 0, errors = 0, processed = 0;
        var imageIds = new System.Collections.Concurrent.ConcurrentBag<Guid>();
        var dbLock = new SemaphoreSlim(1, 1);

        await Parallel.ForEachAsync(
            files,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = ct
            },
            async (filePath, ct) =>
            {
                var fileInfo = new FileInfo(filePath);

                var validation = _validator.ValidateForIngestion(filePath, fileInfo.Length,
                    Path.GetFileNameWithoutExtension(filePath), [], null);
                if (!validation.IsValid)
                {
                    Interlocked.Increment(ref skipped);
                    var p = Interlocked.Increment(ref processed);
                    _logger.LogInformation("[{IngestId}] Skipped (validation): {FilePath} \u2014 {Errors}", ingestId, filePath, string.Join("; ", validation.Errors));
                    await ReportProgressAsync(p, total, $"Skipped: {filePath} \u2014 {string.Join("; ", validation.Errors)}");
                    return;
                }

                _logger.LogInformation("[{IngestId}] Processing: {FilePath} (size={Size}, ext={Ext})", ingestId, filePath, fileInfo.Length, fileInfo.Extension);

                try
                {
                    var assetType = GetAssetType(fileInfo.Extension);

                    ExtractedTextMetadata? textMetadata = null;
                    if (assetType == AssetType.Image)
                    {
                        textMetadata = _metadataExtractor.ExtractTextMetadata(filePath);
                    }

                    _logger.LogInformation("[{IngestId}] Computing SHA256: {FilePath}", ingestId, filePath);
                    var checksum = await _checksumService.ComputeSha256Async(filePath, ct);
                    _logger.LogInformation("[{IngestId}] SHA256 computed: {FilePath} hash={Hash}", ingestId, filePath, checksum);

                    var thumbDir = Path.Combine(Path.GetDirectoryName(_modeManager.DbPath) ?? ".", "thumbnails");
                    try
                    {
                        var thumbPath = await _thumbnailService.GenerateThumbnailAsync(filePath, thumbDir, ct);
                        _logger.LogInformation("[{IngestId}] Thumbnail generated: {SourcePath} -> {ThumbPath}", ingestId, filePath, thumbPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[{IngestId}] Thumbnail generation failed for {FilePath}", ingestId, filePath);
                    }

                    MetadataProfile? metadata = null;
                    if (assetType == AssetType.Image)
                    {
                        try
                        {
                            _logger.LogInformation("[{IngestId}] Extracting metadata profile: {FilePath}", ingestId, filePath);
                            metadata = _metadataExtractor.Extract(filePath);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "[{IngestId}] Metadata extraction failed for {FilePath}", ingestId, filePath);
                        }
                    }

                    _logger.LogDebug("[{IngestId}] Acquiring DB lock: {FilePath}", ingestId, filePath);
                    await dbLock.WaitAsync(ct);
                    try
                    {
                        await using var db = await _modeManager.CreateDbContextAsync(ct).ConfigureAwait(false);

                        _logger.LogDebug("[{IngestId}] Checking duplicates: {FilePath} hash={Hash}", ingestId, filePath, checksum);
                        var existing = await db.DigitalAssets
                            .FirstOrDefaultAsync(a => a.ChecksumSha256 == checksum, ct);
                        if (existing != null)
                        {
                            Interlocked.Increment(ref skipped);
                            var p = Interlocked.Increment(ref processed);
                            _logger.LogInformation("[{IngestId}] Skipped (duplicate): {FilePath} matches existing asset {AssetId}", ingestId, filePath, existing.Id);
                            await ReportProgressAsync(p, total, $"Skipped (duplicate): {filePath}");
                            return;
                        }

                        var assetId = Guid.NewGuid();
                        var asset = new DigitalAsset
                        {
                            Id = assetId,
                            FileName = fileInfo.Name,
                            FileExtension = fileInfo.Extension,
                            MimeType = GetMimeType(fileInfo.Extension),
                            FileSize = fileInfo.Length,
                            ChecksumSha256 = checksum,
                            StoragePath = filePath.Replace('\\', '/'),
                            OriginalPath = filePath.Replace('\\', '/'),
                            Title = !string.IsNullOrWhiteSpace(textMetadata?.Title)
                                ? textMetadata.Title!
                                : Path.GetFileNameWithoutExtension(filePath),
                            Description = textMetadata?.Description,
                            Type = assetType,
                            CreatedAt = DateTimeOffset.UtcNow,
                            ModifiedAt = DateTimeOffset.UtcNow
                        };

                        if (metadata != null)
                        {
                            metadata.DigitalAssetId = asset.Id;
                            if (textMetadata?.Rating.HasValue == true)
                                metadata.Rating = textMetadata.Rating.Value;
                            asset.MetadataProfile = metadata;
                        }

                        if (textMetadata?.Keywords.Count > 0)
                        {
                            var deduped = DeduplicateKeywords(textMetadata.Keywords);
                            await new KeywordService(db).AssociateKeywordsAsync(asset, deduped, ct);
                        }
                        if (textMetadata?.Categories.Count > 0)
                        {
                            await new CategoryService(db).AssociateCategoriesAsync(asset, textMetadata.Categories, ct);
                        }

                        db.DigitalAssets.Add(asset);
                        await db.SaveChangesAsync(ct);

                        Interlocked.Increment(ref ingested);
                        _logger.LogInformation("[{IngestId}] Ingested: {FilePath} -> asset={AssetId}, title=\"{Title}\", keywords={KwCount}, categories={CatCount}",
                            ingestId, filePath, assetId, asset.Title, textMetadata?.Keywords.Count ?? 0, textMetadata?.Categories.Count ?? 0);

                        // Collect image asset IDs for optional AI tagging post-pass
                        if (assetType == AssetType.Image)
                            imageIds.Add(assetId);
                    }
                    finally
                    {
                        dbLock.Release();
                        _logger.LogDebug("[{IngestId}] Released DB lock: {FilePath}", ingestId, filePath);
                    }

                    var pDone = Interlocked.Increment(ref processed);
                    await ReportProgressAsync(pDone, total, $"Ingested: {fileInfo.Name}");
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref errors);
                    var p = Interlocked.Increment(ref processed);
                    if (ex is Microsoft.EntityFrameworkCore.DbUpdateException duex)
                    {
                        _logger.LogError(duex, "[{IngestId}] DB error ingesting {FilePath} (size={Size}, ext={Ext})", ingestId, filePath, fileInfo.Length, fileInfo.Extension);
                        foreach (var entry in duex.Entries)
                            _logger.LogError("[{IngestId}]   Entity: {Entity}, State: {State}", ingestId, entry.Entity.GetType().Name, entry.State);
                    }
                    else
                    {
                        _logger.LogError(ex, "[{IngestId}] Failed to ingest {FilePath} (size={Size}, ext={Ext})", ingestId, filePath, fileInfo.Length, fileInfo.Extension);
                    }
                    await ReportProgressAsync(p, total, $"Error: {fileInfo.Name} \u2014 {ex.GetType().Name}: {ex.Message}");
                }
            });

        _cts.Dispose();
        _cts = null;

        // Phase 9 Trigger A: sequential AI tagging post-pass (D-11)
        if (EnableAiTagging && _aiTaggingService != null && imageIds.Count > 0 && errors == 0 && !ct.IsCancellationRequested)
        {
            _logger.LogInformation("[{IngestId}] AI tagging post-pass starting for {Count} image(s)", ingestId, imageIds.Count);
            int aiDone = 0; var aiTotal = imageIds.Count;
            foreach (var id in imageIds)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await _aiTaggingService.TagAssetAsync(id, ct);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[{IngestId}] AI tagging failed for asset {AssetId}", ingestId, id);
                }
                aiDone++;
                await ReportProgressAsync(aiDone, aiTotal, "AI tagging…");
            }
            _logger.LogInformation("[{IngestId}] AI tagging post-pass complete", ingestId);
        }

        IsIngesting = false;
        ClearFiles();
        _logger.LogInformation("[{IngestId}] Ingestion complete \u2014 Ingested={Ingested}, Skipped={Skipped}, Errors={Errors}", ingestId, ingested, skipped, errors);
        IngestionStatus = $"Ingested: {ingested}, Skipped: {skipped}, Errors: {errors}";
        ProgressText = IngestionStatus;
        IngestionCompleted?.Invoke();
    }

    private async Task ReportProgressAsync(int processed, int total, string text)
    {
        string etaText = string.Empty;
        if (_ingestStopwatch != null && processed > 0 && processed < total)
        {
            var elapsed = _ingestStopwatch.Elapsed;
            var avgPerFile = elapsed.TotalSeconds / processed;
            var remainingSeconds = avgPerFile * (total - processed);
            var eta = TimeSpan.FromSeconds(remainingSeconds);
            etaText = $" (ETA {FormatEta(eta)})";
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            ProgressValue = (int)((double)processed / total * 100);
            ProgressText = $"{text}{etaText}";
        });
    }

    private static string FormatEta(TimeSpan eta)
    {
        if (eta.TotalHours >= 1)
            return $"{(int)eta.TotalHours}h {eta.Minutes}m";
        if (eta.TotalMinutes >= 1)
            return $"{eta.Minutes}m {eta.Seconds}s";
        return $"{eta.Seconds}s";
    }

    private static List<string> DeduplicateKeywords(List<string> keywords)
    {
        // Simple deduplication: trim, remove empties, distinct case-insensitive.
        // Do NOT remove prefix matches (e.g. "red" vs "redwood" are different keywords).
        return keywords
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string GetMimeType(string ext) => FileTypeHelper.GetMimeType("x" + ext);

    private static AssetType GetAssetType(string ext) => FileTypeHelper.GetAssetType("x" + ext);

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
