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
/// YuNet face detection ONNX inference service.
/// Downloads the model on first use, creates an InferenceSession, and detects faces
/// in images using a 320×320 input with anchor decoding + NMS post-processing.
/// </summary>
public sealed class FaceDetectionService : IFaceService
{
    private readonly FaceModelLayout _layout;
    private readonly LiquidVisionOptions _options;
    private readonly HttpClient _httpClient;
    private InferenceSession? _session;
    private bool _disposed;

    // YuNet anchor parameters
    private const int InputSize = 320;
    private const float ConfidenceThreshold = 0.5f;
    private const float NmsThreshold = 0.3f;
    private static readonly int[] Strides = [8, 16, 32];

    public FaceDetectionService(FaceModelLayout layout, LiquidVisionOptions? options = null, HttpClient? httpClient = null)
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
    /// Detects faces in an image and returns bounding boxes with landmarks.
    /// </summary>
    public async Task<IReadOnlyList<DetectedFace>> DetectFacesAsync(
        byte[] imageData, CancellationToken ct = default)
    {
        if (_session == null)
            throw new InvalidOperationException("FaceDetectionService not initialized. Call InitializeAsync first.");

        // Decode and resize
        using var bitmap = DecodeImage(imageData);
        int origWidth = bitmap.Width;
        int origHeight = bitmap.Height;

        using var resized = bitmap.Resize(
            new SKImageInfo(InputSize, InputSize, SKColorType.Rgba8888, SKAlphaType.Unpremul),
            new SKSamplingOptions(SKCubicResampler.Mitchell));
        if (resized == null)
            throw new ModelInferenceException("Failed to resize image for face detection.");

        // Build NCHW BGR tensor
        var inputTensor = new DenseTensor<float>(new[] { 1, 3, InputSize, InputSize });
        var pixels = resized.Pixels;
        for (int y = 0; y < InputSize; y++)
        {
            for (int x = 0; x < InputSize; x++)
            {
                var color = pixels[y * InputSize + x];
                inputTensor[0, 0, y, x] = color.Blue;   // B channel
                inputTensor[0, 1, y, x] = color.Green;  // G channel
                inputTensor[0, 2, y, x] = color.Red;    // R channel
            }
        }

        // Run inference
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input", inputTensor)
        };

        using var results = _session.Run(inputs);

        // YuNet typically returns a single output tensor (loc + conf + landmarks flattened)
        string outputName = _session.OutputMetadata.Keys.First();
        var output = results.First(r => r.Name == outputName);
        var outputTensor = output.AsTensor<float>();

        // Decode anchors and apply NMS
        var rawDetections = DecodeOutput(outputTensor, origWidth, origHeight);
        var detections = ApplyNms(rawDetections);

        var faces = new List<DetectedFace>();
        foreach (var det in detections)
        {
            faces.Add(new DetectedFace
            {
                X = det.X,
                Y = det.Y,
                Width = det.Width,
                Height = det.Height,
                Confidence = det.Confidence,
                Landmarks = det.Landmarks
            });
        }

