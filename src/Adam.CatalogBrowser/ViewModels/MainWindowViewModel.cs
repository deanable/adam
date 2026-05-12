using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Adam.CatalogBrowser.Services;
using Microsoft.Extensions.Logging;

namespace Adam.CatalogBrowser.ViewModels;

public class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly ILogger<MainWindowViewModel> _logger;
    private object? _currentView;
    private string _statusText = "Ready";
    private bool _isBusy;

    public MainWindowViewModel(ILogger<MainWindowViewModel> logger, ModeManager modeManager, SidebarViewModel sidebar, AssetGalleryViewModel assetGallery, AdminPanelViewModel adminPanel, IngestionViewModel ingestion, MetadataEditorViewModel metadataEditor, UserManagementViewModel userManagement, AuditLogViewModel auditLog, MigrationWizardViewModel migrationWizard)
    {
        _logger = logger;
        ModeManager = modeManager;
        Sidebar = sidebar;
        AssetGallery = assetGallery;
        AdminPanel = adminPanel;
        Ingestion = ingestion;
        MetadataEditor = metadataEditor;
        UserManagement = userManagement;
        AuditLog = auditLog;
        MigrationWizard = migrationWizard;
        _currentView = assetGallery;

        ShowGalleryCommand = new RelayCommand(async _ =>
        {
            try
            {
                await assetGallery.LoadAssetsAsync();
                CurrentView = assetGallery;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load gallery");
            }
        });
        ShowAdminCommand = new RelayCommand(_ => CurrentView = adminPanel);
        ShowIngestionCommand = new RelayCommand(_ => CurrentView = ingestion);
        ShowMetadataEditorCommand = new RelayCommand(_ => CurrentView = metadataEditor);
        ShowUserManagementCommand = new RelayCommand(_ => CurrentView = userManagement);
        ShowAuditLogCommand = new RelayCommand(_ => CurrentView = auditLog);

        adminPanel.NavigateToMigrationWizard += () => CurrentView = migrationWizard;

        sidebar.FilterChanged += () =>
        {
            var category = sidebar.SelectedCategory;
            var folderPath = sidebar.SelectedFolder?.Path;
            assetGallery.ApplyFilter(category, folderPath);
        };

        _ = Sidebar.LoadAsync();
        _ = AssetGallery.LoadAssetsAsync();
    }

    public ModeManager ModeManager { get; }
    public SidebarViewModel Sidebar { get; }
    public AssetGalleryViewModel AssetGallery { get; }
    public AdminPanelViewModel AdminPanel { get; }
    public IngestionViewModel Ingestion { get; }
    public MetadataEditorViewModel MetadataEditor { get; }
    public UserManagementViewModel UserManagement { get; }
    public AuditLogViewModel AuditLog { get; }
    public MigrationWizardViewModel MigrationWizard { get; }

    public ICommand ShowGalleryCommand { get; }
    public ICommand ShowAdminCommand { get; }
    public ICommand ShowIngestionCommand { get; }
    public ICommand ShowMetadataEditorCommand { get; }
    public ICommand ShowUserManagementCommand { get; }
    public ICommand ShowAuditLogCommand { get; }

    public object? CurrentView
    {
        get => _currentView;
        set { _currentView = value; OnPropertyChanged(); }
    }

    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set { _isBusy = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _execute(parameter);
}
