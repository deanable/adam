using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LiquidVision.Core.Services;

/// <summary>
/// Native (no-Python) byte-level BPE tokenizer for the LFM2 family, loaded directly from a
/// HuggingFace <c>tokenizer.json</c>. Faithfully reproduces the GPT-2 / cl100k style pipeline:
/// special-token splitting, a regex pre-tokenizer, byte-level encoding, and rank-ordered BPE merges.
/// </summary>
public sealed class Lfm2Tokenizer
{
    // GPT-2 / cl100k pre-tokenization split pattern (from tokenizer.json pre_tokenizer).
    private const string SplitPattern =
        @"(?i:'s|'t|'re|'ve|'m|'ll|'d)|[^\r\n\p{L}\p{N}]?\p{L}+|\p{N}{1,3}| ?[^\s\p{L}\p{N}]+[\r\n]*|\s*[\r\n]+|\s+(?!\S)|\s+";

    private readonly Dictionary<string, int> _vocab;            // token symbol-string -> id
    private readonly Dictionary<int, string> _idToToken;        // id -> token symbol-string (non-special)
    private readonly Dictionary<(string, string), int> _mergeRanks;
    private readonly Dictionary<string, int> _specialToId;      // special content -> id
    private readonly Dictionary<int, string> _idToSpecial;      // id -> special content
    private readonly char[] _byteToChar;                        // 0..255 -> unicode placeholder char
    private readonly Dictionary<char, byte> _charToByte;
    private readonly Regex _splitRegex;
    private readonly Regex? _specialRegex;
    private readonly Dictionary<string, string[]> _bpeCache = new();

    /// <summary>BOS token id (<c>&lt;|startoftext|&gt;</c>).</summary>
    public int BosId { get; }
    /// <summary>EOS token id (<c>&lt;|im_end|&gt;</c>), which terminates generation.</summary>
    public int EosId { get; }
    /// <summary>Padding token id.</summary>
    public int PadId { get; }
    /// <summary>Placeholder token id for image features (<c>&lt;image&gt;</c>).</summary>
    public int ImageTokenId { get; }
    /// <summary>Image-block start token id.</summary>
    public int ImageStartId { get; }
    /// <summary>Image-block end token id.</summary>
    public int ImageEndId { get; }
    /// <summary>Thumbnail marker token id.</summary>
    public int ImageThumbnailId { get; }

    private Lfm2Tokenizer(
        Dictionary<string, int> vocab,
        Dictionary<(string, string), int> mergeRanks,
        Dictionary<string, int> specialToId)
    {
        _vocab = vocab;
        _mergeRanks = mergeRanks;
        _specialToId = specialToId;
        _idToSpecial = specialToId.ToDictionary(kv => kv.Value, kv => kv.Key);
        _idToToken = new Dictionary<int, string>(vocab.Count);
        foreach (var kv in vocab)
            _idToToken[kv.Value] = kv.Key; // specials are overridden via _idToSpecial at decode time

        (_byteToChar, _charToByte) = BuildByteMaps();
        _splitRegex = new Regex(SplitPattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);

        if (specialToId.Count > 0)
        {
            // Longest contents first so e.g. "<|image_start|>" wins over any shorter prefix.
            var alternation = string.Join("|", specialToId.Keys
                .OrderByDescending(s => s.Length)
                .Select(Regex.Escape));
            _specialRegex = new Regex("(" + alternation + ")", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        }

        BosId = ResolveSpecial("<|startoftext|>", fallback: 1);
        EosId = ResolveSpecial("<|im_end|>", fallback: 7);
        PadId = ResolveSpecial("<|pad|>", fallback: 0);
        ImageTokenId = ResolveSpecial("<image>", fallback: 396);
        ImageStartId = ResolveSpecial("<|image_start|>", fallback: 498);
        ImageEndId = ResolveSpecial("<|image_end|>", fallback: 499);
        ImageThumbnailId = ResolveSpecial("<|img_thumbnail|>", fallback: 497);
    }

    /// <summary>Loads a tokenizer from a HuggingFace <c>tokenizer.json</c> on disk.</summary>
    public static Lfm2Tokenizer FromFile(string tokenizerJsonPath)
    {
        using var stream = File.OpenRead(tokenizerJsonPath);
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;
        var model = root.GetProperty("model");

        var vocab = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var prop in model.GetProperty("vocab").EnumerateObject())
            vocab[prop.Name] = prop.Value.GetInt32();

        var mergeRanks = new Dictionary<(string, string), int>();
        if (model.TryGetProperty("merges", out var merges))
        {
            int rank = 0;
            foreach (var merge in merges.EnumerateArray())
            {
                string a, b;
                if (merge.ValueKind == JsonValueKind.Array)
                {
                    // newer format: ["a", "b"]
                    a = merge[0].GetString()!;
                    b = merge[1].GetString()!;
                }
                else
                {
                    // classic format: "a b"
                    var s = merge.GetString()!;
                    int sp = s.IndexOf(' ');
                    a = s[..sp];
                    b = s[(sp + 1)..];
                }
                _ = mergeRanks.TryAdd((a, b), rank++);
            }
        }

        var specialToId = new Dictionary<string, int>(StringComparer.Ordinal);
        if (root.TryGetProperty("added_tokens", out var added))
        {
            foreach (var tok in added.EnumerateArray())
            {
                if (tok.TryGetProperty("special", out var sp) && sp.ValueKind == JsonValueKind.True)
                    specialToId[tok.GetProperty("content").GetString()!] = tok.GetProperty("id").GetInt32();
            }
        }

        return new Lfm2Tokenizer(vocab, mergeRanks, specialToId);
    }

