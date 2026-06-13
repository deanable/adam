namespace Adam.Shared.Contracts;

/// <summary>
/// Standardized status codes returned in <see cref="Envelope.StatusCode"/>.
/// Codes 0-99 are reserved for protocol-level errors; 100+ are application-specific.
/// </summary>
public static class ErrorCode
{
    /// <summary>Success — operation completed without error.</summary>
    public const int Success = 0;

    /// <summary>Unknown or unhandled message type.</summary>
    public const int UnknownMessageType = 3;

    /// <summary>A specific entity ID was not found (asset, user, keyword, etc.).</summary>
    public const int NotFound = 5;

    /// <summary>Entity already exists (conflict).</summary>
    public const int Conflict = 6;

    /// <summary>Insufficient permissions or authentication failure.</summary>
    public const int Forbidden = 7;

    /// <summary>Bad request — null, empty, or malformed payload.</summary>
    public const int BadRequest = 8;

    /// <summary>Internal server error.</summary>
    public const int InternalError = 13;

    /// <summary>Invalid argument or request parameter.</summary>
    public const int InvalidArgument = 14;

    /// <summary>Rate limited or authentication denied.</summary>
    public const int AuthDenied = 16;
}
