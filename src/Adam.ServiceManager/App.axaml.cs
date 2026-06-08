using Adam.ServiceManager.Services;
using Adam.ServiceManager.ViewModels;
using Adam.ServiceManager.Views;
using Adam.Shared.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Adam.ServiceManager;

public partial class App : Application
{
    /// <summary>
    /// Global config instance, loaded once at startup and saved on changes.
    /// </summary>
    internal static ServiceManagerConfig Config { get; private set; } = ServiceManagerConfig.Load();

    /// <summary>
    /// Tray icon service — kept alive for the lifetime of the application.
    /// </summary>
    internal static TrayIconService? TrayIcon { get; private set; }

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
            System.Diagnostics.Debug.WriteLine($"[adam-service] App basePath: {basePath}");
            var logPath = Path.Combine(basePath, "adam-service-manager.log");

            // Create a shared capture for service installation logs (sc.exe, netsh, elevated process)
            var serviceLogCapture = new System.Collections.ObjectModel.ObservableCollection<string>();
            services.AddLogging(builder => builder
                .AddFile(logPath)
                .AddProvider(new LogCaptureProvider(serviceLogCapture))
                .SetMinimumLevel(LogLevel.Information));

            // Register platform-specific service installers for BrokerService management
            services.AddSingleton<IServiceInstaller, WindowsServiceInstaller>();
            services.AddSingleton<IServiceInstaller, MacOsServiceInstaller>();
            services.AddSingleton<IServiceInstaller, LinuxServiceInstaller>();

            // Register ModeManager for database access (standalone mode for server management)
            var modeManager = new ModeManager(basePath);
            services.AddSingleton(modeManager);

            services.AddTransient<UserManagementViewModel>();
            services.AddTransient<AuditLogViewModel>();

            services.AddSingleton<ServiceManagerViewModel>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<ServiceManagerViewModel>>();
                var installers = sp.GetServices<IServiceInstaller>();
                var userManagement = sp.GetRequiredService<UserManagementViewModel>();
                var auditLog = sp.GetRequiredService<AuditLogViewModel>();
                return new ServiceManagerViewModel(installers, logger, serviceLogCapture, userManagement, auditLog);
            });

            var provider = services.BuildServiceProvider();
            var startupLogger = provider.GetRequiredService<ILogger<App>>();

            // Initialize the database based on configuration.
            //
            // This must NEVER prevent the window from appearing: once the BrokerService
            // is running it holds the SQLite catalog database, so EnsureCreated/OpenConnection
            // here can throw "database is locked". If that bubbled out of
            // OnFrameworkInitializationCompleted the MainWindow would never be created and the
            // app would exit silently — the user would see it "not launch" after starting the
            // service. So we swallow init failures, log them, and still show the window. The
            // Users tab re-creates its own DbContext on demand and surfaces any DB error there.
            try
            {
                if (config.DbProvider.Equals("sqlite", StringComparison.OrdinalIgnoreCase))
                {
                    modeManager.InitializeAsync().GetAwaiter().GetResult();
                }
                else
                {
                    modeManager.InitializeMultiUserAsync(
                        config.ServiceHost,
                        config.ServicePort,
                        config.DbProvider,
                        config.DbConnection).GetAwaiter().GetResult();
                }
            }
            catch (Exception ex)
            {
                startupLogger.LogError(ex,
                    "Database initialization failed at startup (the running service may hold the database). " +
                    "Continuing so the Service Manager window still opens.");
            }

            var vm = provider.GetRequiredService<ServiceManagerViewModel>();
            var mainWindow = new MainWindow
            {
                DataContext = vm
            };
            desktop.MainWindow = mainWindow;

            // Create the system tray icon
            TrayIcon = new TrayIconService(mainWindow);

            // Minimize to tray instead of taskbar
            mainWindow.PropertyChanged += (_, e) =>
            {
                if (e.Property == Window.WindowStateProperty && mainWindow.WindowState == WindowState.Minimized)
                {
                    mainWindow.Hide();
                }
            };

            // Dispose the tray icon on exit to clean up native resources
            desktop.Exit += (_, _) =>
            {
                TrayIcon?.Dispose();
                (provider as IDisposable)?.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
