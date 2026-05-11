using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Adam.CatalogBrowser.Services;
using Adam.CatalogBrowser.Views;
using Adam.CatalogBrowser.ViewModels;
using Adam.Shared.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Adam.CatalogBrowser;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = new ServiceCollection();

            var basePath = Path.GetDirectoryName(Environment.ProcessPath)!;
            var broker = new BrokerClient("localhost", 5000);
            var auth = new AuthSession(broker);

            services.AddSingleton(broker);
            services.AddSingleton(auth);
            services.AddSingleton(s => new ModeManager(basePath, broker, auth));

            services.AddSingleton<ChecksumService>();
            services.AddSingleton<DuplicateDetector>();
            services.AddSingleton<DeleteService>();

            services.AddTransient<MainWindowViewModel>();
            services.AddTransient<AssetGalleryViewModel>();
            services.AddSingleton<AdminPanelViewModel>();
            services.AddTransient<MigrationWizardViewModel>();
            services.AddTransient<IngestionViewModel>();
            services.AddTransient<MetadataEditorViewModel>();
            services.AddTransient<UserManagementViewModel>();
            services.AddTransient<AuditLogViewModel>();

            var provider = services.BuildServiceProvider();

            var vm = provider.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = new MainWindow
            {
                DataContext = vm
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
