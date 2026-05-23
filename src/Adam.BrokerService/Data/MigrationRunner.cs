using Adam.BrokerService.Configuration;
using Adam.Shared.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Adam.BrokerService.Data;

public sealed class MigrationRunner
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MigrationRunner> _logger;

    public MigrationRunner(IServiceProvider serviceProvider, ILogger<MigrationRunner> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var dbConfig = scope.ServiceProvider.GetRequiredService<DbProviderConfig>();

        _logger.LogInformation("Running database migrations...");

        try
        {
            await db.Database.EnsureCreatedAsync(ct);
            await MigrateSchemaAsync(db, dbConfig, _logger, ct);
            _logger.LogInformation("Database ready.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database migration failed");
            throw;
        }
    }

    private static async Task MigrateSchemaAsync(AppDbContext db, DbProviderConfig dbConfig, ILogger logger, CancellationToken ct)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE DigitalAssets ADD COLUMN OriginalPath TEXT DEFAULT ''", ct);
            logger.LogInformation("Applied migration: OriginalPath column on DigitalAssets");
        }
        catch
        {
            // column already exists (ok for fresh databases from EnsureCreated)
        }

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE MetadataProfiles ADD COLUMN Category TEXT", ct);
            logger.LogInformation("Applied migration: Category column on MetadataProfiles");
        }
        catch
        {
            // column already exists
        }

        try
        {
            var sql = dbConfig.Provider.ToLowerInvariant() switch
            {
                "sqlite" => """
                    CREATE TABLE IF NOT EXISTS WatchedFolders (
                        Id TEXT PRIMARY KEY,
                        Path TEXT NOT NULL UNIQUE,
                        IsEnabled INTEGER NOT NULL DEFAULT 1,
                        CreatedAt TEXT NOT NULL,
                        ModifiedAt TEXT NOT NULL
                    )
                    """,
                "postgresql" or "postgres" => """
                    CREATE TABLE IF NOT EXISTS "WatchedFolders" (
                        "Id" uuid PRIMARY KEY,
                        "Path" varchar(2000) NOT NULL UNIQUE,
                        "IsEnabled" boolean NOT NULL DEFAULT true,
                        "CreatedAt" timestamptz NOT NULL,
                        "ModifiedAt" timestamptz NOT NULL
                    )
                    """,
                "sqlserver" or "mssql" => """
                    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'WatchedFolders')
                    CREATE TABLE WatchedFolders (
                        Id uniqueidentifier PRIMARY KEY,
                        Path nvarchar(2000) NOT NULL UNIQUE,
                        IsEnabled bit NOT NULL DEFAULT 1,
                        CreatedAt datetimeoffset NOT NULL,
                        ModifiedAt datetimeoffset NOT NULL
                    )
                    """,
                _ => null
            };

            if (sql != null)
            {
                await db.Database.ExecuteSqlRawAsync(sql, ct);
                logger.LogInformation("Applied migration: WatchedFolders table");
            }
        }
        catch
        {
            // table already exists
        }
    }
}
