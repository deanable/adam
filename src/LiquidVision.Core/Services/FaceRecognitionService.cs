using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using LiquidVision.Core.Configuration;
using LiquidVision.Core.Exceptions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace LiquidVision.Core.Services;

/// <summary>
/// ArcFace face recognition ONNX inference service.
/// Downloads the model on first use, creates an InferenceSession, and computes
/// 512-dim face embeddings from aligned 112×112 face crops.
/// </summary>
public sealed class FaceRecognitionService : IFaceService
{
    private readonly FaceModelLayout _layout;
    private readonly LiquidVisionOptions _options;
    private readonly HttpClient _httpClient;
    private InferenceSession? _session;
    private bool _disposed;

    private const int InputSize = 112;

    public FaceRecognitionService(FaceModelLayout layout, LiquidVisionOptions? options = null, HttpClient? httpClient = null)
    {
        _layout = layout;
        _options = options ?? new LiquidVisionOptions();
        _httpClient = httpClient ?? new HttpClient();
        if (httpClient == null)
        {
            _httpClient.Timeout = _options.DownloadTimeout;
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("LiquidVision/1.0");
        }
    }

    public double DownloadProgress { get; private set; }
    public bool IsInitialized => _session != null;

    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task InitializeAsync(IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (IsInitialized) return;

        Directory.CreateDirectory(_layout.ModelDirectory);

        // Download model if needed
        if (!File.Exists(_layout.LocalOnnxPath))
        {
            var remoteFile = _layout.RemoteFiles[0];
            progress?.Report(0);
            DownloadProgress = 0;
            OnPropertyChanged(nameof(DownloadProgress));

            using var response = await _httpClient.GetAsync(
                _layout.BaseUrl + remoteFile.RemotePath,
                HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            await using var httpStream = await response.Content.ReadAsStreamAsync(ct);
            await using var fileStream = new FileStream(
                _layout.LocalOnnxPath, FileMode.Create, FileAccess.Write,
                FileShare.None, 1 << 16, useAsync: true);
            await httpStream.CopyToAsync(fileStream, ct);

            progress?.Report(1.0);
            DownloadProgress = 1.0;
            OnPropertyChanged(nameof(DownloadProgress));
        }

        // Create session
        var sessionOptions = new SessionOptions();
        _options.ConfigureSessionOptions?.Invoke(sessionOptions);
        _session = new InferenceSession(_layout.LocalOnnxPath, sessionOptions);
        DownloadProgress = 1.0;
        OnPropertyChanged(nameof(DownloadProgress));
        OnPropertyChanged(nameof(IsInitialized));
    }

    /// <summary>
    /// Computes a 512-dim face embedding from an aligned 112×112 face crop.
    /// </summary>
    /// <param name="alignedFaceCrop">RGB pixel data as byte[112*112*3] in NHWC layout.</param>
    /// <returns>512-dim float32 embedding vector.</returns>
    public async Task<float[]> GetFaceEmbeddingAsync(
        byte[] alignedFaceCrop, CancellationToken ct = default)
    {
        if (_session == null)
            throw new InvalidOperationException("FaceRecognitionService not initialized. Call InitializeAsync first.");

        if (alignedFaceCrop.Length != InputSize * InputSize * 3)
            throw new ArgumentException(
                $"Expected aligned face crop of {InputSize * InputSize * 3} bytes (112×112 RGB), got {alignedFaceCrop.Length}.");

        // Detect input tensor name dynamically
        string inputName = _session.InputMetadata.Keys.First();

        // Build NHWC tensor: (1, 112, 112, 3), normalized (pixels - 127.5) / 128.0
        var inputTensor = new DenseTensor<float>(new[] { 1, InputSize, InputSize, 3 });
        int idx = 0;
        for (int y = 0; y < InputSize; y++)
        {
            for (int x = 0; x < InputSize; x++)
            {
                float r = (alignedFaceCrop[idx] - 127.5f) / 128.0f;
                float g = (alignedFaceCrop[idx + 1] - 127.5f) / 128.0f;
                float b = (alignedFaceCrop[idx + 2] - 127.5f) / 128.0f;
                idx += 3;

                inputTensor[0, y, x, 0] = r;
                inputTensor[0, y, x, 1] = g;
                inputTensor[0, y, x, 2] = b;
            }
        }

        // Run inference
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(inputName, inputTensor)
        };

        using var results = _session.Run(inputs);

        // Extract 512-dim embedding from first output
        string outputName = _session.OutputMetadata.Keys.First();
        var output = results.First(r => r.Name == outputName);
        var outputTensor = output.AsTensor<float>();

        var embedding = new float[512];            int count = (int)Math.Min(outputTensor.Length, 512);
        for (int i = 0; i < count; i++)
            embedding[i] = outputTensor[0, i];

        // Normalize to unit vector
        float norm = 0;
        for (int i = 0; i < count; i++)
            norm += embedding[i] * embedding[i];
        norm = MathF.Sqrt(norm);

        if (norm > 0)
            for (int i = 0; i < count; i++)
                embedding[i] /= norm;

        return embedding;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            _session?.Dispose();
        }
        await Task.CompletedTask;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
