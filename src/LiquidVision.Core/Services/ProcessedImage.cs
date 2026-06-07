using System.Collections.Generic;
using Microsoft.ML.OnnxRuntime;

namespace LiquidVision.Core.Services;

/// <summary>
/// The result of preprocessing one image: the named tensors the vision encoder consumes, and the
/// prompt-side token-id sequence (image-block delimiters interleaved with N image placeholder tokens)
/// that must be spliced into the conversation. The count of image placeholder tokens equals the number
/// of feature vectors the vision encoder will emit.
/// </summary>
public sealed class ProcessedImage
{
    /// <summary>Named input tensors for the vision encoder session (names match its graph inputs).</summary>
    public required IReadOnlyList<NamedOnnxValue> VisionInputs { get; init; }

    /// <summary>Prompt-side token-id sequence representing the image (delimiters + placeholders).</summary>
    public required IReadOnlyList<int> ImageTokenIds { get; init; }

    /// <summary>Number of image placeholder tokens (== expected vision feature vector count).</summary>
    public required int ImageFeatureCount { get; init; }
}
