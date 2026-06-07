using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LiquidVision.Core.Configuration;

namespace LiquidVision.Core.Services;

/// <summary>
/// Resolves cache locations for a model and supports clearing the cache. The cache is keyed by
/// model id and precision (via <see cref="Lfm2VlModelLayout"/>) so different variants coexist.
/// </summary>
public sealed class ModelCacheManager
{
    public ModelCacheManager(LiquidVisionOptions options)
    {
        var root = options.CacheDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LiquidVision", "models");

        CacheRoot = root;
        Layout = new Lfm2VlModelLayout(options, root);
    }

    /// <summary>Root directory under which all cached models live.</summary>
    public string CacheRoot { get; }

    /// <summary>Resolved file layout for the configured model + precision.</summary>
    public Lfm2VlModelLayout Layout { get; }

    /// <summary>Directory containing the configured model's files.</summary>
    public string ModelDirectory => Layout.ModelDirectory;

    /// <summary>Ensures the model directory (and its <c>onnx/</c> subdirectory) exist.</summary>
    public void EnsureDirectoryExists()
    {
        Directory.CreateDirectory(ModelDirectory);
        Directory.CreateDirectory(Path.Combine(ModelDirectory, "onnx"));
    }

    /// <summary>Deletes the configured model's cached files.</summary>
    public async Task ClearCacheAsync(CancellationToken ct = default)
    {
        if (Directory.Exists(ModelDirectory))
            await Task.Run(() => Directory.Delete(ModelDirectory, recursive: true), ct);
    }
}
