using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Adam.Shared.Services;

/// <summary>
/// Updates configuration files (e.g. appsettings.json) associated with a Windows service.
/// </summary>
public sealed class ServiceConfigWriter
{
    private readonly ILogger _logger;

    public ServiceConfigWriter(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Updates the <c>appsettings.json</c> file alongside the broker executable with the
    /// configured port, so the broker reads the correct port from configuration rather than
    /// relying on command-line arguments (which confuse <c>sc.exe</c>'s parser).
    /// </summary>
    public async Task UpdateBrokerPortAsync(string brokerPath, int port)
    {
        var brokerDir = Path.GetDirectoryName(brokerPath);
        if (string.IsNullOrEmpty(brokerDir)) return;

        var configPath = Path.Combine(brokerDir, "appsettings.json");
        if (!File.Exists(configPath))
        {
            _logger.LogWarning("appsettings.json not found at {ConfigPath} — skipping port configuration.", configPath);
            return;
        }

        try
        {
            _logger.LogInformation("[TIMING] Updating broker port to {Port} in {ConfigPath}...", port, configPath);
            var json = await File.ReadAllTextAsync(configPath);
            var node = JsonNode.Parse(json);
            if (node is JsonObject root &&
                root["Broker"] is JsonObject broker)
            {
                broker["Port"] = port;
                var opts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                await File.WriteAllTextAsync(configPath, root.ToJsonString(opts));
                _logger.LogInformation("[TIMING] Broker port updated to {Port} in appsettings.json.", port);
            }
            else
            {
                _logger.LogWarning("Could not find 'Broker:Port' in appsettings.json — config structure may differ.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update appsettings.json — continuing anyway (broker will use default port).");
        }
    }
}
