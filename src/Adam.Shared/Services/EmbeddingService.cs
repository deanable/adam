using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Adam.Shared.Services;

/// <summary>
/// Produces text embeddings using a local ONNX model (all-MiniLM-L6-v2).
/// Handles model download/caching, BERT tokenization, ONNX inference,
/// mean pooling, and L2 normalization.
/// 
/// The model produces 384-dim float32 vectors. Downloaded on first use
/// and cached alongside LiquidVision models.
/// </summary>
public sealed class EmbeddingService : IAsyncDisposable
{
    // ── Model metadata ─────────────────────────────────────────
    private const string ModelRepoId = "sentence-transformers/all-MiniLM-L6-v2";
    private const string OnnxFileName = "model.onnx";
    private const string TokenizerFileName = "tokenizer.json";
    private const string ModelVersion = "all-MiniLM-L6-v2-v1";
    private const int MaxSequenceLength = 256;
    private const int EmbeddingDimension = 384;

    // ── Built-in special tokens ────────────────────────────────
    private const int ClsTokenId = 101;  // [CLS]
    private const int SepTokenId = 102;  // [SEP]
    private const int PadTokenId = 0;    // [PAD]
    private const int UnkTokenId = 100;  // [UNK]
    private const string UnkToken = "[UNK]";

    private readonly string _modelDir;
    private readonly string _onnxPath;
    private readonly string _tokenizerPath;
    private readonly ILogger<EmbeddingService> _logger;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    private InferenceSession? _session;
    private Dictionary<string, int>? _vocab;
    private bool _initializationAttempted;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public EmbeddingService(
        IDbContextFactory<AppDbContext> dbFactory,
        ILogger<EmbeddingService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;

        // Cache alongside LiquidVision models
        var cacheRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Adam", "models", "embeddings", ModelRepoId.Replace("/", "--"));

        _modelDir = cacheRoot;
        _onnxPath = Path.Combine(_modelDir, OnnxFileName);
        _tokenizerPath = Path.Combine(_modelDir, TokenizerFileName);
    }

    /// <summary>True once the model is downloaded and loaded for inference.</summary>
    public bool IsInitialized => _session != null;

    /// <summary>
    /// Ensures the embedding model is downloaded and the ONNX session is ready.
    /// Safe to call multiple times.
    /// </summary>
    public async Task EnsureInitializedAsync(CancellationToken ct = default)
    {
        if (_session != null) return;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_session != null) return;
            if (_initializationAttempted) return;
            _initializationAttempted = true;

            Directory.CreateDirectory(_modelDir);

            if (!File.Exists(_onnxPath) || !File.Exists(_tokenizerPath))
                await DownloadModelAsync(ct);

            // Load tokenizer vocabulary
            _vocab = await LoadTokenizerAsync(_tokenizerPath, ct);

            // Create ONNX session
            var sessionOptions = new SessionOptions();
            sessionOptions.AppendExecutionProvider_CPU();
            _session = new InferenceSession(_onnxPath, sessionOptions);

