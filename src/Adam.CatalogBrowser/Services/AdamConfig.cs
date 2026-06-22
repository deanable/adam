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
    /// Whether to connect to the broker over TLS. Sourced from the registry
    /// settings published by the Service Manager.
    /// </summary>
    public bool UseTls { get; set; }

    /// <summary>
    /// Whether to accept the broker's self-signed certificate (only relevant when
    /// <see cref="UseTls"/> is enabled).
    /// </summary>
    public bool AllowSelfSigned { get; set; } = true;

    /// <summary>
    /// Last-entered username, pre-filled in the login dialog.
    /// Password is never persisted for security reasons.
    /// </summary>
    public string LastUsername { get; set; } = string.Empty;

    // ── AI Model selection ──

    /// <summary>
    /// Hugging Face repository ID of the selected AI tagging model.
    /// Defaults to the LFM2-VL 1.6B ONNX model.
    /// </summary>
    public string AiModelId { get; set; } = "LiquidAI/LFM2.5-VL-1.6B-ONNX";

    /// <summary>
    /// Weight precision of the selected AI tagging model (e.g. "Q4F16", "Fp16").
    /// </summary>
    public string AiPrecision { get; set; } = "Q4F16";

    /// <summary>
    /// ONNX Runtime execution provider (e.g. "Cpu", "Cuda", "DirectML").
    /// </summary>
    public string AiExecutionProvider { get; set; } = "Cpu";

    /// <summary>
    /// GPU device ID when using a GPU execution provider (default 0).
    /// </summary>
    public int AiGpuDeviceId { get; set; } = 0;

    // ── Recent hosts ──

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
