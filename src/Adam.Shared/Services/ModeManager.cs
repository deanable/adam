using Adam.Shared.Configuration;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Microsoft.EntityFrameworkCore;

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
        await db.Database.EnsureCreatedAsync();
        await ApplyMigrationsAsync(db);
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
        await ApplyMigrationsAsync(db);
    }

    public AppDbContext CreateDbContext()
    {
        if (_dbConfig == null)
            throw new InvalidOperationException("ModeManager not initialized. Call InitializeAsync or InitializeMultiUserAsync first.");

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        _dbConfig.Configure(optionsBuilder);

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

        // Phase 4 metadata columns on DigitalAssets
        var phase4Columns = new[]
        {
            "ALTER TABLE DigitalAssets ADD COLUMN Copyright TEXT",
            "ALTER TABLE DigitalAssets ADD COLUMN Rating INTEGER DEFAULT 0",
            "ALTER TABLE DigitalAssets ADD COLUMN Label INTEGER DEFAULT 0",
            "ALTER TABLE DigitalAssets ADD COLUMN Flag INTEGER DEFAULT 0",
            "ALTER TABLE DigitalAssets ADD COLUMN GpsLatitude REAL",
            "ALTER TABLE DigitalAssets ADD COLUMN GpsLongitude REAL",
            "ALTER TABLE DigitalAssets ADD COLUMN Orientation INTEGER DEFAULT 0",
        };

        foreach (var sql in phase4Columns)
        {
            try { await db.Database.ExecuteSqlRawAsync(sql); }
            catch { }
        }

        // Phase 4 metadata columns on MetadataProfiles
        var profileColumns = new[]
        {
            "ALTER TABLE MetadataProfiles ADD COLUMN DateTaken TEXT",
            "ALTER TABLE MetadataProfiles ADD COLUMN Rating INTEGER",
            "ALTER TABLE MetadataProfiles ADD COLUMN Creator TEXT",
            "ALTER TABLE MetadataProfiles ADD COLUMN Copyright TEXT",
            "ALTER TABLE MetadataProfiles ADD COLUMN UsageTerms TEXT",
            "ALTER TABLE MetadataProfiles ADD COLUMN ContactInfo TEXT",
            "ALTER TABLE MetadataProfiles ADD COLUMN City TEXT",
            "ALTER TABLE MetadataProfiles ADD COLUMN State TEXT",
            "ALTER TABLE MetadataProfiles ADD COLUMN Country TEXT",
            "ALTER TABLE MetadataProfiles ADD COLUMN Headline TEXT",
            "ALTER TABLE MetadataProfiles ADD COLUMN Description TEXT",
            "ALTER TABLE MetadataProfiles ADD COLUMN Title TEXT",
        };

        foreach (var sql in profileColumns)
        {
            try { await db.Database.ExecuteSqlRawAsync(sql); }
            catch { }
        }

        // Create performance indexes if they don't exist (added after initial schema creation)
        var indexCommands = new[]
        {
            "CREATE INDEX IF NOT EXISTS IX_DigitalAssets_Type ON DigitalAssets(Type)",
            "CREATE INDEX IF NOT EXISTS IX_DigitalAssets_StoragePath ON DigitalAssets(StoragePath)",
            "CREATE INDEX IF NOT EXISTS IX_DigitalAssets_CreatedAt ON DigitalAssets(CreatedAt)",
            "CREATE INDEX IF NOT EXISTS IX_DigitalAssets_MimeType ON DigitalAssets(MimeType)",
            "CREATE INDEX IF NOT EXISTS IX_DigitalAssets_FileSize ON DigitalAssets(FileSize)",
            "CREATE INDEX IF NOT EXISTS IX_DigitalAssets_FileName ON DigitalAssets(FileName)",
            "CREATE INDEX IF NOT EXISTS IX_DigitalAssets_ModifiedAt ON DigitalAssets(ModifiedAt)",
            "CREATE INDEX IF NOT EXISTS IX_MetadataProfiles_DateTaken ON MetadataProfiles(DateTaken)",
            "CREATE INDEX IF NOT EXISTS IX_MetadataProfiles_Rating ON MetadataProfiles(Rating)"
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