            _logger.LogInformation(
                "[Embedding] Model loaded: {Model} (dim={Dim}, maxLen={MaxLen})",
                ModelRepoId, EmbeddingDimension, MaxSequenceLength);
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Computes a text embedding vector for the given text.
    /// Returns a 384-dim float32 array.
    /// </summary>
    public async Task<float[]> GetTextEmbeddingAsync(string text, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var tokens = Tokenize(text);
        var (inputIds, attentionMask) = PadOrTruncate(tokens);

        // Run ONNX inference
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(inputIds, [1, MaxSequenceLength])),
            NamedOnnxValue.CreateFromTensor("attention_mask", new DenseTensor<long>(attentionMask, [1, MaxSequenceLength]))
        };

        using var results = _session!.Run(inputs);
        var tokenEmbeddings = results.First().AsTensor<float>();

        // Mean pooling + L2 normalization
        return MeanPoolAndNormalize(tokenEmbeddings, attentionMask);
    }

    /// <summary>
    /// Computes or retrieves the text embedding for a specific asset.
    /// If the asset already has an embedding, returns it; otherwise computes and stores it.
    /// </summary>
    public async Task<float[]> GetAssetEmbeddingAsync(Guid assetId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var existing = await db.AssetEmbeddings
            .FirstOrDefaultAsync(e => e.AssetId == assetId, ct);

        if (existing != null && existing.TextEmbedding.Length == EmbeddingDimension * 4)
            return BytesToFloats(existing.TextEmbedding);

        // Compute text from asset metadata
        var asset = await db.DigitalAssets
            .Include(a => a.Keywords)
            .FirstOrDefaultAsync(a => a.Id == assetId, ct);

        if (asset == null)
            throw new InvalidOperationException($"Asset {assetId} not found");

        var text = BuildEmbeddingText(asset);
        var embedding = await GetTextEmbeddingAsync(text, ct);

        // Store in database
        if (existing != null)
        {
            existing.TextEmbedding = FloatsToBytes(embedding);
            existing.ModelVersion = ModelVersion;
            existing.ComputedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            db.AssetEmbeddings.Add(new AssetEmbedding
            {
                Id = Guid.NewGuid(),
                AssetId = assetId,
                TextEmbedding = FloatsToBytes(embedding),
                ModelVersion = ModelVersion,
                ComputedAt = DateTimeOffset.UtcNow
            });
        }

        await db.SaveChangesAsync(ct);
        return embedding;
    }

    /// <summary>
    /// Returns the number of assets that don't yet have embeddings.
    /// </summary>
    public async Task<int> GetPendingCountAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.DigitalAssets
            .Where(a => !db.AssetEmbeddings.Any(e => e.AssetId == a.Id))
            .CountAsync(ct);
    }

    /// <summary>
    /// Computes embeddings for all assets that don't already have one.
    /// Reports progress as (completed, total).
    /// </summary>
    public async Task ComputeAllEmbeddingsAsync(
        IProgress<(int completed, int total)>? progress = null,
        CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var assetIds = await db.DigitalAssets
            .Where(a => !db.AssetEmbeddings.Any(e => e.AssetId == a.Id))
            .Select(a => a.Id)
            .ToListAsync(ct);

        if (assetIds.Count == 0)
        {
            _logger.LogInformation("[Embedding] All assets already have embeddings");
            return;
        }

        _logger.LogInformation("[Embedding] Computing embeddings for {Count} assets...", assetIds.Count);

        var completed = 0;
        foreach (var id in assetIds)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await GetAssetEmbeddingAsync(id, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Embedding] Failed to compute embedding for asset {AssetId}", id);
            }
            completed++;
            progress?.Report((completed, assetIds.Count));
        }

        _logger.LogInformation("[Embedding] Completed embeddings for {Count} assets", completed);
    }

    // ── Tokenizer ──────────────────────────────────────────────

    /// <summary>
    /// Tokenizes text into WordPiece token IDs using the BERT vocabulary.
    /// </summary>
    private int[] Tokenize(string text)
    {
        if (_vocab == null)
            throw new InvalidOperationException("Tokenizer not loaded");

        var tokens = new List<int> { ClsTokenId };
        var words = WordPieceSplit(text);

        foreach (var word in words)
        {
            if (tokens.Count >= MaxSequenceLength - 1) break;

            var subTokens = WordPieceTokenize(word);
            foreach (var sub in subTokens)
            {
                if (tokens.Count >= MaxSequenceLength - 1) break;
                tokens.Add(sub);
            }
        }

        tokens.Add(SepTokenId);
        return tokens.ToArray();
    }

    /// <summary>
    /// Splits text into words by whitespace and punctuation boundaries.
    /// </summary>
    private static List<string> WordPieceSplit(string text)
    {
        // Normalize: lowercase, basic cleanup
        text = text.ToLowerInvariant();
        text = text.Normalize(NormalizationForm.FormKD);
        text = Regex.Replace(text, @"[^\w\s]", " $0 "); // isolate punctuation
        text = Regex.Replace(text, @"\s+", " ").Trim();

        if (string.IsNullOrWhiteSpace(text))
            return [];

        return [.. text.Split(' ', StringSplitOptions.RemoveEmptyEntries)];
    }

    /// <summary>
    /// Tokenizes a single word into one or more WordPiece sub-tokens.
    /// </summary>
    private List<int> WordPieceTokenize(string word)
    {
        if (_vocab == null)
            throw new InvalidOperationException("Tokenizer not loaded");

        var tokens = new List<int>();

        // Check full word first
        if (_vocab.TryGetValue(word, out var id))
        {
            tokens.Add(id);
            return tokens;
        }

        // WordPiece: split into sub-tokens with ## prefix
        var chars = word.ToCharArray();
        var start = 0;
        var isFirst = true;

        while (start < chars.Length)
        {
            var end = chars.Length;
            var found = false;

            while (end > start)
            {
                var sub = isFirst
                    ? new string(chars[start..end])
                    : "##" + new string(chars[start..end]);

                if (_vocab.TryGetValue(sub, out var subId))
                {
                    tokens.Add(subId);
                    start = end;
                    isFirst = false;
                    found = true;
                    break;
                }
                end--;
            }

            if (!found)
            {
                tokens.Add(UnkTokenId);
                break;
            }
        }

        return tokens;
    }

    /// <summary>
    /// Pads or truncates token IDs to MaxSequenceLength and creates attention mask.
    /// </summary>
    private static (long[] inputIds, long[] attentionMask) PadOrTruncate(int[] tokenIds)
    {
        var inputIds = new long[MaxSequenceLength];
        var attentionMask = new long[MaxSequenceLength];

        var length = Math.Min(tokenIds.Length, MaxSequenceLength);
        for (int i = 0; i < length; i++)
        {
            inputIds[i] = tokenIds[i];
            attentionMask[i] = 1;
        }

        // Pad remaining positions
        for (int i = length; i < MaxSequenceLength; i++)
        {
            inputIds[i] = PadTokenId;
            attentionMask[i] = 0;
        }

        return (inputIds, attentionMask);
    }

    // ── Pooling ────────────────────────────────────────────────

    /// <summary>
    /// Performs mean pooling over token embeddings (masking padding) then L2 normalizes.
    /// </summary>
    private static float[] MeanPoolAndNormalize(Tensor<float> tokenEmbeddings, long[] attentionMask)
    {
        // tokenEmbeddings shape: [1, seq_length, 384]
        var seqLength = tokenEmbeddings.Dimensions[1];
        var dim = tokenEmbeddings.Dimensions[2];
        var pooled = new float[dim];
        var maskSum = 0f;

        for (int j = 0; j < seqLength; j++)
        {
            if (attentionMask[j] == 0) continue;
            maskSum++;
            for (int k = 0; k < dim; k++)
                pooled[k] += tokenEmbeddings[0, j, k];
        }

        if (maskSum > 0)
        {
            for (int k = 0; k < dim; k++)
                pooled[k] /= maskSum;
        }

        // L2 normalize
        var norm = 0f;
        foreach (var v in pooled)
            norm += v * v;
        norm = MathF.Sqrt(norm);

        if (norm > 0)
        {
            for (int k = 0; k < dim; k++)
                pooled[k] /= norm;
        }

        return pooled;
    }

    // ── Model Download ─────────────────────────────────────────

    private async Task DownloadModelAsync(CancellationToken ct)
    {
        _logger.LogInformation("[Embedding] Downloading model from {Repo}...", ModelRepoId);

        var baseUrl = $"https://huggingface.co/{ModelRepoId}/resolve/main";

        // Download ONNX model with progress
        var onnxUrl = $"{baseUrl}/onnx/{OnnxFileName}";
        await DownloadFileAsync(onnxUrl, _onnxPath, "ONNX model", ct);

        // Download tokenizer config
        var tokenizerUrl = $"{baseUrl}/{TokenizerFileName}";
        await DownloadFileAsync(tokenizerUrl, _tokenizerPath, "tokenizer", ct);

        _logger.LogInformation("[Embedding] Model download complete");
    }

    private async Task DownloadFileAsync(string url, string destPath, string label, CancellationToken ct)
    {
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Adam/1.0");

        var partPath = destPath + ".part";
        var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var httpStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
        await httpStream.CopyToAsync(fileStream, ct);
        await fileStream.FlushAsync(ct);

        if (File.Exists(destPath))
            File.Delete(destPath);
        File.Move(partPath, destPath);

        _logger.LogDebug("[Embedding] Downloaded {Label} ({Size} bytes)", label, new FileInfo(destPath).Length);
    }

    // ── Tokenizer Loading ──────────────────────────────────────

    private static async Task<Dictionary<string, int>> LoadTokenizerAsync(string path, CancellationToken ct)
    {
        var json = await File.ReadAllTextAsync(path, ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // HuggingFace tokenizer.json format: model.vocab is a dict of token→id
        if (root.TryGetProperty("model", out var model) &&
            model.TryGetProperty("vocab", out var vocab))
        {
            var dict = new Dictionary<string, int>(vocab.EnumerateObject().Count());
            foreach (var entry in vocab.EnumerateObject())
            {
                dict[entry.Name] = entry.Value.GetInt32();
            }
            return dict;
        }

        // Fallback: try reading directly as a flat token→id dict
        var fallback = new Dictionary<string, int>(root.EnumerateObject().Count());
        foreach (var entry in root.EnumerateObject())
        {
            if (entry.Value.ValueKind == System.Text.Json.JsonValueKind.Number)
                fallback[entry.Name] = entry.Value.GetInt32();
        }

        if (fallback.Count > 0)
            return fallback;

        throw new InvalidOperationException("Could not parse tokenizer vocabulary from tokenizer.json");
    }

    // ── Utility Methods ────────────────────────────────────────

    /// <summary>
    /// Builds the text to embed from an asset's metadata (title, description, keywords, filename).
    /// </summary>
    private static string BuildEmbeddingText(DigitalAsset asset)
    {
        var sb = new StringBuilder();
        sb.Append(asset.Title);
        if (!string.IsNullOrWhiteSpace(asset.Description))
            sb.Append(". ").Append(asset.Description);
        if (asset.Keywords.Count > 0)
            sb.Append(". Keywords: ").Append(string.Join(", ", asset.Keywords.Select(k => k.Name)));
        sb.Append(". File: ").Append(asset.FileName);
        if (!string.IsNullOrWhiteSpace(asset.MetadataProfile?.CameraMake))
            sb.Append(". Camera: ").Append(asset.MetadataProfile.CameraMake);
        if (!string.IsNullOrWhiteSpace(asset.MetadataProfile?.CameraModel))
            sb.Append(" ").Append(asset.MetadataProfile.CameraModel);
        return sb.ToString();
    }

    /// <summary>Converts a float array to a byte array (little-endian).</summary>
    public static byte[] FloatsToBytes(float[] floats)
    {
        var bytes = new byte[floats.Length * 4];
        Buffer.BlockCopy(floats, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    /// <summary>Converts a byte array back to a float array.</summary>
    public static float[] BytesToFloats(byte[] bytes)
    {
        var floats = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);
        return floats;
    }

    // ── Disposal ───────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        _session?.Dispose();
        _initLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
