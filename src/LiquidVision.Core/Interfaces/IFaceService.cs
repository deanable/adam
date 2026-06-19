using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace LiquidVision.Core;

/// <summary>
/// Common interface for face-related ONNX services (detection and recognition).
/// Follows the same initialization pattern as ILiquidVisionAnalyzer.
/// </summary>
public interface IFaceService : INotifyPropertyChanged, IAsyncDisposable
{
    /// <summary>Model download progress in the range [0, 1].</summary>
    double DownloadProgress { get; }

    /// <summary>True once the model is downloaded, verified, and loaded.</summary>
    bool IsInitialized { get; }

    /// <summary>Downloads (if needed), verifies, and loads the model.</summary>
    Task InitializeAsync(IProgress<double>? progress = null, CancellationToken ct = default);
}
