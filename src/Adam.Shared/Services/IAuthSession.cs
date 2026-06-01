namespace Adam.Shared.Services;

/// <summary>
/// Abstraction over broker authentication, enabling testability.
/// </summary>
public interface IAuthSession
{
    /// <summary>
    /// The JWT token after successful login, or null.
    /// </summary>
    string? Token { get; }

    /// <summary>
    /// True when a valid session token is set.
    /// </summary>
    bool IsLoggedIn { get; }

    /// <summary>
    /// Authenticates with the broker service.
    /// </summary>
    /// <param name="username">Username.</param>
    /// <param name="password">Password.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if login succeeded.</returns>
    Task<bool> LoginAsync(string username, string password, CancellationToken ct = default);

    /// <summary>
    /// Clears the current session.
    /// </summary>
    void Logout();
}
