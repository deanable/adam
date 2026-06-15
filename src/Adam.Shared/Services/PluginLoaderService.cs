using System.Reflection;
using Adam.Shared.Configuration;
using Adam.Shared.Extractors;
using Adam.Shared.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Adam.Shared.Services;

/// <summary>
/// Discovers and loads metadata extractor plugins from a plugin directory.
/// Built-in extractors (ImageExtractor, OfficeExtractor) are always registered.
/// Third-party plugins are loaded from the configured plugin directory on disk.
/// Plugins are sorted by priority; null results from an extractor allow fallthrough
/// to the next-lower-priority extractor.
/// </summary>
public sealed class PluginLoaderService
{
    private readonly PluginConfig _config;
    private readonly ILogger<PluginLoaderService> _logger;
    private readonly List<IMetadataExtractor> _extractors = [];
    private readonly List<PluginInfo> _loadedPlugins = [];
    private volatile bool _loaded;

    /// <summary>
    /// Discovered extractors (built-in + plugins), sorted by Priority ascending.
    /// </summary>
    public IReadOnlyList<IMetadataExtractor> Extractors
    {
        get
        {
            EnsureLoaded();
            return _extractors.AsReadOnly();
        }
    }

    /// <summary>
    /// Metadata about loaded plugin assemblies for display in the Plugin Manager UI.
    /// </summary>
    public IReadOnlyList<PluginInfo> LoadedPlugins
    {
        get
        {
            EnsureLoaded();
            return _loadedPlugins.AsReadOnly();
        }
    }

    public PluginLoaderService(
        IOptions<PluginConfig> config,
        ILogger<PluginLoaderService> logger)
    {
        _config = config.Value;
        _logger = logger;

        // Register built-in extractors immediately; plugins are loaded lazily
        RegisterBuiltInExtractors();
    }

    /// <summary>
    /// Explicitly load plugins from the plugin directory.
    /// Idempotent — safe to call multiple times.
    /// </summary>
    public async Task LoadPluginsAsync(CancellationToken ct = default)
    {
        if (_loaded)
            return;

        await Task.Run(() => LoadPluginAssemblies(ct), ct);
        _loaded = true;
    }

    /// <summary>
    /// Returns the highest-priority extractor that <see cref="IMetadataExtractor.CanExtract"/>
    /// returns true for the given file.
    /// Returns null if no extractor can handle the file.
    /// </summary>
    public IMetadataExtractor? GetExtractor(string filePath, string mimeType)
    {
        EnsureLoaded();

        foreach (var extractor in _extractors)
        {
            if (extractor.CanExtract(filePath, mimeType))
                return extractor;
        }

        return null;
    }

    /// <summary>
    /// Runs extractors in priority order for each extraction type (text and profile),
    /// stopping at the first non-null result for each.
    /// If no extractor produces a result, both fields will be null.
    /// </summary>
    public async Task<(ExtractedTextMetadata? Text, Models.MetadataProfile? Profile)>
        ExtractAllAsync(string filePath, string mimeType, CancellationToken ct)
    {
        EnsureLoaded();

        ExtractedTextMetadata? text = null;
        Models.MetadataProfile? profile = null;

        foreach (var extractor in _extractors)
        {
            ct.ThrowIfCancellationRequested();

            if (!extractor.CanExtract(filePath, mimeType))
                continue;

            if (text == null)
            {
                try
                {
                    text = await extractor.ExtractTextAsync(filePath, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Text extraction failed for {Extractor} on {Path}",
                        extractor.Name, filePath);
                }
            }

            if (profile == null)
            {
                try
                {
                    profile = await extractor.ExtractAsync(filePath, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Profile extraction failed for {Extractor} on {Path}",
                        extractor.Name, filePath);
                }
            }

            // Stop early if both extractions succeeded
            if (text != null && profile != null)
                break;
        }

        return (text, profile);
    }

