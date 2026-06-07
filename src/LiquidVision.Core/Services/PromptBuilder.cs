using System.Collections.Generic;

namespace LiquidVision.Core.Services;

/// <summary>
/// Builds the ChatML token-id sequence the LFM2-VL decoder expects, splicing the image placeholder
/// token sequence (produced by the image processor) into the user turn.
/// </summary>
/// <remarks>
/// The LFM2 chat template is:
/// <code>
/// &lt;|startoftext|&gt;&lt;|im_start|&gt;system\n{system}&lt;|im_end|&gt;\n
/// &lt;|im_start|&gt;user\n{image}{instruction}&lt;|im_end|&gt;\n
/// &lt;|im_start|&gt;assistant\n
/// </code>
/// where <c>{image}</c> is the expanded image token sequence (image_start … image tokens … image_end).
/// </remarks>
public sealed class PromptBuilder
{
    private readonly Lfm2Tokenizer _tokenizer;

    public PromptBuilder(Lfm2Tokenizer tokenizer) => _tokenizer = tokenizer;

    /// <summary>
    /// Builds the full input token ids for a single-image conversation.
    /// </summary>
    /// <param name="systemPrompt">System prompt (skipped when null/empty).</param>
    /// <param name="instruction">User instruction text accompanying the image.</param>
    /// <param name="imageTokenIds">
    /// The expanded image token-id sequence from the image processor (delimiters + N image tokens).
    /// When null/empty a text-only prompt is produced.
    /// </param>
    public IReadOnlyList<int> Build(string? systemPrompt, string instruction, IReadOnlyList<int>? imageTokenIds)
    {
        var ids = new List<int>(256);

        var header = "<|startoftext|>";
        if (!string.IsNullOrEmpty(systemPrompt))
            header += $"<|im_start|>system\n{systemPrompt}<|im_end|>\n";
        header += "<|im_start|>user\n";
        ids.AddRange(_tokenizer.Encode(header));

        if (imageTokenIds is { Count: > 0 })
            ids.AddRange(imageTokenIds);

        ids.AddRange(_tokenizer.Encode(instruction));
        ids.AddRange(_tokenizer.Encode("<|im_end|>\n<|im_start|>assistant\n"));

        return ids;
    }
}
