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

namespace Adam.CatalogBrowser.ViewModels;

public class IngestionViewModel : INotifyPropertyChanged
{
    private readonly ModeManager _modeManager;
    private readonly AssetValidator _validator = new();
    private readonly DuplicateDetector _duplicateDetector;
    private readonly ThumbnailService _thumbnailService = new();
    private readonly ChecksumService _checksumService = new();
    private readonly MetadataExtractorService _metadataExtractor = new();
    private readonly ILogger<IngestionViewModel> _logger;
    private int _progressValue;
    private string _progressText = string.Empty;
    private bool _isIngesting;
    private string _ingestionStatus = string.Empty;

    public event Action? IngestionCompleted;

    public IngestionViewModel(ModeManager modeManager, DuplicateDetector duplicateDetector, ILogger<IngestionViewModel> logger)
    {
        _modeManager = modeManager;
        _duplicateDetector = duplicateDetector;
        _logger = logger;
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
            await using var db = _modeManager.CreateDbContext();
            var storagePaths = await db.DigitalAssets
                .Select(a => a.StoragePath)
                .Where(p => p != null && p.Length > 0)
                .Distinct()
                .ToListAsync();

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
        IsIngesting = true;
        IngestionStatus = string.Empty;
        var ingested = 0;
        var skipped = 0;
        var errors = 0;
        var total = PendingFiles.Count;

        _logger.LogInformation("Starting ingestion of {Total} file(s)", total);

        for (var i = 0; i < total; i++)
        {
            var filePath = PendingFiles[i];
            var fileInfo = new FileInfo(filePath);

            var validation = _validator.ValidateForIngestion(filePath, fileInfo.Length,
                Path.GetFileNameWithoutExtension(filePath), [], null);
            if (!validation.IsValid)
            {
                skipped++;
                ProgressText = $"Skipped: {filePath} \u2014 {string.Join("; ", validation.Errors)}";
                ProgressValue = (int)((i + 1.0) / total * 100);
                continue;
            }

            try
            {
                await using var db = _modeManager.CreateDbContext();

                var existing = await _duplicateDetector.FindDuplicateAsync(filePath, db);
                if (existing != null)
                {
                    skipped++;
                    ProgressText = $"Skipped (duplicate): {filePath}";
                    ProgressValue = (int)((i + 1.0) / total * 100);
                    continue;
                }

                ProgressText = $"Ingesting: {fileInfo.Name}";

                var assetType = GetAssetType(fileInfo.Extension);
                var textMetadata = assetType == AssetType.Image
                    ? _metadataExtractor.ExtractTextMetadata(filePath)
                    : null;

                var asset = new DigitalAsset
                {
                    Id = Guid.NewGuid(),
                    FileName = fileInfo.Name,
                    FileExtension = fileInfo.Extension,
                    MimeType = GetMimeType(fileInfo.Extension),
                    FileSize = fileInfo.Length,
                    ChecksumSha256 = await _checksumService.ComputeSha256Async(filePath),
                    StoragePath = filePath.Replace('\\', '/'),
                    OriginalPath = filePath.Replace('\\', '/'),
                    Title = !string.IsNullOrWhiteSpace(textMetadata?.Title)
                        ? textMetadata.Title!
                        : Path.GetFileNameWithoutExtension(filePath),
                    Description = textMetadata?.Description,
                    Tags = textMetadata?.Keywords.ToArray() ?? [],
                    Type = assetType,
                    CreatedAt = DateTimeOffset.UtcNow,
                    ModifiedAt = DateTimeOffset.UtcNow
                };

                var thumbDir = Path.Combine(Path.GetDirectoryName(_modeManager.DbPath) ?? ".", "thumbnails");
                try
                {
                    var thumbPath = await _thumbnailService.GenerateThumbnailAsync(filePath, thumbDir);
                    _logger.LogInformation("Thumbnail generated: {SourcePath} -> {ThumbPath}", filePath, thumbPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Thumbnail generation failed for {FilePath}", filePath);
                }

                if (asset.Type == AssetType.Image)
                {
                    try
                    {
                        var metadata = _metadataExtractor.Extract(filePath);
                        metadata.DigitalAssetId = asset.Id;
                        if (textMetadata?.Rating.HasValue == true)
                            metadata.Rating = textMetadata.Rating.Value;
                        asset.MetadataProfile = metadata;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Metadata extraction failed for {FilePath}", filePath);
                    }
                }

                if (textMetadata?.Keywords.Count > 0)
                {
                    var deduped = DeduplicateKeywords(textMetadata.Keywords);
                    await db.AssociateKeywordsAsync(asset, deduped);
                }

                db.DigitalAssets.Add(asset);
                await db.SaveChangesAsync();

                ingested++;
            }
            catch (Exception ex)
            {
                errors++;
                _logger.LogError(ex, "Failed to ingest {FilePath}", filePath);
                ProgressText = $"Error: {fileInfo.Name} \u2014 {ex.Message}";
            }

            ProgressValue = (int)((i + 1.0) / total * 100);
        }

        IsIngesting = false;
        ClearFiles();
        _logger.LogInformation("Ingestion complete \u2014 Ingested={Ingested}, Skipped={Skipped}, Errors={Errors}", ingested, skipped, errors);
        IngestionStatus = $"Ingested: {ingested}, Skipped: {skipped}, Errors: {errors}";
        ProgressText = IngestionStatus;
        IngestionCompleted?.Invoke();
    }

    private static List<string> DeduplicateKeywords(List<string> keywords)
    {
        var sorted = keywords
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(k => k.Length)
            .ToList();

        var result = new List<string>();
        foreach (var kw in sorted)
        {
            if (!result.Any(r => r.StartsWith(kw, StringComparison.OrdinalIgnoreCase)))
                result.Add(kw);
        }
        return result;
    }

    private static string GetMimeType(string ext) => ext.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".tiff" or ".tif" => "image/tiff",
        ".cr2" => "image/x-canon-cr2",
        ".nef" => "image/x-nikon-nef",
        ".arw" => "image/x-sony-arw",
        ".dng" => "image/x-adobe-dng",
        ".mp4" => "video/mp4",
        ".mov" => "video/quicktime",
        ".pdf" => "application/pdf",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".txt" => "text/plain",
        ".mp3" => "audio/mpeg",
        ".wav" => "audio/wav",
        _ => "application/octet-stream"
    };

    private static AssetType GetAssetType(string ext) => ext.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" or ".png" or ".webp" or ".tiff" or ".tif" or ".cr2" or ".nef" or ".arw" or ".dng" => AssetType.Image,
        ".mp4" or ".mov" => AssetType.Video,
        ".pdf" or ".docx" or ".txt" => AssetType.Document,
        ".mp3" or ".wav" => AssetType.Audio,
        _ => AssetType.Other
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
