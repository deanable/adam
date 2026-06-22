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
            await db.Database.EnsureCreatedAsync();

            // Seed the __EFMigrationsHistory table so that future migration-based
            // deployment (dotnet ef database update) knows the current state.
            await SeedMigrationHistoryAsync(db);
        }

        // Always patch missing columns, regardless of which code path was taken.
        // The AddMissingColumnsAsync method handles "duplicate column" gracefully
        // and uses CREATE TABLE IF NOT EXISTS / CREATE INDEX IF NOT EXISTS for
        // tables and indexes, so it is safe to run every startup.
        await AddMissingColumnsAsync(db);

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
    /// Adds columns, tables, and indexes that were added to the EF Core model after
    /// the database was initially created. Since standalone mode uses <c>EnsureCreatedAsync</c>
    /// (which does NOT alter existing tables), these schema additions are applied via raw SQL.
    /// </summary>
    private async Task AddMissingColumnsAsync(AppDbContext db)
    {
        await AddColumnIfMissingAsync(db, "DigitalAssets", "\"SortOrder\" INTEGER NOT NULL DEFAULT 0");
        await AddColumnIfMissingAsync(db, "DigitalAssets", "\"Orientation\" INTEGER NOT NULL DEFAULT 0");
        await AddColumnIfMissingAsync(db, "Collections", "\"IsSmart\" INTEGER NOT NULL DEFAULT 0");
        await AddColumnIfMissingAsync(db, "Collections", "\"SmartQueryJson\" TEXT NULL");
        await AddColumnIfMissingAsync(db, "Collections", "\"LastAutoRefreshedAt\" TEXT NULL");
        await AddColumnIfMissingAsync(db, "Keywords", "\"IsAiGenerated\" INTEGER NOT NULL DEFAULT 0");
        await AddColumnIfMissingAsync(db, "Categories", "\"IsAiGenerated\" INTEGER NOT NULL DEFAULT 0");

        // Post-creation columns for tables that may already exist from a previous run
        await AddColumnIfMissingAsync(db, "AssetEmbeddings", "\"ComputedAt\" TEXT NOT NULL DEFAULT '1970-01-01 00:00:00+00:00'");
        await AddColumnIfMissingAsync(db, "SearchClickLogs", "\"DwellTimeMs\" INTEGER NOT NULL DEFAULT 0");
        await AddColumnIfMissingAsync(db, "SearchClickLogs", "\"RankPosition\" INTEGER NOT NULL DEFAULT 0");

        // Ensure indexes exist (IF NOT EXISTS is safe for CREATE INDEX)
        await TryExecuteAsync(db, "CREATE INDEX IF NOT EXISTS \"IX_DigitalAssets_SortOrder\" ON \"DigitalAssets\" (\"SortOrder\");");
        await TryExecuteAsync(db, "CREATE INDEX IF NOT EXISTS \"IX_DigitalAssets_CollectionId_SortOrder\" ON \"DigitalAssets\" (\"CollectionId\", \"SortOrder\");");

        // Create the UserPreferences table and index
        await TryExecuteAsync(db, @"
CREATE TABLE IF NOT EXISTS ""UserPreferences"" (
    ""Id"" TEXT NOT NULL,
    ""UserId"" TEXT NULL,
    ""Key"" TEXT NOT NULL,
    ""ValueJson"" TEXT NOT NULL,
    ""UpdatedAt"" TEXT NOT NULL,
    ""Version"" INTEGER NOT NULL,
    PRIMARY KEY (""Id"")
);");
        await TryExecuteAsync(db, @"
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_UserPreferences_UserId_Key""
    ON ""UserPreferences"" (""UserId"", ""Key"");");

        // Create the Persons table (Phase 23: facial recognition)
        await TryExecuteAsync(db, @"
CREATE TABLE IF NOT EXISTS ""Persons"" (
    ""Id"" TEXT NOT NULL,
    ""Name"" TEXT NOT NULL,
    ""Notes"" TEXT NULL,
    ""ThumbnailImage"" BLOB NULL,
    ""CentroidEmbedding"" BLOB NULL,
    ""EmbeddingModelVersion"" TEXT NULL,
    ""CreatedAt"" TEXT NOT NULL,
    ""ModifiedAt"" TEXT NOT NULL,
    PRIMARY KEY (""Id"")
);");
        await TryExecuteAsync(db, "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Persons_Name\" ON \"Persons\" (\"Name\");");
        await TryExecuteAsync(db, "CREATE INDEX IF NOT EXISTS \"IX_Persons_CreatedAt\" ON \"Persons\" (\"CreatedAt\");");

        // Create the AssetFaces table (Phase 23: facial recognition)
        await TryExecuteAsync(db, @"
CREATE TABLE IF NOT EXISTS ""AssetFaces"" (
    ""Id"" TEXT NOT NULL,
    ""AssetId"" TEXT NOT NULL,
    ""FaceEmbedding"" BLOB NOT NULL,
    ""BoundingBoxJson"" TEXT NOT NULL,
    ""ThumbnailImage"" BLOB NULL,
    ""PersonId"" TEXT NULL,
    ""DetectionConfidence"" REAL NOT NULL DEFAULT 0.0,
    ""MatchingConfidence"" REAL NOT NULL DEFAULT 0.0,
    ""IsAutoAssigned"" INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (""Id""),
    FOREIGN KEY (""AssetId"") REFERENCES ""DigitalAssets""(""Id"") ON DELETE CASCADE,
    FOREIGN KEY (""PersonId"") REFERENCES ""Persons""(""Id"") ON DELETE SET NULL
);");
        await TryExecuteAsync(db, "CREATE INDEX IF NOT EXISTS \"IX_AssetFaces_AssetId\" ON \"AssetFaces\" (\"AssetId\");");
        await TryExecuteAsync(db, "CREATE INDEX IF NOT EXISTS \"IX_AssetFaces_PersonId\" ON \"AssetFaces\" (\"PersonId\");");
        await TryExecuteAsync(db, "CREATE INDEX IF NOT EXISTS \"IX_AssetFaces_DetectionConfidence\" ON \"AssetFaces\" (\"DetectionConfidence\");");

        // Create the AssetEmbeddings table (Phase 19: semantic search) if missing
        await TryExecuteAsync(db, @"
CREATE TABLE IF NOT EXISTS ""AssetEmbeddings"" (
    ""Id"" TEXT NOT NULL,
    ""AssetId"" TEXT NOT NULL,
    ""TextEmbedding"" BLOB NOT NULL,
    ""ImageEmbedding"" BLOB NULL,
    ""ModelVersion"" TEXT NOT NULL,
    ""ComputedAt"" TEXT NOT NULL,
    PRIMARY KEY (""Id""),
    FOREIGN KEY (""AssetId"") REFERENCES ""DigitalAssets""(""Id"") ON DELETE CASCADE
);");
        await TryExecuteAsync(db, "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_AssetEmbeddings_AssetId\" ON \"AssetEmbeddings\" (\"AssetId\");");

        // Create the SearchClickLog table (Phase 22: search ranking) if missing
        await TryExecuteAsync(db, @"
CREATE TABLE IF NOT EXISTS ""SearchClickLogs"" (
    ""Id"" TEXT NOT NULL,
    ""AssetId"" TEXT NOT NULL,
    ""UserId"" TEXT NULL,
    ""QueryText"" TEXT NOT NULL,
    ""NormalizedQuery"" TEXT NULL,
    ""ClickedAt"" TEXT NOT NULL,
    ""DwellTimeMs"" INTEGER NOT NULL DEFAULT 0,
    ""RankPosition"" INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (""Id""),
    FOREIGN KEY (""AssetId"") REFERENCES ""DigitalAssets""(""Id"") ON DELETE CASCADE,
    FOREIGN KEY (""UserId"") REFERENCES ""Users""(""Id"") ON DELETE SET NULL
);");
        await TryExecuteAsync(db, "CREATE INDEX IF NOT EXISTS \"IX_SearchClickLogs_NormalizedQuery_ClickedAt\" ON \"SearchClickLogs\" (\"NormalizedQuery\", \"ClickedAt\");");
        await TryExecuteAsync(db, "CREATE INDEX IF NOT EXISTS \"IX_SearchClickLogs_AssetId_NormalizedQuery\" ON \"SearchClickLogs\" (\"AssetId\", \"NormalizedQuery\");");

        // Create the SavedSearches table (Phase 22: search/saved searches)
        await TryExecuteAsync(db, @"
CREATE TABLE IF NOT EXISTS ""SavedSearches"" (
    ""Id"" TEXT NOT NULL,
    ""Name"" TEXT NOT NULL,
    ""QueryText"" TEXT NULL,
    ""FiltersJson"" TEXT NOT NULL,
    ""IsPinned"" INTEGER NOT NULL DEFAULT 0,
    ""UserId"" TEXT NULL,
    ""CreatedAt"" TEXT NOT NULL,
    ""ModifiedAt"" TEXT NOT NULL,
    PRIMARY KEY (""Id""),
    FOREIGN KEY (""UserId"") REFERENCES ""Users""(""Id"") ON DELETE SET NULL
);");
        await TryExecuteAsync(db, "CREATE INDEX IF NOT EXISTS \"IX_SavedSearches_UserId\" ON \"SavedSearches\" (\"UserId\");");
        await TryExecuteAsync(db, "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_SavedSearches_UserId_Name\" ON \"SavedSearches\" (\"UserId\", \"Name\");");

        // Create the SearchHistoryEntries table (Phase 22: search history)
        await TryExecuteAsync(db, @"
CREATE TABLE IF NOT EXISTS ""SearchHistoryEntries"" (
    ""Id"" TEXT NOT NULL,
    ""QueryText"" TEXT NOT NULL,
    ""FiltersJson"" TEXT NOT NULL,
    ""IsSemantic"" INTEGER NOT NULL DEFAULT 0,
    ""ExecutedAt"" TEXT NOT NULL,
    ""UserId"" TEXT NULL,
    PRIMARY KEY (""Id""),
    FOREIGN KEY (""UserId"") REFERENCES ""Users""(""Id"") ON DELETE SET NULL
);");
        await TryExecuteAsync(db, "CREATE INDEX IF NOT EXISTS \"IX_SearchHistoryEntries_UserId\" ON \"SearchHistoryEntries\" (\"UserId\");");
        await TryExecuteAsync(db, "CREATE INDEX IF NOT EXISTS \"IX_SearchHistoryEntries_ExecutedAt\" ON \"SearchHistoryEntries\" (\"ExecutedAt\");");

        // Create the Comments table (Phase 22: asset commenting)
        await TryExecuteAsync(db, @"
CREATE TABLE IF NOT EXISTS ""Comments"" (
    ""Id"" TEXT NOT NULL,
    ""AssetId"" TEXT NOT NULL,
    ""ParentCommentId"" TEXT NULL,
    ""UserId"" TEXT NOT NULL,
    ""Body"" TEXT NOT NULL,
    ""CreatedAt"" TEXT NOT NULL,
    ""EditedAt"" TEXT NULL,
    ""IsDeleted"" INTEGER NOT NULL DEFAULT 0,
    ""Version"" INTEGER NOT NULL DEFAULT 1,
    PRIMARY KEY (""Id""),
    FOREIGN KEY (""AssetId"") REFERENCES ""DigitalAssets""(""Id"") ON DELETE CASCADE,
    FOREIGN KEY (""ParentCommentId"") REFERENCES ""Comments""(""Id"") ON DELETE RESTRICT,
    FOREIGN KEY (""UserId"") REFERENCES ""Users""(""Id"") ON DELETE RESTRICT
);");
        await TryExecuteAsync(db, "CREATE INDEX IF NOT EXISTS \"IX_Comments_AssetId\" ON \"Comments\" (\"AssetId\");");
        await TryExecuteAsync(db, "CREATE INDEX IF NOT EXISTS \"IX_Comments_CreatedAt\" ON \"Comments\" (\"CreatedAt\");");
        await TryExecuteAsync(db, "CREATE INDEX IF NOT EXISTS \"IX_Comments_ParentCommentId\" ON \"Comments\" (\"ParentCommentId\");");
        await TryExecuteAsync(db, "CREATE INDEX IF NOT EXISTS \"IX_Comments_UserId\" ON \"Comments\" (\"UserId\");");

        _logger.LogInformation("Missing schema additions applied successfully");
    }

    /// <summary>
    /// Adds a column to a SQLite table if it doesn't already exist.
    /// SQLite does not support IF NOT EXISTS for ALTER TABLE ADD COLUMN,
    /// so we catch the "duplicate column" exception.
    /// </summary>
    private static async Task AddColumnIfMissingAsync(AppDbContext db, string table, string columnDef)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                $"ALTER TABLE \"{table}\" ADD COLUMN {columnDef};");
        }
        catch (Exception ex) when (ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase))
        {
            // Column already exists — no action needed
        }
    }

    /// <summary>
    /// Executes a raw SQL command and logs non-fatal warnings on failure.
    /// </summary>
    private async Task TryExecuteAsync(AppDbContext db, string sql)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(sql);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Schema migration SQL failed (non-fatal): {Sql}", sql);
        }
    }

    /// <summary>
    /// Checks whether the <c>__EFMigrationsHistory</c> table exists in the database.
    /// Used to determine whether to use <c>MigrateAsync()</c> (migration-tracked DB)
    /// or <c>EnsureCreatedAsync()</c> (fresh/bootstrap DB).
    /// Note: <c>ExecuteSqlRawAsync</c> returns rows *affected* (always -1 for SELECT),
    /// not the query result, so we rely on catching the "no such table" exception instead.
    /// </summary>
    private async Task<bool> MigrationHistoryTableExistsAsync(AppDbContext db)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "SELECT 1 FROM \"__EFMigrationsHistory\" LIMIT 1");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Migration history table does not exist yet (expected on fresh database)");
            return false;
        }
    }

    /// <summary>
    /// Seeds the <c>__EFMigrationsHistory</c> table with all migrations known to the
    /// assembly so that future <c>MigrateAsync()</c> calls only apply new migrations.
    /// This is needed because <c>EnsureCreatedAsync()</c> creates the schema directly
    /// without populating the migration history.
    /// </summary>
    private async Task SeedMigrationHistoryAsync(AppDbContext db)
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
        catch (Exception ex)
        {
            // Non-fatal — schema is already correct from EnsureCreatedAsync.
            // Migration history seeding failure just means future migrations
            // will need to handle the fallback path.
            _logger.LogWarning(ex, "Failed to seed migration history (non-fatal)");
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
