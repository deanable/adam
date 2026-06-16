using System.Collections.ObjectModel;
using Avalonia.Media;

namespace Adam.CatalogBrowser.Services;

/// <summary>
/// Represents a single design theme parsed from a <c>.design/*.md</c> file.
/// </summary>
public sealed record DesignTheme
{
    /// <summary>Filename without extension (used as identifier).</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Display name from YAML frontmatter or filename.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Optional description from YAML frontmatter.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Version string from YAML frontmatter.</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>Parsed color tokens keyed by design-file name (e.g. "ink", "canvas", "accent").</summary>
    public IReadOnlyDictionary<string, string> RawColors { get; init; } = new Dictionary<string, string>();

    /// <summary>Parsed rounded tokens.</summary>
    public IReadOnlyDictionary<string, string> RawRounded { get; init; } = new Dictionary<string, string>();

    /// <summary>Parsed spacing tokens.</summary>
    public IReadOnlyDictionary<string, string> RawSpacing { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// Resolved color values as Avalonia Color objects.
    /// Only includes tokens that parsed successfully.
    /// </summary>
    public IReadOnlyDictionary<string, Color> ResolvedColors
    {
        get
        {
            if (_resolved != null)
                return _resolved;
            _resolved = RawColors
                .Select(kv => (Key: kv.Key, Color: YamlFrontmatterParser.ParseColor(kv.Value)))
                .Where(x => x.Color.HasValue)
                .ToDictionary(x => x.Key, x => x.Color!.Value);
            return _resolved;
        }
    }
    private Dictionary<string, Color>? _resolved;
}

/// <summary>
/// Scans <c>.design/*.md</c> files, parses their YAML frontmatter, and generates
/// Avalonia resource dictionaries for dynamic theme switching.
/// </summary>
public sealed class DesignThemeService
{
    private readonly string _designDir;
    private readonly AdamConfig _config;

    /// <summary>Available themes discovered from the design directory.</summary>
    public ObservableCollection<DesignTheme> AvailableThemes { get; } = [];

    /// <summary>Currently active theme, or <c>null</c> if using the default (hardcoded) theme.</summary>
    public DesignTheme? CurrentTheme { get; private set; }

    /// <summary>
    /// Event raised when the active theme changes. Subscribers (e.g. App.axaml.cs) should
    /// call <see cref="ApplyTheme"/> to regenerate resource dictionaries.
    /// </summary>
    public event Action<DesignTheme?>? ThemeChanged;

