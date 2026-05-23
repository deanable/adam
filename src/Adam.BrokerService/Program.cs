using Adam.BrokerService.Configuration;
using Adam.BrokerService.Data;
using Adam.BrokerService.Handlers;
using Adam.BrokerService.Hosting;
using Adam.BrokerService.Services;
using Adam.BrokerService.Transport;
using Adam.Shared.Data;
using Adam.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((ctx, services) =>
    {
        var dbConfig = DbProviderConfig.FromConfiguration(ctx.Configuration);
        services.AddSingleton(dbConfig);
        services.AddDbContext<AppDbContext>(opts => dbConfig.Configure(opts));

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
