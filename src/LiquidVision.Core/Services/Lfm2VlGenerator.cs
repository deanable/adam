using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using LiquidVision.Core.Configuration;
using LiquidVision.Core.Exceptions;
using Float16 = Microsoft.ML.OnnxRuntime.Float16;

namespace LiquidVision.Core.Services;

/// <summary>
/// Drives the LFM2-VL decode loop across the three ONNX graphs: embed text tokens, encode the image,
/// merge image features into the text embeddings at the image-token positions, then autoregressively
/// decode with a KV/conv cache that is discovered generically from the decoder's graph metadata
/// (so the hybrid conv/attention layout needs no hardcoding).
/// </summary>
/// <remarks>
/// Precision-aware: each ONNX boundary's element type is discovered at runtime from the graph metadata
/// (<see cref="NodeMetadata.ElementType"/>) and tensors are converted fp32&lt;-&gt;fp16 only where the graph
/// demands it. The internal working representation stays <see cref="float"/> (so feature scatter and token
/// sampling are precision-independent); conversions happen only at the ORT boundary. KV/conv caches are kept
/// in their native dtype end-to-end to avoid per-step precision loss and CPU cost. This supports the fp32,
/// fp16, and q4f16 (fp16-activation) graph exports with no code change.
/// </remarks>
public sealed class Lfm2VlGenerator
{
    private readonly LoadedModel _m;
    private readonly LiquidVisionOptions _o;

    private readonly string _embedInput;
    private readonly string _embedOutput;
    private readonly NodeMetadata _embedOutputMeta;
    private readonly string _decoderEmbedsInput;
    private readonly NodeMetadata _decoderEmbedsMeta;
    private readonly string? _attnInput;
    private readonly string? _posInput;
    private readonly string _logitsOutput;
    private readonly NodeMetadata _logitsMeta;
    private readonly string _visionOutput;
    private readonly NodeMetadata _visionOutputMeta;
    private readonly List<CacheSlot> _caches;

    private sealed record CacheSlot(string PastInput, string PresentOutput, int[] Dims, Type ElementType);

    public Lfm2VlGenerator(LoadedModel model, LiquidVisionOptions options)
    {
        _m = model;
        _o = options;

        // --- embed_tokens graph ---
        _embedInput = model.EmbedTokens.InputMetadata.Keys.First();
        _embedOutput = model.EmbedTokens.OutputMetadata.Keys.First();
        _embedOutputMeta = model.EmbedTokens.OutputMetadata[_embedOutput];

        // --- vision encoder graph: first output is the image features ---
        _visionOutput = model.VisionEncoder.OutputMetadata.Keys.First();
        _visionOutputMeta = model.VisionEncoder.OutputMetadata[_visionOutput];

        // --- decoder graph: locate roles by name, discover caches generically ---
        var dIn = model.Decoder.InputMetadata;
        _decoderEmbedsInput = dIn.Keys.FirstOrDefault(k => k.Contains("inputs_embeds", StringComparison.OrdinalIgnoreCase))
            ?? throw new ModelInferenceException(
                "Decoder graph does not expose an 'inputs_embeds' input; cannot merge image features. " +
                $"Available inputs: {string.Join(", ", dIn.Keys)}");
        _decoderEmbedsMeta = dIn[_decoderEmbedsInput];
        _attnInput = dIn.Keys.FirstOrDefault(k => k.Contains("attention_mask", StringComparison.OrdinalIgnoreCase));
        _posInput = dIn.Keys.FirstOrDefault(k => k.Contains("position_ids", StringComparison.OrdinalIgnoreCase));

        _logitsOutput = model.Decoder.OutputMetadata.Keys.FirstOrDefault(k => k.Contains("logits", StringComparison.OrdinalIgnoreCase))
            ?? model.Decoder.OutputMetadata.Keys.First();
        _logitsMeta = model.Decoder.OutputMetadata[_logitsOutput];

        _caches = DiscoverCaches(model.Decoder);
        if (_caches.Count == 0)
            throw new ModelInferenceException("Decoder graph exposes no past/present cache tensors; unexpected export layout.");
    }

