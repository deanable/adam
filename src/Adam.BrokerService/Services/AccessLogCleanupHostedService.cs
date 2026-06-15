using Adam.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Adam.BrokerService.Services;

/// <summary>
/// Hosted service that runs AccessLog pruning on startup and every 24 hours (T15.3).
/// </summary>
public sealed class AccessLogCleanupHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AccessLogCleanupHostedService> _logger;

    public AccessLogCleanupHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<AccessLogCleanupHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Run once on startup
        await RunPruneAsync(stoppingToken);

        // Run periodically every 24 hours
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunPruneAsync(stoppingToken);
        }
    }

    private async Task RunPruneAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var cleanupService = scope.ServiceProvider.GetRequiredService<AccessLogCleanupService>();
            var deleted = await cleanupService.PruneAsync(ct: ct);
            _logger.LogInformation("AccessLog cleanup completed: {Count} entries deleted", deleted);
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AccessLog cleanup failed");
        }
    }
}
