using System.Globalization;
using System.Text;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace Adam.CatalogBrowser.Services;

/// <summary>
/// Represents a single row of metadata parsed from a CSV file.
/// Fields that are null were not present in the CSV or were left empty.
/// </summary>
public class CsvMetadataRow
{
    /// <summary>Matching key: file name (must be unique within the export set).</summary>
    public string FileName { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Keywords { get; set; } // pipe-separated
    public string? Categories { get; set; } // pipe-separated
    public int? Rating { get; set; }
    public string? Label { get; set; }
    public string? Flag { get; set; }
    public string? Copyright { get; set; }
    public double? GpsLatitude { get; set; }
    public double? GpsLongitude { get; set; }
    public string? CameraMake { get; set; }
    public string? CameraModel { get; set; }
}

/// <summary>
/// Service for exporting and importing asset metadata to/from CSV files.
/// Follows RFC 4180 with UTF-8 BOM for Excel compatibility.
/// </summary>
public class CsvMetadataService
{
    // Field descriptors: name → (header label, getter, setter)
    private static readonly List<CsvFieldDef> FieldDefs =
    [
        new CsvFieldDef("FileName",    a => a.FileName,        false),
        new CsvFieldDef("Title",       a => a.Title,           false),
        new CsvFieldDef("Description", a => a.Description ?? string.Empty, false),
        new CsvFieldDef("Keywords",    a => string.Join("|", a.Keywords.Select(k => k.Name)), false),
        new CsvFieldDef("Categories",  a => string.Join("|", a.Categories.Select(c => c.Name)), false),
        new CsvFieldDef("Rating",      a => a.Rating.ToString(), false),
        new CsvFieldDef("Label",       a => a.Label.ToString(), false),
        new CsvFieldDef("Flag",        a => a.Flag.ToString(), false),
        new CsvFieldDef("Copyright",   a => a.Copyright ?? string.Empty, false),
        new CsvFieldDef("GpsLatitude", a => a.GpsLatitude?.ToString("F6", CultureInfo.InvariantCulture) ?? string.Empty, false),
        new CsvFieldDef("GpsLongitude",a => a.GpsLongitude?.ToString("F6", CultureInfo.InvariantCulture) ?? string.Empty, false),
        new CsvFieldDef("CameraMake",  a => a.MetadataProfile?.CameraMake ?? string.Empty, false),
        new CsvFieldDef("CameraModel", a => a.MetadataProfile?.CameraModel ?? string.Empty, false)
    ];

    private static readonly string[] CsvHeader = FieldDefs.Select(f => f.Header).ToArray();
    private static readonly HashSet<string> CsvHeaderSet = [.. CsvHeader.Select(h => h.ToLowerInvariant())];

    // Filter group names → set of header columns they expand to
    private static readonly Dictionary<string, HashSet<string>> FilterToHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["title"] = ["Title"],
        ["description"] = ["Description"],
        ["keywords"] = ["Keywords"],
        ["categories"] = ["Categories"],
        ["rating"] = ["Rating"],
        ["label"] = ["Label"],
        ["flag"] = ["Flag"],
        ["copyright"] = ["Copyright"],
        ["gps"] = ["GpsLatitude", "GpsLongitude"],
        ["camera"] = ["CameraMake", "CameraModel"]
    };

    private sealed record CsvFieldDef(string Header, Func<DigitalAsset, string> Getter, bool IsReadOnly);

    /// <summary>
    /// Exports a list of <see cref="DigitalAsset"/> records to a CSV file at <paramref name="outputPath"/>.
    /// </summary>
    /// <param name="assets">Assets to export.</param>
    /// <param name="outputPath">Output file path.</param>
    /// <param name="fieldFilter">Optional set of field group names to include (omit for all fields).
    /// Valid values: title, description, keywords, categories, rating, label, flag, copyright, gps, camera.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task ExportToCsvAsync(List<DigitalAsset> assets, string outputPath, HashSet<string>? fieldFilter = null, CancellationToken ct = default)
    {
        var activeHeaders = GetActiveHeaders(fieldFilter);
        var activeDefs = FieldDefs.Where(f => activeHeaders.Contains(f.Header)).ToList();

        var sb = new StringBuilder();

        // Header row
        sb.AppendLine(string.Join(",", activeDefs.Select(d => EscapeCsvField(d.Header))));

        // Data rows (same column set as header)
        foreach (var asset in assets)
        {
            var row = activeDefs.Select(d => EscapeCsvField(d.Getter(asset))).ToArray();
            sb.AppendLine(string.Join(",", row));
        }

        // UTF-8 BOM for Excel compatibility
        await File.WriteAllTextAsync(outputPath, sb.ToString(), new UTF8Encoding(true), ct);
    }

