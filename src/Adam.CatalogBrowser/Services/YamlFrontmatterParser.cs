using System.Globalization;
using System.Text.RegularExpressions;

namespace Adam.CatalogBrowser.Services;

/// <summary>
/// Minimal YAML frontmatter parser for <c>.design/*.md</c> files.
/// Extracts the frontmatter block between <c>---</c> markers and parses
/// top-level scalar values and one level of nested key-value pairs.
/// <para/>
/// If no valid frontmatter is found, falls back to scanning the markdown body
/// for inferred values (H1 heading for name, hex colors with context keywords).
/// Default values are used for everything the body doesn't supply.
/// </summary>
internal static class YamlFrontmatterParser
{
    /// <summary>
    /// Parsed representation of a design file's frontmatter.
    /// </summary>
    internal sealed record DesignFrontmatter(
        string FileName,
        string Name,
        string Description,
        string Version,
        IReadOnlyDictionary<string, string> Colors,
        IReadOnlyDictionary<string, string> Rounded,
        IReadOnlyDictionary<string, string> Spacing
    );

    /// <summary>
    /// Parses the frontmatter from the given markdown file.
    /// Falls back to body inference when no <c>---</c> frontmatter is found.
    /// Never returns <c>null</c>.
    /// </summary>
    internal static DesignFrontmatter Parse(string filePath)
    {
        var content = File.ReadAllText(filePath);
        return ParseContent(Path.GetFileNameWithoutExtension(filePath), content);
    }

    /// <summary>
    /// Parses the frontmatter from the given markdown content.
    /// Falls back to body inference when no <c>---</c> frontmatter is found.
    /// Never returns <c>null</c>.
    /// </summary>
    internal static DesignFrontmatter ParseContent(string fileName, string content)
    {
        // First try: parse YAML frontmatter (existing behavior)
        var fm = TryParseFrontmatter(fileName, content);
        if (fm != null)
            return fm;

        // Fallback: scan body for inferred values
        return InferFromBody(fileName, content);
    }

