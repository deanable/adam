using System;

namespace LiquidVision.Core.Exceptions;

/// <summary>Thrown when inference fails (e.g. unexpected graph I/O, tensor mismatch, or runtime error).</summary>
public class ModelInferenceException : LiquidVisionException
{
    public ModelInferenceException(string message) : base(message) { }
    public ModelInferenceException(string message, Exception innerException) : base(message, innerException) { }
}
