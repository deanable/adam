using Adam.Shared.Data;
using Adam.Shared.Models;
using Adam.Shared.Services;
using Microsoft.EntityFrameworkCore;

namespace Adam.BrokerService.Services;

public sealed class DbMigrationService : IDbMigrationService
{
    public event EventHandler<MigrationProgressEventArgs>? Progress;

    public async Task MigrateAsync(string sourceConnectionString, string targetProvider, string targetConnectionString, CancellationToken ct = default)
    {
        var sourceOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(sourceConnectionString)
            .Options;

        DbContextOptions<AppDbContext> targetOptions;
        switch (targetProvider.ToLowerInvariant())
        {
            case "postgresql":
            case "postgres":
                targetOptions = new DbContextOptionsBuilder<AppDbContext>()
                    .UseNpgsql(targetConnectionString)
                    .Options;
                break;
            case "sqlserver":
            case "mssql":
                targetOptions = new DbContextOptionsBuilder<AppDbContext>()
                    .UseSqlServer(targetConnectionString)
                    .Options;
                break;
            default:
                targetOptions = new DbContextOptionsBuilder<AppDbContext>()
                    .UseSqlite(targetConnectionString)
                    .Options;
                break;
        }

        await using var source = new AppDbContext(sourceOptions);
        await using var target = new AppDbContext(targetOptions);

        await target.Database.EnsureCreatedAsync(ct);

        await MigrateTableAsync<Collection>(source, target, ct);
        await MigrateTableAsync<MetadataProfile>(source, target, ct);
        await MigrateTableAsync<RatingInfo>(source, target, ct);
        await MigrateTableAsync<Keyword>(source, target, ct);
        await MigrateTableAsync<DigitalAsset>(source, target, ct);

        if (targetProvider.ToLowerInvariant() != "sqlite")
        {
            await MigrateTableAsync<User>(source, target, ct);
            await MigrateTableAsync<Role>(source, target, ct);
            await MigrateTableAsync<AccessLog>(source, target, ct);
            await MigrateTableAsync<ModeConfiguration>(source, target, ct);
        }
    }

    private async Task MigrateTableAsync<T>(AppDbContext source, AppDbContext target, CancellationToken ct) where T : class
    {
        var tableName = typeof(T).Name;
        Report(tableName, 0, 0, $"Reading {tableName}...");

        var items = await source.Set<T>().IgnoreQueryFilters().ToListAsync(ct);
        if (items.Count == 0)
        {
            Report(tableName, 0, 0, $"No {tableName} to migrate.");
            return;
        }

        Report(tableName, 0, items.Count, $"Migrating {items.Count} {tableName} record(s)...");
        target.Set<T>().AddRange(items);
        await target.SaveChangesAsync(ct);
        Report(tableName, items.Count, items.Count, $"Migrated {items.Count} {tableName}.");
    }

    private void Report(string table, int rows, int total, string message)
    {
        Progress?.Invoke(this, new MigrationProgressEventArgs
        {
            Table = table,
            RowsMigrated = rows,
            TotalRows = total,
            Message = message
        });
    }
}
