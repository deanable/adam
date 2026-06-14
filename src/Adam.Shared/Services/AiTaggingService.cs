using System.ComponentModel;
using System.Runtime.CompilerServices;
using Adam.Shared.Data;
using LiquidVision.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Adam.Shared.Services;

/// <summary>
/// Represents a keyword with an estimated confidence score and accept/reject state.
/// Implements <see cref="INotifyPropertyChanged"/> so the review dialog's checkbox
/// bindings can notify the ViewModel to recalculate counts.
/// </summary>
public sealed class KeywordScore : INotifyPropertyChanged
{
    private bool _isAccepted = true;

    public string Name { get; set; } = string.Empty;
    public double Confidence { get; set; }

    public bool IsAccepted
    {
        get => _isAccepted;
        set
        {
            if (_isAccepted == value) return;
            _isAccepted = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAccepted)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Represents a category with an estimated confidence score and accept/reject state.
/// Implements <see cref="INotifyPropertyChanged"/> so the review dialog's checkbox
/// bindings can notify the ViewModel to recalculate counts.
/// </summary>
public sealed class CategoryScore : INotifyPropertyChanged
{
    private bool _isAccepted = true;

    public string Name { get; set; } = string.Empty;
    public double Confidence { get; set; }

    public bool IsAccepted
    {
        get => _isAccepted;
        set
        {
            if (_isAccepted == value) return;
            _isAccepted = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAccepted)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Structured AI tagging result with per-item confidence scores.
/// Confidence is estimated from keyword/category rank order
/// (the model outputs most relevant tags first).
/// </summary>
public sealed class AiTagResult
{
    public string? Description { get; set; }
    public List<KeywordScore> Keywords { get; set; } = [];
    public List<CategoryScore> Categories { get; set; } = [];
    public double ProcessingTimeMs { get; set; }
    public string ModelVersion { get; set; } = string.Empty;
}

/// <summary>
/// Orchestrates AI-powered image tagging via <see cref="ILiquidVisionAnalyzer"/>.
/// Provides lazy model initialization, image-only guard, and automatic merging
/// of AI-generated keywords, categories, and descriptions into the catalog.
/// Implements <see cref="INotifyPropertyChanged"/> to relay download progress
/// from the underlying analyzer.
/// </summary>
public sealed class AiTaggingService : INotifyPropertyChanged
{
    private readonly ILiquidVisionAnalyzer _analyzer;
    private readonly ModeManager _modeManager;
    private readonly ILogger<AiTaggingService> _logger;
    private bool _initializationAttempted;

    public AiTaggingService(
        ILiquidVisionAnalyzer analyzer,
        ModeManager modeManager,
        ILogger<AiTaggingService> logger)
    {
        _analyzer = analyzer;
        _modeManager = modeManager;
        _logger = logger;

        // Relay the analyzer's INPC events so the service can be subscribed to directly (D-14)
        analyzer.PropertyChanged += OnAnalyzerPropertyChanged;
    }

    /// <summary>
    /// Model download progress in the range [0, 1]. Relayed from the underlying analyzer.
    /// </summary>
    public double DownloadProgress => _analyzer.DownloadProgress;

    /// <summary>
    /// True once the model is downloaded, verified, and loaded.
    /// </summary>
    public bool IsInitialized => _analyzer.IsInitialized;

    /// <summary>
    /// Ensures the analyzer's model is downloaded and initialized.
    /// Safe to call multiple times — the analyzer's semaphore guard inside
    /// <see cref="ILiquidVisionAnalyzer.InitializeAsync"/> ensures only one
    /// download happens even with concurrent callers.
    /// </summary>
    public async Task EnsureInitializedAsync(IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (_initializationAttempted && _analyzer.IsInitialized)
            return;

        _initializationAttempted = true;
        await _analyzer.InitializeAsync(progress, ct);
    }

    /// <summary>
    /// Analyzes a single image asset and writes the AI-generated keywords,
    /// categories, and description directly to the catalog database.
    /// Non-image assets are silently skipped.
    /// </summary>
    public async Task TagAssetAsync(Guid assetId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(null, ct);

        await using var db = await _modeManager.CreateDbContextAsync(ct).ConfigureAwait(false);

        var asset = await db.DigitalAssets
            .Include(a => a.Keywords)
            .Include(a => a.Categories)
            .FirstOrDefaultAsync(a => a.Id == assetId, ct).ConfigureAwait(false);

        if (asset is null)
        {
            _logger.LogWarning("[AiTag] Asset {AssetId} not found, skipping", assetId);
            return;
        }

        // Image-only guard (D-04)
        if (asset.Type != Models.AssetType.Image)
        {
            _logger.LogDebug("[AiTag] Asset {AssetId} is not an image (type={Type}), skipping", assetId, asset.Type);
            return;
        }

        if (string.IsNullOrWhiteSpace(asset.StoragePath) || !File.Exists(asset.StoragePath))
        {
            _logger.LogWarning("[AiTag] Asset {AssetId} storage path '{Path}' not found, skipping", assetId, asset.StoragePath);
            return;
        }

        var result = await _analyzer.AnalyzeAsync(asset.StoragePath, ct);

        // Merge keywords (D-05)
        if (result.Keywords.Count > 0)
        {
            await new KeywordService(db).AssociateKeywordsAsync(asset, result.Keywords, ct);
        }

        // Merge categories (D-05)
        if (result.Categories.Count > 0)
        {
            await new CategoryService(db).AssociateCategoriesAsync(asset, result.Categories, ct);
        }

        // Fill description only when empty (D-06)
        if (string.IsNullOrWhiteSpace(asset.Description) && !string.IsNullOrWhiteSpace(result.Description))
        {
            asset.Description = result.Description;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogInformation(
            "[AiTag] Tagged asset {AssetId} — {Keywords} keywords, {Categories} categories, description={HasDesc}",
            assetId, result.Keywords.Count, result.Categories.Count, !string.IsNullOrWhiteSpace(result.Description));
    }

    /// <summary>
    /// Analyzes multiple image assets sequentially, tagging each one.
    /// Progress is reported via <paramref name="progress"/> as (completed, total).
    /// </summary>
    public async Task TagAssetsAsync(
        IEnumerable<Guid> assetIds,
        IProgress<(int completed, int total)>? progress = null,
        CancellationToken ct = default)
    {
        var ids = assetIds.ToList();
        if (ids.Count == 0) return;

        await EnsureInitializedAsync(null, ct);

        var completed = 0;
        var total = ids.Count;

        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                await TagAssetAsync(id, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AiTag] Failed to tag asset {AssetId}", id);
            }

            completed++;
            progress?.Report((completed, total));
        }
    }

    /// <summary>
    /// Analyzes a single image asset and returns the raw <see cref="ImageTagResult"/>
    /// WITHOUT writing anything to the database. Useful for Trigger B where results
    /// should flow through the editor's in-memory dirty/Save cycle.
    /// </summary>
    public async Task<ImageTagResult> AnalyzeAssetAsync(Guid assetId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(null, ct);

        await using var db = await _modeManager.CreateDbContextAsync(ct).ConfigureAwait(false);

        var asset = await db.DigitalAssets
            .FirstOrDefaultAsync(a => a.Id == assetId, ct).ConfigureAwait(false);

        if (asset is null)
            throw new InvalidOperationException($"Asset {assetId} not found");

        if (asset.Type != Models.AssetType.Image)
            throw new InvalidOperationException($"Asset {assetId} is not an image");

        if (string.IsNullOrWhiteSpace(asset.StoragePath) || !File.Exists(asset.StoragePath))
            throw new InvalidOperationException($"Asset {assetId} storage path not found");

        return await _analyzer.AnalyzeAsync(asset.StoragePath, ct);
    }

    /// <summary>
    /// Analyzes a single image asset and returns an <see cref="AiTagResult"/> with
    /// rank-based confidence scores estimated from keyword/category order.
    /// The model outputs the most relevant tags first, so rank position
    /// serves as a confidence heuristic:
    /// Top 25% → confidence 0.9+, next 50% → 0.6-0.9, bottom 25% → 0.3-0.6.
    /// </summary>
    public AiTagResult AnalyzeImageTagResult(ImageTagResult raw)
    {
        var result = new AiTagResult
        {
            Description = raw.Description,
            ProcessingTimeMs = raw.ProcessingTimeMs,
            ModelVersion = raw.ModelVersion
        };

        // Estimate confidence from rank order (first = highest confidence)
        var totalKw = raw.Keywords.Count;
        for (int i = 0; i < totalKw; i++)
        {
            var rank = (double)i / Math.Max(totalKw - 1, 1); // 0.0 = first, 1.0 = last
            var confidence = rank switch
            {
                <= 0.25 => 0.95 - rank * 0.1,  // top quarter: 0.925-0.95
                <= 0.75 => 0.85 - (rank - 0.25) * 0.6, // middle half: 0.55-0.85
                _ => 0.50 - (rank - 0.75) * 0.8  // bottom quarter: 0.3-0.5
            };
            result.Keywords.Add(new KeywordScore
            {
                Name = raw.Keywords[i],
                Confidence = Math.Round(Math.Max(0.1, confidence), 2)
            });
        }

        var totalCat = raw.Categories.Count;
        for (int i = 0; i < totalCat; i++)
        {
            var rank = (double)i / Math.Max(totalCat - 1, 1);
            var confidence = rank switch
            {
                <= 0.25 => 0.95 - rank * 0.1,
                <= 0.75 => 0.85 - (rank - 0.25) * 0.6,
                _ => 0.50 - (rank - 0.75) * 0.8
            };
            result.Categories.Add(new CategoryScore
            {
                Name = raw.Categories[i],
                Confidence = Math.Round(Math.Max(0.1, confidence), 2)
            });
        }

        return result;
    }

    /// <summary>
    /// Analyzes a single image asset and returns an <see cref="AiTagResult"/>
    /// with per-item confidence scores, WITHOUT writing to the database.
    /// </summary>
    public async Task<AiTagResult> AnalyzeAssetWithScoresAsync(Guid assetId, CancellationToken ct = default)
    {
        var raw = await AnalyzeAssetAsync(assetId, ct);
        return AnalyzeImageTagResult(raw);
    }

    /// <summary>
    /// Analyzes multiple image assets and returns their results without DB writes.
    /// </summary>
    public async Task<IReadOnlyList<(Guid assetId, ImageTagResult result)>> AnalyzeAssetsAsync(
        IEnumerable<Guid> assetIds,
        IProgress<(int completed, int total)>? progress = null,
        CancellationToken ct = default)
    {
        var ids = assetIds.ToList();
        var results = new List<(Guid, ImageTagResult)>(ids.Count);

        await EnsureInitializedAsync(null, ct);

        var completed = 0;
        var total = ids.Count;

        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var result = await AnalyzeAssetAsync(id, ct);
                results.Add((id, result));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AiTag] Failed to analyze asset {AssetId}", id);
            }

            completed++;
            progress?.Report((completed, total));
        }

        return results;
    }

    /// <summary>
    /// Relays the analyzer's PropertyChanged events so UI code can subscribe
    /// to <see cref="AiTaggingService.PropertyChanged"/> directly (D-14).
    /// </summary>
    private void OnAnalyzerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ILiquidVisionAnalyzer.DownloadProgress))
        {
            OnPropertyChanged(nameof(DownloadProgress));
        }
        else if (e.PropertyName == nameof(ILiquidVisionAnalyzer.IsInitialized))
        {
            OnPropertyChanged(nameof(IsInitialized));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
