using Adam.Shared.Extractors;
using Adam.Shared.Models;

namespace SampleMetadataPlugin;

/// <summary>
/// Sample plugin that demonstrates the <see cref="IMetadataExtractor"/> interface.
/// Counts vowels in text files and returns the count as a keyword.
/// Built-in extractors use Priority 100-200; plugins should use 1000+.
/// </summary>
public sealed class SampleVowelCounterExtractor : IMetadataExtractor
{
    public int Priority => 1000;

    public string Name => "Vowel Counter Sample";

    public bool CanExtract(string filePath, string mimeType)
        => mimeType.StartsWith("text/") || filePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase);

    public Task<ExtractedTextMetadata?> ExtractTextAsync(string filePath, CancellationToken ct)
    {
        // Read only the first 1 MB to avoid OOM on large files
        const int maxBytes = 1_048_576;
        string text;
        using (var sr = new StreamReader(new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096),
                      detectEncodingFromByteOrderMarks: true))
        {
            var buffer = new char[maxBytes];
            var read = sr.Read(buffer, 0, maxBytes);
            text = new string(buffer, 0, read);
        }
        var vowels = text.Count(c => "aeiouAEIOU".Contains(c));
        var result = new ExtractedTextMetadata
        {
            Title = $"Vowel Analysis: {vowels} vowels found",
            Description = $"The file '{Path.GetFileName(filePath)}' contains {vowels} vowels."
        };
        result.Keywords.Add($"vowel-count:{vowels}");
        return Task.FromResult<ExtractedTextMetadata?>(result);
    }

    public Task<MetadataProfile?> ExtractAsync(string filePath, CancellationToken ct)
        => Task.FromResult<MetadataProfile?>(null); // Text files don't have rich EXIF/XMP metadata
}