    /// <summary>Generates the assistant response text for a prompt + optional image.</summary>
    public string Generate(IReadOnlyList<int> promptIds, ProcessedImage? image, CancellationToken ct = default, Action<int>? onToken = null)
    {
        int hidden = _m.Config.TextHiddenSize;

        // 1) Text embeddings for the full prompt (placeholders included).
        var embeds = RunEmbed(promptIds.Select(i => (long)i).ToArray()); // [1, seq, hidden]

        // 2) Vision features merged into the placeholder positions.
        if (image is { ImageFeatureCount: > 0 })
        {
            var (visionFlat, visionRows) = RunVision(image, hidden);
            ScatterImageFeatures(embeds, promptIds, visionFlat, visionRows, hidden);
        }

        // 3) Initialize caches, masks, positions.
        var caches = InitCaches();
        int totalLen = promptIds.Count;
        var generated = new List<int>();
        var currentEmbeds = embeds;

        for (int step = 0; step < _o.MaxNewTokens; step++)
        {
            ct.ThrowIfCancellationRequested();

            int curLen = currentEmbeds.Dimensions[1];
            var inputs = new List<NamedOnnxValue> { MakeFloatInput(_decoderEmbedsInput, _decoderEmbedsMeta, currentEmbeds) };

            if (_attnInput is not null)
                inputs.Add(NamedOnnxValue.CreateFromTensor(_attnInput, Ones2D(totalLen)));
            if (_posInput is not null)
                inputs.Add(NamedOnnxValue.CreateFromTensor(_posInput, PositionIds(totalLen - curLen, curLen)));
            foreach (var slot in _caches)
                inputs.Add(caches[slot.PastInput]);

            int nextId;
            using (var results = _m.Decoder.Run(inputs))
            {
                var logits = results.First(r => r.Name == _logitsOutput);
                var lastRow = ReadLogitsLastRow(logits, _logitsMeta, curLen, out int vocab);
                nextId = SelectNextToken(lastRow, vocab);

                // Materialize present -> next past (must copy out before results dispose), in native dtype.
                foreach (var slot in _caches)
                    caches[slot.PastInput] = CloneNativePresent(results, slot);
            }

            generated.Add(nextId);
            onToken?.Invoke(nextId);

            if (_m.Tokenizer.IsEos(nextId))
                break;

            // Next step feeds only the new token's embedding.
            currentEmbeds = RunEmbed(new[] { (long)nextId });
            totalLen += 1;
        }

        return _m.Tokenizer.Decode(generated, skipSpecialTokens: true);
    }

    // ---- graph helpers ----

    private DenseTensor<float> RunEmbed(long[] ids)
    {
        var input = new DenseTensor<long>(new[] { 1, ids.Length });
        for (int i = 0; i < ids.Length; i++) input[0, i] = ids[i];

        using var results = _m.EmbedTokens.Run(new[] { NamedOnnxValue.CreateFromTensor(_embedInput, input) });
        return ReadFloatOutput(results, _embedOutput, _embedOutputMeta);
    }

    private (float[] flat, int rows) RunVision(ProcessedImage image, int hidden)
    {
        using var results = _m.VisionEncoder.Run(image.VisionInputs);
        var outT = ReadFloatOutput(results, _visionOutput, _visionOutputMeta);
        var flat = outT.ToArray();
        if (flat.Length % hidden != 0)
            throw new ModelInferenceException($"Vision output length {flat.Length} is not a multiple of hidden size {hidden}.");
        return (flat, flat.Length / hidden);
    }

    private void ScatterImageFeatures(DenseTensor<float> embeds, IReadOnlyList<int> promptIds, float[] visionFlat, int visionRows, int hidden)
    {
        int imageTokenId = _m.Config.ImageTokenId;
        var buffer = embeds.Buffer.Span;
        int placeholders = 0;

        for (int p = 0; p < promptIds.Count; p++)
        {
            if (promptIds[p] != imageTokenId) continue;
            if (placeholders >= visionRows) break;
            int dst = p * hidden;
            int src = placeholders * hidden;
            visionFlat.AsSpan(src, hidden).CopyTo(buffer.Slice(dst, hidden));
            placeholders++;
        }

        if (placeholders != visionRows)
            throw new ModelInferenceException(
                $"Image placeholder/feature mismatch: {placeholders} placeholder tokens but {visionRows} vision feature vectors. " +
                "The image preprocessing token count must match the vision encoder output.");
    }

