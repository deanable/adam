using System.Security.Cryptography;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Adam.Shared.Services;

public class FileIndexer
{
    private readonly IFileService _fileService;
    private readonly MetadataExtractorService _metadataExtractor;
    private readonly ILogger<FileIndexer> _logger;

    public FileIndexer(IFileService fileService, MetadataExtractorService metadataExtractor, ILogger<FileIndexer> logger)
    {
        _fileService = fileService;
        _metadataExtractor = metadataExtractor;
        _logger = logger;
    }

    public bool IsSupported(string filePath) => FileTypeHelper.IsSupported(filePath);

    public AssetType GetAssetType(string filePath) => FileTypeHelper.GetAssetType(filePath);

    public string GetMimeType(string filePath) => FileTypeHelper.GetMimeType(filePath);

    public async Task<DigitalAsset> IndexFileAsync(string filePath, string rootPath, CancellationToken ct = default)
    {
        var fileInfo = new FileInfo(filePath);
        var ext = Path.GetExtension(filePath);
        var assetType = GetAssetType(filePath);

        var textMetadata = assetType == AssetType.Image
            ? _metadataExtractor.ExtractTextMetadata(filePath)
            : null;

        var asset = new DigitalAsset
        {
            Id = Guid.NewGuid(),
            FileName = fileInfo.Name,
            FileExtension = ext,
            MimeType = GetMimeType(filePath),
            FileSize = fileInfo.Length,
            StoragePath = filePath.Replace('\\', '/'),
            OriginalPath = filePath.Replace('\\', '/'),
            Title = !string.IsNullOrWhiteSpace(textMetadata?.Title)
                ? textMetadata.Title!
                : Path.GetFileNameWithoutExtension(filePath),
            Description = textMetadata?.Description,
            Type = assetType,
            ChecksumSha256 = await _fileService.ComputeChecksumAsync(filePath, ct),
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow
        };

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

        return asset;
    }

    public async Task<List<DigitalAsset>> ScanDirectoryAsync(string rootPath, IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        _logger.LogInformation("Starting directory scan: {RootPath}", rootPath);

        var assets = new List<DigitalAsset>();
        var files = Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories);

        var total = 0;
        var processed = 0;
        var skipped = 0;
        var errors = 0;

        foreach (var filePath in files)
        {
            ct.ThrowIfCancellationRequested();
            total++;

            if (!IsSupported(filePath))
            {
                skipped++;
                continue;
            }

            try
            {
                var asset = await IndexFileAsync(filePath, rootPath, ct);
                assets.Add(asset);
                processed++;
            }
            catch (Exception ex)
            {
                errors++;
                _logger.LogError(ex, "Error indexing {FilePath}", filePath);
            }

            progress?.Report(new ScanProgress(processed, skipped, errors, total, filePath));
        }

        _logger.LogInformation("Directory scan complete: {RootPath} \u2014 Processed={Processed}, Skipped={Skipped}, Errors={Errors}",
            rootPath, processed, skipped, errors);

        return assets;
    }
}

public record ScanProgress(int Processed, int Skipped, int Errors, int Total, string CurrentFile);
