using System.Collections.Generic;

namespace LiquidVision.Core;

/// <summary>
/// Structured result of analyzing a single image with the LFM2-VL model.
/// </summary>
/// <param name="Description">A short natural-language description of the image.</param>
/// <param name="Keywords">Descriptive keyword tags extracted for the image.</param>
/// <param name="Categories">Broad category labels for the image.</param>
/// <param name="RawOutput">The raw generated model text, before JSON parsing (useful for diagnostics).</param>
/// <param name="ProcessingTimeMs">Wall-clock inference time in milliseconds.</param>
/// <param name="ModelVersion">Version stamp of the model that produced the result.</param>
public record ImageTagResult(
    string Description,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<string> Categories,
    string RawOutput,
    double ProcessingTimeMs,
    string ModelVersion
);
