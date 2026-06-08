using System.ComponentModel;
using Adam.Shared.Contracts;

namespace Adam.Shared.Services;

/// <summary>
/// Abstraction over broker authentication, enabling testability.
/// </summary>
public interface IAuthSession : INotifyPropertyChanged
{
    /// <summary>
    /// The JWT token after successful login, or null.
    /// </summary>
    string? Token { get; }

    /// <summary>
    /// The currently authenticated user profile, or null if not logged in.
    /// </summary>
    UserProfile? CurrentUser { get; }

    /// <summary>
    /// True when a valid session token is set.
    /// </summary>
    bool IsLoggedIn { get; }

    /// <summary>
    /// The Unix timestamp (seconds) when the current token expires, or 0.
    /// </summary>
    long TokenExpiresAt { get; }

    /// <summary>
    /// Returns true if the current token has expired.
    /// </summary>
    bool IsTokenExpired();

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

    /// <summary>
    /// Validates the current token with the broker and returns the current user profile.
    /// If the account was deactivated, returns null and the caller should force-logout.
    /// </summary>
    Task<UserProfile?> ValidateTokenAsync(CancellationToken ct = default);
}
