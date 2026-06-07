using System;
using System.Net;

namespace LiquidVision.Core.Exceptions;

public class ModelDownloadException : LiquidVisionException
{
    public HttpStatusCode? StatusCode { get; }

    public ModelDownloadException(string message) : base(message) { }
    public ModelDownloadException(string message, Exception innerException) : base(message, innerException) { }
    public ModelDownloadException(string message, HttpStatusCode statusCode) : base(message)
    {
        StatusCode = statusCode;
    }
}
