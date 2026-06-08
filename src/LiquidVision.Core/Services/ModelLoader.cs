using System;
using Microsoft.ML.OnnxRuntime;
using LiquidVision.Core.Configuration;
using LiquidVision.Core.Exceptions;

namespace LiquidVision.Core.Services;

/// <summary>A fully loaded model: the three ONNX graphs plus tokenizer and parsed configuration.</summary>
public sealed class LoadedModel : IDisposable
{
    public required InferenceSession EmbedTokens { get; init; }
    public required InferenceSession VisionEncoder { get; init; }
    public required InferenceSession Decoder { get; init; }
    public required Lfm2Tokenizer Tokenizer { get; init; }
    public required ModelConfig Config { get; init; }
    public required PreprocessorConfig PreprocessorConfig { get; init; }

    public void Dispose()
    {
        EmbedTokens.Dispose();
        VisionEncoder.Dispose();
        Decoder.Dispose();
    }
}

/// <summary>
/// Loads the LFM2-VL split graphs into ONNX Runtime sessions (with the configured execution provider),
/// plus the native tokenizer and JSON configs. External <c>.onnx_data</c> files are auto-loaded by ORT
/// from beside each <c>.onnx</c>.
/// </summary>
public sealed class ModelLoader : IDisposable
{
    private LoadedModel? _loaded;

    public LoadedModel Load(Lfm2VlModelLayout layout, LiquidVisionOptions options)
    {
        try
        {
            using var sessionOptions = CreateSessionOptions(options);

            var embed = new InferenceSession(layout.EmbedTokensPath, sessionOptions);
            var vision = new InferenceSession(layout.VisionEncoderPath, sessionOptions);
            var decoder = new InferenceSession(layout.DecoderPath, sessionOptions);

            var tokenizer = Lfm2Tokenizer.FromFile(layout.TokenizerJsonPath);
            var config = ModelConfig.FromFile(layout.ConfigPath);
            var preprocessor = PreprocessorConfig.FromFile(layout.ResolvedPreprocessorConfigPath);

            _loaded = new LoadedModel
            {
                EmbedTokens = embed,
                VisionEncoder = vision,
                Decoder = decoder,
                Tokenizer = tokenizer,
                Config = config,
                PreprocessorConfig = preprocessor
            };
            return _loaded;
        }
        catch (Exception ex) when (ex is not ModelLoadException)
        {
            throw new ModelLoadException($"Failed to load ONNX model from {layout.ModelDirectory}", ex);
        }
    }

    private static SessionOptions CreateSessionOptions(LiquidVisionOptions options)
    {
        var so = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
        };

        // GPU providers (CUDA/DirectML) live in separate native packages whose managed entry points are
        // not present in the base Microsoft.ML.OnnxRuntime package. To keep this library dependency-light
        // and always-compilable, non-CPU providers are wired via the ConfigureSessionOptions hook.
        if (options.ExecutionProvider != ExecutionProviderKind.Cpu && options.ConfigureSessionOptions is null)
        {
            throw new ModelLoadException(
                $"ExecutionProvider '{options.ExecutionProvider}' requires installing the matching " +
                "Microsoft.ML.OnnxRuntime native package and registering it via " +
                "LiquidVisionOptions.ConfigureSessionOptions (e.g. so => so.AppendExecutionProvider_CUDA(0)).");
        }

        options.ConfigureSessionOptions?.Invoke(so);
        return so;
    }

    public void Dispose() => _loaded?.Dispose();
}
