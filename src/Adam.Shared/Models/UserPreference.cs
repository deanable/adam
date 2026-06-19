namespace Adam.Shared.Models;

/// <summary>
/// Stores user preferences and UI state as JSON in the catalog database.
/// Scoped by UserId (null = standalone/local user).
/// Portable — follows user across devices in multi-user mode.
/// Machine-local settings (paths, endpoints, window geometry) remain in AdamConfig.
/// </summary>
public sealed class UserPreference
{
    public Guid Id { get; set; }

    /// <summary>
    /// Null for standalone/local user; set to the authenticated user's ID in multi-user mode.
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// Preference key (e.g. "ui", "appearance", "gallery").
    /// </summary>
    public string Key { get; set; } = "ui";

    /// <summary>
    /// JSON blob containing typed preference values.
    /// Must be round-trip tolerant — preserve unknown keys across versions.
    /// </summary>
    public string ValueJson { get; set; } = "{}";

    /// <summary>Last modification timestamp.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Optimistic concurrency token.</summary>
    public uint Version { get; set; } = 1;
}
