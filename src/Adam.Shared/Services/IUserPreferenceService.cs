namespace Adam.Shared.Services;

/// <summary>
/// Service for reading and writing user preferences.
/// Standalone mode reads/writes directly from AppDbContext.
/// Multi-user mode routes through BrokerClient protobuf messages.
/// </summary>
public interface IUserPreferenceService
{
    /// <summary>
    /// Gets a typed preference value for the specified key.
    /// </summary>
    Task<T?> GetAsync<T>(string key, T? defaultValue = default, CancellationToken ct = default) where T : class;

    /// <summary>
    /// Gets a typed preference value, or a default if not found.
    /// </summary>
    Task<T> GetOrDefaultAsync<T>(string key, T defaultValue, CancellationToken ct = default);

    /// <summary>
    /// Sets a typed preference value for the specified key.
    /// </summary>
    Task SetAsync<T>(string key, T value, CancellationToken ct = default);

    /// <summary>
    /// Deletes a preference by key.
    /// </summary>
    Task ResetAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Deletes all preferences for the current user/context.
    /// </summary>
    Task ResetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Loads all preferences from storage and applies them.
    /// Called on app startup after mode/auth is resolved.
    /// </summary>
    Task LoadAsync(CancellationToken ct = default);
}
