using System;
using System.Collections.Generic;
using System.Text.Json;

namespace LiquidVision.Core.Services;

/// <summary>Parsed fields extracted from the model's generated text.</summary>
public readonly record struct ParsedTags(string Description, IReadOnlyList<string> Keywords, IReadOnlyList<string> Categories);

/// <summary>
/// Tolerantly extracts the <c>{description, keywords, categories}</c> contract from model output,
/// coping with code fences, surrounding prose, and minor format drift.
/// </summary>
public static class TagResultParser
{
    /// <summary>Attempts to parse structured tags from raw generated text.</summary>
    /// <returns><c>true</c> if a JSON object with at least one expected field was found.</returns>
    public static bool TryParse(string raw, out ParsedTags result)
    {
        result = new ParsedTags(string.Empty, Array.Empty<string>(), Array.Empty<string>());
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var json = ExtractJsonObject(raw);
        if (json is null)
            return false;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            string description = GetString(root, "description") ?? string.Empty;
            var keywords = GetStringList(root, "keywords");
            var categories = GetStringList(root, "categories");

            if (description.Length == 0 && keywords.Count == 0 && categories.Count == 0)
                return false;

            result = new ParsedTags(description, keywords, categories);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Isolates the outermost JSON object substring, stripping code fences/prose.</summary>
    private static string? ExtractJsonObject(string raw)
    {
        int start = raw.IndexOf('{');
        int end = raw.LastIndexOf('}');
        if (start < 0 || end <= start)
            return null;
        return raw.Substring(start, end - start + 1);
    }

    private static string? GetString(JsonElement obj, string name)
    {
        foreach (var prop in obj.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                return prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : prop.Value.ToString();
        }
        return null;
    }

    private static IReadOnlyList<string> GetStringList(JsonElement obj, string name)
    {
        foreach (var prop in obj.EnumerateObject())
        {
            if (!string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                continue;

            var value = prop.Value;
            if (value.ValueKind == JsonValueKind.Array)
            {
                var list = new List<string>();
                foreach (var item in value.EnumerateArray())
                {
                    var s = item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString();
                    if (!string.IsNullOrWhiteSpace(s))
                        list.Add(s.Trim());
                }
                return list;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                // Tolerate a comma-separated string instead of an array.
                var parts = value.GetString()!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                return parts;
            }
        }
        return Array.Empty<string>();
    }
}
