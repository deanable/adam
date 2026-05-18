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

    public void Initialize()
    {
        Mode = "Standalone";
        DbProvider = "sqlite";
        DbPath = Path.Combine(_basePath, ".adam", "catalog.db");
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);

        System.Diagnostics.Debug.WriteLine($"[adam] ModeManager DbPath: {DbPath}");
        System.Diagnostics.Debug.WriteLine($"[adam] ModeManager basePath: {_basePath}");

        using var db = CreateDbContext();
        db.Database.EnsureCreated();
        ApplyMigrations(db);
    }

    public void InitializeMultiUser(string host, int port, string dbProvider = "sqlite")
    {
        Mode = "MultiUser";
        DbProvider = dbProvider;
        ServiceEndpoint = $"{host}:{port}";
        DbPath = Path.Combine(_basePath, ".adam", "catalog.db");
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);

        using var db = CreateDbContext();
        db.Database.EnsureCreated();
        ApplyMigrations(db);
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

    private static void ApplyMigrations(AppDbContext db)
    {
        try
        {
            db.Database.ExecuteSqlRaw("ALTER TABLE DigitalAssets ADD COLUMN OriginalPath TEXT DEFAULT ''");
        }
        catch { }

        try
        {
            db.Database.ExecuteSqlRaw("ALTER TABLE MetadataProfiles ADD COLUMN Category TEXT");
        }
        catch { }
    }
}
