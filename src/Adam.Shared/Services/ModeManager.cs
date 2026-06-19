using Adam.Shared.Configuration;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.Shared.Services;

public sealed class ModeManager
{
    private readonly string _basePath;
    private readonly ILogger<ModeManager> _logger;
    private DbProviderConfig? _dbConfig;

    public ModeManager(string basePath, BrokerClient? broker = null, AuthSession? authSession = null, ILogger<ModeManager>? logger = null)
    {
        _basePath = basePath;
        BrokerClient = broker;
        AuthSession = authSession;
        _logger = logger ?? NullLogger<ModeManager>.Instance;
    }

    public string Mode { get; private set; } = "Standalone";
    public string DbProvider => _dbConfig?.Provider ?? "sqlite";
    public string DbPath { get; private set; } = string.Empty;
    public string? ServiceEndpoint { get; private set; }

    public BrokerClient? BrokerClient { get; }
    public AuthSession? AuthSession { get; }

    public bool IsStandalone => Mode == "Standalone";
    public bool IsMultiUser => Mode == "MultiUser";
    public bool IsConnected => BrokerClient?.IsConnected == true;
    public bool IsLoggedIn => AuthSession?.IsLoggedIn == true;

    /// <summary>Optional FTS service injected via DI for standalone mode initialization.</summary>
    public IFtsService? FtsService { get; set; }

    /// <summary>Startup profiling timing, measured by InitializeAsync (T12.12).</summary>
    public long InitElapsedMs { get; private set; }

