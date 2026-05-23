using Microsoft.Data.Sqlite;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: dotnet run --project scripts/migrate -- <catalog-db-path>");
    return 1;
}

var dbPath = args[0];
if (!File.Exists(dbPath))
{
    Console.Error.WriteLine($"Database not found: {dbPath}");
    return 2;
}

await using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
await conn.OpenAsync();

await using var busy = conn.CreateCommand();
busy.CommandText = "PRAGMA busy_timeout = 10000";
await busy.ExecuteNonQueryAsync();

static async Task<string[]> GetExistingColumnsAsync(SqliteConnection conn, string table)
{
    var cmd = conn.CreateCommand();
    cmd.CommandText = $"SELECT name FROM pragma_table_info('{table}')";
    var names = new List<string>();
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync()) names.Add(reader.GetString(0));
    return names.ToArray();
}

static async Task AddColumnIfMissingAsync(SqliteConnection conn, string table, string colDef)
{
    var colName = colDef.Split(' ')[0];
    var existing = await GetExistingColumnsAsync(conn, table);
    if (existing.Contains(colName))
    {
        Console.WriteLine($"  EXISTS (skipped): {table}.{colName}");
        return;
    }
    Console.WriteLine($"  ADD COLUMN: {table}.{colDef}");
    var cmd = conn.CreateCommand();
    cmd.CommandText = $"ALTER TABLE [{table}] ADD COLUMN {colDef}";
    await cmd.ExecuteNonQueryAsync();
}

// DigitalAssets
Console.WriteLine("=== DigitalAssets ===");
string[] digitalAssetCols = [
    "OriginalPath TEXT DEFAULT ''",
    "Copyright TEXT",
    "Rating INTEGER DEFAULT 0",
    "Label INTEGER DEFAULT 0",
    "Flag INTEGER DEFAULT 0",
    "GpsLatitude REAL",
    "GpsLongitude REAL",
    "Orientation INTEGER DEFAULT 0"
];
foreach (var col in digitalAssetCols)
    await AddColumnIfMissingAsync(conn, "DigitalAssets", col);

// MetadataProfiles
Console.WriteLine("\n=== MetadataProfiles ===");
string[] metadataCols = [
    "Category TEXT",
    "DateTaken TEXT",
    "Rating INTEGER",
    "Creator TEXT",
    "Copyright TEXT",
    "UsageTerms TEXT",
    "ContactInfo TEXT",
    "City TEXT",
    "State TEXT",
    "Country TEXT",
    "Headline TEXT",
    "Description TEXT",
    "Title TEXT"
];
foreach (var col in metadataCols)
    await AddColumnIfMissingAsync(conn, "MetadataProfiles", col);

Console.WriteLine("\nMigration complete.");
Console.WriteLine($"  DigitalAssets: ({string.Join(", ", await GetExistingColumnsAsync(conn, "DigitalAssets"))})");
Console.WriteLine($"  MetadataProfiles: ({string.Join(", ", await GetExistingColumnsAsync(conn, "MetadataProfiles"))})");

return 0;
