using System.Diagnostics;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Adam.Shared.Services;
using Microsoft.EntityFrameworkCore;

// ─────────────────────────────────────────────────────────────
//  Adam Benchmark — T8.1: Performance baseline at 100K assets
// ─────────────────────────────────────────────────────────────
//
// Usage:
//   dotnet run --project scripts/benchmark -- [--seed-only] [--db-path <path>]
//
//   --seed-only   Only generate the 100K database, skip benchmarks
//   --db-path     Path to the SQLite database (default: temp file)
//
// Output: Results written to console and scripts/benchmark/results.md

var seedOnly = args.Contains("--seed-only");
var dbArg = args.SkipWhile(a => a != "--db-path").Skip(1).FirstOrDefault();

var basePath = dbArg is not null
    ? Path.GetDirectoryName(Path.GetFullPath(dbArg))!
    : Path.Combine(Path.GetTempPath(), $"adam-bench-{Guid.NewGuid():n}");

var benchmark = new AdamBenchmark(basePath, dbArg);
try
{
    await benchmark.InitializeAsync();

    if (!seedOnly)
        await benchmark.RunAllBenchmarksAsync();
    else
        Console.WriteLine("--seed-only: Database seeded, skipping benchmarks.");
}
finally
{
    await benchmark.ReportAsync();
    await benchmark.CleanupAsync();
}

// ═════════════════════════════════════════════════════════════
//  Benchmark harness
// ═════════════════════════════════════════════════════════════

internal sealed class AdamBenchmark(string basePath, string? explicitDbPath)
{
    private readonly List<BenchmarkResult> _results = [];
    private Stopwatch _sw = new();
    private ModeManager _modeManager = null!;
    private int _totalAssets;
    private string _dbPath = string.Empty;

    public async Task InitializeAsync()
    {
        Console.WriteLine("=== Adam Benchmark: 100K Asset Baseline ===\n");
        Console.WriteLine($"Base path: {basePath}");

        var sw = Stopwatch.StartNew();
        _modeManager = new ModeManager(basePath);
        await _modeManager.InitializeAsync();
        _dbPath = _modeManager.DbPath;
        Console.WriteLine($"Database: {_dbPath}");
        Console.WriteLine($"Init time: {sw.Elapsed.TotalSeconds:F2}s\n");

        await using var db = _modeManager.CreateDbContext();

        // Check if already seeded
        _totalAssets = await db.DigitalAssets.CountAsync();
        if (_totalAssets >= 100_000)
        {
            Console.WriteLine($"Database already has {_totalAssets} assets — skipping seed.\n");
            return;
        }

        Console.WriteLine($"Seeding {100_000 - _totalAssets:N0} synthetic assets...");
        sw.Restart();
        await SeedAssetsAsync(db, 100_000 - _totalAssets);
        Console.WriteLine($"Seed complete: {sw.Elapsed.TotalSeconds:F2}s\n");
    }

    // ── Seed ────────────────────────────────────────────────

    private static readonly string[] ImageExtensions = [".jpg", ".png", ".webp", ".bmp", ".tiff", ".cr2", ".nef"];
    private static readonly string[] VideoExtensions = [".mp4", ".mov", ".avi", ".mkv", ".webm"];
    private static readonly string[] DocumentExtensions = [".pdf", ".docx", ".xlsx", ".pptx"];
    private static readonly string[] AudioExtensions = [".mp3", ".flac", ".wav", ".m4a", ".ogg"];

    private static readonly string[] KeywordPool =
    [
        "Nature", "Portrait", "Landscape", "Architecture", "Street",
        "Macro", "Aerial", "Underwater", "Night", "Black & White",
        "Vintage", "Modern", "Abstract", "Minimalist", "Vibrant",
        "Travel", "Food", "Fashion", "Sports", "Wildlife"
    ];

    private static readonly string[] CategoryPool =
    [
        "Client Work", "Personal", "Stock", "Archive", "Portfolio"
    ];

