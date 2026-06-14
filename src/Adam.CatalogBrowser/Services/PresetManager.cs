using System.Text.Json;
using Adam.Shared.Models;

namespace Adam.CatalogBrowser.Services;

/// <summary>
/// Manages named metadata presets as JSON files in the application data folder.
/// Presets capture a subset of metadata fields and can be applied to assets.
/// </summary>
public sealed class PresetManager
{
    /// <summary>
    /// Gets the base directory for preset storage.
    /// Defaults to %LOCALAPPDATA%/Adam/CatalogBrowser/presets.
    /// Override for testing with an isolated temp directory.
    /// </summary>
    public static string BaseDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Adam", "CatalogBrowser", "presets");

    /// <summary>
    /// The resolved presets directory (alias for <see cref="BaseDirectory"/>).
    /// </summary>
    private static string PresetsDir => BaseDirectory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Returns the list of all saved preset names, sorted alphabetically.
    /// </summary>
    public Task<List<string>> ListPresetsAsync(CancellationToken ct = default)
    {
        EnsureDirectoryExists();
        var files = Directory.GetFiles(PresetsDir, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => n != null)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Select(n => n!)
            .ToList();
        return Task.FromResult(files);
    }

    /// <summary>
    /// Loads a preset by name. Returns null if not found.
    /// </summary>
    public async Task<MetadataPreset?> LoadPresetAsync(string name, CancellationToken ct = default)
    {
        var path = GetPresetPath(name);
        if (!File.Exists(path))
            return null;

        var json = await File.ReadAllTextAsync(path, ct);
        return JsonSerializer.Deserialize<MetadataPreset>(json, JsonOptions);
    }

    /// <summary>
    /// Saves a preset (creates or overwrites). Updates SavedAt to now.
    /// </summary>
    public async Task SavePresetAsync(MetadataPreset preset, CancellationToken ct = default)
    {
        EnsureDirectoryExists();
        preset.SavedAt = DateTime.UtcNow;
        var path = GetPresetPath(preset.Name);
        var json = JsonSerializer.Serialize(preset, JsonOptions);
        await File.WriteAllTextAsync(path, json, ct);
    }

    /// <summary>
    /// Captures the current metadata values into a new preset and saves it.
    /// </summary>
    public async Task CaptureAndSavePresetAsync(
        string name,
        DigitalAsset asset,
        CancellationToken ct = default)
    {
        var preset = new MetadataPreset
        {
            Name = name,
            Description = string.IsNullOrWhiteSpace(asset.Description) ? null : asset.Description,
            Title = string.IsNullOrWhiteSpace(asset.Title) ? null : asset.Title,
            Keywords = asset.Keywords.Count > 0
                ? string.Join("|", asset.Keywords.Select(k => k.Name))
                : null,
            Categories = asset.Categories.Count > 0
                ? string.Join("|", asset.Categories.Select(c => c.Name))
                : null,
            Rating = asset.Rating > 0 ? asset.Rating : null,
            Label = asset.Label != AssetLabel.None ? asset.Label.ToString() : null,
            Flag = asset.Flag != AssetFlag.Unflagged ? asset.Flag.ToString() : null,
            Copyright = string.IsNullOrWhiteSpace(asset.Copyright) ? null : asset.Copyright,
            GpsLatitude = asset.GpsLatitude,
            GpsLongitude = asset.GpsLongitude,
            CameraMake = asset.MetadataProfile?.CameraMake,
            CameraModel = asset.MetadataProfile?.CameraModel,
            DateTaken = asset.MetadataProfile?.DateTaken
        };

        await SavePresetAsync(preset, ct);
    }

    /// <summary>
    /// Deletes a preset by name. Returns true if it existed and was deleted.
    /// </summary>
    public Task<bool> DeletePresetAsync(string name)
    {
        var path = GetPresetPath(name);
        if (!File.Exists(path))
            return Task.FromResult(false);

        File.Delete(path);
        return Task.FromResult(true);
    }

    /// <summary>
    /// Renames a preset. Returns true if successful.
    /// </summary>
    public async Task<bool> RenamePresetAsync(string oldName, string newName, CancellationToken ct = default)
    {
        var oldPath = GetPresetPath(oldName);
        if (!File.Exists(oldPath))
            return false;

        var newPath = GetPresetPath(newName);
        if (File.Exists(newPath))
            return false; // avoid overwriting

        // Load, update name, re-save with new name, delete old
        var preset = await LoadPresetAsync(oldName, ct);
        if (preset == null)
            return false;

        preset.Name = newName;
        preset.SavedAt = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(preset, JsonOptions);
        await File.WriteAllTextAsync(newPath, json, ct);
        File.Delete(oldPath);
        return true;
    }

    /// <summary>
    /// Returns the full path for a preset file.
    /// </summary>
    private static string GetPresetPath(string name)
    {
        var safe = SanitizeFileName(name);
        return Path.Combine(PresetsDir, $"{safe}.json");
    }

    /// <summary>
    /// Sanitizes a preset name to be file-system safe.
    /// </summary>
    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(ch => invalid.Contains(ch) ? '_' : ch));
    }

    private static void EnsureDirectoryExists()
    {
        if (!Directory.Exists(PresetsDir))
            Directory.CreateDirectory(PresetsDir);
    }
}
