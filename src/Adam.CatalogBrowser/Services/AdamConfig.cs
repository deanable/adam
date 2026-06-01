using System.Text.Json;

namespace Adam.CatalogBrowser.Services;

/// <summary>
/// Persists user-configurable settings for the Adam CatalogBrowser application.
/// Stored as JSON in the app data directory.
/// </summary>
public sealed class AdamConfig
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Adam", "CatalogBrowser");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "settings.json");

    public string Mode { get; set; } = "Standalone";
    public string ServiceHost { get; set; } = "localhost";
    public int ServicePort { get; set; } = 9100;

    /// <summary>
    /// Last-entered username, pre-filled in the login dialog.
    /// Password is never persisted for security reasons.
    /// </summary>
    public string LastUsername { get; set; } = string.Empty;

    /// <summary>
    /// Recently used server addresses in "host:port" format.
    /// Most recent at index 0. Max 5 entries. Deduplicated.
    /// </summary>
    public List<string> RecentHosts { get; set; } = [];

    /// <summary>
    /// Adds or moves a "host:port" entry to the top of the recent list,
    /// capping at 5 entries.
    /// </summary>
    public void PushRecentHost(string host, int port)
    {
        var entry = $"{host}:{port}";
        RecentHosts.RemoveAll(e => e.Equals(entry, StringComparison.OrdinalIgnoreCase));
        RecentHosts.Insert(0, entry);
        if (RecentHosts.Count > 5)
            RecentHosts.RemoveRange(5, RecentHosts.Count - 5);
    }

    /// <summary>
    /// Loads config from disk, or returns default values if no file exists.
    /// </summary>
    public static AdamConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
                return new AdamConfig();

            var json = File.ReadAllText(ConfigPath);
            var config = JsonSerializer.Deserialize<AdamConfig>(json);
            return config ?? new AdamConfig();
        }
        catch
        {
            return new AdamConfig();
        }
    }

    /// <summary>
    /// Saves the current config to disk.
    /// </summary>
    public void Save()
    {
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigPath, json);
    }
}
