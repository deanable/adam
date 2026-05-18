using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Adam.CatalogBrowser.Models;
using Adam.CatalogBrowser.Services;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;

namespace Adam.CatalogBrowser.ViewModels;

public class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly ModeManager _modeManager;
    private object? _currentView;
    private string _statusText = "Ready";
    private bool _isBusy;
    private AssetListItem? _selectedAsset;
    private string _selectedAssetFileName = "No selection";
    private string _selectedAssetType = "—";
    private string _selectedAssetDimensions = "—";
    private string _selectedAssetSize = "—";
    private string _selectedAssetDate = "—";
    private string _selectedAssetCameraMake = "—";
    private string _selectedAssetCameraModel = "—";

    public MainWindowViewModel(ILogger<MainWindowViewModel> logger, ModeManager modeManager, SidebarViewModel sidebar, AssetGalleryViewModel assetGallery, AdminPanelViewModel adminPanel, IngestionViewModel ingestion, MetadataEditorViewModel metadataEditor, UserManagementViewModel userManagement, AuditLogViewModel auditLog, MigrationWizardViewModel migrationWizard)
    {
        _logger = logger;
        _modeManager = modeManager;
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
        ShowIngestionCommand = new RelayCommand(async _ =>
        {
            CurrentView = ingestion;
            await ingestion.LoadIngestedFoldersAsync();
        });
        ShowMetadataEditorCommand = new RelayCommand(async _ =>
        {
            if (_selectedAsset != null)
                await metadataEditor.LoadAssetAsync(_selectedAsset.Id);
            CurrentView = metadataEditor;
        });
        ShowUserManagementCommand = new RelayCommand(_ => CurrentView = userManagement);
        ShowAuditLogCommand = new RelayCommand(_ => CurrentView = auditLog);

        adminPanel.NavigateToMigrationWizard += () => CurrentView = migrationWizard;

        sidebar.FilterChanged += () =>
        {
            var mediaFormat = sidebar.SelectedMediaFormat?.Name ?? "All";
            var folderPath = sidebar.SelectedFolder?.Path;
            var keywordIds = GetDescendantKeywordIds(sidebar.SelectedKeyword);
            var categoryIds = GetDescendantCategoryIds(sidebar.SelectedMetadataCategory);
            assetGallery.ApplyFilter(mediaFormat, folderPath, keywordIds, categoryIds);
        };

        ingestion.IngestionCompleted += async () =>
        {
            await Sidebar.LoadAsync();
            await AssetGallery.LoadAssetsAsync();
        };

        assetGallery.SelectionChanged += async asset =>
        {
            _selectedAsset = asset;
            await LoadSelectedAssetMetadataAsync();
        };

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await Sidebar.LoadAsync();
                await AssetGallery.LoadAssetsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load sidebar and gallery on startup");
            }
        }, DispatcherPriority.Background);
    }

    private static List<Guid> GetDescendantKeywordIds(KeywordNode? node)
    {
        if (node == null || node.KeywordId == Guid.Empty) return [];
        var result = new List<Guid> { node.KeywordId };
        foreach (var child in node.Children)
            result.AddRange(GetDescendantKeywordIds(child));
        return result;
    }

    private static List<Guid> GetDescendantCategoryIds(CategoryNode? node)
    {
        if (node == null || node.CategoryId == Guid.Empty) return [];
        var result = new List<Guid> { node.CategoryId };
        foreach (var child in node.Children)
            result.AddRange(GetDescendantCategoryIds(child));
        return result;
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

    public AssetListItem? SelectedAsset => _selectedAsset;

    public string SelectedAssetFileName
    {
        get => _selectedAssetFileName;
        set { _selectedAssetFileName = value; OnPropertyChanged(); }
    }

    public string SelectedAssetType
    {
        get => _selectedAssetType;
        set { _selectedAssetType = value; OnPropertyChanged(); }
    }

    public string SelectedAssetDimensions
    {
        get => _selectedAssetDimensions;
        set { _selectedAssetDimensions = value; OnPropertyChanged(); }
    }

    public string SelectedAssetSize
    {
        get => _selectedAssetSize;
        set { _selectedAssetSize = value; OnPropertyChanged(); }
    }

    public string SelectedAssetDate
    {
        get => _selectedAssetDate;
        set { _selectedAssetDate = value; OnPropertyChanged(); }
    }

    public string SelectedAssetCameraMake
    {
        get => _selectedAssetCameraMake;
        set { _selectedAssetCameraMake = value; OnPropertyChanged(); }
    }

    public string SelectedAssetCameraModel
    {
        get => _selectedAssetCameraModel;
        set { _selectedAssetCameraModel = value; OnPropertyChanged(); }
    }

    public bool HasSelectedAsset => _selectedAsset != null;

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

    private async Task LoadSelectedAssetMetadataAsync()
    {
        OnPropertyChanged(nameof(SelectedAsset));
        OnPropertyChanged(nameof(HasSelectedAsset));

        if (_selectedAsset == null)
        {
            SelectedAssetFileName = "No selection";
            SelectedAssetType = "—";
            SelectedAssetDimensions = "—";
            SelectedAssetSize = "—";
            SelectedAssetDate = "—";
            SelectedAssetCameraMake = "—";
            SelectedAssetCameraModel = "—";
            return;
        }

        SelectedAssetFileName = _selectedAsset.FileName;
        SelectedAssetType = _selectedAsset.FileType;
        SelectedAssetDimensions = _selectedAsset.Width.HasValue && _selectedAsset.Height.HasValue
            ? $"{_selectedAsset.Width} x {_selectedAsset.Height}"
            : "—";
        SelectedAssetSize = FormatFileSize(_selectedAsset.FileSize);
        SelectedAssetDate = _selectedAsset.CreatedAt.ToLocalTime().ToString("g");

        if (_modeManager.IsStandalone)
        {
            try
            {
                await using var db = _modeManager.CreateDbContext();
                var profile = await db.MetadataProfiles
                    .AsNoTracking()
                    .FirstOrDefaultAsync(m => m.DigitalAssetId == _selectedAsset.Id);
                if (profile != null)
                {
                    SelectedAssetCameraMake = profile.CameraMake ?? "—";
                    SelectedAssetCameraModel = profile.CameraModel ?? "—";
                }
                else
                {
                    SelectedAssetCameraMake = "—";
                    SelectedAssetCameraModel = "—";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load metadata for selected asset");
            }
        }
    }

    private static string FormatFileSize(long bytes)
    {
        const long kb = 1024;
        const long mb = kb * 1024;
        const long gb = mb * 1024;
        return bytes >= gb ? $"{bytes / (double)gb:F2} GB"
            : bytes >= mb ? $"{bytes / (double)mb:F2} MB"
            : bytes >= kb ? $"{bytes / (double)kb:F2} KB"
            : $"{bytes} B";
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
