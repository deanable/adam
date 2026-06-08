using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Adam.CatalogBrowser.Services;
using Adam.CatalogBrowser.Views;
using Adam.CatalogBrowser.ViewModels;
using Adam.Shared.Configuration;
using Adam.Shared.Services;
using LiquidVision.Core.Configuration;
using LiquidVision.Core.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Adam.CatalogBrowser;

public partial class App : Application
{
    /// <summary>
    /// Global config instance, loaded once at startup and saved on changes.
    /// </summary>
    internal static AdamConfig Config { get; private set; } = AdamConfig.Load();

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
            System.Diagnostics.Debug.WriteLine($"[adam] App basePath: {basePath}");
            var logPath = Path.Combine(basePath, "adam-catalog.log");

            // Reset the connection debug log at startup (captures all connection attempts in a single session file)
            ConnectionDebugLogger.Reset();
            ConnectionDebugLogger.Info($"[APP] Adam CatalogBrowser starting (basePath={basePath})");
            ConnectionDebugLogger.Info($"[APP] Config: host={config.ServiceHost}, port={config.ServicePort}, TLS={config.UseTls}, mode={config.Mode}");

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
                System.Diagnostics.Debug.WriteLine(
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
            services.AddSingleton<BulkOperationQueue>();
            services.AddSingleton<MetadataWritebackService>();

            // Phase 9: AI Image Tagging (D-09)
            services.AddLiquidVision(o =>
            {
                // 1.6B is ~3.5x larger than the 450M model and notably more accurate. It ships only
                // fp32/fp16 (no q4f16), so run fp16 — handled by the precision-aware generator.
                o.ModelId = "onnx-community/LFM2-VL-1.6B-ONNX";
                o.Precision = ModelPrecision.Fp16;
                o.ExecutionProvider = ExecutionProviderKind.Cpu;
            });
            services.AddSingleton<AiTaggingService>();

            services.AddTransient<SidebarViewModel>();
            services.AddTransient<MainWindowViewModel>();
            services.AddTransient<AssetGalleryViewModel>();
            services.AddTransient<IngestionViewModel>();
            services.AddTransient<MetadataEditorViewModel>();
            services.AddTransient<AuditLogViewModel>();
            services.AddTransient<PropertyInspectorViewModel>();
            services.AddTransient<ConnectionViewModel>();
            services.AddTransient<StatusBarViewModel>();

            var provider = services.BuildServiceProvider();

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
