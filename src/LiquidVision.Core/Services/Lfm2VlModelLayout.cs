using System.Collections.Generic;
using System.IO;
using LiquidVision.Core.Configuration;

namespace LiquidVision.Core.Services;

/// <summary>A file that must be fetched from the model repository.</summary>
/// <param name="RemotePath">Path relative to the repository root (and to the local model directory).</param>
/// <param name="LocalPath">Absolute local destination path.</param>
/// <param name="Optional">When true, a 404 is tolerated (e.g. external data that is inlined for some precisions).</param>
public sealed record RemoteModelFile(string RemotePath, string LocalPath, bool Optional);

/// <summary>
/// Resolves the on-disk and remote layout of an LFM2-VL / LFM2.5-VL ONNX model for a
/// given <see cref="LiquidVisionOptions"/>. Shared by the downloader, verification marker, and
/// loader so they all agree on file names and locations. Supports architecture-specific naming
/// conventions (graph names, precision suffix fallbacks) detected from the model ID.
/// </summary>
public sealed class Lfm2VlModelLayout
{
    private readonly LiquidVisionOptions _options;

    public Lfm2VlModelLayout(LiquidVisionOptions options, string cacheRoot)
    {
        _options = options;
        var modelDir = SanitizeForPath(options.ModelId);
        ModelDirectory = Path.Combine(cacheRoot, modelDir, options.Precision.ToString().ToLowerInvariant());
    }

    /// <summary>Absolute directory containing this model + precision's files.</summary>
    public string ModelDirectory { get; }

    /// <summary>Base URL (with trailing slash) for downloading repository files.</summary>
    public string BaseUrl => $"https://huggingface.co/{_options.ModelId}/resolve/{_options.ModelRevision}/";

    /// <summary>The detected model architecture (LFM2-VL vs LFM2.5-VL).</summary>
    public ModelArchitecture Architecture => _options.Architecture;

    // ─────────────────────────────────────────────────────────────
    //  Architecture-aware graph names
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the ONNX graph file name for a logical component.
    /// Different model variants use different file names:
    ///   LFM2-VL:    vision_encoder
    ///   LFM2.5-VL:  embed_images
    /// </summary>
    private static string VisionEncoderGraphName(ModelArchitecture arch) => arch switch
    {
        ModelArchitecture.Lfm25Vl => "embed_images",
        _ => "vision_encoder"
    };

    /// <summary>
    /// Resolves the ONNX graph file name for a logical component.
    /// Different model variants use different file names:
    ///   LFM2-VL:    decoder_model_merged
    ///   LFM2.5-VL:  decoder
    /// </summary>
    private static string DecoderGraphName(ModelArchitecture arch) => arch switch
    {
        ModelArchitecture.Lfm25Vl => "decoder",
        _ => "decoder_model_merged"
    };

    public string EmbedTokensPath => LocalOnnx("embed_tokens");
    public string VisionEncoderPath => LocalOnnx(VisionEncoderGraphName(Architecture));
    public string DecoderPath => LocalOnnx(DecoderGraphName(Architecture));

    public string TokenizerJsonPath => Path.Combine(ModelDirectory, "tokenizer.json");
    public string TokenizerConfigPath => Path.Combine(ModelDirectory, "tokenizer_config.json");
    public string ConfigPath => Path.Combine(ModelDirectory, "config.json");
    public string GenerationConfigPath => Path.Combine(ModelDirectory, "generation_config.json");
    public string PreprocessorConfigPath => Path.Combine(ModelDirectory, "preprocessor_config.json");

    /// <summary>Some repos ship a combined <c>processor_config.json</c> (fields nested under <c>image_processor</c>) instead of a flat <c>preprocessor_config.json</c>.</summary>
    public string ProcessorConfigPath => Path.Combine(ModelDirectory, "processor_config.json");

    /// <summary>The preprocessing config that actually exists on disk (flat preprocessor first, then combined processor).</summary>
    public string ResolvedPreprocessorConfigPath =>
        File.Exists(PreprocessorConfigPath) ? PreprocessorConfigPath : ProcessorConfigPath;

    // ─────────────────────────────────────────────────────────────
    //  Architecture-aware precision suffix
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Maps the configured precision to the repository file-name suffix.
    /// Architecture-aware: LFM2.5-VL doesn't ship a <c>_q4f16</c> variant,
    /// so <see cref="ModelPrecision.Q4F16"/> falls back to <c>_q4</c>.
    /// </summary>
    public string PrecisionSuffix
    {
        get
        {
            // LFM2.5-VL doesn't ship _q4f16; fall back to _q4 (best available quantized variant).
            if (Architecture == ModelArchitecture.Lfm25Vl && _options.Precision == ModelPrecision.Q4F16)
                return "_q4";

            return _options.Precision switch
            {
                ModelPrecision.Fp32 => "",
                ModelPrecision.Fp16 => "_fp16",
                ModelPrecision.Q4 => "_q4",
                ModelPrecision.Q4F16 => "_q4f16",
                ModelPrecision.Q8 => "_q8",
                ModelPrecision.Quantized => "_quantized",
                _ => ""
            };
        }
    }

    /// <summary>All files that must be present locally, with their remote sources.</summary>
    public IReadOnlyList<RemoteModelFile> RemoteFiles
    {
        get
        {
            var files = new List<RemoteModelFile>();
            var suffix = PrecisionSuffix;
            var arch = Architecture;

            foreach (var graph in new[] { "embed_tokens", VisionEncoderGraphName(arch), DecoderGraphName(arch) })
            {
                var onnx = $"onnx/{graph}{suffix}.onnx";
                files.Add(new RemoteModelFile(onnx, Path.Combine(ModelDirectory, ToLocal(onnx)), Optional: false));
                // External data sits beside the .onnx so ORT auto-loads it. Large graphs (e.g. the decoder) are
                // sharded across .onnx_data, .onnx_data_1, .onnx_data_2 — all optional (a precision may inline them
                // or use fewer shards).
                foreach (var data in new[] { onnx + "_data", onnx + "_data_1", onnx + "_data_2" })
                    files.Add(new RemoteModelFile(data, Path.Combine(ModelDirectory, ToLocal(data)), Optional: true));
            }

            // Required configs every variant ships.
            foreach (var cfg in new[]
            {
                "config.json", "generation_config.json",
                "tokenizer.json", "tokenizer_config.json"
            })
            {
                files.Add(new RemoteModelFile(cfg, Path.Combine(ModelDirectory, cfg), Optional: false));
            }

            // A model ships exactly one of preprocessor_config.json / processor_config.json; chat_template.jinja
            // is not present in every repo. All optional so a 404 is tolerated.
            foreach (var cfg in new[] { "preprocessor_config.json", "processor_config.json", "chat_template.jinja" })
            {
                files.Add(new RemoteModelFile(cfg, Path.Combine(ModelDirectory, cfg), Optional: true));
            }

            return files;
        }
    }

    private string LocalOnnx(string graph) =>
        Path.Combine(ModelDirectory, "onnx", $"{graph}{PrecisionSuffix}.onnx");

    private static string ToLocal(string remoteRelative) =>
        remoteRelative.Replace('/', Path.DirectorySeparatorChar);

    private static string SanitizeForPath(string modelId)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            modelId = modelId.Replace(c, '_');
        return modelId.Replace('/', '_');
    }
}