    private static readonly string[] FolderPool =
    [
        "/photos/2024", "/photos/2025", "/photos/2026",
        "/projects/client-a", "/projects/client-b", "/projects/personal",
        "/exports", "/imports", "/backups"
    ];

    private async Task SeedAssetsAsync(AppDbContext db, int count)
    {
        var batchSize = 1000;
        var rng = new Random(42);
        var now = DateTimeOffset.UtcNow;

        // Pre-create categories
        var categories = new List<Category>();
        foreach (var name in CategoryPool)
        {
            var cat = new Category
            {
                Id = Guid.NewGuid(),
                Name = name,
                NormalizedName = name.ToLowerInvariant()
            };
            db.Categories.Add(cat);
            categories.Add(cat);
        }
        await db.SaveChangesAsync();

        // Pre-create keywords
        var keywords = new List<Keyword>();
        foreach (var name in KeywordPool)
        {
            var kw = new Keyword
            {
                Id = Guid.NewGuid(),
                Name = name,
                NormalizedName = name.ToLowerInvariant()
            };
            db.Keywords.Add(kw);
            keywords.Add(kw);
        }
        await db.SaveChangesAsync();

        var batches = (int)Math.Ceiling(count / (double)batchSize);
        var totalInserted = 0;

        for (var b = 0; b < batches; b++)
        {
            var batchCount = Math.Min(batchSize, count - totalInserted);
            var assets = new List<DigitalAsset>(batchCount);

            for (var i = 0; i < batchCount; i++)
            {
                var idx = totalInserted + i;
                var assetType = rng.Next(0, 10) switch
                {
                    < 6 => AssetType.Image,
                    < 8 => AssetType.Video,
                    < 9 => AssetType.Document,
                    < 10 => AssetType.Audio,
                    _ => AssetType.Other
                };

                var ext = assetType switch
                {
                    AssetType.Image => ImageExtensions[rng.Next(ImageExtensions.Length)],
                    AssetType.Video => VideoExtensions[rng.Next(VideoExtensions.Length)],
                    AssetType.Document => DocumentExtensions[rng.Next(DocumentExtensions.Length)],
                    AssetType.Audio => AudioExtensions[rng.Next(AudioExtensions.Length)],
                    _ => ".bin"
                };

                var fileName = $"asset_{idx:D6}{ext}";
                var mimeType = assetType switch
                {
                    AssetType.Image => "image/jpeg",
                    AssetType.Video => "video/mp4",
                    AssetType.Document => "application/pdf",
                    AssetType.Audio => "audio/mpeg",
                    _ => "application/octet-stream"
                };

                var daysAgo = rng.Next(0, 730); // 2 year span
                var createdAt = now.AddDays(-daysAgo).AddMinutes(rng.Next(0, 1440));

                var folder = FolderPool[rng.Next(FolderPool.Length)];
                var storagePath = $"{folder}/{fileName}";
                var title = $"Asset {idx} — " + (assetType switch
                {
                    AssetType.Image => "Photo",
                    AssetType.Video => "Video",
                    AssetType.Document => "Document",
                    AssetType.Audio => "Audio",
                    _ => "File"
                });

                var asset = new DigitalAsset
                {
                    Id = Guid.NewGuid(),
                    FileName = fileName,
                    FileExtension = ext,
                    MimeType = mimeType,
                    FileSize = rng.NextInt64(1024, 500_000_000), // 1KB to 500MB
                    ChecksumSha256 = Convert.ToHexString(
                        System.Security.Cryptography.SHA256.HashData(
                            System.Text.Encoding.UTF8.GetBytes($"{idx}:{fileName}"))
                    ),
                    StoragePath = storagePath,
                    OriginalPath = storagePath,
                    Title = title,
                    Description = rng.NextDouble() > 0.3 ? $"Synthetic asset #{idx} with {assetType} metadata for benchmark testing." : null,
                    Type = assetType,
                    Width = assetType == AssetType.Image ? rng.Next(640, 8000) : null,
                    Height = assetType == AssetType.Image ? rng.Next(480, 6000) : null,
                    Duration = assetType == AssetType.Video ? Math.Round(rng.NextDouble() * 300 + 5, 1) : null,
                    Rating = rng.Next(0, 5),
                    CreatedAt = createdAt,
                    ModifiedAt = createdAt,
                    Version = 1
                };

                // Assign 1-3 keywords to 80% of assets
                if (rng.NextDouble() < 0.8)
                {
                    var kwCount = rng.Next(1, 4);
                    var assigned = new HashSet<string>();
                    for (var k = 0; k < kwCount; k++)
                    {
                        var kw = keywords[rng.Next(keywords.Count)];
                        if (assigned.Add(kw.Name))
                            asset.Keywords.Add(kw);
                    }
                }

                // Assign 1 category to 60% of assets
                if (rng.NextDouble() < 0.6)
                {
                    asset.Categories.Add(categories[rng.Next(categories.Count)]);
                }

                assets.Add(asset);
            }

            db.DigitalAssets.AddRange(assets);
            await db.SaveChangesAsync();
            totalInserted += batchCount;

            if ((b + 1) % 5 == 0 || b == batches - 1)
                Console.WriteLine($"  Seeded {totalInserted,7:N0} / {count:N0} assets...");
        }

        _totalAssets = await db.DigitalAssets.CountAsync();
    }

