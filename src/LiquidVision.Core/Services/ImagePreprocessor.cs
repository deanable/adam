using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;
using LiquidVision.Core.Exceptions;

namespace LiquidVision.Core.Services;

/// <summary>
/// Decodes and preprocesses an image into SigLIP2-NaFlex vision inputs for LFM2-VL, using SkiaSharp
/// (no Python). Produces a flattened patch sequence (<c>pixel_values</c>), per-patch attention mask,
/// and <c>spatial_shapes</c>, plus the prompt-side image token sequence.
/// </summary>
/// <remarks>
/// PARITY NOTES — these reconstruct the HF <c>Lfm2VlImageProcessorFast</c> / SigLIP2 NaFlex behavior and
/// must be confirmed against the reference Python (pixel_values shape + first generated tokens):
/// <list type="bullet">
/// <item>Patch flatten order is channel-major (<c>c*ps*ps + py*ps + px</c>).</item>
/// <item>smart-resize rounds each side to a multiple of <c>patch*downsample</c> and scales to keep the
/// patch count within [min_image_tokens, max_image_tokens]·downsample².</item>
/// <item>Multi-tile splitting for very large images is not yet implemented; large images are downscaled
/// into the single-image patch budget (lower detail, still correct), which matches the model's
/// native single-image path. Tile support is a planned enhancement.</item>
/// <item>Vision input tensor names and dtypes are discovered from the graph metadata at runtime.</item>
/// </list>
/// </remarks>
public sealed class ImagePreprocessor
{
    private const int Channels = 3;

    private readonly PreprocessorConfig _cfg;
    private readonly Lfm2Tokenizer _tokenizer;
    private readonly IReadOnlyDictionary<string, NodeMetadata> _visionInputs;

    public ImagePreprocessor(PreprocessorConfig cfg, Lfm2Tokenizer tokenizer, InferenceSession visionEncoder)
    {
        _cfg = cfg;
        _tokenizer = tokenizer;
        _visionInputs = visionEncoder.InputMetadata;
    }

    /// <summary>Preprocesses encoded image bytes (PNG/JPEG/etc.) into vision inputs + image token ids.</summary>
    public ProcessedImage Process(byte[] imageData)
    {
        using var bitmap = Decode(imageData);

        int factor = _cfg.PatchSize * _cfg.DownsampleFactor; // 32: side must be a multiple of this
        int maxPatches = _cfg.MaxNumPatches;                 // 1024
        int minPatches = _cfg.MinImageTokens * _cfg.DownsampleFactor * _cfg.DownsampleFactor; // 64*4 = 256

        var (h2, w2) = SmartResize(bitmap.Height, bitmap.Width, minPatches, maxPatches, factor);

        using var resized = bitmap.Resize(
            new SKImageInfo(w2, h2, SKColorType.Rgba8888, SKAlphaType.Unpremul),
            new SKSamplingOptions(SKCubicResampler.Mitchell));
        if (resized is null)
            throw new ModelInferenceException("Failed to resize image.");

        int rows = h2 / _cfg.PatchSize;
        int cols = w2 / _cfg.PatchSize;
        int numPatches = rows * cols;
        int patchDim = Channels * _cfg.PatchSize * _cfg.PatchSize; // 768

        // Tokens after pixel-unshuffle by downsample_factor.
        int tokenRows = rows / _cfg.DownsampleFactor;
        int tokenCols = cols / _cfg.DownsampleFactor;
        int imageTokens = tokenRows * tokenCols;

        var pixelValues = BuildPatchTensor(resized, rows, cols, patchDim, maxPatches);
        var visionInputs = AssembleVisionInputs(pixelValues, numPatches, maxPatches, rows, cols);
        var imageTokenIds = BuildImageTokenIds(imageTokens);

        return new ProcessedImage
        {
            VisionInputs = visionInputs,
            ImageTokenIds = imageTokenIds,
            ImageFeatureCount = imageTokens
        };
    }