    private List<CacheSlot> DiscoverCaches(InferenceSession decoder)
    {
        var outputs = decoder.OutputMetadata.Keys.ToHashSet();
        var slots = new List<CacheSlot>();

        foreach (var (name, meta) in decoder.InputMetadata)
        {
            if (!name.StartsWith("past", StringComparison.OrdinalIgnoreCase))
                continue;

            // Map past input -> present output by swapping the conventional prefix.
            string present = name.StartsWith("past_key_values", StringComparison.OrdinalIgnoreCase)
                ? "present" + name.Substring("past_key_values".Length)
                : "present" + name.Substring("past".Length);

            if (!outputs.Contains(present))
            {
                // Fall back: match any present output sharing the numeric/suffix tail.
                var tail = name.Substring(name.IndexOf('.') is var d && d >= 0 ? d : 0);
                present = outputs.FirstOrDefault(o => o.StartsWith("present", StringComparison.OrdinalIgnoreCase) && o.EndsWith(tail))
                          ?? throw new ModelInferenceException($"No matching 'present' output for decoder cache input '{name}'.");
            }

            slots.Add(new CacheSlot(name, present, meta.Dimensions.ToArray(), meta.ElementType));
        }

        return slots;
    }

    private Dictionary<string, NamedOnnxValue> InitCaches()
    {
        var dict = new Dictionary<string, NamedOnnxValue>();
        foreach (var slot in _caches)
        {
            // Replace dynamic dims: batch (index 0) -> 1, any other dynamic (e.g. past sequence length) -> 0.
            // Fixed dims (e.g. heads, head_dim, conv length) are kept, so conv caches start zero-filled at length.
            var dims = new int[slot.Dims.Length];
            for (int i = 0; i < dims.Length; i++)
                dims[i] = slot.Dims[i] >= 0 ? slot.Dims[i] : (i == 0 ? 1 : 0);

            // Zero-init in native dtype. default(Float16) is bit-pattern 0 == +0.0, so this is correct.
            NamedOnnxValue value = IsFp16(slot.ElementType)
                ? NamedOnnxValue.CreateFromTensor(slot.PastInput, new DenseTensor<Float16>(dims))
                : NamedOnnxValue.CreateFromTensor(slot.PastInput, new DenseTensor<float>(dims));
            dict[slot.PastInput] = value;
        }
        return dict;
    }

    // ---- precision boundary helpers ----

    private static bool IsFp16(NodeMetadata meta) => meta.ElementType == typeof(Float16);
    private static bool IsFp16(Type elementType) => elementType == typeof(Float16);

    /// <summary>
    /// Wraps a float source tensor as the decoder input, converting to Float16 when the graph expects it.
    /// fp32 takes a zero-copy fast path.
    /// </summary>
    private static NamedOnnxValue MakeFloatInput(string name, NodeMetadata meta, DenseTensor<float> src)
    {
        if (!IsFp16(meta))
            return NamedOnnxValue.CreateFromTensor(name, src);

        var dst = new DenseTensor<Float16>(src.Dimensions.ToArray());
        var s = src.Buffer.Span;
        var d = dst.Buffer.Span;
        for (int i = 0; i < s.Length; i++) d[i] = (Float16)s[i];
        return NamedOnnxValue.CreateFromTensor(name, dst);
    }

    /// <summary>Reads a graph output as a float dense tensor, converting from Float16 when needed.</summary>
    private static DenseTensor<float> ReadFloatOutput(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results, string name, NodeMetadata meta)
    {
        var result = results.First(r => r.Name == name);
        if (!IsFp16(meta))
            return CloneTensor(result.AsTensor<float>());

        var src = result.AsTensor<Float16>();
        var dims = src.Dimensions.ToArray();
        var clone = new DenseTensor<float>(dims);
        var d = clone.Buffer.Span;
        if (src is DenseTensor<Float16> dense)
        {
            var s = dense.Buffer.Span;
            for (int i = 0; i < s.Length; i++) d[i] = (float)s[i];
        }
        else
        {
            var arr = src.ToArray();
            for (int i = 0; i < arr.Length; i++) d[i] = (float)arr[i];
        }
        return clone;
    }

    /// <summary>
    /// Extracts only the last position's logits as a float[vocab], converting from Float16 when needed.
    /// Avoids materializing the full [1, curLen, vocab] tensor.
    /// </summary>
    private static float[] ReadLogitsLastRow(DisposableNamedOnnxValue logits, NodeMetadata meta, int curLen, out int vocab)
    {
        int row = curLen - 1;
        if (!IsFp16(meta))
        {
            var t = logits.AsTensor<float>();
            vocab = t.Dimensions[^1];
            var outF = new float[vocab];
            for (int v = 0; v < vocab; v++) outF[v] = t[0, row, v];
            return outF;
        }

        var h = logits.AsTensor<Float16>();
        vocab = h.Dimensions[^1];
        var outH = new float[vocab];
        for (int v = 0; v < vocab; v++) outH[v] = (float)h[0, row, v];
        return outH;
    }