    // ── Benchmarks ──────────────────────────────────────────

    public async Task RunAllBenchmarksAsync()
    {
        Console.WriteLine("Running benchmarks...\n");

        // Warmup
        await using (var warmup = _modeManager.CreateDbContext())
            _ = await warmup.DigitalAssets.FirstOrDefaultAsync();

        // 1. Count (no filter)
        await BenchmarkAsync("1. Full table count (no filter)", async db =>
        {
            return await db.DigitalAssets.CountAsync();
        });

        // 2. Type filter (50%+ of assets should match)
        await BenchmarkAsync("2. Type filter (Images ~60%)", async db =>
        {
            return await db.DigitalAssets.Where(a => a.Type == AssetType.Image).CountAsync();
        });

        // 3. Date range (last 90 days) — use raw SQL to avoid EF Core SQLite DateTimeOffset translation limits
        await BenchmarkAsync("3. Date range filter (last 90 days)", async db =>
        {
            var since = DateTime.UtcNow.AddDays(-90).ToString("O");
            var param = new Microsoft.Data.Sqlite.SqliteParameter("@since", since);
            return await db.Database.SqlQueryRaw<int>(
                $"SELECT COUNT(*) AS Value FROM \"DigitalAssets\" WHERE \"CreatedAt\" >= @since AND \"IsDeleted\" = 0",
                param).FirstAsync();
        });

        // 4. Combined filter: Type + Date + FileSize
        await BenchmarkAsync("4. Combined filter (Image + 2025 + <100MB)", async db =>
        {
            var from = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToString("O");
            var to = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToString("O");
            var intParam = new Microsoft.Data.Sqlite.SqliteParameter("@intParam", 100_000_000);
            var fromParam = new Microsoft.Data.Sqlite.SqliteParameter("@from", from);
            var toParam = new Microsoft.Data.Sqlite.SqliteParameter("@to", to);
            return await db.Database.SqlQueryRaw<int>(
                $"SELECT COUNT(*) AS Value FROM \"DigitalAssets\" WHERE \"Type\" = 0 AND \"CreatedAt\" >= @from AND \"CreatedAt\" < @to AND \"FileSize\" < @intParam AND \"IsDeleted\" = 0",
                fromParam, toParam, intParam).FirstAsync();
        });

        // 5. Sort by Date Added (page 1 of 50) — fully raw SQL to avoid DateTimeOffset translation
        await BenchmarkAsync("5. Sort by Date Added (page 1 of 50)", async db =>
        {
            return await db.DigitalAssets
                .FromSqlRaw($"SELECT * FROM \"DigitalAssets\" WHERE \"IsDeleted\" = 0 ORDER BY \"CreatedAt\" DESC, \"Id\" LIMIT 50")
                .ToListAsync();
        });

        // 6. Sort by File Size (page 1 of 50)
        await BenchmarkAsync("6. Sort by File Size (page 1 of 50)", async db =>
        {
            return await db.DigitalAssets
                .OrderBy(a => a.FileSize)
                .ThenBy(a => a.Id)
                .Take(50)
                .ToListAsync();
        });

        // 7. Paginated: deep page (page 1000 of 50 = items 50K-50K+50)
        await BenchmarkAsync("7. Deep pagination (page 1000 of 50)", async db =>
        {
            return await db.DigitalAssets
                .OrderBy(a => a.FileName)
                .ThenBy(a => a.Id)
                .Skip(1000 * 50)
                .Take(50)
                .ToListAsync();
        });

        // 8. Keyword filter (assets with "Nature" keyword)
        await BenchmarkAsync("8. Keyword filter (Nature)", async db =>
        {
            var normalized = "nature".ToLowerInvariant();
            return await db.DigitalAssets
                .Where(a => a.Keywords.Any(k => k.NormalizedName == normalized))
                .CountAsync();
        });

        // 9. Category filter (any category)
        await BenchmarkAsync("9. Category filter", async db =>
        {
            return await db.DigitalAssets
                .Where(a => a.Categories.Any())
                .CountAsync();
        });

        // 10. Folder path prefix filter (LIKE/StartsWith)
        await BenchmarkAsync("10. Folder prefix filter (/photos/)", async db =>
        {
            return await db.DigitalAssets
                .Where(a => a.StoragePath.StartsWith("/photos/"))
                .CountAsync();
        });

        // 11. Title/Description text search (LIKE)
        await BenchmarkAsync("11. Title text search (contains 'Photo')", async db =>
        {
            return await db.DigitalAssets
                .Where(a => a.Title.Contains("Photo"))
                .CountAsync();
        });

        // 12. Full table load (no filter, all columns, page 1)
        await BenchmarkAsync("12. Gallery load (page 1 of 50, all columns)", async db =>
        {
            return await db.DigitalAssets
                .OrderBy(a => a.FileName)
                .ThenBy(a => a.Id)
                .Take(50)
                .ToListAsync();
        });

        Console.WriteLine("\nAll benchmarks complete.\n");
    }

