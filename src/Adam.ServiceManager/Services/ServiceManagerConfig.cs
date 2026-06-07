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
    /// Whether clients should connect using TLS. Published to the registry so
    /// the Catalog Browser configures its connection to match the service.
    /// </summary>
    public bool UseTls { get; set; }

    /// <summary>
    /// Whether clients should accept the service's self-signed certificate.
    /// Only meaningful when <see cref="UseTls"/> is enabled.
    /// </summary>
    public bool AllowSelfSigned { get; set; } = true;

    /// <summary>
    /// Database provider for the Broker service this manager is controlling.
    /// Used by ModeManager to connect to the correct database for user management.
    /// </summary>
    public string DbProvider { get; set; } = "sqlite";

    /// <summary>
    /// Database connection string for the Broker service.
    /// </summary>
    public string DbConnection { get; set; } = "Data Source=catalog.db";

    /// <summary>
    /// When true, closing the window minimizes to the system tray instead of exiting.
    /// Managed by the toggle in Service Configuration. Defaults to true since
    /// the app has a tray icon and background service polling.
    /// </summary>
    public bool MinimizeToTrayOnClose { get; set; } = true;

    /// <summary>
    /// Polling interval in seconds for automatic service status refresh.
    /// Backed by a background <see cref="System.Threading.Timer"/> so it does not
    /// block or flicker the UI thread. Range: 1–300 seconds.
    /// </summary>
    public int PollingIntervalSeconds { get; set; } = 5;

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
