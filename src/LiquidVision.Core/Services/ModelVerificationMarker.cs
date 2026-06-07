using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LiquidVision.Core.Services;

/// <summary>
/// Tracks whether a model has been fully and correctly downloaded. Unlike a simple "done" flag,
/// verification re-checks that every required file still exists on disk with the expected byte size,
/// so a partial or tampered cache is detected and triggers a re-download.
/// </summary>
public sealed class ModelVerificationMarker
{
    private static readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly string _markerFilePath;
    private readonly Lfm2VlModelLayout _layout;

    public ModelVerificationMarker(Lfm2VlModelLayout layout)
    {
        _layout = layout;
        _markerFilePath = Path.Combine(layout.ModelDirectory, ".model_verified");
    }

    /// <summary>
    /// Returns true only if the marker matches <paramref name="expectedVersion"/> AND every required
    /// file recorded in the marker is present with the recorded size.
    /// </summary>
    public async Task<bool> IsModelVerifiedAsync(string expectedVersion, CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);
        try
        {
            if (!File.Exists(_markerFilePath))
                return false;

            await using var stream = File.OpenRead(_markerFilePath);
            var marker = await JsonSerializer.DeserializeAsync<MarkerData>(stream, cancellationToken: ct);
            if (marker is null || marker.ModelVersion != expectedVersion || marker.Files.Count == 0)
                return false;

            foreach (var (relativePath, expectedSize) in marker.Files)
            {
                var full = Path.Combine(_layout.ModelDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(full))
                    return false;
                if (expectedSize >= 0 && new FileInfo(full).Length != expectedSize)
                    return false;
            }

            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false; // corrupt marker → treat as unverified
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Writes a verification marker recording the actual on-disk size of every required (present) file.
    /// </summary>
    public async Task WriteVerificationMarkerAsync(string modelVersion, CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);
        try
        {
            var files = new Dictionary<string, long>();
            foreach (var file in _layout.RemoteFiles)
            {
                if (File.Exists(file.LocalPath))
                {
                    var rel = Path.GetRelativePath(_layout.ModelDirectory, file.LocalPath).Replace('\\', '/');
                    files[rel] = new FileInfo(file.LocalPath).Length;
                }
                else if (!file.Optional)
                {
                    throw new FileNotFoundException($"Required model file missing while writing marker: {file.RemotePath}", file.LocalPath);
                }
            }

            var data = new MarkerData
            {
                ModelVersion = modelVersion,
                VerifiedAt = DateTime.UtcNow,
                Files = files
            };

            var tempFile = _markerFilePath + ".tmp";
            await using (var stream = File.Create(tempFile))
                await JsonSerializer.SerializeAsync(stream, data, cancellationToken: ct);
            File.Move(tempFile, _markerFilePath, overwrite: true);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>Removes the verification marker.</summary>
    public async Task ClearAsync(CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);
        try
        {
            if (File.Exists(_markerFilePath))
                File.Delete(_markerFilePath);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private sealed class MarkerData
    {
        public string ModelVersion { get; set; } = string.Empty;
        public DateTime VerifiedAt { get; set; }
        public Dictionary<string, long> Files { get; set; } = new();
    }
}
