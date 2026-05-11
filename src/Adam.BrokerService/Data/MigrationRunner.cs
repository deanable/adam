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
            _logger.LogInformation("Database ready.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database migration failed");
            throw;
        }
    }
}
