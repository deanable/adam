using System;

namespace LiquidVision.Core.Exceptions;

public class ModelLoadException : LiquidVisionException
{
    public ModelLoadException(string message) : base(message) { }
    public ModelLoadException(string message, Exception innerException) : base(message, innerException) { }
}