    public async Task InitializeAsync()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        Mode = "Standalone";
        DbPath = Path.Combine(_basePath, ".adam", "catalog.db");
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);

        _dbConfig = new DbProviderConfig
        {
            Provider = "sqlite",
            ConnectionString = $"Data Source={DbPath}"
        };

        _logger.LogDebug("ModeManager DbPath: {DbPath}", DbPath);
        _logger.LogDebug("ModeManager basePath: {BasePath}", _basePath);

        await using var db = CreateDbContext();

        // Apply pending EF Core migrations first if the __EFMigrationsHistory table
        // exists (i.e., a previous run initialized the migration pipeline).
        // If it doesn't exist yet, fall through to EnsureCreatedAsync which creates
        // the full schema from the model directly.
        var migrationHistoryExists = await MigrationHistoryTableExistsAsync(db);
        if (migrationHistoryExists)
        {
            var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
            if (pending.Count > 0)
            {
                _logger.LogInformation("Applying {Count} pending EF Core migration(s)...", pending.Count);
                await db.Database.MigrateAsync();
            }
        }
        else
        {
            _logger.LogDebug("No EF Core migration history found — using EnsureCreatedAsync to bootstrap schema.");

            // Use EnsureCreatedAsync during development to auto-generate schema from model
            // without maintaining EF Core migration files for every schema change.
            // The BrokerService uses DbMigrationService for production schema management.
            var wasCreated = await db.Database.EnsureCreatedAsync();

            // If the database already existed (wasCreated == false), EnsureCreatedAsync
            // does NOT add columns that were added to the model after the DB was created.
            // Manually add the SortOrder column and its indexes for existing databases.
            // This is safe to run repeatedly — SQLite's IF NOT EXISTS semantics on ALTER
            // TABLE ADD COLUMN only exist in custom SQL via PRAGMA or exception handling.
            if (!wasCreated)
            {
                await AddMissingColumnsAsync(db);
            }

            // Seed the __EFMigrationsHistory table so that future migration-based
            // deployment (dotnet ef database update) knows the current state.
            await SeedMigrationHistoryAsync(db);
        }

        // T11.5: Initialize FTS5 tables and triggers after schema creation
        if (FtsService != null)
        {
            try
            {
                await FtsService.EnsureReadyAsync();
                _logger.LogDebug("FTS5 index ready");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FTS5 init failed (non-fatal): {Message}", ex.Message);
            }
        }

        sw.Stop();
        InitElapsedMs = sw.ElapsedMilliseconds;
        _logger.LogInformation("ModeManager.InitializeAsync completed in {ElapsedMs}ms", InitElapsedMs);
    }

    /// <summary>
    /// Adds columns and indexes that were added to the EF Core model after the database
    /// was initially created. Since standalone mode uses <c>EnsureCreatedAsync</c> (which
    /// does NOT alter existing tables), these schema additions are applied via raw SQL.
    /// </summary>
    private static async Task AddMissingColumnsAsync(AppDbContext db)
    {
        // SQLite does not support IF NOT EXISTS for ALTER TABLE ADD COLUMN.
        // Catch the "duplicate column" exception and ignore it.
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"DigitalAssets\" ADD COLUMN \"SortOrder\" INTEGER NOT NULL DEFAULT 0;");
        }
        catch (Exception ex) when (ex.Message.Contains("duplicate column"))
        {
            // Column already exists — no action needed
        }

        // Create indexes for SortOrder (IF NOT EXISTS syntax works for CREATE INDEX)
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "CREATE INDEX IF NOT EXISTS \"IX_DigitalAssets_SortOrder\" ON \"DigitalAssets\" (\"SortOrder\");");
        }
        catch
        {
            // Index creation failure is non-fatal
        }

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "CREATE INDEX IF NOT EXISTS \"IX_DigitalAssets_CollectionId_SortOrder\" ON \"DigitalAssets\" (\"CollectionId\", \"SortOrder\");");
        }
        catch
        {
            // Index creation failure is non-fatal
        }
    }

    /// <summary>
    /// Checks whether the <c>__EFMigrationsHistory</c> table exists in the database.
    /// Used to determine whether to use <c>MigrateAsync()</c> (migration-tracked DB)
    /// or <c>EnsureCreatedAsync()</c> (fresh/bootstrap DB).
    /// Note: <c>ExecuteSqlRawAsync</c> returns rows *affected* (always -1 for SELECT),
    /// not the query result, so we rely on catching the "no such table" exception instead.
    /// </summary>
    private static async Task<bool> MigrationHistoryTableExistsAsync(AppDbContext db)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "SELECT 1 FROM \"__EFMigrationsHistory\" LIMIT 1");
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Seeds the <c>__EFMigrationsHistory</c> table with all migrations known to the
    /// assembly so that future <c>MigrateAsync()</c> calls only apply new migrations.
    /// This is needed because <c>EnsureCreatedAsync()</c> creates the schema directly
    /// without populating the migration history.
    /// </summary>
    private static async Task SeedMigrationHistoryAsync(AppDbContext db)
    {
        try
        {
            var allMigrations = db.Database.GetMigrations().ToList();
            if (allMigrations.Count == 0)
                return;

            // Create __EFMigrationsHistory table if it doesn't exist
            await db.Database.ExecuteSqlRawAsync(
                "CREATE TABLE IF NOT EXISTS \"__EFMigrationsHistory\" (" +
                "\"MigrationId\" TEXT NOT NULL, " +
                "\"ProductVersion\" TEXT NOT NULL);");

            await db.Database.ExecuteSqlRawAsync(
                "CREATE UNIQUE INDEX IF NOT EXISTS \"IX___EFMigrationsHistory_MigrationId\" " +
                "ON \"__EFMigrationsHistory\" (\"MigrationId\");");

            // Insert each migration as already applied
            foreach (var migrationId in allMigrations)
            {
                await db.Database.ExecuteSqlRawAsync(
                    $"INSERT OR IGNORE INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") " +
                    $"VALUES ({{0}}, '10.0.9');",
                    migrationId);
            }
        }
        catch
        {
            // Non-fatal — schema is already correct from EnsureCreatedAsync.
            // Migration history seeding failure just means future migrations
            // will need to handle the fallback path.
        }
    }

    public async Task InitializeMultiUserAsync(string host, int port, string dbProvider = "sqlite", string? connectionString = null)
    {
        Mode = "MultiUser";
        ServiceEndpoint = $"{host}:{port}";
        DbPath = Path.Combine(_basePath, ".adam", "catalog.db");
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);

        _dbConfig = new DbProviderConfig
        {
            Provider = dbProvider,
            ConnectionString = connectionString ?? $"Data Source={DbPath}"
        };

        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
    }

    public AppDbContext CreateDbContext()
    {
        if (_dbConfig == null)
            throw new InvalidOperationException("ModeManager not initialized. Call InitializeAsync or InitializeMultiUserAsync first.");

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        _dbConfig.Configure(optionsBuilder);
        // EF Core 10 promotes PendingModelChangesWarning to an exception by default.
        // `dotnet ef migrations has-pending-model-changes` confirms there are none;
        // the warning is a false-positive from the computed Permissions property being
        // Ignored in OnModelCreating. Log it instead of throwing.
        optionsBuilder.ConfigureWarnings(w =>
            w.Log(RelationalEventId.PendingModelChangesWarning));

        return new AppDbContext(optionsBuilder.Options);
    }

    /// <summary>
    /// Async version of <see cref="CreateDbContext"/> that opens the connection
    /// asynchronously so the calling thread is not blocked during I/O.
    /// </summary>
    public async Task<AppDbContext> CreateDbContextAsync(CancellationToken ct = default)
    {
        if (_dbConfig == null)
            throw new InvalidOperationException("ModeManager not initialized. Call InitializeAsync or InitializeMultiUserAsync first.");

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        _dbConfig.Configure(optionsBuilder);

        var db = new AppDbContext(optionsBuilder.Options);
        
        // Ensure the connection is actually opened asynchronously if it's not already
        await db.Database.OpenConnectionAsync(ct).ConfigureAwait(false);
        
        return db;
    }

}
