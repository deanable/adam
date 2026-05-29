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
