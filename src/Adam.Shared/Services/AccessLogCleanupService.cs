using Adam.Shared.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.Shared.Services;

/// <summary>
/// Periodically prunes <see cref="Models.AccessLog"/> entries older than a configurable retention period (T15.3).
/// </summary>
public class AccessLogCleanupService
{
    private readonly ModeManager _modeManager;
    private readonly IConfiguration? _configuration;
    private readonly ILogger<AccessLogCleanupService> _logger;

    public AccessLogCleanupService(
        ModeManager modeManager,
        IConfiguration? configuration = null,
        ILogger<AccessLogCleanupService>? logger = null)
    {
        _modeManager = modeManager;
        _configuration = configuration;
        _logger = logger ?? NullLogger<AccessLogCleanupService>.Instance;
    }

    /// <summary>
    /// Gets the configured retention days from appsettings.json (default 30).
    /// A value of 0 disables pruning.
    /// </summary>
    public int GetRetentionDays()
    {
        var retention = _configuration?.GetSection("AccessLog")?["RetentionDays"];
        if (int.TryParse(retention, out var days) && days >= 0)
            return days;
        return 30; // default
    }

    /// <summary>
    /// Prunes access log entries older than <paramref name="retentionDays"/> days.
    /// If <paramref name="retentionDays"/> is 0, pruning is disabled.
    /// </summary>
    /// <returns>The number of deleted entries.</returns>
    public async Task<int> PruneAsync(int? retentionDays = null, CancellationToken ct = default)
    {
        var retention = retentionDays ?? GetRetentionDays();
        if (retention <= 0)
        {
            _logger.LogInformation("AccessLog pruning is disabled (RetentionDays={Retention})", retention);
            return 0;
        }

        await using var db = await _modeManager.CreateDbContextAsync(ct).ConfigureAwait(false);

        // SQLite EF Core does not support DateTimeOffset comparison operators (<, >)
        // in Where clauses. To support all database providers, load entries into memory
        // and filter client-side. On PostgreSQL/SQL Server, the native comparison works.
        var cutoff = DateTimeOffset.UtcNow.AddDays(-retention);
        var allLogs = await db.AccessLogs.ToListAsync(ct).ConfigureAwait(false);
        var oldEntries = allLogs.Where(l => l.Timestamp < cutoff).ToList();

        if (oldEntries.Count == 0)
        {
            _logger.LogInformation("No access log entries to prune (retention={Retention} days)", retention);
            return 0;
        }

        db.AccessLogs.RemoveRange(oldEntries);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Pruned {Count} access log entries older than {Retention} days",
            oldEntries.Count, retention);

        return oldEntries.Count;
    }
}
