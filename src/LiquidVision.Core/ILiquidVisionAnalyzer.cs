using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace LiquidVision.Core;

/// <summary>
/// Analyzes images with the LFM2-VL model, producing a description, keywords, and categories.
/// </summary>
public interface ILiquidVisionAnalyzer : INotifyPropertyChanged, IAsyncDisposable
{
    /// <summary>Model download progress in the range [0, 1].</summary>
    double DownloadProgress { get; }

    /// <summary>True once the model is downloaded, verified, and loaded.</summary>
    bool IsInitialized { get; }

    /// <summary>Downloads (if needed), verifies, and loads the model.</summary>
    Task InitializeAsync(IProgress<double>? progress = null, CancellationToken ct = default);

    /// <summary>Analyzes an image from a file path.</summary>
    Task<ImageTagResult> AnalyzeAsync(string imagePath, CancellationToken ct = default);

    /// <summary>Analyzes an image from in-memory encoded bytes (PNG/JPEG/etc.).</summary>
    Task<ImageTagResult> AnalyzeAsync(byte[] imageData, CancellationToken ct = default);
}
