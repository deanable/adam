using Adam.Shared.Data;
using Adam.Shared.Models;
using Adam.Shared.Validation;
using Adam.Shared.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.Shared.Services;

/// <summary>
/// Reusable service for scanning a folder and ingesting new assets (T15.4).
/// Extracted from the IngestionViewModel pattern — handles file discovery,
/// validation, duplicate checking, metadata extraction, and thumbnail generation.
/// </summary>
public class FolderScanService
{
    private readonly ModeManager _modeManager;
    private readonly AssetValidator _validator = new();
    private readonly ThumbnailService _thumbnailService = new();
    private readonly ChecksumService _checksumService = new();
    private readonly MetadataExtractorService _metadataExtractor = new();
    private readonly OfficeDocumentExtractor _officeExtractor = new();
    private readonly ILogger<FolderScanService> _logger;

    public FolderScanService(
        ModeManager modeManager,
        ILogger<FolderScanService>? logger = null)
    {
        _modeManager = modeManager;
        _logger = logger ?? NullLogger<FolderScanService>.Instance;
    }

    /// <summary>
    /// Scans the specified folder for new assets, ingests any non-duplicate files,
    /// and returns the count of newly ingested assets (T15.4).
    /// </summary>
    /// <param name="folderPath">Absolute path to the folder to scan.</param>
    /// <param name="recursive">Whether to scan subdirectories recursively.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Number of newly ingested assets.</returns>
    public async Task<int> ScanFolderAsync(string folderPath, bool recursive = true, CancellationToken ct = default)
    {
        if (!Directory.Exists(folderPath))
        {
            _logger.LogWarning("Scan folder does not exist: {Path}", folderPath);
            return 0;
        }

        _logger.LogInformation("Starting folder scan: {Path} (recursive={Recursive})", folderPath, recursive);

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.EnumerateFiles(folderPath, "*", searchOption)
            .Where(f => FileTypeHelper.IsSupported(Path.GetExtension(f)))
            .ToList();

        _logger.LogInformation("Found {Count} supported files in {Path}", files.Count, folderPath);

        var ingested = 0;
        var skipped = 0;
        var errors = 0;

        foreach (var filePath in files)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var fileInfo = new FileInfo(filePath);
                var extension = fileInfo.Extension;

                var validation = _validator.ValidateForIngestion(filePath, fileInfo.Length,
                    Path.GetFileNameWithoutExtension(filePath), [], null);
                if (!validation.IsValid)
                {
                    skipped++;
                    _logger.LogDebug("Skipped (validation): {File} — {Errors}", filePath, string.Join("; ", validation.Errors));
                    continue;
                }

                var checksum = await _checksumService.ComputeSha256Async(filePath, ct);

                // Check for duplicates
                await using (var checkDb = await _modeManager.CreateDbContextAsync(ct).ConfigureAwait(false))
                {
                    var existing = await checkDb.DigitalAssets
                        .FirstOrDefaultAsync(a => a.ChecksumSha256 == checksum, ct);
                    if (existing != null)
                    {
                        skipped++;
                        _logger.LogDebug("Skipped (duplicate): {File}", filePath);
                        continue;
                    }
                }

                var assetType = GetAssetType(extension);

                // Generate thumbnail
                var dbDir = Path.GetDirectoryName(_modeManager.DbPath) ?? ".";
                var thumbDir = Path.Combine(dbDir, "thumbnails");
                try
                {
                    await _thumbnailService.GenerateThumbnailAsync(filePath, thumbDir, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Thumbnail generation failed for {File}", filePath);
                }

                // Extract metadata
                ExtractedTextMetadata? textMetadata = null;
                if (assetType == AssetType.Image)
                    textMetadata = _metadataExtractor.ExtractTextMetadata(filePath);
                else if (assetType == AssetType.Document)
                    textMetadata = _officeExtractor.Extract(filePath);

                MetadataProfile? metadata = null;
                if (assetType == AssetType.Image)
                {
                    try { metadata = _metadataExtractor.Extract(filePath); }
                    catch (Exception ex) { _logger.LogWarning(ex, "Metadata extraction failed for {File}", filePath); }
                }

                // Create and save the asset
                await using var db = await _modeManager.CreateDbContextAsync(ct).ConfigureAwait(false);

                var assetId = Guid.NewGuid();
                var asset = new DigitalAsset
                {
                    Id = assetId,
                    FileName = fileInfo.Name,
                    FileExtension = extension,
                    MimeType = GetMimeType(extension),
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
                    await new KeywordService(db).AssociateKeywordsAsync(asset, deduped, isAiGenerated: false, ct);
                }
                if (textMetadata?.Categories.Count > 0)
                {
                    await new CategoryService(db).AssociateCategoriesAsync(asset, textMetadata.Categories, isAiGenerated: false, ct);
                }

                db.DigitalAssets.Add(asset);
                await db.SaveChangesAsync(ct);

                ingested++;
                _logger.LogDebug("Ingested: {File} -> asset={AssetId}", filePath, assetId);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors++;
                _logger.LogError(ex, "Failed to ingest {File}", filePath);
            }
        }

        _logger.LogInformation(
            "Folder scan complete: {Path} — Ingested={Ingested}, Skipped={Skipped}, Errors={Errors}",
            folderPath, ingested, skipped, errors);

        return ingested;
    }

    private static List<string> DeduplicateKeywords(List<string> keywords)
    {
        return keywords
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string GetMimeType(string ext) => FileTypeHelper.GetMimeType("x" + ext);
    private static AssetType GetAssetType(string ext) => FileTypeHelper.GetAssetType("x" + ext);
}
