using Adam.Shared.Data;
using Adam.Shared.Models;
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

        using var db = CreateDbContext();
        db.Database.EnsureCreated();
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
    }

    public AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={DbPath}")
            .Options;
        return new AppDbContext(options);
    }
}