    /// <summary>
    /// Returns the set of header column names that should be included based on the field filter.
    /// FileName is always included.
    /// </summary>
    private static HashSet<string> GetActiveHeaders(HashSet<string>? fieldFilter)
    {
        if (fieldFilter == null || fieldFilter.Count == 0)
            return [.. CsvHeader];

        var result = new HashSet<string> { "FileName" };
        foreach (var filterName in fieldFilter)
        {
            if (FilterToHeaders.TryGetValue(filterName, out var headers))
                foreach (var h in headers)
                    result.Add(h);
        }
        return result;
    }

    /// <summary>
    /// Reads a CSV file and returns the parsed rows. Validates the header row.
    /// </summary>
    public async Task<List<CsvMetadataRow>> ReadCsvAsync(string inputPath, CancellationToken ct = default)
    {
        var lines = await File.ReadAllLinesAsync(inputPath, Encoding.UTF8, ct);
        if (lines.Length < 2)
            return [];

        var header = ParseCsvLine(lines[0]);
        var colMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Length; i++)
        {
            if (CsvHeaderSet.Contains(header[i].Trim().ToLowerInvariant()))
                colMap[header[i].Trim()] = i;
        }

        if (!colMap.ContainsKey("FileName"))
            throw new InvalidDataException("CSV file is missing the required 'FileName' column.");

        var rows = new List<CsvMetadataRow>(lines.Length - 1);
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var fields = ParseCsvLine(line);
            var row = new CsvMetadataRow();

            if (TryGetField(fields, colMap, "FileName", out var fileName)) row.FileName = fileName;
            if (TryGetField(fields, colMap, "Title", out var title)) row.Title = title;
            if (TryGetField(fields, colMap, "Description", out var desc)) row.Description = desc;
            if (TryGetField(fields, colMap, "Keywords", out var keywords)) row.Keywords = keywords;
            if (TryGetField(fields, colMap, "Categories", out var categories)) row.Categories = categories;
            if (TryGetField(fields, colMap, "Rating", out var ratingStr) && int.TryParse(ratingStr, out var rating)) row.Rating = rating;
            if (TryGetField(fields, colMap, "Label", out var label)) row.Label = label;
            if (TryGetField(fields, colMap, "Flag", out var flag)) row.Flag = flag;
            if (TryGetField(fields, colMap, "Copyright", out var copyright)) row.Copyright = copyright;
            if (TryGetField(fields, colMap, "GpsLatitude", out var latStr) && double.TryParse(latStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var lat)) row.GpsLatitude = lat;
            if (TryGetField(fields, colMap, "GpsLongitude", out var lonStr) && double.TryParse(lonStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var lon)) row.GpsLongitude = lon;
            if (TryGetField(fields, colMap, "CameraMake", out var camMake)) row.CameraMake = camMake;
            if (TryGetField(fields, colMap, "CameraModel", out var camModel)) row.CameraModel = camModel;

