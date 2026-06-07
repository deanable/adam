using System;
using Microsoft.ML.OnnxRuntime;

namespace LiquidVision.Core.Configuration;

/// <summary>
/// Model weight precision / quantization variant to download and run.
/// Maps to the file-name suffix used in the ONNX community repository
/// (e.g. <c>decoder_model_merged_q4f16.onnx</c>).
/// </summary>
public enum ModelPrecision
{
    /// <summary>Full precision (no suffix). Largest and slowest, closest to the reference Python output.</summary>
    Fp32,
    /// <summary>Half precision (<c>_fp16</c>).</summary>
    Fp16,
    /// <summary>4-bit quantized weights (<c>_q4</c>).</summary>
    Q4,
    /// <summary>4-bit weights with fp16 activations (<c>_q4f16</c>). Recommended balance for production.</summary>
    Q4F16,
    /// <summary>8-bit dynamic quantization (<c>_quantized</c>).</summary>
    Quantized
}

/// <summary>ONNX Runtime execution provider to use for inference.</summary>
public enum ExecutionProviderKind
{
    /// <summary>Default CPU execution provider. Always available.</summary>
    Cpu,
    /// <summary>NVIDIA CUDA. Requires the CUDA execution provider native assets.</summary>
    Cuda,
    /// <summary>DirectML (Windows GPU). Requires the DirectML execution provider native assets.</summary>
    DirectML
}

/// <summary>
/// Configuration for <see cref="LiquidVisionAnalyzer"/>: which model to run, where to cache it,
/// how to drive generation, and what prompt to use.
/// </summary>
public sealed class LiquidVisionOptions
{
    /// <summary>Hugging Face repository id of the ONNX model.</summary>
    public string ModelId { get; set; } = "onnx-community/LFM2-VL-450M-ONNX";

    /// <summary>Repository revision (branch, tag, or commit).</summary>
    public string ModelRevision { get; set; } = "main";

    /// <summary>Logical version stamp written into the verification marker and results.</summary>
    public string ModelVersion { get; set; } = "1.0";

    /// <summary>Weight precision / quantization variant to download and run.</summary>
    public ModelPrecision Precision { get; set; } = ModelPrecision.Fp32;

    /// <summary>Execution provider for ONNX Runtime.</summary>
    public ExecutionProviderKind ExecutionProvider { get; set; } = ExecutionProviderKind.Cpu;

    /// <summary>GPU device id when using a GPU execution provider.</summary>
    public int GpuDeviceId { get; set; } = 0;

    /// <summary>
    /// Optional hook to customize the ONNX Runtime <see cref="SessionOptions"/> before sessions are created.
    /// Use this to register a GPU execution provider (CUDA, DirectML, etc.) after installing the matching
    /// <c>Microsoft.ML.OnnxRuntime.*</c> native package — e.g. <c>opts.ConfigureSessionOptions = so =&gt; so.AppendExecutionProvider_CUDA(0);</c>.
    /// When set, this runs in addition to (after) the built-in <see cref="ExecutionProvider"/> handling.
    /// </summary>
    public Action<SessionOptions>? ConfigureSessionOptions { get; set; }

    /// <summary>
    /// Root directory for cached models. When <c>null</c>, a per-user location under
    /// <see cref="Environment.SpecialFolder.LocalApplicationData"/> is used.
    /// </summary>
    public string? CacheDirectory { get; set; }

    /// <summary>Maximum number of tokens to generate per image.</summary>
    public int MaxNewTokens { get; set; } = 512;

    /// <summary>When <c>true</c> (default) use deterministic greedy decoding; otherwise sample with <see cref="Temperature"/>/<see cref="TopP"/>.</summary>
    public bool Greedy { get; set; } = true;

    /// <summary>Sampling temperature (used only when <see cref="Greedy"/> is <c>false</c>).</summary>
    public float Temperature { get; set; } = 0.7f;

    /// <summary>Nucleus sampling probability mass (used only when <see cref="Greedy"/> is <c>false</c>).</summary>
    public float TopP { get; set; } = 0.9f;

    /// <summary>Optional RNG seed for reproducible sampling.</summary>
    public int? Seed { get; set; }

    /// <summary>System prompt prepended to every conversation.</summary>
    public string SystemPrompt { get; set; } = "You are a helpful multimodal assistant by Liquid AI.";

    /// <summary>User instruction sent alongside the image. Should request a JSON object the library can parse.</summary>
    public string InstructionPrompt { get; set; } = DefaultInstructionPrompt;

    /// <summary>Per-file HTTP timeout for model downloads.</summary>
    public TimeSpan DownloadTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Number of times a failed file download is retried (with exponential backoff) before giving up.</summary>
    public int MaxDownloadRetries { get; set; } = 4;

    /// <summary>
    /// Default instruction that asks the model for the keywords/categories/description JSON contract
    /// the analyzer parses into <see cref="ImageTagResult"/>.
    /// </summary>
    public const string DefaultInstructionPrompt =
        "Analyze this image for cataloguing. Respond with ONLY a single JSON object and nothing else " +
        "(no markdown, no code fences, no commentary). The object must have exactly these fields: " +
        "\"description\": a concise one-or-two sentence description of the image; " +
        "\"keywords\": an array of 5 to 15 short lowercase descriptive keyword strings; " +
        "\"categories\": an array of 1 to 5 broad category strings. " +
        "Example: {\"description\":\"A red sailboat on a calm lake at sunset.\"," +
        "\"keywords\":[\"sailboat\",\"lake\",\"sunset\",\"water\",\"sky\"],\"categories\":[\"nature\",\"travel\"]}";
}
