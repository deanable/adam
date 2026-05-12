using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Adam.CatalogBrowser.Services;
using Adam.Shared.Models;
using Adam.Shared.Services;
using Adam.Shared.Services.Storage;
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
    private readonly LocalFileSystemProvider _storageProvider = new();
    private readonly ILogger<IngestionViewModel> _logger;
    private int _progressValue;
    private string _progressText = string.Empty;
    private bool _isIngesting;
    private string _ingestionStatus = string.Empty;

    public IngestionViewModel(ModeManager modeManager, DuplicateDetector duplicateDetector, ILogger<IngestionViewModel> logger)
    {
        _modeManager = modeManager;
        _duplicateDetector = duplicateDetector;
        _logger = logger;
        StartIngestionCommand = new RelayCommand(async _ => await StartIngestionAsync(), _ => !IsIngesting && PendingFiles.Count > 0);
        ClearCommand = new RelayCommand(_ => ClearFiles());
    }

    public ObservableCollection<string> PendingFiles { get; } = [];

    public bool HasPendingFiles => PendingFiles.Count > 0;

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

                var storageDir = Path.Combine(Path.GetDirectoryName(_modeManager.DbPath) ?? ".", "storage");
                var storedPath = await _storageProvider.StoreFileAsync(filePath, storageDir, default);

                var asset = new DigitalAsset
                {
                    Id = Guid.NewGuid(),
                    FileName = fileInfo.Name,
                    FileExtension = fileInfo.Extension,
                    MimeType = GetMimeType(fileInfo.Extension),
                    FileSize = fileInfo.Length,
                    ChecksumSha256 = await _checksumService.ComputeSha256Async(filePath),
                    StoragePath = storedPath,
                    Title = Path.GetFileNameWithoutExtension(filePath),
                    Type = GetAssetType(fileInfo.Extension),
                    CreatedAt = DateTimeOffset.UtcNow,
                    ModifiedAt = DateTimeOffset.UtcNow
                };

                var thumbnailDir = Path.Combine(Path.GetDirectoryName(_modeManager.DbPath) ?? ".", "thumbnails");
                try
                {
                    var fullStoredPath = Path.Combine(storageDir, storedPath);
                    await _thumbnailService.GenerateThumbnailAsync(fullStoredPath, thumbnailDir);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Thumbnail generation failed for {FilePath}", filePath);
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
