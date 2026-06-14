using System.Collections.Generic;

namespace LiquidVision.Core.Configuration;

/// <summary>
/// Describes an available AI model variant shown in the model selector dropdown.
/// </summary>
public sealed class AiModelDefinition
{
    /// <summary>User-friendly display name (e.g. "LFM2-VL 450M (Q4, Fast)").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Hugging Face repository ID.</summary>
    public string ModelId { get; set; } = string.Empty;

    /// <summary>Weight precision / quantization variant.</summary>
    public ModelPrecision Precision { get; set; }

    /// <summary>Detected architecture variant.</summary>
    public ModelArchitecture Architecture => ModelId switch
    {
        not null when ModelId.Contains("LFM2.5", System.StringComparison.OrdinalIgnoreCase) => ModelArchitecture.Lfm25Vl,
        _ => ModelArchitecture.Lfm2Vl
    };

    /// <summary>Human-readable download size (e.g. "~250 MB").</summary>
    public string DownloadSize { get; set; } = string.Empty;

    /// <summary>Approximate size in bytes for progress estimation.</summary>
    public long DownloadSizeBytes { get; set; }

    /// <summary>Short description shown as tooltip.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Label combining name and size for the dropdown display.</summary>
    public string DisplayLabel => $"{Name} ({DownloadSize})";

    /// <summary>Built-in list of all available model variants.</summary>
    public static readonly List<AiModelDefinition> All = new()
    {
        // ── LFM2-VL 450M (small, CPU-friendly) ──
        new()
        {
            Name = "LFM2-VL 450M (Q4, Fast)",
            ModelId = "onnx-community/LFM2-VL-450M-ONNX",
            Precision = ModelPrecision.Q4,
            DownloadSize = "~250 MB",
            DownloadSizeBytes = 250_000_000,
            Description = "Smallest model, fastest on CPU, reasonable accuracy"
        },
        new()
        {
            Name = "LFM2-VL 450M (Q4F16, Balanced)",
            ModelId = "onnx-community/LFM2-VL-450M-ONNX",
            Precision = ModelPrecision.Q4F16,
            DownloadSize = "~300 MB",
            DownloadSizeBytes = 300_000_000,
            Description = "Balanced speed/quality for CPU, recommended entry-level"
        },

        // ── LFM2-VL 1.6B (medium, good accuracy) ──
        new()
        {
            Name = "LFM2-VL 1.6B (Q4, CPU)",
            ModelId = "onnx-community/LFM2-VL-1.6B-ONNX",
            Precision = ModelPrecision.Q4,
            DownloadSize = "~900 MB",
            DownloadSizeBytes = 900_000_000,
            Description = "Good accuracy, CPU-friendly 4-bit quantized"
        },
        new()
        {
            Name = "LFM2-VL 1.6B (Q4F16, Recommended)",
            ModelId = "onnx-community/LFM2-VL-1.6B-ONNX",
            Precision = ModelPrecision.Q4F16,
            DownloadSize = "~1.1 GB",
            DownloadSizeBytes = 1_100_000_000,
            Description = "Best balance of accuracy and speed on CPU (recommended)"
        },
        new()
        {
            Name = "LFM2-VL 1.6B (FP16, Accurate)",
            ModelId = "onnx-community/LFM2-VL-1.6B-ONNX",
            Precision = ModelPrecision.Fp16,
            DownloadSize = "~3.2 GB",
            DownloadSizeBytes = 3_200_000_000,
            Description = "Highest accuracy, requires GPU (CUDA or DirectML)"
        },

        // ── LFM2.5-VL 1.6B (newer architecture) ──
        new()
        {
            Name = "LFM2.5-VL 1.6B (Q4, CPU)",
            ModelId = "LiquidAI/LFM2.5-VL-1.6B-ONNX",
            Precision = ModelPrecision.Q4,
            DownloadSize = "~900 MB",
            DownloadSizeBytes = 900_000_000,
            Description = "Newer architecture, CPU-friendly 4-bit"
        },
        new()
        {
            Name = "LFM2.5-VL 1.6B (FP16, Accurate)",
            ModelId = "LiquidAI/LFM2.5-VL-1.6B-ONNX",
            Precision = ModelPrecision.Fp16,
            DownloadSize = "~3.2 GB",
            DownloadSizeBytes = 3_200_000_000,
            Description = "Newest architecture, highest quality, requires GPU"
        },
    };

    /// <summary>
    /// Finds the <see cref="AiModelDefinition"/> that best matches a given ModelId and Precision.
    /// Falls back to the first entry if no match is found.
    /// </summary>
    public static AiModelDefinition FindOrDefault(string modelId, ModelPrecision precision)
    {
        var match = All.Find(m =>
            m.ModelId.Equals(modelId, System.StringComparison.OrdinalIgnoreCase) &&
            m.Precision == precision);
        return match ?? All[0];
    }
}
