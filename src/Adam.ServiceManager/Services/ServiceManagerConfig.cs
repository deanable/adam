using System.Text.Json;

namespace Adam.ServiceManager.Services;

/// <summary>
/// Persists user-configurable settings for the Adam Service Manager application.
/// Stored as JSON in the app data directory.
/// </summary>
public sealed class ServiceManagerConfig
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Adam", "ServiceManager");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "settings.json");

    public string Mode { get; set; } = "Standalone";
    public string ServiceHost { get; set; } = "localhost";
    public int ServicePort { get; set; } = 9100;

    /// <summary>
    /// Loads config from disk, or returns default values if no file exists.
    /// </summary>
    public static ServiceManagerConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
                return new ServiceManagerConfig();

            var json = File.ReadAllText(ConfigPath);
            var config = JsonSerializer.Deserialize<ServiceManagerConfig>(json);
            return config ?? new ServiceManagerConfig();
        }
        catch
        {
            return new ServiceManagerConfig();
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