    public DesignThemeService(AdamConfig config)
    {
        _config = config;

        // Design directory: alongside the executable, or fallback to app data
        var baseDir = AppContext.BaseDirectory;
        _designDir = Path.Combine(baseDir, ".design");
        if (!Directory.Exists(_designDir))
        {
            // Fallback: look relative to the solution root for development
            var solutionDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));
            _designDir = Path.Combine(solutionDir, ".design");
        }
    }

    /// <summary>
    /// Scans the <c>.design/</c> directory and loads all valid design files.
    /// </summary>
    public void LoadThemes()
    {
        AvailableThemes.Clear();

        if (!Directory.Exists(_designDir))
        {
            // Create the directory if it doesn't exist
            Directory.CreateDirectory(_designDir);
            return;
        }

        foreach (var filePath in Directory.GetFiles(_designDir, "*.md"))
        {
            try
            {
                var fm = YamlFrontmatterParser.Parse(filePath);
                if (fm == null)
                    continue;

                var theme = new DesignTheme
                {
                    Id = fm.FileName,
                    Name = fm.Name,
                    Description = fm.Description,
                    Version = fm.Version,
                    RawColors = fm.Colors,
                    RawRounded = fm.Rounded,
                    RawSpacing = fm.Spacing
                };

                AvailableThemes.Add(theme);
            }
            catch
            {
                // Skip files that fail to parse
            }
        }

        // Auto-select the saved theme or the first available one
        var savedThemeId = _config.DesignThemeFile;
        if (!string.IsNullOrEmpty(savedThemeId))
        {
            var saved = AvailableThemes.FirstOrDefault(t =>
                t.Id.Equals(savedThemeId, StringComparison.OrdinalIgnoreCase));
            if (saved != null)
            {
                CurrentTheme = saved;
                return;
            }
        }

        // Default to the first theme or null (use hardcoded defaults)
        CurrentTheme = AvailableThemes.FirstOrDefault();
    }

    /// <summary>
    /// Applies the given theme by generating a <see cref="ResourceDictionary"/>
    /// and merging it into <c>Application.Current.Resources.MergedDictionaries</c>.
    /// </summary>
    public void ApplyTheme(DesignTheme? theme)
    {
        CurrentTheme = theme;

        var app = Avalonia.Application.Current;
        if (app == null)
            return;

        // Remove any previously injected theme dictionary
        var existing = app.Resources.MergedDictionaries
            .OfType<DesignThemeResourceDictionary>()
            .FirstOrDefault();
        if (existing != null)
            app.Resources.MergedDictionaries.Remove(existing);

        if (theme == null)
        {
            // No theme selected — use hardcoded defaults (ColorResources.axaml still loaded)
            ThemeChanged?.Invoke(null);
            return;
        }

        var dict = GenerateResourceDictionary(theme);
        app.Resources.MergedDictionaries.Add(dict);
        ThemeChanged?.Invoke(theme);

        // Persist selection
        _config.DesignThemeFile = theme.Id;
        _config.Save();
    }

    /// <summary>
    /// Maps design file color tokens to XAML resource keys and generates a ResourceDictionary.
    /// </summary>
    private static DesignThemeResourceDictionary GenerateResourceDictionary(DesignTheme theme)
    {
        var dict = new DesignThemeResourceDictionary();
        var colors = theme.ResolvedColors;

        // Apply each mapped resource
        foreach (var (xamlKey, designKey) in ResourceMap)
        {
            if (TryGetDesignColor(colors, designKey, out var color))
            {
                dict[xamlKey] = new SolidColorBrush(color.Value);
            }
        }

        // Apply derived resources that combine multiple design tokens
        ApplyDerivedResources(dict, colors);

        return dict;
    }

    /// <summary>
    /// Tries to get a color from the design file's color tokens.
    /// Supports fallback paths: "primary" → "accent" → null
    /// </summary>
    private static bool TryGetDesignColor(
        IReadOnlyDictionary<string, Color> colors,
        string[] designKeys,
        out Color? color)
    {
        foreach (var key in designKeys)
        {
            if (colors.TryGetValue(key, out var c))
            {
                color = c;
                return true;
            }
        }
        color = null;
        return false;
    }

    /// <summary>
    /// Generates derived resources like hover/pressed states and semi-transparent overlays.
    /// </summary>
    private static void ApplyDerivedResources(
        DesignThemeResourceDictionary dict,
        IReadOnlyDictionary<string, Color> colors)
    {
        // TitleBarBrush: use primary or accent, darkened
        if (TryGetDesignColor(colors, ["primary", "accent", "ink"], out var primary))
        {
            var primaryVal = primary!.Value;
            dict["TitleBarBrush"] = new SolidColorBrush(primaryVal);

            // Hover/pressed states (darken by 10%/20%)
            dict["PrimaryHoverBrush"] = new SolidColorBrush(Darken(primaryVal, 0.1f));
            dict["PrimaryPressedBrush"] = new SolidColorBrush(Darken(primaryVal, 0.2f));
        }

        // TitleBar separator and text — use on-primary or white
        if (TryGetDesignColor(colors, ["on-primary", "canvas"], out var onPrimary))
        {
            var op = onPrimary!.Value;
            dict["TitleBarSeparatorBrush"] = new SolidColorBrush(Color.FromArgb(85, op.R, op.G, op.B)); // ~33% opacity
            dict["TitleBarTextBrush"] = new SolidColorBrush(Color.FromArgb(153, op.R, op.G, op.B)); // ~60% opacity
            dict["TitleBarTextSubtleBrush"] = new SolidColorBrush(Color.FromArgb(170, op.R, op.G, op.B)); // ~67% opacity
            dict["TitleBarStatusBrush"] = new SolidColorBrush(op);
        }

        // Derive accent hover/pressed from primary
        if (TryGetDesignColor(colors, ["accent", "primary", "ink"], out var accentVal))
        {
            var accent = accentVal!.Value;
            dict["PrimaryHoverBrush"] ??= new SolidColorBrush(Darken(accent, 0.1f));
            dict["PrimaryPressedBrush"] ??= new SolidColorBrush(Darken(accent, 0.2f));
        }

        // SurfaceLight = Surface lightened, SurfaceSubtle = Surface darkened slightly
        if (TryGetDesignColor(colors, ["canvas", "surface-soft"], out var surface))
        {
            var s = surface!.Value;
            // Lighten for SurfaceLight if the surface is already light, or darken
            var lightness = (s.R * 0.299 + s.G * 0.587 + s.B * 0.114) / 255.0;
            if (lightness > 0.5)
            {
                dict["SurfaceLightBrush"] ??= new SolidColorBrush(Lighten(s, 0.03f));
                dict["SurfaceSubtleBrush"] ??= new SolidColorBrush(Darken(s, 0.03f));
                dict["SurfaceDisabledBrush"] ??= new SolidColorBrush(Darken(s, 0.06f));
            }
            else
            {
                dict["SurfaceLightBrush"] ??= new SolidColorBrush(Lighten(s, 0.1f));
                dict["SurfaceSubtleBrush"] ??= new SolidColorBrush(Lighten(s, 0.05f));
                dict["SurfaceDisabledBrush"] ??= new SolidColorBrush(Lighten(s, 0.03f));
            }
        }

        // Border from surface (slightly darker/lighter)
        if (TryGetDesignColor(colors, ["hairline", "canvas", "surface-soft"], out var border))
        {
            var b = border!.Value;
            dict["BorderBrush"] ??= new SolidColorBrush(b);
            dict["BorderLightBrush"] ??= new SolidColorBrush(Lighten(b, 0.05f));
            dict["InputBorderBrush"] ??= new SolidColorBrush(b);
        }

        // Selection from primary (used as tint)
        if (TryGetDesignColor(colors, ["primary", "accent"], out var sel))
        {
            var s = sel!.Value;
            dict["SelectionHoverBrush"] ??= new SolidColorBrush(Color.FromArgb(25, s.R, s.G, s.B)); // ~10%
            dict["SelectionBrush"] ??= new SolidColorBrush(Color.FromArgb(40, s.R, s.G, s.B)); // ~16%
            dict["SelectionActiveBrush"] ??= new SolidColorBrush(Color.FromArgb(55, s.R, s.G, s.B)); // ~22%
        }

        // Admin from primary darkened
        if (TryGetDesignColor(colors, ["primary", "ink"], out var admin))
        {
            var a = admin!.Value;
            dict["AdminBrush"] ??= new SolidColorBrush(Darken(a, 0.5f));
            dict["AdminHoverBrush"] ??= new SolidColorBrush(Darken(a, 0.4f));
            dict["AdminTextBrush"] ??= new SolidColorBrush(Color.FromArgb(255, 204, 255, 204)); // CCFFCC — light green
        }

        // AiTag from accent
        if (TryGetDesignColor(colors, ["accent", "primary"], out var aiTag))
        {
            var a = aiTag!.Value;
            dict["AiTagBrush"] ??= new SolidColorBrush(a);
            dict["AiTagHoverBrush"] ??= new SolidColorBrush(Darken(a, 0.1f));
            dict["AiTagPressedBrush"] ??= new SolidColorBrush(Darken(a, 0.2f));
        }

        // Overlay
        if (TryGetDesignColor(colors, ["canvas", "on-primary", "surface-soft"], out var overlay))
        {
            var o = overlay!.Value;
            dict["OverlayBrush"] ??= new SolidColorBrush(Color.FromArgb(204, o.R, o.G, o.B));
        }

        // ServiceMode brush
        if (TryGetDesignColor(colors, ["accent", "primary"], out var service))
        {
            var sv = service!.Value;
            dict["ServiceModeBrush"] ??= new SolidColorBrush(Color.FromArgb(40, sv.R, sv.G, sv.B));
        }

        // Disabled button
        if (TryGetDesignColor(colors, ["mute", "stone", "ash"], out var disabled))
        {
            dict["DisabledButtonBrush"] ??= new SolidColorBrush(disabled!.Value);
        }
    }

    /// <summary>
    /// Maps XAML resource keys to design file color token names (with fallbacks).
    /// Each entry is [xamlKey] = [preferred, fallback1, fallback2, ...]
    /// Resources with empty arrays are skipped during ResourceMap iteration
    /// and are handled entirely in <see cref="ApplyDerivedResources"/>.
    /// </summary>
    private static readonly Dictionary<string, string[]> ResourceMap = new()
    {
        // Brand / Primary — hover/pressed handled by ApplyDerivedResources
        ["PrimaryBrush"] = ["primary", "accent", "ink"],

        // Semantic colors
        ["SuccessBrush"] = ["success", "green"],
        ["SuccessHoverBrush"] = ["success"],
        ["SuccessLightBrush"] = ["success", "green"],
        ["SuccessBackgroundBrush"] = ["success", "green"],
        ["DangerBrush"] = ["danger", "red"],
        ["DangerHoverBrush"] = ["danger", "red"],
        ["DangerPressedBrush"] = ["danger", "red"],
        ["WarningBrush"] = ["warning", "orange"],
        ["WarningTextBrush"] = ["warning", "orange"],
        ["WarningHoverBrush"] = ["warning", "orange"],
        ["WarningBackgroundBrush"] = ["warning", "orange"],
        ["WarningBorderBrush"] = ["warning", "orange"],
        ["WarningHoverBackgroundBrush"] = ["warning", "orange"],

        // Text
        ["TextPrimaryBrush"] = ["ink", "body", "charcoal", "primary"],
        ["TextSecondaryBrush"] = ["body", "ink", "mute", "charcoal"],
        ["TextMutedBrush"] = ["mute", "stone", "body", "ash"],
        ["TextTertiaryBrush"] = ["stone", "mute", "ash", "body"],
        ["TextPlaceholderBrush"] = ["ash", "stone", "mute"],
        ["TextDisabledBrush"] = ["ash", "stone"],
        ["TextEmptyBrush"] = ["ash", "stone"],
        ["TextEmptySecondaryBrush"] = ["ash"],
        ["TextEmptyTertiaryBrush"] = ["ash"],

        // Surface — Light/Subtle/Disabled handled by ApplyDerivedResources
        ["SurfaceBrush"] = ["canvas", "surface-soft", "on-primary"],

        // All other resources (Border, Selection, TitleBar, AiTag, Admin, Overlay, etc.)
        // are derived dynamically in ApplyDerivedResources().
    };

    /// <summary>Darkens a color by the given factor (0-1).</summary>
    private static Color Darken(Color c, float factor)
    {
        return Color.FromArgb(
            c.A,
            (byte)Math.Max(0, c.R * (1 - factor)),
            (byte)Math.Max(0, c.G * (1 - factor)),
            (byte)Math.Max(0, c.B * (1 - factor)));
    }

    /// <summary>Lightens a color by the given factor (0-1).</summary>
    private static Color Lighten(Color c, float factor)
    {
        return Color.FromArgb(
            c.A,
            (byte)Math.Min(255, c.R + (255 - c.R) * factor),
            (byte)Math.Min(255, c.G + (255 - c.G) * factor),
            (byte)Math.Min(255, c.B + (255 - c.B) * factor));
    }
}

/// <summary>
/// Marker type so <see cref="DesignThemeService"/> can find and remove
/// previously injected theme dictionaries from MergedDictionaries.
/// </summary>
internal sealed class DesignThemeResourceDictionary : Avalonia.Controls.ResourceDictionary;
