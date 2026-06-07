using System;

namespace LiquidVision.Core.Exceptions;

public class LiquidVisionException : Exception
{
    public LiquidVisionException(string message) : base(message) { }
    public LiquidVisionException(string message, Exception innerException) : base(message, innerException) { }
}
