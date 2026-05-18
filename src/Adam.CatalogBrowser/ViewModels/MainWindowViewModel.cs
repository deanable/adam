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

public class MetadataEntry
{
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly ModeManager _modeManager;
    private object? _currentView;
    private string _statusText = "Ready";
    private bool _isBusy;
    private AssetListItem? _selectedAsset;
    private ObservableCollection<MetadataEntry> _selectedAssetMetadata = [];

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
                _logger.LogInformation("[Startup] Beginning Sidebar.LoadAsync()...");
                await Sidebar.LoadAsync();
                _logger.LogInformation("[Startup] Sidebar.LoadAsync() completed");

                _logger.LogInformation("[Startup] Beginning AssetGallery.LoadAssetsAsync()...");
                await AssetGallery.LoadAssetsAsync();
                _logger.LogInformation("[Startup] AssetGallery.LoadAssetsAsync() completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Startup] FAILED to load sidebar and gallery on startup. Exception type={ExType}, Message={Message}", ex.GetType().Name, ex.Message);
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

    public ObservableCollection<MetadataEntry> SelectedAssetMetadata
    {
        get => _selectedAssetMetadata;
        set { _selectedAssetMetadata = value; OnPropertyChanged(); }
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
            SelectedAssetMetadata = [];
            return;
        }

        var entries = new List<MetadataEntry>();

        // File info
        entries.Add(new MetadataEntry { Label = "File name:", Value = _selectedAsset.FileName });
        entries.Add(new MetadataEntry { Label = "Title:", Value = _selectedAsset.Title });

        // Load full asset + profile from DB for remaining fields
        if (_modeManager.IsStandalone)
        {
            try
            {
                await using var db = _modeManager.CreateDbContext();
                var asset = await db.DigitalAssets
                    .Include(a => a.Keywords)
                    .Include(a => a.Categories)
                    .Include(a => a.MetadataProfile)
                    .FirstOrDefaultAsync(a => a.Id == _selectedAsset.Id);

                if (asset != null)
                {
                    AddIfValue(entries, "Description:", asset.Description);
                    AddIfValue(entries, "Type:", asset.MimeType);
                    AddIfValue(entries, "Dimensions:", asset.Width.HasValue && asset.Height.HasValue
                        ? $"{asset.Width} x {asset.Height}" : null);
                    AddIfValue(entries, "Size:", FormatFileSize(asset.FileSize));
                    AddIfValue(entries, "Duration:", asset.Duration.HasValue
                        ? $"{asset.Duration.Value:F2} s" : null);
                    AddIfValue(entries, "Date added:", asset.CreatedAt.ToLocalTime().ToString("g"));
                    AddIfValue(entries, "Date modified:", asset.ModifiedAt.ToLocalTime().ToString("g"));
                    AddIfValue(entries, "Version:", asset.Version.ToString());
                    AddIfValue(entries, "Checksum (SHA-256):", asset.ChecksumSha256);
                    AddIfValue(entries, "Storage path:", asset.StoragePath);

                    if (asset.Keywords.Count > 0)
                        AddIfValue(entries, "Keywords:", string.Join(", ", asset.Keywords.Select(k => k.Name)));

                    if (asset.Categories.Count > 0)
                        AddIfValue(entries, "Categories:", string.Join(", ", asset.Categories.Select(c => c.Name)));

                    if (asset.MetadataProfile != null)
                    {
                        var p = asset.MetadataProfile;
                        AddIfValue(entries, "Camera make:", p.CameraMake);
                        AddIfValue(entries, "Camera model:", p.CameraModel);
                        AddIfValue(entries, "Lens model:", p.LensModel);
                        AddIfValue(entries, "Focal length:", p.FocalLength.HasValue ? $"{p.FocalLength.Value:F1} mm" : null);
                        AddIfValue(entries, "Aperture:", p.Aperture.HasValue ? $"f/{p.Aperture.Value:F1}" : null);
                        AddIfValue(entries, "Exposure time:", p.ExposureTime);
                        AddIfValue(entries, "ISO:", p.Iso?.ToString());
                        AddIfValue(entries, "Flash:", p.Flash.HasValue ? (p.Flash.Value ? "Yes" : "No") : null);
                        AddIfValue(entries, "Orientation:", p.Orientation);
                        AddIfValue(entries, "Date taken:", p.DateTaken?.ToString("g"));
                        AddIfValue(entries, "GPS latitude:", p.GpsLatitude?.ToString("F6"));
                        AddIfValue(entries, "GPS longitude:", p.GpsLongitude?.ToString("F6"));
                        AddIfValue(entries, "GPS altitude:", p.GpsAltitude?.ToString("F1"));
                        AddIfValue(entries, "Rating:", p.Rating?.ToString());
                        AddIfValue(entries, "Creator:", p.Creator);
                        AddIfValue(entries, "Copyright:", p.Copyright);
                        AddIfValue(entries, "Usage terms:", p.UsageTerms);
                        AddIfValue(entries, "Contact info:", p.ContactInfo);
                        AddIfValue(entries, "City:", p.City);
                        AddIfValue(entries, "State:", p.State);
                        AddIfValue(entries, "Country:", p.Country);
                        AddIfValue(entries, "Headline:", p.Headline);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load metadata for selected asset");
            }
        }

        SelectedAssetMetadata = new ObservableCollection<MetadataEntry>(entries);
    }

    private static void AddIfValue(List<MetadataEntry> entries, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            entries.Add(new MetadataEntry { Label = label, Value = value });
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
