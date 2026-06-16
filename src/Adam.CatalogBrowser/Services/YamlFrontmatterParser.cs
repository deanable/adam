using System.Globalization;

namespace Adam.CatalogBrowser.Services;

/// <summary>
/// Minimal YAML frontmatter parser for <c>.design/*.md</c> files.
/// Extracts the frontmatter block between <c>---</c> markers and parses
/// top-level scalar values and one level of nested key-value pairs.
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
    /// Parses the frontmatter from the given markdown file content.
    /// Returns <c>null</c> if no valid frontmatter is found.
    /// </summary>
    internal static DesignFrontmatter? Parse(string filePath)
    {
        var content = File.ReadAllText(filePath);
        return ParseContent(Path.GetFileNameWithoutExtension(filePath), content);
    }

    internal static DesignFrontmatter? ParseContent(string fileName, string content)
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
