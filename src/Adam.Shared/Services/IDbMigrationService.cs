namespace Adam.Shared.Services;

public class MigrationProgressEventArgs : EventArgs
{
    public string Table { get; init; } = string.Empty;
    public int RowsMigrated { get; init; }
    public int TotalRows { get; init; }
    public string Message { get; init; } = string.Empty;
}

public interface IDbMigrationService
{
    event EventHandler<MigrationProgressEventArgs>? Progress;
    Task MigrateAsync(string sourceConnectionString, string targetProvider, string targetConnectionString, CancellationToken ct = default);
}
