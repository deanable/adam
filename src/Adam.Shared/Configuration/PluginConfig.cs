namespace Adam.Shared.Configuration;

/// <summary>
/// Configuration for the plugin system, bound from appsettings.json or DI options.
/// </summary>
public sealed class PluginConfig
{
    /// <summary>
    /// Directory path where plugin assemblies (*.dll) are discovered.
    /// Default: %LOCALAPPDATA%/Adam/plugins/ (Windows) or ~/.local/share/Adam/plugins/ (Linux/macOS).
    /// </summary>
    public string PluginDirectory { get; set; } = GetDefaultPluginDirectory();

    private static string GetDefaultPluginDirectory()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(appData))
            return Path.Combine(appData, "Adam", "plugins");

        // Linux/macOS fallback
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".local", "share", "Adam", "plugins");
    }
}