        return faces;
    }

    private static List<RawDetection> DecodeOutput(
        Tensor<float> output, int origWidth, int origHeight)
    {
        // YuNet output shape: [1, num_anchors, 14]
        // Each anchor: [x, y, w, h, confidence, l0x, l0y, l1x, l1y, l2x, l2y, l3x, l3y, l4x, l4y]
        var detections = new List<RawDetection>();
        var dims = output.Dimensions;
        int numAnchors = dims.Length >= 3 ? (int)dims[1] : (int)(output.Length / 14);

        // Generate anchor grid
        var anchors = GenerateAnchors();

        float scaleX = (float)origWidth / InputSize;
        float scaleY = (float)origHeight / InputSize;

        for (int i = 0; i < anchors.Count && i < numAnchors; i++)
        {
            float confidence = output[0, i, 14]; // Confidence score at index 14

            if (confidence < ConfidenceThreshold)
                continue;

            // Bounding box (center-offset format → corner format)
            // Indices 0-3: x, y, w, h (center-relative offsets)
            float cx = output[0, i, 0] * anchors[i].Stride + anchors[i].X;
            float cy = output[0, i, 1] * anchors[i].Stride + anchors[i].Y;
            float w = output[0, i, 2] * anchors[i].Stride;
            float h = output[0, i, 3] * anchors[i].Stride;

            // Convert to corner format and scale to original image dimensions
            float x = (cx - w / 2) * scaleX;
            float y = (cy - h / 2) * scaleY;
            w = w * scaleX;
            h = h * scaleY;

            // Landmarks at indices 4-13 (5 landmarks × 2 coords), scale to original
            var landmarks = new (float X, float Y)[5];
            for (int l = 0; l < 5; l++)
            {
                float lx = output[0, i, 4 + l * 2] * anchors[i].Stride + anchors[i].X;
                float ly = output[0, i, 4 + l * 2 + 1] * anchors[i].Stride + anchors[i].Y;
                landmarks[l] = (lx * scaleX, ly * scaleY);
            }

            detections.Add(new RawDetection
            {
                X = Math.Max(0, x),
                Y = Math.Max(0, y),
                Width = w,
                Height = h,
                Confidence = confidence,
                Landmarks = landmarks
            });
        }

        return detections;
    }

    private static List<(float X, float Y, int Stride)> GenerateAnchors()
    {
        // Generate anchor points for each stride level (8, 16, 32)
        var anchors = new List<(float X, float Y, int Stride)>();
        foreach (int stride in Strides)
        {
            int gridSize = InputSize / stride;
            for (int gy = 0; gy < gridSize; gy++)
            {
                for (int gx = 0; gx < gridSize; gx++)
                {
                    // Anchor center at grid cell center + 0.5 offset, scaled by stride
                    anchors.Add((
                        (gx + 0.5f) * stride,
                        (gy + 0.5f) * stride,
                        stride
                    ));
                }
            }
        }
        return anchors;
    }

    private static List<RawDetection> ApplyNms(List<RawDetection> detections)
    {
        if (detections.Count <= 1)
            return detections;

        // Sort by confidence descending
        var sorted = detections.OrderByDescending(d => d.Confidence).ToList();
        var result = new List<RawDetection>();

        while (sorted.Count > 0)
        {
            var best = sorted[0];
            result.Add(best);
            sorted.RemoveAt(0);

            // Remove overlapping detections
            sorted.RemoveAll(d => ComputeIoU(best, d) > NmsThreshold);
        }

        return result;
    }

    private static float ComputeIoU(RawDetection a, RawDetection b)
    {
        float x1 = Math.Max(a.X, b.X);
        float y1 = Math.Max(a.Y, b.Y);
        float x2 = Math.Min(a.X + a.Width, b.X + b.Width);
        float y2 = Math.Min(a.Y + a.Height, b.Y + b.Height);

        float inter = Math.Max(0, x2 - x1) * Math.Max(0, y2 - y1);
        float areaA = a.Width * a.Height;
        float areaB = b.Width * b.Height;
        float union = areaA + areaB - inter;

        return union > 0 ? inter / union : 0;
    }

    private static SKBitmap DecodeImage(byte[] data)
    {
        var raw = SKBitmap.Decode(data)
            ?? throw new ModelInferenceException("Could not decode image data.");
        return raw;
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

    private sealed class RawDetection
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public float Confidence { get; set; }
        public (float X, float Y)[] Landmarks { get; set; } = [];
    }
}

/// <summary>
/// A detected face with bounding box, confidence score, and facial landmarks.
/// </summary>
public sealed record DetectedFace
{
    public float X { get; init; }
    public float Y { get; init; }
    public float Width { get; init; }
    public float Height { get; init; }
    public float Confidence { get; init; }
    public IReadOnlyList<(float X, float Y)>? Landmarks { get; init; }
}
