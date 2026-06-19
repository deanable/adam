using System.Collections.Generic;
using System.IO;
using LiquidVision.Core.Configuration;

namespace LiquidVision.Core.Services;

/// <summary>
/// Resolves the on-disk and remote layout of a face recognition ONNX model (YuNet or ArcFace).
/// Each model is a single file, unlike the multi-file LFM2-VL layout.
/// </summary>
public sealed class FaceModelLayout
{
    private FaceModelLayout(string modelName, string baseUrl, string remoteFileName, string cacheRoot)
    {
        ModelDirectory = Path.Combine(cacheRoot, "face", modelName);
        LocalOnnxPath = Path.Combine(ModelDirectory, remoteFileName);

        BaseUrl = baseUrl;
        RemoteFiles = new List<RemoteModelFile>
        {
            new(remoteFileName, LocalOnnxPath, Optional: false)
        };
    }

    /// <summary>Absolute directory containing this model's files.</summary>
    public string ModelDirectory { get; }

    /// <summary>Absolute local path to the ONNX file.</summary>
    public string LocalOnnxPath { get; }

    /// <summary>Base URL (with trailing slash) for downloading repository files.</summary>
    public string BaseUrl { get; }

    /// <summary>All files that must be present locally, with their remote sources.</summary>
    public IReadOnlyList<RemoteModelFile> RemoteFiles { get; }

    /// <summary>
    /// Creates the layout for the YuNet face detection model from the OpenCV Zoo.
    /// Input: (1, 3, H, W) NCHW BGR, Output: raw detection tensors requiring anchor decoding + NMS.
    /// </summary>
    public static FaceModelLayout YuNet(LiquidVisionOptions? options = null, string? cacheRoot = null)
    {
        var cache = cacheRoot ?? GetDefaultCacheRoot();
        return new FaceModelLayout(
            "yunet",
            "https://github.com/opencv/opencv_zoo/raw/main/models/face_detection_yunet/",
            "face_detection_yunet_2026may.onnx",
            cache);
    }

    /// <summary>
    /// Creates the layout for the ArcFace face recognition model from HuggingFace.
    /// Input: (1, 112, 112, 3) NHWC RGB normalized, Output: 512-dim float32 embedding.
    /// </summary>
    public static FaceModelLayout ArcFace(LiquidVisionOptions? options = null, string? cacheRoot = null)
    {
        var cache = cacheRoot ?? GetDefaultCacheRoot();
        return new FaceModelLayout(
            "arcface",
            "https://huggingface.co/garavv/arcface-onnx/resolve/main/",
            "arc.onnx",
            cache);
    }

    /// <summary>
    /// Creates the layout for the legacy YuNet model (fixed 320×320 input, no dynamic shapes).
    /// </summary>
    public static FaceModelLayout YuNetLegacy(LiquidVisionOptions? options = null, string? cacheRoot = null)
    {
        var cache = cacheRoot ?? GetDefaultCacheRoot();
        return new FaceModelLayout(
            "yunet",
            "https://github.com/opencv/opencv_zoo/raw/main/models/face_detection_yunet/",
            "face_detection_yunet_2023mar.onnx",
            cache);
    }

    private static string GetDefaultCacheRoot()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Adam", "models");
    }
}