    /// <summary>Clones a decoder 'present' output into the next 'past' input, preserving native dtype.</summary>
    private static NamedOnnxValue CloneNativePresent(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results, CacheSlot slot)
    {
        var present = results.First(r => r.Name == slot.PresentOutput);
        if (IsFp16(slot.ElementType))
        {
            var src = present.AsTensor<Float16>();
            var clone = new DenseTensor<Float16>(src.Dimensions.ToArray());
            if (src is DenseTensor<Float16> dense)
                dense.Buffer.Span.CopyTo(clone.Buffer.Span);
            else
                src.ToArray().CopyTo(clone.Buffer.Span);
            return NamedOnnxValue.CreateFromTensor(slot.PastInput, clone);
        }

        return NamedOnnxValue.CreateFromTensor(slot.PastInput, CloneTensor(present.AsTensor<float>()));
    }

    // ---- tensor utilities ----

    private static DenseTensor<long> Ones2D(int length)
    {
        var t = new DenseTensor<long>(new[] { 1, length });
        var span = t.Buffer.Span;
        for (int i = 0; i < span.Length; i++) span[i] = 1;
        return t;
    }

    private static DenseTensor<long> PositionIds(int start, int count)
    {
        var t = new DenseTensor<long>(new[] { 1, count });
        for (int i = 0; i < count; i++) t[0, i] = start + i;
        return t;
    }

    private int SelectNextToken(float[] lastRow, int vocab)
    {
        if (_o.Greedy)
            return Argmax(lastRow, vocab);

        return SampleTopP(lastRow, vocab, _o.Temperature, _o.TopP, _o.Seed);
    }

    /// <summary>
    /// Nucleus (top-p) sampling over a 1-D logits row. Exposed internally for unit testing; the instance
    /// path supplies temperature/top-p/seed from <see cref="LiquidVisionOptions"/>.
    /// </summary>
    internal static int SampleTopP(float[] logits, int vocab, float temperature, float topP, int? seed)
    {
        var probs = new float[vocab];
        float max = float.NegativeInfinity;
        for (int v = 0; v < vocab; v++) max = Math.Max(max, logits[v]);

        float sum = 0f;
        float invT = 1f / Math.Max(temperature, 1e-6f);
        for (int v = 0; v < vocab; v++)
        {
            float e = MathF.Exp((logits[v] - max) * invT);
            probs[v] = e;
            sum += e;
        }
        for (int v = 0; v < vocab; v++) probs[v] /= sum;

        var idx = Enumerable.Range(0, vocab).ToArray();
        Array.Sort(idx, (a, b) => probs[b].CompareTo(probs[a]));

        var rng = seed is int s ? new Random(s) : Random.Shared;
        float cumulative = 0f;
        double r = rng.NextDouble();
        float kept = 0f;
        int last = 0;
        // Renormalize over the nucleus.
        var nucleus = new List<int>();
        foreach (var v in idx)
        {
            nucleus.Add(v);
            kept += probs[v];
            last = v;
            if (kept >= topP) break;
        }
        double pick = r * kept;
        foreach (var v in nucleus)
        {
            cumulative += probs[v];
            if (pick <= cumulative) return v;
        }
        return last;
    }

    /// <summary>Greedy argmax over a 1-D logits row. Exposed internally for unit testing.</summary>
    internal static int Argmax(float[] logits, int vocab)
    {
        int best = 0;
        float bestVal = float.NegativeInfinity;
        for (int v = 0; v < vocab; v++)
        {
            if (logits[v] > bestVal) { bestVal = logits[v]; best = v; }
        }
        return best;
    }

    private static DenseTensor<float> CloneTensor(Tensor<float> source)
    {
        var dims = source.Dimensions.ToArray();
        var clone = new DenseTensor<float>(dims);
        if (source is DenseTensor<float> dense)
            dense.Buffer.Span.CopyTo(clone.Buffer.Span);
        else
            source.ToArray().CopyTo(clone.Buffer.Span);
        return clone;
    }
}
