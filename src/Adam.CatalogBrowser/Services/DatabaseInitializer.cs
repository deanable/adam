using Adam.Shared.Data;
using Microsoft.EntityFrameworkCore;

namespace Adam.CatalogBrowser.Services;

public class DatabaseInitializer
{
    private readonly string _connectionString;

    public DatabaseInitializer(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connectionString)
            .Options;

        await using var context = new AppDbContext(options);
        await context.Database.MigrateAsync(ct);
    }

    public AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connectionString)
            .Options;

        return new AppDbContext(options);
    }
}
