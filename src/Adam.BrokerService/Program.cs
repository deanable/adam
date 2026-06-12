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
ConnectionDebugLogger.Info($"[BROKER] BrokerService starting (args={string.Join(" ", args)})");
ConnectionDebugLogger.Info($"[BROKER] Machine={Environment.MachineName}, OS={Environment.OSVersion}");
ConnectionDebugLogger.Info($"[BROKER] Debug log: {ConnectionDebugLogger.LogFilePath}");

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
        services.AddSingleton<ConnectionRegistry>();
        services.AddSingleton<ChangeNotificationService>();
        services.AddSingleton<TcpListenerService>();
        services.AddSingleton<MetadataWritebackService>();
        services.AddHostedService<TcpListenerHostedService>();
        services.AddTransient<MigrationRunner>();

        services.AddSingleton<DbMigrationService>();
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