    private static SKBitmap Decode(byte[] data)
    {
        var raw = SKBitmap.Decode(data)
            ?? throw new ModelInferenceException("Could not decode image data (unsupported or corrupt format).");

        // Normalize to straight-alpha RGBA8888 for deterministic channel access.
        if (raw.ColorType == SKColorType.Rgba8888 && raw.AlphaType == SKAlphaType.Unpremul)
            return raw;

        var converted = new SKBitmap(new SKImageInfo(raw.Width, raw.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using (raw)
        {
            if (!raw.CopyTo(converted, SKColorType.Rgba8888))
                throw new ModelInferenceException("Could not convert image to RGBA.");
        }
        return converted;
    }

    /// <summary>
    /// Chooses a resized (height, width), each a multiple of <paramref name="factor"/>, keeping the patch
    /// count within [<paramref name="minPatches"/>, <paramref name="maxPatches"/>] and aspect ratio close.
    /// </summary>
    private (int h, int w) SmartResize(int h, int w, int minPatches, int maxPatches, int factor)
    {
        int ps = _cfg.PatchSize;

        int h2 = Math.Max(factor, (int)Math.Round((double)h / factor) * factor);
        int w2 = Math.Max(factor, (int)Math.Round((double)w / factor) * factor);

        double Patches(int hh, int ww) => (double)(hh / ps) * (ww / ps);

        if (Patches(h2, w2) > maxPatches)
        {
            double scale = Math.Sqrt(maxPatches / Patches(h2, w2));
            h2 = Math.Max(factor, (int)Math.Floor(h2 * scale / factor) * factor);
            w2 = Math.Max(factor, (int)Math.Floor(w2 * scale / factor) * factor);
        }
        else if (Patches(h2, w2) < minPatches)
        {
            double scale = Math.Sqrt(minPatches / Patches(h2, w2));
            h2 = Math.Max(factor, (int)Math.Ceiling(h2 * scale / factor) * factor);
            w2 = Math.Max(factor, (int)Math.Ceiling(w2 * scale / factor) * factor);
        }

        return (h2, w2);
    }

    /// <summary>Extracts row-major patches, normalized, into a [1, maxPatches, patchDim] tensor.</summary>
    private DenseTensor<float> BuildPatchTensor(SKBitmap img, int rows, int cols, int patchDim, int maxPatches)
    {
        int ps = _cfg.PatchSize;
        float mean0 = _cfg.ImageMean[0], mean1 = _cfg.ImageMean[1], mean2 = _cfg.ImageMean[2];
        float std0 = _cfg.ImageStd[0], std1 = _cfg.ImageStd[1], std2 = _cfg.ImageStd[2];
        float rescale = _cfg.RescaleFactor;

        var pixels = img.Pixels; // SKColor[] row-major, length w*h
        int width = img.Width;

        var tensor = new DenseTensor<float>(new[] { 1, maxPatches, patchDim });
        var buf = tensor.Buffer.Span;
        int patchPixels = ps * ps;

        for (int pr = 0; pr < rows; pr++)
        {
            for (int pc = 0; pc < cols; pc++)
            {
                int patchIndex = pr * cols + pc;
                int baseOffset = patchIndex * patchDim;

                for (int py = 0; py < ps; py++)
                {
                    int srcY = pr * ps + py;
                    int rowStart = srcY * width + pc * ps;
                    for (int px = 0; px < ps; px++)
                    {
                        var color = pixels[rowStart + px];
                        // channel-major within patch: [c, py, px]  (PARITY: verify against reference)
                        float r = _cfg.DoRescale ? color.Red * rescale : color.Red;
                        float g = _cfg.DoRescale ? color.Green * rescale : color.Green;
                        float b = _cfg.DoRescale ? color.Blue * rescale : color.Blue;
                        if (_cfg.DoNormalize)
                        {
                            r = (r - mean0) / std0;
                            g = (g - mean1) / std1;
                            b = (b - mean2) / std2;
                        }
                        int inPatch = py * ps + px;
                        buf[baseOffset + 0 * patchPixels + inPatch] = r;
                        buf[baseOffset + 1 * patchPixels + inPatch] = g;
                        buf[baseOffset + 2 * patchPixels + inPatch] = b;
                    }
                }
            }
        }

        return tensor;
    }

    /// <summary>Builds the named vision inputs, matching graph input names and dtypes by introspection.</summary>
    private List<NamedOnnxValue> AssembleVisionInputs(DenseTensor<float> pixelValues, int numPatches, int maxPatches, int rows, int cols)
    {
        var inputs = new List<NamedOnnxValue>();
        bool havePixels = false;

        foreach (var (name, meta) in _visionInputs)
        {
            var lname = name.ToLowerInvariant();
            if (lname.Contains("pixel") && lname.Contains("mask"))
            {
                inputs.Add(BuildMask(name, meta, numPatches, maxPatches));
            }
            else if (lname.Contains("pixel"))
            {
                inputs.Add(NamedOnnxValue.CreateFromTensor(name, pixelValues));
                havePixels = true;
            }
            else if (lname.Contains("spatial") || lname.Contains("shape"))
            {
                inputs.Add(BuildSpatial(name, meta, rows, cols));
            }
            else if (lname.Contains("mask") || lname.Contains("attention"))
            {
                inputs.Add(BuildMask(name, meta, numPatches, maxPatches));
            }
            else
            {
                throw new ModelInferenceException(
                    $"Unrecognized vision encoder input '{name}'. Known inputs: {string.Join(", ", _visionInputs.Keys)}. " +
                    "The image preprocessor needs updating for this graph layout.");
            }
        }

        if (!havePixels)
            throw new ModelInferenceException(
                $"Vision encoder exposes no pixel_values-like input. Inputs: {string.Join(", ", _visionInputs.Keys)}.");

        return inputs;
    }

    private static NamedOnnxValue BuildSpatial(string name, NodeMetadata meta, int rows, int cols)
    {
        // spatial_shapes: [1, 2] = (patch rows, patch cols)
        if (meta.ElementType == typeof(int))
        {
            var t = new DenseTensor<int>(new[] { 1, 2 });
            t[0, 0] = rows; t[0, 1] = cols;
            return NamedOnnxValue.CreateFromTensor(name, t);
        }
        var l = new DenseTensor<long>(new[] { 1, 2 });
        l[0, 0] = rows; l[0, 1] = cols;
        return NamedOnnxValue.CreateFromTensor(name, l);
    }

    private static NamedOnnxValue BuildMask(string name, NodeMetadata meta, int numPatches, int maxPatches)
    {
        // attention mask over patches: 1 for valid, 0 for padding.
        if (meta.ElementType == typeof(bool))
        {
            var t = new DenseTensor<bool>(new[] { 1, maxPatches });
            for (int i = 0; i < numPatches; i++) t[0, i] = true;
            return NamedOnnxValue.CreateFromTensor(name, t);
        }
        if (meta.ElementType == typeof(float))
        {
            var t = new DenseTensor<float>(new[] { 1, maxPatches });
            for (int i = 0; i < numPatches; i++) t[0, i] = 1f;
            return NamedOnnxValue.CreateFromTensor(name, t);
        }
        var l = new DenseTensor<long>(new[] { 1, maxPatches });
        for (int i = 0; i < numPatches; i++) l[0, i] = 1;
        return NamedOnnxValue.CreateFromTensor(name, l);
    }

    private IReadOnlyList<int> BuildImageTokenIds(int imageTokens)
    {
        var ids = new List<int>(imageTokens + 2) { _tokenizer.ImageStartId };
        for (int i = 0; i < imageTokens; i++)
            ids.Add(_tokenizer.ImageTokenId);
        ids.Add(_tokenizer.ImageEndId);
        return ids;
    }
}
