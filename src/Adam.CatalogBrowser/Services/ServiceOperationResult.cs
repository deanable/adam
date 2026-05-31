namespace Adam.CatalogBrowser.Services;

/// <summary>
/// Represents the result of a remote service operation (start/stop)
/// sent via TCP to the BrokerService.
/// </summary>
public sealed class ServiceOperationResult
{
    public bool Success { get; }
    public string Message { get; }
    public int StatusCode { get; }

    public ServiceOperationResult(bool success, string message, int statusCode = 0)
    {
        Success = success;
        Message = message;
        StatusCode = statusCode;
    }
}
