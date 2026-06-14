using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Adam.CatalogBrowser.Services;
using Adam.CatalogBrowser.Views;
using Adam.CatalogBrowser.ViewModels;
using Adam.Shared.Configuration;
using Adam.Shared.Data;
using Adam.Shared.Services;
using LiquidVision.Core.Configuration;
using LiquidVision.Core.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Adam.CatalogBrowser;

public partial class App : Application
{
    /// <summary>
    /// Global config instance, loaded once at startup and saved on changes.
    /// </summary>
    internal static AdamConfig Config { get; private set; } = AdamConfig.Load();

    /// <summary>
    /// The DI service provider, set after container build.
    /// </summary>
    internal static System.IServiceProvider? ServiceProvider { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var config = Config;
            var services = new ServiceCollection();
            var basePath = AppContext.BaseDirectory;
            var logPath = Path.Combine(basePath, "adam-catalog.log");

            // Reset the connection debug log at startup
            ConnectionDebugLogger.Reset();
            ConnectionDebugLogger.Info($"[APP] Starting: host={config.ServiceHost}:{config.ServicePort}, TLS={config.UseTls}, mode={config.Mode}");

            services.AddLogging(builder => builder
                .AddFile(logPath)
                .SetMinimumLevel(LogLevel.Information));

            // Connection settings published by the (elevated) Service Manager to HKLM
            // are applied when the client is already configured for multi-user mode.
            // This avoids stale or mismatched registry values (e.g. a previous server
            // IP, port, or TLS setting) overriding fresh defaults or a standalone config.
            // The user can always update the connection bar manually to pull in registry
            // values when switching to multi-user mode.
            var published = RegistrySettings.Load();
            if (published != null && config.Mode == "MultiUser")
            {
                ConnectionDebugLogger.Info(
                    $"[adam] Using connection settings from registry: {published.ServiceHost}:{published.ServicePort} (Tls={published.UseTls})");
                config.ServiceHost = published.ServiceHost;
                config.ServicePort = published.ServicePort;
                config.UseTls = published.UseTls;
                config.AllowSelfSigned = published.AllowSelfSigned;
                if (!string.IsNullOrWhiteSpace(published.Username) && string.IsNullOrEmpty(config.LastUsername))
                    config.LastUsername = published.Username;
                config.Save();
            }

            var broker = new BrokerClient(config.ServiceHost, config.ServicePort, config.UseTls, config.AllowSelfSigned);
            var auth = new AuthSession(broker);

            services.AddSingleton(broker);
            services.AddSingleton(auth);
            var modeManager = new ModeManager(basePath, broker, auth);
            services.AddSingleton(modeManager);

            services.AddSingleton<ChecksumService>();
            services.AddSingleton<DuplicateDetector>();
            services.AddSingleton<DeleteService>();
            services.AddSingleton<ToastService>();
            services.AddSingleton<BulkOperationQueue>();
            services.AddSingleton<MetadataWritebackService>();

            // T11.8: Full-text search service (always SQLite in CatalogBrowser standalone mode)
            services.AddDbContextFactory<AppDbContext>(opts =>
                opts.UseSqlite($"Data Source={Path.Combine(basePath, ".adam", "catalog.db")}"));
            services.AddSingleton<IFtsService, SqliteFtsService>();

            // Phase 9: AI Image Tagging (D-09) — configured from saved settings
            services.AddLiquidVision(o =>
            {
                o.ModelId = !string.IsNullOrWhiteSpace(config.AiModelId)
                    ? config.AiModelId
                    : "onnx-community/LFM2-VL-1.6B-ONNX";

                o.Precision = Enum.TryParse<ModelPrecision>(config.AiPrecision, ignoreCase: true, out var precision)
                    ? precision
                    : ModelPrecision.Q4F16;

                o.ExecutionProvider = ExecutionProviderKind.Cpu;
            });
            services.AddSingleton<AiTaggingService>();

            services.AddTransient<SidebarViewModel>();
            services.AddTransient<MainWindowViewModel>();
            services.AddTransient<AssetGalleryViewModel>(sp =>
                new AssetGalleryViewModel(
                    sp.GetRequiredService<ModeManager>(),
                    sp.GetRequiredService<ILogger<AssetGalleryViewModel>>(),
                    sp.GetRequiredService<IFtsService>()));
            services.AddTransient<IngestionViewModel>();
            services.AddTransient<MetadataEditorViewModel>();
            services.AddTransient<AuditLogViewModel>();
            services.AddTransient<PropertyInspectorViewModel>();
            services.AddTransient<ConnectionViewModel>();
            services.AddTransient<StatusBarViewModel>();
            services.AddTransient<ActivityFeedViewModel>();
            services.AddTransient<TrashViewModel>();

            var provider = services.BuildServiceProvider();
            ServiceProvider = provider;

            // T11.8: Wire FTS service into ModeManager for startup initialization
            modeManager.FtsService = provider.GetRequiredService<IFtsService>();

            var vm = provider.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = new MainWindow
            {
                DataContext = vm
            };

            // Dispose the DI container on app exit
            desktop.Exit += (_, _) =>
            {
                (provider as IDisposable)?.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