    /// <summary>
    /// Attempts to extract a structured frontmatter block between <c>---</c> markers.
    /// Returns <c>null</c> if no valid frontmatter is found.
    /// </summary>
    private static DesignFrontmatter? TryParseFrontmatter(string fileName, string content)
    {
        // Extract frontmatter between --- markers
        const string sep = "---";
        var firstSep = content.IndexOf(sep, StringComparison.Ordinal);
        if (firstSep < 0)
            return null;

        var secondSep = content.IndexOf(sep, firstSep + 3, StringComparison.Ordinal);
        if (secondSep < 0)
            return null;

        var frontmatter = content[(firstSep + 3)..secondSep].Trim();
        if (string.IsNullOrEmpty(frontmatter))
            return null;

        var lines = frontmatter.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
            return null;

        var name = fileName;
        var description = string.Empty;
        var version = string.Empty;
        var colors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var rounded = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var spacing = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Track which section we're in
        var inColors = false;
        var inRounded = false;
        var inSpacing = false;

        foreach (var line in lines)
        {
            // Detect section headers (e.g. "colors:", "typography:", "rounded:", "spacing:", "components:")
            if (line.EndsWith(':') && !line.StartsWith('-') && !line.Contains(':'))
            {
                // Not a key-value pair, it's a section header
                var section = line[..^1].Trim().ToLowerInvariant();
                inColors = section == "colors";
                inRounded = section == "rounded";
                inSpacing = section == "spacing";
                // Reset other sections if needed
                continue;
            }

            // Parse key-value pairs (top-level or indented)
            var colonIdx = line.IndexOf(':');
            if (colonIdx < 0)
                continue;

            var key = line[..colonIdx].Trim().TrimStart('-').Trim();
            var value = line[(colonIdx + 1)..].Trim().Trim('"');

            // Check if this is a top-level scalar (no indentation)
            var leadingSpaces = line.TakeWhile(char.IsWhiteSpace).Count();

            if (leadingSpaces == 0 && line[0] != ' ')
            {
                // Top-level scalar
                if (string.Equals(key, "name", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(value))
                    name = value;
                else if (string.Equals(key, "description", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(value))
                    description = value;
                else if (string.Equals(key, "version", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(value))
                    version = value;
                continue;
            }

            // Nested key-value (within a section)
            if (inColors)
            {
                colors[key] = value;
            }
            else if (inRounded)
            {
                rounded[key] = value;
            }
            else if (inSpacing)
            {
                spacing[key] = value;
            }
        }

        return new DesignFrontmatter(
            FileName: fileName,
            Name: name,
            Description: description,
            Version: version,
            Colors: colors,
            Rounded: rounded,
            Spacing: spacing
        );
    }

    /// <summary>
    /// Infers a <see cref="DesignFrontmatter"/> from the markdown body when no
    /// YAML frontmatter is present. Extracts what it can (H1 heading, hex colors
    /// near context keywords) and uses defaults for the rest.
    /// </summary>
    internal static DesignFrontmatter InferFromBody(string fileName, string content)
    {
        var name = InferName(fileName, content);
        var colors = InferColors(content);

        return new DesignFrontmatter(
            FileName: fileName,
            Name: name,
            Description: string.Empty,
            Version: string.Empty,
            Colors: colors,
            Rounded: new Dictionary<string, string>(),
            Spacing: new Dictionary<string, string>()
        );
    }

    /// <summary>
    /// Extracts a display name from the first H1 heading, or falls back to the filename.
    /// </summary>
    private static string InferName(string fileName, string content)
    {
        foreach (var line in content.Split('\n', StringSplitOptions.None))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("# ") && trimmed.Length > 2)
                return trimmed[2..].Trim();
        }
        return fileName;
    }

    /// <summary>
    /// Keyword-to-token mapping for body color inference, ordered from most
    /// specific (multi-word phrases) to most generic (single-word hints).
    /// The first match for each line determines the token for any hex/rgba
    /// colors found on that line.
    /// </summary>
    private static readonly (string Keyword, string[] Tokens)[] BodyColorHints =
    [
        // ── Multi-word phrases (most specific, checked first) ──
        ("navigation text", ["ink"]),
        ("body text", ["body"]),
        ("tertiary text", ["mute"]),
        ("placeholder text", ["ash"]),
        ("page background", ["canvas"]),
        ("disabled text", ["ash"]),
        ("dark surface", ["ink"]),
        ("alternate surface", ["surface-soft"]),

        // ── Descriptive single words ──
        ("heading", ["ink"]),
        ("headline", ["ink"]),
        ("ink", ["ink"]),
        ("body", ["body"]),
        ("charcoal", ["charcoal"]),
        ("mute", ["mute"]),
        ("stone", ["stone"]),
        ("placeholder", ["ash"]),
        ("disabled", ["ash"]),
        ("ash", ["ash"]),
        ("background", ["canvas"]),
        ("canvas", ["canvas"]),
        ("surface", ["canvas"]),
        ("border", ["hairline"]),
        ("divider", ["hairline"]),
        ("hairline", ["hairline"]),
        ("danger", ["danger"]),
        ("error", ["danger"]),
        ("destructive", ["danger"]),
        ("warning", ["warning"]),
        ("caution", ["warning"]),
        ("success", ["success"]),
        ("positive", ["success"]),
        ("confirm", ["success"]),
        ("silver", ["stone"]),
        ("graphite", ["body"]),
        ("carbon", ["ink"]),
        ("white", ["canvas"]),
        ("cream", ["canvas"]),
        ("transparent", ["canvas"]),

        // ── Generic brand/interaction terms (checked last) ──
        ("primary", ["primary"]),
        ("accent", ["accent"]),
        ("brand", ["primary"]),
        ("cta", ["primary"]),
    ];

    private static readonly Regex HexColorPattern = new(
        @"(?:^|[^#\w])#([0-9a-fA-F]{3,8})(?=[\s\),;:\]\.`]|$)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex RgbaColorPattern = new(
        @"rgba\((\d+),\s*(\d+),\s*(\d+),\s*([\d.]+)\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Scans the markdown body for hex color values and rgba() values, then
    /// maps them to design tokens based on keyword context on the same line.
    /// Only the first useful token per line is assigned.
    /// </summary>
    private static Dictionary<string, string> InferColors(string content)
    {
        var colors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = content.Split('\n', StringSplitOptions.None);

        foreach (var line in lines)
        {
            // Collect all hex and rgba color values on this line
            var hexMatches = HexColorPattern.Matches(line);
            var rgbaMatches = RgbaColorPattern.Matches(line);

            if (hexMatches.Count == 0 && rgbaMatches.Count == 0)
                continue;

            // Find the best token hint from keywords on this line
            var matchedTokens = FindBestTokenHint(line);
            if (matchedTokens.Length == 0)
                continue; // no keyword context — skip this line's colors

            // Assign colors to the first available token slot
            var targetToken = matchedTokens[0];
            foreach (Match match in hexMatches)
            {
                if (!colors.ContainsKey(targetToken))
                    colors[targetToken] = "#" + match.Groups[1].Value;
            }
            foreach (Match match in rgbaMatches)
            {
                if (!colors.ContainsKey(targetToken))
                    colors[targetToken] = match.Groups[0].Value;
            }
        }

        return colors;
    }

    /// <summary>
    /// Finds the best token hint for a line by scanning against
    /// <see cref="BodyColorHints"/> in order (most specific first).
    /// Returns the token array from the first matching hint, or an empty array.
    /// </summary>
    private static string[] FindBestTokenHint(string line)
    {
        var lineLower = line.ToLowerInvariant();
        foreach (var (keyword, tokens) in BodyColorHints)
        {
            if (lineLower.Contains(keyword, StringComparison.Ordinal))
                return tokens;
        }
        return [];
    }

    /// <summary>
    /// Attempts to parse a color value from the design file's color token.
    /// Supports hex colors (#RRGGBB), rgb() strings, and named colors.
    /// </summary>
    internal static Avalonia.Media.Color? ParseColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = value.Trim();

        // Hex color
        if (value.StartsWith('#'))
        {
            // Handle rgba hex: #RRGGBB or #AARRGGBB or #RGB or #ARGB
            var hex = value[1..];
            if (hex.Length == 3 || hex.Length == 4)
            {
                // Expand short form: #RGB → #RRGGBB
                hex = string.Concat(hex.Select(c => $"{c}{c}"));
            }

            if (hex.Length == 6 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            {
                var r = (byte)(rgb >> 16);
                var g = (byte)(rgb >> 8);
                var b = (byte)rgb;
                return Avalonia.Media.Color.FromArgb(255, r, g, b);
            }

            if (hex.Length == 8 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
            {
                var a = (byte)(argb >> 24);
                var r = (byte)(argb >> 16);
                var g = (byte)(argb >> 8);
                var b = (byte)argb;
                return Avalonia.Media.Color.FromArgb(a, r, g, b);
            }
        }

        // rgba() format
        if (value.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase))
        {
            var inner = value["rgba(".Length..^1];
            var parts = inner.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length == 4 &&
                byte.TryParse(parts[0], out var r) &&
                byte.TryParse(parts[1], out var g) &&
                byte.TryParse(parts[2], out var b) &&
                double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var a))
            {
                var alpha = (byte)(a * 255);
                return Avalonia.Media.Color.FromArgb(alpha, r, g, b);
            }
        }

        return null;
    }
}