    /// <summary>
    /// Encodes text to token ids. Recognized special-token substrings (e.g. <c>&lt;|im_start|&gt;</c>,
    /// <c>&lt;image&gt;</c>) are emitted as their atomic ids. No BOS/EOS is added automatically — callers
    /// that need them should include the token text (the chat template already starts with the BOS token).
    /// </summary>
    public IReadOnlyList<int> Encode(string text)
    {
        var ids = new List<int>();
        if (string.IsNullOrEmpty(text))
            return ids;

        foreach (var (segment, isSpecial) in SplitOnSpecials(text))
        {
            if (isSpecial)
            {
                ids.Add(_specialToId[segment]);
                continue;
            }

            foreach (Match m in _splitRegex.Matches(segment))
            {
                if (m.Value.Length == 0) continue;
                var symbols = ByteEncode(m.Value);
                foreach (var sym in Bpe(symbols))
                {
                    if (_vocab.TryGetValue(sym, out var id))
                        ids.Add(id);
                    // byte-level vocab is complete for single bytes, so misses should not occur
                }
            }
        }

        return ids;
    }

    /// <summary>Decodes token ids back to a string, optionally omitting special tokens.</summary>
    public string Decode(IEnumerable<int> ids, bool skipSpecialTokens = true)
    {
        var sb = new StringBuilder();
        var bytes = new List<byte>();

        void Flush()
        {
            if (bytes.Count == 0) return;
            sb.Append(Encoding.UTF8.GetString(bytes.ToArray()));
            bytes.Clear();
        }

        foreach (var id in ids)
        {
            if (_idToSpecial.TryGetValue(id, out var special))
            {
                Flush();
                if (!skipSpecialTokens)
                    sb.Append(special);
                continue;
            }

            if (_idToToken.TryGetValue(id, out var token))
            {
                foreach (var ch in token)
                    if (_charToByte.TryGetValue(ch, out var b))
                        bytes.Add(b);
            }
        }

        Flush();
        return sb.ToString();
    }

    /// <summary>True if the id is the EOS token.</summary>
    public bool IsEos(int id) => id == EosId;

    private int ResolveSpecial(string content, int fallback) =>
        _specialToId.TryGetValue(content, out var id) ? id : fallback;

    private IEnumerable<(string segment, bool isSpecial)> SplitOnSpecials(string text)
    {
        if (_specialRegex is null)
        {
            yield return (text, false);
            yield break;
        }

        int last = 0;
        foreach (Match m in _specialRegex.Matches(text))
        {
            if (m.Index > last)
                yield return (text[last..m.Index], false);
            yield return (m.Value, true);
            last = m.Index + m.Length;
        }
        if (last < text.Length)
            yield return (text[last..], false);
    }

    private string ByteEncode(string piece)
    {
        var bytes = Encoding.UTF8.GetBytes(piece);
        var chars = new char[bytes.Length];
        for (int i = 0; i < bytes.Length; i++)
            chars[i] = _byteToChar[bytes[i]];
        return new string(chars);
    }

    private string[] Bpe(string word)
    {
        if (_bpeCache.TryGetValue(word, out var cached))
            return cached;

        var symbols = new List<string>(word.Length);
        foreach (var ch in word)
            symbols.Add(ch.ToString());

        if (symbols.Count == 1)
        {
            var single = symbols.ToArray();
            _bpeCache[word] = single;
            return single;
        }

        while (true)
        {
            // Find the adjacent pair with the lowest merge rank.
            int bestRank = int.MaxValue;
            int bestIdx = -1;
            for (int i = 0; i < symbols.Count - 1; i++)
            {
                if (_mergeRanks.TryGetValue((symbols[i], symbols[i + 1]), out var rank) && rank < bestRank)
                {
                    bestRank = rank;
                    bestIdx = i;
                }
            }

            if (bestIdx < 0)
                break;

            var first = symbols[bestIdx];
            var second = symbols[bestIdx + 1];
            var merged = first + second;

            // Merge all non-overlapping occurrences of (first, second) in one pass (GPT-2 behavior).
            var next = new List<string>(symbols.Count);
            int j = 0;
            while (j < symbols.Count)
            {
                if (j < symbols.Count - 1 && symbols[j] == first && symbols[j + 1] == second)
                {
                    next.Add(merged);
                    j += 2;
                }
                else
                {
                    next.Add(symbols[j]);
                    j += 1;
                }
            }

            symbols = next;
            if (symbols.Count == 1)
                break;
        }

        var result = symbols.ToArray();
        _bpeCache[word] = result;
        return result;
    }

    private static (char[] byteToChar, Dictionary<char, byte> charToByte) BuildByteMaps()
    {
        var bs = new List<int>();
        var seen = new HashSet<int>();
        void Add(int from, int to)
        {
            for (int i = from; i <= to; i++) { bs.Add(i); seen.Add(i); }
        }
        Add('!', '~');       // 33..126
        Add('¡', '¬'); // 161..172
        Add('®', 'ÿ'); // 174..255

        var cs = new List<int>(bs);
        int n = 0;
        for (int b = 0; b < 256; b++)
        {
            if (!seen.Contains(b))
            {
                bs.Add(b);
                cs.Add(256 + n);
                n++;
            }
        }

        var byteToChar = new char[256];
        var charToByte = new Dictionary<char, byte>();
        for (int i = 0; i < bs.Count; i++)
        {
            byteToChar[bs[i]] = (char)cs[i];
            charToByte[(char)cs[i]] = (byte)bs[i];
        }
        return (byteToChar, charToByte);
    }
}
