using Adam.Shared.Configuration;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Adam.Shared.Services;

public sealed class ModeManager
{
    private readonly string _basePath;
    private DbProviderConfig? _dbConfig;

    public ModeManager(string basePath, BrokerClient? broker = null, AuthSession? authSession = null)
    {
        _basePath = basePath;
        BrokerClient = broker;
        AuthSession = authSession;
    }

    public string Mode { get; private set; } = "Standalone";
    public string DbProvider => _dbConfig?.Provider ?? "sqlite";
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
        DbPath = Path.Combine(_basePath, ".adam", "catalog.db");
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);

        _dbConfig = new DbProviderConfig
        {
            Provider = "sqlite",
            ConnectionString = $"Data Source={DbPath}"
        };

        System.Diagnostics.Debug.WriteLine($"[adam] ModeManager DbPath: {DbPath}");
        System.Diagnostics.Debug.WriteLine($"[adam] ModeManager basePath: {_basePath}");

        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public async Task InitializeMultiUserAsync(string host, int port, string dbProvider = "sqlite", string? connectionString = null)
    {
        Mode = "MultiUser";
        ServiceEndpoint = $"{host}:{port}";
        DbPath = Path.Combine(_basePath, ".adam", "catalog.db");
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);

        _dbConfig = new DbProviderConfig
        {
            Provider = dbProvider,
            ConnectionString = connectionString ?? $"Data Source={DbPath}"
        };

        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public AppDbContext CreateDbContext()
    {
        if (_dbConfig == null)
            throw new InvalidOperationException("ModeManager not initialized. Call InitializeAsync or InitializeMultiUserAsync first.");

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        _dbConfig.Configure(optionsBuilder);
        // EF Core 10 promotes PendingModelChangesWarning to an exception by default.
        // `dotnet ef migrations has-pending-model-changes` confirms there are none;
        // the warning is a false-positive from the computed Permissions property being
        // Ignored in OnModelCreating. Log it instead of throwing.
        optionsBuilder.ConfigureWarnings(w =>
            w.Log(RelationalEventId.PendingModelChangesWarning));

        return new AppDbContext(optionsBuilder.Options);
    }

    /// <summary>
    /// Async version of <see cref="CreateDbContext"/> that opens the connection
    /// asynchronously so the calling thread is not blocked during I/O.
    /// </summary>
    public async Task<AppDbContext> CreateDbContextAsync(CancellationToken ct = default)
    {
        if (_dbConfig == null)
            throw new InvalidOperationException("ModeManager not initialized. Call InitializeAsync or InitializeMultiUserAsync first.");

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        _dbConfig.Configure(optionsBuilder);

        var db = new AppDbContext(optionsBuilder.Options);
        
        // Ensure the connection is actually opened asynchronously if it's not already
        await db.Database.OpenConnectionAsync(ct).ConfigureAwait(false);
        
        return db;
    }

}
