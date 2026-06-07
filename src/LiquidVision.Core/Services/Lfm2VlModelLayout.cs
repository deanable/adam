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
/// Resolves the on-disk and remote layout of an LFM2-VL ONNX model for a given
/// <see cref="LiquidVisionOptions"/>. Shared by the downloader, verification marker, and loader so
/// they all agree on file names and locations.
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

    public string EmbedTokensPath => LocalOnnx("embed_tokens");
    public string VisionEncoderPath => LocalOnnx("vision_encoder");
    public string DecoderPath => LocalOnnx("decoder_model_merged");
    public string TokenizerJsonPath => Path.Combine(ModelDirectory, "tokenizer.json");
    public string TokenizerConfigPath => Path.Combine(ModelDirectory, "tokenizer_config.json");
    public string ConfigPath => Path.Combine(ModelDirectory, "config.json");
    public string GenerationConfigPath => Path.Combine(ModelDirectory, "generation_config.json");
    public string PreprocessorConfigPath => Path.Combine(ModelDirectory, "preprocessor_config.json");

    /// <summary>Maps the configured precision to the repository file-name suffix.</summary>
    public string PrecisionSuffix => _options.Precision switch
    {
        ModelPrecision.Fp32 => "",
        ModelPrecision.Fp16 => "_fp16",
        ModelPrecision.Q4 => "_q4",
        ModelPrecision.Q4F16 => "_q4f16",
        ModelPrecision.Quantized => "_quantized",
        _ => ""
    };

    /// <summary>All files that must be present locally, with their remote sources.</summary>
    public IReadOnlyList<RemoteModelFile> RemoteFiles
    {
        get
        {
            var files = new List<RemoteModelFile>();

            foreach (var graph in new[] { "embed_tokens", "vision_encoder", "decoder_model_merged" })
            {
                var onnx = $"onnx/{graph}{PrecisionSuffix}.onnx";
                files.Add(new RemoteModelFile(onnx, Path.Combine(ModelDirectory, ToLocal(onnx)), Optional: false));
                // External data sits beside the .onnx so ORT auto-loads it. Optional: some precisions inline it.
                var data = onnx + "_data";
                files.Add(new RemoteModelFile(data, Path.Combine(ModelDirectory, ToLocal(data)), Optional: true));
            }

            foreach (var cfg in new[]
            {
                "config.json", "generation_config.json", "preprocessor_config.json",
                "processor_config.json", "tokenizer.json", "tokenizer_config.json", "chat_template.jinja"
            })
            {
                files.Add(new RemoteModelFile(cfg, Path.Combine(ModelDirectory, cfg), Optional: false));
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
