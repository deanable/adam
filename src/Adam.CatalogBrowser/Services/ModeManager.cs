using Adam.Shared.Data;
using Adam.Shared.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Adam.CatalogBrowser.Services;

public sealed class ModeManager
{
    private readonly string _basePath;

    public ModeManager(string basePath, BrokerClient? broker = null, AuthSession? authSession = null)
    {
        _basePath = basePath;
        BrokerClient = broker;
        AuthSession = authSession;
    }

    public string Mode { get; private set; } = "Standalone";
    public string DbProvider { get; private set; } = "sqlite";
    public string DbPath { get; private set; } = string.Empty;
    public string? ServiceEndpoint { get; private set; }

    public BrokerClient? BrokerClient { get; }
    public AuthSession? AuthSession { get; }

    public bool IsStandalone => Mode == "Standalone";
    public bool IsMultiUser => Mode == "MultiUser";
    public bool IsConnected => BrokerClient?.IsConnected == true;
    public bool IsLoggedIn => AuthSession?.IsLoggedIn == true;

    public async Task InitializeAsync()
    {
        Mode = "Standalone";
        DbProvider = "sqlite";
        DbPath = Path.Combine(_basePath, ".adam", "catalog.db");
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);

        System.Diagnostics.Debug.WriteLine($"[adam] ModeManager DbPath: {DbPath}");
        System.Diagnostics.Debug.WriteLine($"[adam] ModeManager basePath: {_basePath}");

        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        await ApplyMigrationsAsync(db);
    }

    public async Task InitializeMultiUserAsync(string host, int port, string dbProvider = "sqlite")
    {
        Mode = "MultiUser";
        DbProvider = dbProvider;
        ServiceEndpoint = $"{host}:{port}";
        DbPath = Path.Combine(_basePath, ".adam", "catalog.db");
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);

        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        await ApplyMigrationsAsync(db);
    }

    public AppDbContext CreateDbContext()
    {
        var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={DbPath};Pooling=False");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA busy_timeout = 10000;";
        cmd.ExecuteNonQuery();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        return new AppDbContext(options);
    }

    /// <summary>
    /// Async version of <see cref="CreateDbContext"/> that opens the connection
    /// asynchronously so the calling thread is not blocked during I/O.
    /// </summary>
    public async Task<AppDbContext> CreateDbContextAsync(CancellationToken ct = default)
    {
        var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={DbPath};Pooling=False");
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA busy_timeout = 10000;";
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        return new AppDbContext(options);
    }

    private static async Task ApplyMigrationsAsync(AppDbContext db)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE DigitalAssets ADD COLUMN OriginalPath TEXT DEFAULT ''");
        }
        catch { }

        try
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE MetadataProfiles ADD COLUMN Category TEXT");
        }
        catch { }

        // Create performance indexes if they don't exist (added after initial schema creation)
        var indexCommands = new[]
        {
            "CREATE INDEX IF NOT EXISTS IX_DigitalAssets_Type ON DigitalAssets(Type)",
            "CREATE INDEX IF NOT EXISTS IX_DigitalAssets_StoragePath ON DigitalAssets(StoragePath)",
            "CREATE INDEX IF NOT EXISTS IX_DigitalAssets_CreatedAt ON DigitalAssets(CreatedAt)",
            "CREATE INDEX IF NOT EXISTS IX_DigitalAssets_MimeType ON DigitalAssets(MimeType)",
            "CREATE INDEX IF NOT EXISTS IX_DigitalAssets_FileSize ON DigitalAssets(FileSize)",
            "CREATE INDEX IF NOT EXISTS IX_DigitalAssets_FileName ON DigitalAssets(FileName)"
        };

        foreach (var cmd in indexCommands)
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync(cmd);
            }
            catch { }
        }
    }
}
