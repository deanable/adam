namespace Adam.Shared.Extractors;

/// <summary>
/// Metadata about a loaded plugin extractor, displayed in the Plugin Manager UI.
/// </summary>
public sealed record PluginInfo(
    string Name,
    string AssemblyName,
    int Priority,
    bool IsBuiltIn,
    string Status); // "Loaded", "Load Error: ..."
