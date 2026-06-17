using System.Data.Common;
using Adam.Shared.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Adam.Shared.Services;

/// <summary>
/// Base class for all FTS provider implementations.
/// Provides common helper methods shared across providers.
/// </summary>
public abstract class FtsServiceBase
{
    protected readonly IDbContextFactory<AppDbContext> DbFactory;
    protected readonly ILogger Logger;

    protected FtsServiceBase(IDbContextFactory<AppDbContext> dbFactory, ILogger logger)
    {
        DbFactory = dbFactory;
        Logger = logger;
    }

    /// <summary>
    /// Checks whether the given text contains any of the search terms (case-insensitive).
    /// </summary>
    protected static bool MatchesAny(string? text, string[] terms)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var lower = text.ToLowerInvariant();
        return terms.Any(t => lower.Contains(t.ToLowerInvariant()));
    }

    /// <summary>
    /// Creates and adds a parameter to a DbCommand.
    /// </summary>
    protected static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    /// <summary>
    /// Executes a non-query SQL command against the given connection.
    /// </summary>
    protected static async Task ExecAsync(DbConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
