using System.IO;
using System.Text.Json;

namespace LiquidVision.Core.Services;

/// <summary>
/// Strongly-typed subset of <c>config.json</c> needed for inference.
/// </summary>
public sealed class ModelConfig
{
    public int ImageTokenId { get; init; } = 396;
    public int TextHiddenSize { get; init; } = 1024;
    public int EosTokenId { get; init; } = 7;
    public int BosTokenId { get; init; } = 1;
    public int PadTokenId { get; init; } = 0;
    public int DownsampleFactor { get; init; } = 2;

    public static ModelConfig FromFile(string configPath)
    {
        using var stream = File.OpenRead(configPath);
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;

        int imageTokenId = GetInt(root, "image_token_id", 396);
        int downsample = GetInt(root, "downsample_factor", 2);

        int hidden = 1024, eos = 7, bos = 1, pad = 0;
        if (root.TryGetProperty("text_config", out var text))
        {
            hidden = GetInt(text, "hidden_size", hidden);
            eos = GetInt(text, "eos_token_id", eos);
        }

        return new ModelConfig
        {
            ImageTokenId = imageTokenId,
            TextHiddenSize = hidden,
            EosTokenId = eos,
            BosTokenId = bos,
            PadTokenId = pad,
            DownsampleFactor = downsample
        };
    }

    private static int GetInt(JsonElement obj, string name, int fallback) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : fallback;
}

/// <summary>
/// Strongly-typed <c>preprocessor_config.json</c> driving image preprocessing/tiling.
/// </summary>
public sealed class PreprocessorConfig
{
    public bool DoImageSplitting { get; init; } = true;
    public bool DoNormalize { get; init; } = true;
    public bool DoRescale { get; init; } = true;
    public bool UseThumbnail { get; init; } = true;
    public int TileSize { get; init; } = 512;
    public int Height { get; init; } = 512;
    public int Width { get; init; } = 512;
    public int MinTiles { get; init; } = 2;
    public int MaxTiles { get; init; } = 10;
    public int PatchSize { get; init; } = 16;
    public int EncoderPatchSize { get; init; } = 16;
    public int DownsampleFactor { get; init; } = 2;
    public int MaxImageTokens { get; init; } = 256;
    public int MinImageTokens { get; init; } = 64;
    public int MaxNumPatches { get; init; } = 1024;
    public double MaxPixelsTolerance { get; init; } = 2.0;
    public float RescaleFactor { get; init; } = 1f / 255f;
    public float[] ImageMean { get; init; } = { 0.5f, 0.5f, 0.5f };
    public float[] ImageStd { get; init; } = { 0.5f, 0.5f, 0.5f };

    public static PreprocessorConfig FromFile(string path)
    {
        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);
        var r = doc.RootElement;

        return new PreprocessorConfig
        {
            DoImageSplitting = GetBool(r, "do_image_splitting", true),
            DoNormalize = GetBool(r, "do_normalize", true),
            DoRescale = GetBool(r, "do_rescale", true),
            UseThumbnail = GetBool(r, "use_thumbnail", true),
            TileSize = GetInt(r, "tile_size", 512),
            Height = GetSize(r, "height", 512),
            Width = GetSize(r, "width", 512),
            MinTiles = GetInt(r, "min_tiles", 2),
            MaxTiles = GetInt(r, "max_tiles", 10),
            PatchSize = GetInt(r, "patch_size", 16),
            EncoderPatchSize = GetInt(r, "encoder_patch_size", 16),
            DownsampleFactor = GetInt(r, "downsample_factor", 2),
            MaxImageTokens = GetInt(r, "max_image_tokens", 256),
            MinImageTokens = GetInt(r, "min_image_tokens", 64),
            MaxNumPatches = GetInt(r, "max_num_patches", 1024),
            MaxPixelsTolerance = GetDouble(r, "max_pixels_tolerance", 2.0),
            RescaleFactor = (float)GetDouble(r, "rescale_factor", 1.0 / 255.0),
            ImageMean = GetFloatArray(r, "image_mean", new[] { 0.5f, 0.5f, 0.5f }),
            ImageStd = GetFloatArray(r, "image_std", new[] { 0.5f, 0.5f, 0.5f })
        };
    }

    private static bool GetBool(JsonElement o, string n, bool d) =>
        o.TryGetProperty(n, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) ? v.GetBoolean() : d;

    private static int GetInt(JsonElement o, string n, int d) =>
        o.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : d;

    private static double GetDouble(JsonElement o, string n, double d) =>
        o.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : d;

    private static int GetSize(JsonElement o, string key, int d) =>
        o.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Object ? GetInt(s, key, d) : d;

    private static float[] GetFloatArray(JsonElement o, string n, float[] d)
    {
        if (!o.TryGetProperty(n, out var v) || v.ValueKind != JsonValueKind.Array)
            return d;
        var list = new System.Collections.Generic.List<float>();
        foreach (var item in v.EnumerateArray())
            list.Add((float)item.GetDouble());
        return list.Count > 0 ? list.ToArray() : d;
    }
}
