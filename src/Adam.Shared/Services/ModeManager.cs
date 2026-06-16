using Adam.Shared.Configuration;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.Shared.Services;

public sealed class ModeManager
{
    private readonly string _basePath;
    private readonly ILogger<ModeManager> _logger;
    private DbProviderConfig? _dbConfig;

    public ModeManager(string basePath, BrokerClient? broker = null, AuthSession? authSession = null, ILogger<ModeManager>? logger = null)
    {
        _basePath = basePath;
        BrokerClient = broker;
        AuthSession = authSession;
        _logger = logger ?? NullLogger<ModeManager>.Instance;
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

    /// <summary>Optional FTS service injected via DI for standalone mode initialization.</summary>
    public IFtsService? FtsService { get; set; }

    /// <summary>Startup profiling timing, measured by InitializeAsync (T12.12).</summary>
    public long InitElapsedMs { get; private set; }

    public async Task InitializeAsync()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        Mode = "Standalone";
        DbPath = Path.Combine(_basePath, ".adam", "catalog.db");
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);

        _dbConfig = new DbProviderConfig
        {
            Provider = "sqlite",
            ConnectionString = $"Data Source={DbPath}"
        };

        _logger.LogDebug("ModeManager DbPath: {DbPath}", DbPath);
        _logger.LogDebug("ModeManager basePath: {BasePath}", _basePath);

        await using var db = CreateDbContext();
        // Use EnsureCreatedAsync during development to auto-generate schema from model
        // without maintaining EF Core migration files for every schema change.
        // The BrokerService uses DbMigrationService for production schema management.
        await db.Database.EnsureCreatedAsync();

        // T11.5: Initialize FTS5 tables and triggers after schema creation
        if (FtsService != null)
        {
            try
            {
                await FtsService.EnsureReadyAsync();
                _logger.LogDebug("FTS5 index ready");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FTS5 init failed (non-fatal): {Message}", ex.Message);
            }
        }

        sw.Stop();
        InitElapsedMs = sw.ElapsedMilliseconds;
        _logger.LogInformation("ModeManager.InitializeAsync completed in {ElapsedMs}ms", InitElapsedMs);
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
        await db.Database.EnsureCreatedAsync();
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