    /// <summary>
    /// Forces a full reload: clears all extractors (including built-in), re-registers
    /// built-in extractors, and re-scans the plugin directory.
    /// </summary>
    public async Task ReloadAsync(CancellationToken ct = default)
    {
        _loaded = false;
        _extractors.Clear();
        _loadedPlugins.Clear();
        RegisterBuiltInExtractors();
        await LoadPluginsAsync(ct);
    }

    private void EnsureLoaded()
    {
        if (!_loaded)
        {
            _logger.LogDebug("PluginLoaderService used before LoadPluginsAsync called — loading synchronously");
            LoadPluginAssemblies(CancellationToken.None);
            _loaded = true;
        }
    }

    private void RegisterBuiltInExtractors()
    {
        var metadataExtractor = new MetadataExtractorService();
        var officeExtractor = new OfficeDocumentExtractor();

        _extractors.Add(new ImageExtractor(metadataExtractor));
        _extractors.Add(new OfficeExtractor(officeExtractor));

        _loadedPlugins.Add(new PluginInfo(
            Name: "Image EXIF/XMP Extractor",
            AssemblyName: "Adam.Shared",
            Priority: 100,
            IsBuiltIn: true,
            Status: "Loaded"));

        _loadedPlugins.Add(new PluginInfo(
            Name: "Office Document Extractor",
            AssemblyName: "Adam.Shared",
            Priority: 200,
            IsBuiltIn: true,
            Status: "Loaded"));

        _logger.LogDebug("Registered 2 built-in metadata extractors");
    }

    private void LoadPluginAssemblies(CancellationToken ct)
    {
        var dir = _config.PluginDirectory;

        try
        {
            if (!Directory.Exists(dir))
            {
                _logger.LogInformation("Plugin directory does not exist, creating: {Dir}", dir);
                Directory.CreateDirectory(dir);
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create plugin directory: {Dir}", dir);
            return;
        }

        string[] dllFiles;
        try
        {
            dllFiles = Directory.GetFiles(dir, "*.dll");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate plugin directory: {Dir}", dir);
            return;
        }

        if (dllFiles.Length == 0)
        {
            _logger.LogDebug("No plugin DLLs found in: {Dir}", dir);
            return;
        }

        foreach (var dllPath in dllFiles)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var assembly = Assembly.LoadFrom(dllPath);
                var extractorTypes = assembly.GetTypes()
                    .Where(t => !t.IsAbstract && !t.IsInterface &&
                                typeof(IMetadataExtractor).IsAssignableFrom(t))
                    .ToList();

                foreach (var type in extractorTypes)
                {
                    try
                    {
                        if (Activator.CreateInstance(type) is IMetadataExtractor instance)
                        {
                            _extractors.Add(instance);
                            _loadedPlugins.Add(new PluginInfo(
                                Name: instance.Name,
                                AssemblyName: assembly.GetName().Name ?? "Unknown",
                                Priority: instance.Priority,
                                IsBuiltIn: false,
                                Status: "Loaded"));

                            _logger.LogInformation(
                                "Loaded plugin extractor: {Name} (priority={Priority}, assembly={Assembly})",
                                instance.Name, instance.Priority, assembly.GetName().Name);
                        }
                    }
                    catch (Exception ex)
                    {
                        var assemblyName = assembly.GetName().Name ?? "Unknown";
                        _loadedPlugins.Add(new PluginInfo(
                            Name: type.Name,
                            AssemblyName: assemblyName,
                            Priority: 0,
                            IsBuiltIn: false,
                            Status: $"Load Error: {ex.GetType().Name} — {ex.Message}"));

                        _logger.LogWarning(ex,
                            "Failed to instantiate plugin extractor {Type} from {Assembly}",
                            type.FullName, assemblyName);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load plugin assembly: {Path}", dllPath);
            }
        }

        // Re-sort extractors by priority after adding plugins
        _extractors.Sort((a, b) => a.Priority.CompareTo(b.Priority));

        _logger.LogInformation(
            "Plugin loading complete: {Count} total extractors ({BuiltIn} built-in, {Plugin} plugin(s))",
            _extractors.Count,
            _loadedPlugins.Count(p => p.IsBuiltIn),
            _loadedPlugins.Count(p => !p.IsBuiltIn));
    }
}
