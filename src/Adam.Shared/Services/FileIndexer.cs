using System.Security.Cryptography;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Adam.Shared.Services;

public class FileIndexer
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".tiff", ".tif",
        ".cr2", ".nef", ".arw", ".dng",
        ".mp4", ".mov",
        ".pdf", ".docx", ".txt",
        ".mp3", ".wav"
    };

    private static readonly Dictionary<string, AssetType> ExtensionTypeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = AssetType.Image, [".jpeg"] = AssetType.Image,
        [".png"] = AssetType.Image, [".webp"] = AssetType.Image,
        [".tiff"] = AssetType.Image, [".tif"] = AssetType.Image,
        [".cr2"] = AssetType.Image, [".nef"] = AssetType.Image,
        [".arw"] = AssetType.Image, [".dng"] = AssetType.Image,
        [".mp4"] = AssetType.Video, [".mov"] = AssetType.Video,
        [".pdf"] = AssetType.Document, [".docx"] = AssetType.Document,
        [".txt"] = AssetType.Document,
        [".mp3"] = AssetType.Audio, [".wav"] = AssetType.Audio
    };

    private readonly IFileService _fileService;
    private readonly MetadataExtractorService _metadataExtractor;
    private readonly ILogger<FileIndexer> _logger;

    public FileIndexer(IFileService fileService, MetadataExtractorService metadataExtractor, ILogger<FileIndexer> logger)
    {
        _fileService = fileService;
        _metadataExtractor = metadataExtractor;
        _logger = logger;
    }

    public bool IsSupported(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return SupportedExtensions.Contains(ext);
    }

    public AssetType GetAssetType(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return ExtensionTypeMap.TryGetValue(ext, out var type) ? type : AssetType.Other;
    }

    public string GetMimeType(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
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
    }

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
