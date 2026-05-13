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

        _logger.LogInformation("Running database migrations...");

        try
        {
            await db.Database.EnsureCreatedAsync(ct);
            await MigrateSchemaAsync(db, _logger, ct);
            _logger.LogInformation("Database ready.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database migration failed");
            throw;
        }
    }

    private static async Task MigrateSchemaAsync(AppDbContext db, ILogger logger, CancellationToken ct)
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
    }
}