            rows.Add(row);
        }

        return rows;
    }

    /// <summary>
    /// Imports metadata from CSV rows into the database. Matches assets by FileName.
    /// Returns the number of assets that were updated.
    /// </summary>
    public async Task<int> ImportFromCsvAsync(
        List<CsvMetadataRow> rows,
        AppDbContext db,
        ConflictMode conflictMode = ConflictMode.Overwrite,
        IProgress<(int processed, int total)>? progress = null,
        CancellationToken ct = default)
    {
        var updated = 0;
        var total = rows.Count;

        for (var i = 0; i < rows.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var row = rows[i];
            if (string.IsNullOrWhiteSpace(row.FileName))
                continue;

            var asset = await db.DigitalAssets
                .Include(a => a.Keywords)
                .Include(a => a.Categories)
                .Include(a => a.MetadataProfile)
                .FirstOrDefaultAsync(a => a.FileName == row.FileName, ct);

            if (asset == null)
                continue;

            ApplyRowToAsset(row, asset, conflictMode);
            updated++;

            progress?.Report((i + 1, total));

            if (updated % 50 == 0)
                await db.SaveChangesAsync(ct);
        }

        await db.SaveChangesAsync(ct);
        return updated;
    }

    /// <summary>
    /// Applies a single CSV row's values to a <see cref="DigitalAsset"/>.
    /// </summary>
    public void ApplyRowToAsset(CsvMetadataRow row, DigitalAsset asset, ConflictMode conflictMode = ConflictMode.Overwrite)
    {
        if (row.Title != null) asset.Title = row.Title;
        if (row.Description != null) asset.Description = row.Description;
        if (row.Copyright != null) asset.Copyright = row.Copyright;
        if (row.Rating.HasValue) asset.Rating = Math.Clamp(row.Rating.Value, 0, 5);
        if (row.Label != null && Enum.TryParse<AssetLabel>(row.Label, ignoreCase: true, out var label)) asset.Label = label;
        if (row.Flag != null && Enum.TryParse<AssetFlag>(row.Flag, ignoreCase: true, out var flag)) asset.Flag = flag;
        if (row.GpsLatitude.HasValue) asset.GpsLatitude = row.GpsLatitude;
        if (row.GpsLongitude.HasValue) asset.GpsLongitude = row.GpsLongitude;

        // Keywords
        if (row.Keywords != null)
        {
            var names = row.Keywords.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (conflictMode == ConflictMode.AppendKeywords)
            {
                var existing = new HashSet<string>(asset.Keywords.Select(k => k.Name), StringComparer.OrdinalIgnoreCase);
                foreach (var name in names)
                {
                    if (!existing.Contains(name))
                        asset.Keywords.Add(MakeKeyword(name));
                }
            }
            else
            {
                asset.Keywords.Clear();
                foreach (var name in names)
                {
                    if (!string.IsNullOrWhiteSpace(name))
                        asset.Keywords.Add(MakeKeyword(name));
                }
            }
        }

        // Categories
        if (row.Categories != null)
        {
            var names = row.Categories.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (conflictMode == ConflictMode.AppendKeywords)
            {
                var existing = new HashSet<string>(asset.Categories.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
                foreach (var name in names)
                {
                    if (!existing.Contains(name))
                        asset.Categories.Add(MakeCategory(name));
                }
            }
            else
            {
                asset.Categories.Clear();
                foreach (var name in names)
                {
                    if (!string.IsNullOrWhiteSpace(name))
                        asset.Categories.Add(MakeCategory(name));
                }
            }
        }

        // Camera metadata
        if (row.CameraMake != null || row.CameraModel != null)
        {
            asset.MetadataProfile ??= new MetadataProfile { Id = Guid.NewGuid(), DigitalAssetId = asset.Id };
            if (row.CameraMake != null) asset.MetadataProfile.CameraMake = row.CameraMake;
            if (row.CameraModel != null) asset.MetadataProfile.CameraModel = row.CameraModel;
        }
    }

    /// <summary>Creates a new Keyword with NormalizedName set.</summary>
    private static Keyword MakeKeyword(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name.Trim(),
        NormalizedName = name.Trim().ToLowerInvariant()
    };

    /// <summary>Creates a new Category with NormalizedName set.</summary>
    private static Category MakeCategory(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name.Trim(),
        NormalizedName = name.Trim().ToLowerInvariant()
    };

    /// <summary>
    /// Previews what a CSV import would change without writing to the database.
    /// </summary>
    public async Task<List<string>> PreviewImportAsync(List<CsvMetadataRow> rows, AppDbContext db, CancellationToken ct = default)
    {
        var preview = new List<string>(rows.Count);

        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(row.FileName))
                continue;

            var asset = await db.DigitalAssets
                .Include(a => a.Keywords)
                .Include(a => a.Categories)
                .FirstOrDefaultAsync(a => a.FileName == row.FileName, ct);

            if (asset == null)
            {
                preview.Add($"✕ {row.FileName} — not found (will be skipped)");
                continue;
            }

            var changes = new List<string>();

            if (row.Title != null && row.Title != asset.Title)
                changes.Add($"Title: \"{asset.Title}\" → \"{row.Title}\"");

            if (row.Rating.HasValue && row.Rating != asset.Rating)
                changes.Add($"Rating: {asset.Rating} → {row.Rating}");

            if (row.Keywords != null)
            {
                var current = asset.Keywords.Select(k => k.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var incoming = row.Keywords.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var added = incoming.Where(k => !current.Contains(k)).ToArray();
                if (added.Length > 0)
                    changes.Add($"Keywords: +{string.Join(", ", added)}");
            }

            if (changes.Count > 0)
                preview.Add($"✓ {row.FileName}: {string.Join("; ", changes)}");
            else
                preview.Add($"○ {row.FileName} — no changes");
        }

        return preview;
    }

    // ─── CSV parsing helpers (RFC 4180) ───

    private static string EscapeCsvField(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private static string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else inQuotes = false;
                }
                else current.Append(c);
            }
            else
            {
                if (c == '"') inQuotes = true;
                else if (c == ',') { fields.Add(current.ToString().Trim()); current.Clear(); }
                else current.Append(c);
            }
        }

        fields.Add(current.ToString().Trim());
        return [.. fields];
    }

    private static bool TryGetField(string[] fields, Dictionary<string, int> colMap, string colName, out string value)
    {
        if (colMap.TryGetValue(colName, out var idx) && idx < fields.Length)
        {
            value = fields[idx];
            return !string.IsNullOrEmpty(value);
        }
        value = string.Empty;
        return false;
    }
}

/// <summary>How conflicting fields are handled during CSV import.</summary>
public enum ConflictMode
{
    Overwrite,
    SkipIfEmpty,
    AppendKeywords
}
