namespace Adam.Shared.Services;

/// <summary>
/// Defines the elevated operation request sent to the helper process via a temp JSON file.
/// </summary>
public sealed record ElevatedRequest
{
    public string Operation { get; init; } = string.Empty; // "install", "uninstall", "start", "stop"
    public string? BrokerPath { get; init; }
    public int Port { get; init; }
}

/// <summary>
/// Defines the result written back by the elevated helper process.
/// </summary>
public sealed record ElevatedResponse
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
}