    private async Task BenchmarkAsync<T>(string name, Func<AppDbContext, Task<T>> query)
    {
        // Cold run (first time — no EF Core query plan cache, SQLite page cache cold)
        await using (var db = _modeManager.CreateDbContext())
        {
            // Set production-friendly SQLite PRAGMAs for realistic measurement
            await db.Database.ExecuteSqlRawAsync("PRAGMA cache_size = -64000");
            await db.Database.ExecuteSqlRawAsync("PRAGMA synchronous = NORMAL");
            await db.Database.ExecuteSqlRawAsync("PRAGMA temp_store = MEMORY");

            _sw.Restart();
            var _ = await query(db);
            _sw.Stop();
            var coldMs = _sw.Elapsed.TotalMilliseconds;

            // Warm run (query plan cached + SQLite pages in cache)
            _sw.Restart();
            _ = await query(db);
            _sw.Stop();
            var warmMs = _sw.Elapsed.TotalMilliseconds;

            _results.Add(new BenchmarkResult(name, coldMs, warmMs));
            Console.WriteLine($"  {name,-55} Cold: {coldMs,8:F1} ms  Warm: {warmMs,8:F1} ms");
        }
    }

    // ── Reporting ───────────────────────────────────────────

    public async Task ReportAsync()
    {
        if (_results.Count == 0) return;

        var md = new System.Text.StringBuilder();
        md.AppendLine("# Adam Benchmark Results — 100K Assets");
        md.AppendLine();
        md.AppendLine($"- **Date**: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss UTC}");
        md.AppendLine($"- **Database**: {_dbPath}");
        md.AppendLine($"- **Asset count**: {_totalAssets:N0}");
        var sqliteVersion = await GetSqliteVersionAsync();
        md.AppendLine($"- **SQLite version**: {sqliteVersion}");
        md.AppendLine();
        md.AppendLine("| # | Scenario | Cold (ms) | Warm (ms) |");
        md.AppendLine("|---|----------|-----------|-----------|");

        for (var i = 0; i < _results.Count; i++)
        {
            var r = _results[i];
            var scenario = r.Name.Split(". ", 2) is { Length: 2 } parts ? parts[1] : r.Name;
            md.AppendLine($"| {i + 1} | {scenario} | {r.ColdMs:F1} | {r.WarmMs:F1} |");
        }

        // Summary and observations
        var avgCold = _results.Average(r => r.ColdMs);
        var avgWarm = _results.Average(r => r.WarmMs);
        var worstCold = _results.MaxBy(r => r.ColdMs)!;
        var bestWarm = _results.MinBy(r => r.WarmMs)!;

        md.AppendLine();
        md.AppendLine("## Observations");
        md.AppendLine();
        md.AppendLine($"- **Average cold query**: {avgCold:F1} ms");
        md.AppendLine($"- **Average warm query**: {avgWarm:F1} ms");
        md.AppendLine($"- **Slowest cold query**: \"{worstCold.Name}\" at {worstCold.ColdMs:F1} ms");
        md.AppendLine($"- **Fastest warm query**: \"{bestWarm.Name}\" at {bestWarm.WarmMs:F1} ms");
        md.AppendLine();

        if (avgCold > 2000)
            md.AppendLine("⚠️ **Cold queries exceed 2s target** — consider additional indexes or FTS5 optimization.");
        else
            md.AppendLine("✅ **Cold queries within 2s target** — 100K baseline acceptable.");

        if (worstCold.ColdMs > 5000)
            md.AppendLine($"⚠️ **Worst-case query** (\"{worstCold.Name}\") exceeds 5s — flag for optimization in T8.3 (FTS5) or T8.2 (index audit).");

        md.AppendLine();
        md.AppendLine("## Next Steps");
        md.AppendLine();
        md.AppendLine("1. Review slow queries and add missing composite indexes (T8.2)");
        md.AppendLine("2. Evaluate FTS5 full-text search for Title/Description/Keyword searches (T8.3)");
        md.AppendLine("3. Profile thumbnail generation and gallery rendering at 100K (T8.4–T8.5)");

        // Resolve results.md relative to the project directory (scripts/benchmark/)
        var projectDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
        var resultsPath = Path.Combine(projectDir, "results.md");
        Directory.CreateDirectory(Path.GetDirectoryName(resultsPath)!);
        File.WriteAllText(resultsPath, md.ToString());
        Console.WriteLine($"\nResults written to: {resultsPath}");

        // Also print summary to console
        Console.WriteLine();
        Console.WriteLine("═══ Summary ═══");
        Console.WriteLine($"  Avg cold: {avgCold,8:F1} ms");
        Console.WriteLine($"  Avg warm: {avgWarm,8:F1} ms");
        Console.WriteLine($"  Worst cold: {worstCold.Name} ({worstCold.ColdMs:F1} ms)");
        Console.WriteLine($"  Best warm: {bestWarm.Name} ({bestWarm.WarmMs:F1} ms)");
    }

    private async Task<string> GetSqliteVersionAsync()
    {
        try
        {
            await using var db = _modeManager.CreateDbContext();
            await db.Database.OpenConnectionAsync();
            await using var cmd = db.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = "SELECT sqlite_version()";
            var result = await cmd.ExecuteScalarAsync();
            return result?.ToString() ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    public async Task CleanupAsync()
    {
        // Don't delete the database if --db-path was explicitly provided
        if (explicitDbPath is null && _modeManager is not null)
        {
            try
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                var dbDir = Path.GetDirectoryName(_dbPath);
                if (dbDir is not null && Directory.Exists(dbDir))
                    Directory.Delete(dbDir, recursive: true);
            }
            catch { /* best effort */ }
        }
    }

    private sealed record BenchmarkResult(string Name, double ColdMs, double WarmMs);
}
