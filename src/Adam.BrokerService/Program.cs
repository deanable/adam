using Adam.Shared.Configuration;
using Adam.BrokerService.Data;
using Adam.BrokerService.Handlers;
using Adam.BrokerService.Services;
using Adam.BrokerService.Transport;
using Adam.Shared.Data;
using Adam.Shared.Services;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Configure connection debug log to a directory the broker can always write to,
// even when running as a Windows Service (SYSTEM/LOCAL SERVICE).
var brokerLogDir = Path.Combine(Path.GetTempPath(), "Adam", "BrokerService");
ConnectionDebugLogger.LogDirectory = brokerLogDir;
ConnectionDebugLogger.Reset();
ConnectionDebugLogger.Info($"[BROKER] Starting: Machine={Environment.MachineName}, OS={Environment.OSVersion}, log={ConnectionDebugLogger.LogFilePath}");

var exeDir = Path.GetDirectoryName(typeof(Program).Assembly.Location)!;

var host = Host.CreateDefaultBuilder(args)
    .UseContentRoot(exeDir)
    .UseWindowsService()
    .ConfigureServices((ctx, services) =>
    {
        var dbConfig = DbProviderConfig.FromConfiguration(ctx.Configuration);
        services.AddSingleton(dbConfig);
        services.AddDbContext<AppDbContext>(opts =>
        {
            dbConfig.Configure(opts);
            opts.ConfigureWarnings(w => w.Log(RelationalEventId.PendingModelChangesWarning));
        });
        services.AddScoped<KeywordService>();
        services.AddScoped<CategoryService>();

        services.AddSingleton<IConnectionHandler, ConnectionHandler>();
        services.AddSingleton<LoginRateLimiter>();
        services.AddSingleton<AuthHandler>();
        services.AddSingleton<AssetHandler>();
        services.AddSingleton<CollectionHandler>();
        services.AddSingleton<ChangeHandler>();
        services.AddSingleton<UserHandler>();
        services.AddSingleton<AuditLogHandler>();
        services.AddSingleton<AuditLogger>();
        services.AddSingleton<AuthorizationMiddleware>();
        services.AddSingleton<StatusHandler>();
        services.AddSingleton<SidebarHandler>();
        services.AddSingleton<WatchedFolderHandler>();
        services.AddSingleton<CommentHandler>();
        services.AddSingleton<SavedSearchHandler>();
        services.AddSingleton<SearchHistoryHandler>();
        services.AddSingleton<SemanticSearchHandler>();
        services.AddSingleton<SearchRankingHandler>();
        services.AddSingleton<EmbeddingService>();
        services.AddSingleton<SemanticSearchService>();
        services.AddSingleton<SearchRankingService>();
        services.AddSingleton<NearDuplicateService>();
        services.AddSingleton<EmbeddingClusterService>();
        services.AddSingleton<ConnectionRegistry>();
        services.AddSingleton<ChangeNotificationService>();
        services.AddSingleton<TcpListenerService>();
        services.AddSingleton<MetadataWritebackService>();
        services.AddSingleton<AccessLogCleanupService>();
        services.AddHostedService<TcpListenerHostedService>();
        services.AddHostedService<AccessLogCleanupHostedService>();
        // Not registering FolderScanService here — it requires ModeManager which
        // is only available in CatalogBrowser. Re-scan is standalone-only for now.

        services.AddTransient<MigrationRunner>();

        services.AddSingleton<DbMigrationService>();

        // T11.8: Register IDbContextFactory for FTS services (reuses existing DbProviderConfig)
        services.AddDbContextFactory<AppDbContext>(opts =>
        {
            dbConfig.Configure(opts);
            opts.ConfigureWarnings(w => w.Log(RelationalEventId.PendingModelChangesWarning));
        });

        // T11.8: Register IFtsService based on configured DB provider
        switch (dbConfig.Provider.ToLowerInvariant())
        {
            case "postgresql":
            case "postgres":
                services.AddSingleton<IFtsService, PostgresFtsService>();
                break;
            case "sqlserver":
            case "mssql":
                services.AddSingleton<IFtsService, SqlServerFtsService>();
                break;
            default:
                services.AddSingleton<IFtsService, SqliteFtsService>();
                break;
        }
        services.AddSingleton<IServiceInstaller, WindowsServiceInstaller>();
        services.AddSingleton<IServiceInstaller, MacOsServiceInstaller>();
        services.AddSingleton<IServiceInstaller, LinuxServiceInstaller>();
        services.AddHostedService<FolderWatcherHostedService>();
    })
    .ConfigureLogging(logging =>
    {
        logging.AddConsole();
        logging.SetMinimumLevel(LogLevel.Information);
    })
    .Build();

var runner = host.Services.GetRequiredService<MigrationRunner>();
await runner.RunAsync();

await host.RunAsync();
