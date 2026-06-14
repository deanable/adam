using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Adam.CatalogBrowser.Controls;
using Adam.CatalogBrowser.Models;
using Adam.CatalogBrowser.Services;
using Adam.Shared.Models;
using Adam.Shared.Services;
using Avalonia;
using Avalonia.Input;
using Avalonia.Threading;
using LiquidVision.Core;
using LiquidVision.Core.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TextCopy;

namespace Adam.CatalogBrowser.ViewModels;

public class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly ModeManager _modeManager;
    private readonly MetadataWritebackService _writeback;
    private readonly AiTaggingService? _aiTaggingService;
    private readonly BulkOperationQueue _bulkQueue;
    private readonly DeleteService _deleteService;
    internal readonly ToastService ToastService;
    private readonly IUiDispatcher _dispatcher;
    private object? _currentView;
    private readonly DispatcherTimer? _sessionCheckTimer;

    public MainWindowViewModel(
        ILogger<MainWindowViewModel> logger,
        ModeManager modeManager,
        MetadataWritebackService writeback,
        SidebarViewModel sidebar,
        AssetGalleryViewModel assetGallery,
        IngestionViewModel ingestion,
        MetadataEditorViewModel metadataEditor,
        AuditLogViewModel auditLog,
        BulkOperationQueue bulkQueue,
        PropertyInspectorViewModel propertyInspector,
        ConnectionViewModel connection,
        StatusBarViewModel statusBar,
        DeleteService deleteService,
        ToastService toastService,
        ActivityFeedViewModel activityFeed,
        AiTaggingService? aiTaggingService = null,
        LiquidVisionOptions? liquidVisionOptions = null,
        bool startUp = true,
        bool startSessionTimer = true,
        IUiDispatcher? dispatcher = null)
    {
        _logger = logger;
        _modeManager = modeManager;
        _writeback = writeback;
        _bulkQueue = bulkQueue;
        _deleteService = deleteService;
        ToastService = toastService;
        _dispatcher = dispatcher ?? new AvaloniaUiDispatcher();
        Sidebar = sidebar;
        AssetGallery = assetGallery;
        Ingestion = ingestion;
        ActivityFeed = activityFeed;
        AiModelSelector = new AiModelSelectorViewModel(aiTaggingService, liquidVisionOptions ?? new LiquidVisionOptions(), _dispatcher);
        MetadataEditor = metadataEditor;
        AuditLog = auditLog;
        PropertyInspector = propertyInspector;
        Connection = connection;
        StatusBar = statusBar;
        _currentView = assetGallery;

        // Phase 7: Session check timer — checks token expiry every 60s (T7.3)
        // Skipped when startSessionTimer is false (unit tests) to avoid requiring
        // an Avalonia platform thread.
        if (startSessionTimer)
        {
            _sessionCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            _sessionCheckTimer.Tick += OnSessionCheckTick;
            _sessionCheckTimer.Start();
        }

        AssignKeywordDropCommand = new RelayCommand(OnAssignKeywordDrop);
        AssignCategoryDropCommand = new RelayCommand(OnAssignCategoryDrop);
        _aiTaggingService = aiTaggingService;

        // Trigger C: AI tag selected assets
        AiTagSelectedCommand = new RelayCommand(async _ => await AiTagSelectedAssetsAsync(),
            _ => _aiTaggingService != null && AssetGallery.SelectedAssets.Count > 0);

        // Wire model-download progress to status bar (D-14)
        if (aiTaggingService != null)
        {
            SubscribeToDownloadProgress(aiTaggingService);
        }

        // Update AiTagSelectedCommand can-execute when selection changes
        assetGallery.MultiSelectionChanged += _ =>
        {
            (AiTagSelectedCommand as RelayCommand)?.RaiseCanExecuteChanged();
        };
        assetGallery.SelectionChanged += _ =>
        {
            (AiTagSelectedCommand as RelayCommand)?.RaiseCanExecuteChanged();
        };

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

        ShowIngestionCommand = new RelayCommand(async _ =>
        {
            CurrentView = ingestion;
            await ingestion.LoadIngestedFoldersAsync();
        }, _ => CanIngest);

        ShowMetadataEditorCommand = new RelayCommand(async _ =>
        {
            if (PropertyInspector.SelectedAsset != null)
                await metadataEditor.LoadAssetAsync(PropertyInspector.SelectedAsset.Id);
            CurrentView = metadataEditor;
        }, _ => CanEditMetadata);

        ShowAuditLogCommand = new RelayCommand(_ => CurrentView = auditLog, _ => CanAudit);
        ShowServiceManagerCommand = new RelayCommand(_ => LaunchServiceManager(), _ => CanAdminister);

        ShowActivityFeedCommand = new RelayCommand(async _ =>
        {
            CurrentView = activityFeed;
            await activityFeed.LoadRecentActivityAsync();
        });
        ExportCommand = new RelayCommand(_ => ShowExportDialog(), _ => CanEditMetadata);
        ImportMetadataCommand = new RelayCommand(async _ => await ShowImportDialogAsync(), _ => CanEditMetadata);
        SavePresetCommand = new RelayCommand(async _ => await ShowPresetDialogAsync(saveMode: true), _ => CanEditMetadata);
        LoadPresetCommand = new RelayCommand(async _ => await ShowPresetDialogAsync(saveMode: false), _ => CanEditMetadata);

        RotateClockwiseCommand = new RelayCommand(async _ => await RotateAsync(ImageAdjustmentService.Rotate90Cw), _ => CanEditMetadata);
        RotateCounterClockwiseCommand = new RelayCommand(async _ => await RotateAsync(ImageAdjustmentService.Rotate90Ccw), _ => CanEditMetadata);
        FlipHorizontalCommand = new RelayCommand(async _ => await RotateAsync(ImageAdjustmentService.ToggleFlipHorizontal), _ => CanEditMetadata);
        FlipVerticalCommand = new RelayCommand(async _ => await RotateAsync(ImageAdjustmentService.ToggleFlipVertical), _ => CanEditMetadata);

        // Wave 1: Delete / Trash (T8.17)
        DeleteSelectedCommand = new RelayCommand(async _ => await DeleteSelectedAssetsAsync(),
            _ => AssetGallery.SelectedAssets.Count > 0 && CanEditMetadata);
        ShowTrashCommand = new RelayCommand(async _ => await ShowTrashViewAsync());

        // Wave 1: Reveal / Copy / Add-to-collection (T8.19)
        RevealInFolderCommand = new RelayCommand(_ => RevealInFolder(), _ => AssetGallery.SelectedAssets.Count > 0);
        CopyFilePathCommand = new RelayCommand(_ => CopyFilePath(), _ => AssetGallery.SelectedAssets.Count > 0);
        CopyFileCommand = new RelayCommand(async _ => await CopyFileAsync(), _ => AssetGallery.SelectedAssets.Count > 0);
        AddToCollectionCommand = new RelayCommand(async _ => await AddToCollectionAsync(), _ => AssetGallery.SelectedAssets.Count > 0);

        // Wave 1: Per-asset rate/label/flag commands (T8.16)
        RateAssetCommand = new RelayCommand(async _ => await RateSelectedAsync(), _ => AssetGallery.SelectedAssets.Count > 0 && CanEditMetadata);
        SetLabelCommand = new RelayCommand(async _ => await SetLabelSelectedAsync(), _ => AssetGallery.SelectedAssets.Count > 0 && CanEditMetadata);
        SetFlagCommand = new RelayCommand(async _ => await SetFlagSelectedAsync(), _ => AssetGallery.SelectedAssets.Count > 0 && CanEditMetadata);

        // T8.21: Keyboard rating digits — sets exact rating value via command parameter
        SetRatingCommand = new RelayCommand(async p => await SetRatingByKeyAsync(p), _ => AssetGallery.SelectedAssets.Count > 0 && CanEditMetadata);

        // T8.21: P = Pick flag, X = Reject flag (direct-set commands)
        SetFlagPickCommand = new RelayCommand(async _ => await SetFlagByKeyAsync(AssetFlag.Pick), _ => AssetGallery.SelectedAssets.Count > 0 && CanEditMetadata);
        SetFlagRejectCommand = new RelayCommand(async _ => await SetFlagByKeyAsync(AssetFlag.Reject), _ => AssetGallery.SelectedAssets.Count > 0 && CanEditMetadata);

        // T8.21: F2 = Rename asset title
        RenameAssetCommand = new RelayCommand(async _ => await RenameAssetAsync(), _ => AssetGallery.SelectedAssets.Count == 1 && CanEditMetadata);

        // T8.21: Ctrl+F = Focus keyword search (wired via event to MainWindow code-behind)
        FocusSearchCommand = new RelayCommand(_ => RequestFocusSearch?.Invoke());

        // Re-evaluate delete/trash/reveal/copy commands when selection changes
        assetGallery.MultiSelectionChanged += _ =>
        {
            (DeleteSelectedCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RevealInFolderCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (CopyFilePathCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (CopyFileCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (AddToCollectionCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RateAssetCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (SetLabelCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (SetFlagCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (SetRatingCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (SetFlagPickCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (SetFlagRejectCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RenameAssetCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (PropertyInspector.ApplyBatchEditCommand as RelayCommand)?.RaiseCanExecuteChanged();
        };
        assetGallery.SelectionChanged += _ =>
        {
            (DeleteSelectedCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RevealInFolderCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (CopyFilePathCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (CopyFileCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (AddToCollectionCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RateAssetCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (SetLabelCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (SetFlagCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (SetRatingCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (SetFlagPickCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (SetFlagRejectCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RenameAssetCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (PropertyInspector.ApplyBatchEditCommand as RelayCommand)?.RaiseCanExecuteChanged();
        };

        sidebar.FilterChanged += () =>
        {
            var mediaFormat = sidebar.SelectedMediaFormat?.Name ?? "All";
            var folderPath = sidebar.SelectedFolder?.Path;
            var keywordIds = GetDescendantKeywordIds(sidebar.SelectedKeyword);
            var categoryIds = GetDescendantCategoryIds(sidebar.SelectedMetadataCategory);

            DateTime? dateFrom = null;
            DateTime? dateTo = null;
            var selectedDate = sidebar.SelectedDateTaken;
            if (selectedDate != null && selectedDate.Year.HasValue)
            {
                if (selectedDate.Month.HasValue)
                {
                    dateFrom = new DateTime(selectedDate.Year.Value, selectedDate.Month.Value, 1);
                    dateTo = dateFrom.Value.AddMonths(1);
                }
                else
                {
                    dateFrom = new DateTime(selectedDate.Year.Value, 1, 1);
                    dateTo = dateFrom.Value.AddYears(1);
                }
            }

            // T14.5: Advanced filters — rating, label, flag
            var ratingFilter = sidebar.SelectedRatingFilter;
            var labelFilter = sidebar.SelectedLabelFilter;
            var flagFilter = sidebar.SelectedFlagFilter;

            assetGallery.ApplyFilter(mediaFormat, folderPath, keywordIds, categoryIds, dateFrom, dateTo, ratingFilter, labelFilter, flagFilter);
        };

        ingestion.IngestionCompleted += () =>
        {
            _ = Task.Run(async () =>
            {
                await Sidebar.LoadAsync();
                await AssetGallery.LoadAssetsAsync();
            });
        };

        metadataEditor.SaveCompleted += () =>
        {
            _ = Task.Run(async () =>
            {
                await Sidebar.LoadAsync();
                await AssetGallery.LoadAssetsAsync();
                if (PropertyInspector.SelectedAsset != null)
                    await PropertyInspector.LoadSelectedAssetMetadataAsync();
            });
        };

        assetGallery.SelectionChanged += asset =>
        {
            PropertyInspector.SelectedAsset = asset;
        };

        assetGallery.MultiSelectionChanged += assets =>
        {
            PropertyInspector.SetMultiSelection(assets);
        };

        Connection.RequestLogin += async (auth, host, port) => await TryShowLoginDialogAsync(auth, host, port);
        Connection.RequestLocalSwitch += async () =>
        {
            await _modeManager.InitializeAsync();
            await Sidebar.LoadAsync();
            await AssetGallery.LoadAssetsAsync();
            App.Config.Mode = "Standalone";
            App.Config.Save();
        };
        // After a successful two-stage connect (server + authentication), switch into
        // multi-user mode and reload so data is served from the broker, not the local DB.
        Connection.ServiceConnected += async () =>
        {
            await _modeManager.InitializeMultiUserAsync(Connection.ServiceHost, Connection.ServicePort);
            await Sidebar.LoadAsync();
            await AssetGallery.LoadAssetsAsync();
            App.Config.Mode = "MultiUser";
            App.Config.Save();

            // Update permission-aware UI after login
            await RefreshPermissionsAsync();
        };

        // Re-evaluate permissions when connection state changes (login/logout)
        Connection.PropertyChanged += async (_, e) =>
        {
            if (e.PropertyName is nameof(ConnectionViewModel.IsConnectedToService) or nameof(ConnectionViewModel.IsServiceMode))
            {
                await RefreshPermissionsAsync();
            }
        };

        // Phase 7 T7.4: Handle forced logout (account deactivation / token expiry)
        Connection.ForceLogout += async () =>
        {
            _logger.LogWarning("Forced logout triggered — clearing session");
            await _dispatcher.InvokeAsync(() =>
            {
                Connection.IsConnectedToService = false;
                Connection.ServiceConnectionStatus = "Session terminated — please reconnect";
                StatusBar.StatusText = "Session terminated — account may have been deactivated";
            });
            await RefreshPermissionsAsync();
        };

        // Re-evaluate when user logs in/out via AuthSession
        if (_modeManager.AuthSession != null)
        {
            _modeManager.AuthSession.PropertyChanged += async (_, e) =>
            {
                if (e.PropertyName == nameof(IAuthSession.CurrentUser))
                {
                    await RefreshPermissionsAsync();
                }
            };
        }

        if (startUp)
        {
            var totalSw = Stopwatch.StartNew();
            StatusBar.IsInitialLoading = true;
            _ = Task.Run(async () =>
            {
                try
                {
                    var config = App.Config;

                    if (config.Mode == "MultiUser")
                    {
                        var sw = Stopwatch.StartNew();
                        await _modeManager.InitializeMultiUserAsync(config.ServiceHost, config.ServicePort);
                        sw.Stop();

                        await _dispatcher.InvokeAsync(() =>
                        {
                            Connection.IsServiceMode = true;
                            Connection.ServiceHost = config.ServiceHost;
                            Connection.ServicePort = config.ServicePort;
                        });

                        if (_modeManager.BrokerClient != null && _modeManager.AuthSession != null)
                        {
                            await _modeManager.BrokerClient.ConnectAsync();
                            var authenticated = await TryShowLoginDialogAsync(_modeManager.AuthSession, config.ServiceHost, config.ServicePort);

                            if (authenticated)
                            {
                                await _dispatcher.InvokeAsync(() =>
                                {
                                    Connection.IsConnectedToService = true;
                                    Connection.ServiceConnectionStatus = $"Connected to {config.ServiceHost}:{config.ServicePort}";
                                });
                            }
                            else
                            {
                                await _modeManager.BrokerClient.DisconnectAsync();
                                await _dispatcher.InvokeAsync(() => Connection.ServiceConnectionStatus = "Login cancelled — connect manually");
                            }
                        }
                        else
                        {
                            _logger.LogError("[STARTUP] BrokerClient or AuthSession is null after InitializeMultiUserAsync");
                        }
                    }
                    else
                    {
                        await _modeManager.InitializeAsync();
                    }

                    var swData = Stopwatch.StartNew();
                    await Sidebar.LoadAsync();
                    var sidebarMs = swData.ElapsedMilliseconds;
                    swData.Restart();
                    await AssetGallery.LoadAssetsAsync();
                    var galleryMs = swData.ElapsedMilliseconds;
                    await PropertyInspector.LoadTagAutoCompleteSourceAsync();
                    await activityFeed.LoadRecentActivityAsync(50);
                    swData.Stop();

                    totalSw.Stop();
                    _logger.LogInformation("Startup complete: sidebar={SidebarMs}ms, gallery={GalleryMs}ms, total={TotalMs}ms",
                        sidebarMs, galleryMs, totalSw.ElapsedMilliseconds);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[Startup] FAILED to load sidebar and gallery");
                }
                finally
                {
                    _dispatcher.Post(() => StatusBar.IsInitialLoading = false);
                }
            });
        }
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

    public SidebarViewModel Sidebar { get; }
    public AssetGalleryViewModel AssetGallery { get; }
    public IngestionViewModel Ingestion { get; }
    public MetadataEditorViewModel MetadataEditor { get; }
    public AuditLogViewModel AuditLog { get; }
    public ActivityFeedViewModel ActivityFeed { get; }
    public PropertyInspectorViewModel PropertyInspector { get; }
    public ConnectionViewModel Connection { get; }
    public StatusBarViewModel StatusBar { get; }
    public AiModelSelectorViewModel AiModelSelector { get; }

    public ICommand ShowGalleryCommand { get; }
    public ICommand ShowIngestionCommand { get; }
    public ICommand ShowMetadataEditorCommand { get; }
    public ICommand ShowAuditLogCommand { get; }
    public ICommand ShowActivityFeedCommand { get; }
    public ICommand ShowServiceManagerCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand ImportMetadataCommand { get; }
    public ICommand SavePresetCommand { get; }
    public ICommand LoadPresetCommand { get; }
    public ICommand RotateClockwiseCommand { get; }
    public ICommand RotateCounterClockwiseCommand { get; }
    public ICommand FlipHorizontalCommand { get; }
    public ICommand FlipVerticalCommand { get; }
    public ICommand AssignKeywordDropCommand { get; }
    public ICommand AssignCategoryDropCommand { get; }
    public ICommand AiTagSelectedCommand { get; }
    public ICommand DeleteSelectedCommand { get; }
    public ICommand ShowTrashCommand { get; }
    public ICommand RevealInFolderCommand { get; }
    public ICommand CopyFilePathCommand { get; }
    public ICommand CopyFileCommand { get; }
    public ICommand AddToCollectionCommand { get; }
    public ICommand RateAssetCommand { get; }
    public ICommand SetLabelCommand { get; }
    public ICommand SetFlagCommand { get; }
    public ICommand SetRatingCommand { get; }
    public ICommand SetFlagPickCommand { get; }
    public ICommand SetFlagRejectCommand { get; }
    public ICommand RenameAssetCommand { get; }
    public ICommand FocusSearchCommand { get; }

    /// <summary>
    /// Event fired when the user presses Ctrl+F. MainWindow code-behind
    /// subscribes to focus the keyword SearchableTreeView.
    /// </summary>
    public event Action? RequestFocusSearch;

    public object? CurrentView
    {
        get => _currentView;
        set
        {
            if (_currentView is IDisposable oldDisposable && !ReferenceEquals(_currentView, value))
                oldDisposable.Dispose();
            _currentView = value;
            OnPropertyChanged();
        }
    }

    private void OnAssignKeywordDrop(object? parameter)
    {
        if (parameter is not DropPayload payload || payload.TargetNode is not KeywordNode kw || kw.KeywordId == Guid.Empty) return;
        var assetIds = ParseAssetIds(payload.AssetIdsCsv);
        if (assetIds.Count > 0)
            _bulkQueue.Enqueue(new BulkOperation { AssetIds = assetIds, Name = kw.Name, IsKeyword = true });
    }

    private void OnAssignCategoryDrop(object? parameter)
    {
        if (parameter is not DropPayload payload || payload.TargetNode is not CategoryNode cat || cat.CategoryId == Guid.Empty) return;
        var assetIds = ParseAssetIds(payload.AssetIdsCsv);
        if (assetIds.Count > 0)
            _bulkQueue.Enqueue(new BulkOperation { AssetIds = assetIds, Name = cat.Name, IsKeyword = false });
    }

    private static List<Guid> ParseAssetIds(string csv)
    {
        try { return csv.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).Distinct().ToList(); }
        catch { return []; }
    }

    private async void ShowExportDialog()
    {
        var selected = AssetGallery.SelectedAssets.ToList();
        if (selected.Count == 0) return;
        var vm = new ExportDialogViewModel(_modeManager) { SelectedAssets = selected };
        var dialog = new Views.ExportDialog(vm);
        if (App.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
            await dialog.ShowDialog(desktop.MainWindow);
    }

    private async Task ApplyPresetToSelectedAssetsAsync(MetadataPreset preset)
    {
        var selected = AssetGallery.SelectedAssets.ToList();
        if (selected.Count == 0) return;

        try
        {
            await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);
            var ids = selected.Select(a => a.Id).ToList();
            var assets = await db.DigitalAssets
                .Include(a => a.Keywords)
                .Include(a => a.Categories)
                .Include(a => a.MetadataProfile)
                .Where(a => ids.Contains(a.Id))
                .ToListAsync().ConfigureAwait(false);

            foreach (var asset in assets)
            {
                if (preset.Title != null) asset.Title = preset.Title;
                if (preset.Description != null) asset.Description = preset.Description;
                if (preset.Copyright != null) asset.Copyright = preset.Copyright;
                if (preset.Rating.HasValue) asset.Rating = Math.Clamp(preset.Rating.Value, 0, 5);
                if (preset.Label != null && Enum.TryParse<AssetLabel>(preset.Label, ignoreCase: true, out var label)) asset.Label = label;
                if (preset.Flag != null && Enum.TryParse<AssetFlag>(preset.Flag, ignoreCase: true, out var flag)) asset.Flag = flag;
                if (preset.GpsLatitude.HasValue) asset.GpsLatitude = preset.GpsLatitude;
                if (preset.GpsLongitude.HasValue) asset.GpsLongitude = preset.GpsLongitude;

                // Keywords
                if (!string.IsNullOrWhiteSpace(preset.Keywords))
                {
                    asset.Keywords.Clear();
                    var names = preset.Keywords.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (names.Length > 0)
                        await new KeywordService(db).AssociateKeywordsAsync(asset, names).ConfigureAwait(false);
                }

                // Categories
                if (!string.IsNullOrWhiteSpace(preset.Categories))
                {
                    asset.Categories.Clear();
                    var names = preset.Categories.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (names.Length > 0)
                        await new CategoryService(db).AssociateCategoriesAsync(asset, names).ConfigureAwait(false);
                }

                // Camera metadata
                if (preset.CameraMake != null || preset.CameraModel != null || preset.DateTaken.HasValue)
                {
                    asset.MetadataProfile ??= new MetadataProfile { Id = Guid.NewGuid(), DigitalAssetId = asset.Id };
                    if (preset.CameraMake != null) asset.MetadataProfile.CameraMake = preset.CameraMake;
                    if (preset.CameraModel != null) asset.MetadataProfile.CameraModel = preset.CameraModel;
                    if (preset.DateTaken.HasValue) asset.MetadataProfile.DateTaken = preset.DateTaken;
                }
            }

            await db.SaveChangesAsync().ConfigureAwait(false);

            ToastService.Show($"Preset applied to {assets.Count} asset(s)", Services.ToastLevel.Success);

            // Refresh gallery and sidebar
            _ = Task.Run(async () =>
            {
                await Sidebar.LoadAsync();
                await AssetGallery.LoadAssetsAsync();
            });

            // Refresh the property inspector if a single asset is selected
            if (selected.Count == 1)
            {
                await PropertyInspector.LoadSelectedAssetMetadataAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply preset");
            ToastService.Show("Failed to apply preset", Services.ToastLevel.Error);
        }
    }

    private async Task ShowImportDialogAsync()
    {
        var importVm = new ImportViewModel(_modeManager);
        importVm.ImportCompleted += () =>
        {
            _ = Task.Run(async () =>
            {
                await Sidebar.LoadAsync();
                await AssetGallery.LoadAssetsAsync();
            });
            ToastService.Show("CSV import completed — gallery refreshed", Services.ToastLevel.Success);
        };

        var dialog = new Views.ImportDialog(importVm);
        if (App.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
            await dialog.ShowDialog(desktop.MainWindow);
    }

    private async Task ShowPresetDialogAsync(bool saveMode)
    {
        var presetManager = new PresetManager();
        var vm = new PresetDialogViewModel(presetManager);

        if (saveMode && PropertyInspector.SelectedAsset != null)
        {
            // Load the selected asset's current metadata to capture as a preset
            try
            {
                await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);
                var asset = await db.DigitalAssets
                    .Include(a => a.Keywords)
                    .Include(a => a.Categories)
                    .Include(a => a.MetadataProfile)
                    .FirstOrDefaultAsync(a => a.Id == PropertyInspector.SelectedAsset.Id)
                    .ConfigureAwait(false);

                if (asset != null)
                {
                    vm.SourceAsset = asset;
                    vm.StatusText = $"Ready to save preset from '{asset.Title}' — enter a name and click Save";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load asset for preset capture");
            }
        }

        vm.OnApplyPreset = async preset =>
        {
            await ApplyPresetToSelectedAssetsAsync(preset);
        };

        var dialog = new Views.PresetDialog(vm);
        if (App.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
            await dialog.ShowDialog(desktop.MainWindow);
    }

    private async Task RotateAsync(Func<ImageOrientation, ImageOrientation> transform)
    {
        if (PropertyInspector.SelectedAsset == null || !_modeManager.IsStandalone) return;
        try
        {
            await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);
            var asset = await db.DigitalAssets.FirstOrDefaultAsync(a => a.Id == PropertyInspector.SelectedAsset.Id).ConfigureAwait(false);
            if (asset == null) return;

            var newOrientation = transform(asset.Orientation);
            asset.Orientation = newOrientation;
            await db.SaveChangesAsync().ConfigureAwait(false);

            var dbDir = Path.GetDirectoryName(_modeManager.DbPath) ?? ".";
            var thumbnailDir = Path.Combine(dbDir, "thumbnails");
            var thumbnailService = new ThumbnailService();
            var thumbnailPath = thumbnailService.GetThumbnailPath(asset.StoragePath, thumbnailDir);

            if (File.Exists(thumbnailPath)) File.Delete(thumbnailPath);
            await thumbnailService.GenerateThumbnailAsync(asset.StoragePath, thumbnailDir, newOrientation).ConfigureAwait(false);

            var item = AssetGallery.Assets.FirstOrDefault(a => a.Id == PropertyInspector.SelectedAsset.Id);
            if (item != null)
            {
                await _dispatcher.InvokeAsync(() => item.ThumbnailPath = thumbnailPath);
                await item.LoadThumbnailAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to rotate/flip asset"); }
    }

    private void LaunchServiceManager()
    {
        try
        {
            var exeDir = AppContext.BaseDirectory;
            var solutionRoot = Path.GetFullPath(Path.Combine(exeDir, "..", "..", "..", "..", "..", "src"));
            var serviceManagerPath = Path.GetFullPath(Path.Combine(solutionRoot, "Adam.ServiceManager", "bin", "Debug", "net10.0", "Adam.ServiceManager.exe"));

            if (!File.Exists(serviceManagerPath))
            {
                serviceManagerPath = Path.GetFullPath(Path.Combine(solutionRoot, "Adam.ServiceManager", "bin", "Release", "net10.0", "Adam.ServiceManager.exe"));
            }

            if (!File.Exists(serviceManagerPath))
            {
                // Fallback: try alongside the current executable (published layout)
                serviceManagerPath = Path.Combine(exeDir, "Adam.ServiceManager.exe");
            }

            if (File.Exists(serviceManagerPath))
            {
                _logger.LogInformation("Launching ServiceManager from {Path}", serviceManagerPath);
                Process.Start(new ProcessStartInfo(serviceManagerPath) { UseShellExecute = true });
            }
            else
            {
                _logger.LogWarning("Could not find ServiceManager executable at any expected location.");
                StatusBar.StatusText = "ServiceManager not found";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to launch ServiceManager");
            StatusBar.StatusText = "Failed to launch ServiceManager";
        }
    }

    private static async Task<bool> TryShowLoginDialogAsync(IAuthSession authSession, string host, int port)
    {
        var cfg = App.Config;
        var recentHosts = new ObservableCollection<string>(cfg.RecentHosts);
        var loginVm = new LoginDialogViewModel
        {
            ServiceHost = host,
            ServicePort = port,
            Username = cfg.LastUsername,
            RecentHosts = recentHosts,
            TestConnectionAsync = async (h, p) =>
            {
                try { using var client = new System.Net.Sockets.TcpClient(); using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3)); await client.ConnectAsync(h, p, cts.Token); return null; }
                catch (Exception ex) { return ex.Message; }
            },
            ClearCredentialsAsync = () => { cfg.ServiceHost = "localhost"; cfg.ServicePort = 9100; cfg.LastUsername = string.Empty; cfg.RecentHosts.Clear(); cfg.Save(); return Task.CompletedTask; },
            AuthenticateAsync = async (_, _, username, password) =>
            {
                try { var ok = await authSession.LoginAsync(username, password); return ok ? null : "Authentication failed. Check your credentials."; }
                catch (Exception ex) { return ex.Message; }
            }
        };

        var loginDialog = new Views.LoginDialog(loginVm);
        var mainWindow = App.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null;
        if (mainWindow == null) return false;

        var loginResult = await loginDialog.ShowDialog<bool?>(mainWindow);
        if (loginResult == true)
        {
            cfg.LastUsername = loginVm.Username;
            cfg.PushRecentHost(loginVm.ServiceHost, loginVm.ServicePort);
            cfg.Save();
        }
        return loginResult == true;
    }

    /// <summary>
    /// Trigger C: AI-tags all selected image assets and refreshes the gallery.
    /// </summary>
    private async Task AiTagSelectedAssetsAsync()
    {
        if (_aiTaggingService is null) return;

        var imageIds = AssetGallery.SelectedAssets
            .Select(a => a.Id)
            .ToList();

        if (imageIds.Count == 0) return;

        StatusBar.IsAiTaggingActive = true;
        StatusBar.AiTaggingProgressText = "AI tagging selected assets…";
        StatusBar.AiTaggingPercentage = 0;

        var progress = new Progress<(int completed, int total)>(p =>
        {
            _dispatcher.Post(() =>
            {
                StatusBar.AiTaggingPercentage = p.total > 0 ? (double)p.completed / p.total * 100 : 0;
                StatusBar.AiTaggingProgressText = $"AI tagging... ({p.completed}/{p.total})";
            });
        });

        try
        {
            await _aiTaggingService.TagAssetsAsync(imageIds, progress);
            StatusBar.StatusText = $"AI tagged {imageIds.Count} asset(s)";

            // Refresh gallery + sidebar
            await Sidebar.LoadAsync();
            await AssetGallery.LoadAssetsAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to AI tag selected assets");
            StatusBar.StatusText = $"AI tagging failed: {ex.Message}";
        }
        finally
        {
            StatusBar.IsAiTaggingActive = false;
        }
    }

    /// <summary>
    /// Subscribes to the AiTaggingService's relayed download progress so it surfaces in the status bar (D-14).
    /// </summary>
    private void SubscribeToDownloadProgress(AiTaggingService aiTaggingService)
    {
        aiTaggingService.PropertyChanged += (_, e) =>
        {            if (e.PropertyName == nameof(AiTaggingService.DownloadProgress))
        {
            _dispatcher.Post(() =>
            {
                var pct = aiTaggingService.DownloadProgress;
                StatusBar.IsModelDownloading = pct > 0 && pct < 1.0;
                StatusBar.ModelDownloadPercentage = pct * 100;
                if (pct > 0 && pct < 1.0)
                    StatusBar.StatusText = $"Downloading AI model ({pct * 100:F0}%)";
                else if (pct >= 1.0)
                    StatusBar.StatusText = "AI model ready";
            });
            }            else if (e.PropertyName == nameof(AiTaggingService.IsInitialized) && aiTaggingService.IsInitialized)
        {
            _dispatcher.Post(() =>
            {
                StatusBar.IsModelDownloading = false;
            });
            }
        };
    }

    // ─────────────────────────────────────────────────────────────────
    //  Wave 1: Delete / Trash / Reveal / Copy (T8.17, T8.19)
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Soft-deletes the currently selected assets after confirmation (T8.17).
    /// Uses a single database transaction for bulk operations.
    /// </summary>
    private async Task DeleteSelectedAssetsAsync()
    {
        var selected = AssetGallery.SelectedAssets.ToList();
        if (selected.Count == 0) return;

        var mainWindow = App.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
        if (mainWindow == null) return;

        var message = selected.Count == 1
            ? $"Are you sure you want to delete '{selected[0].Title}'?\n\nThe asset will be moved to the Trash and can be restored later."
            : $"Are you sure you want to delete {selected.Count} selected assets?\n\nAll assets will be moved to the Trash and can be restored later.";

        var confirmed = await Views.ConfirmationDialog.ShowAsync(mainWindow,
            "Delete Assets", message, "Delete", "Cancel", isDestructive: true);

        if (!confirmed) return;

        try
        {
            var succeeded = await _deleteService.BulkSoftDeleteAsync(selected.Select(a => a.Id).ToList());
            var failed = selected.Count - succeeded;

            ToastService.Show($"Deleted {succeeded} asset(s)" + (failed > 0 ? $" ({failed} failed)" : ""),
                failed > 0 ? Services.ToastLevel.Warning : Services.ToastLevel.Success);

            // Refresh gallery and sidebar
            await Sidebar.LoadAsync();
            await AssetGallery.LoadAssetsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bulk delete operation failed");
            ToastService.Show("Delete operation failed", Services.ToastLevel.Error);
        }
    }

    /// <summary>
    /// Switches to the Trash view to browse deleted assets (T8.17).
    /// </summary>
    private async Task ShowTrashViewAsync()
    {
        if (CurrentView is TrashViewModel trashVm)
        {
            await trashVm.LoadDeletedAssetsAsync();
            return;
        }

        var provider = App.ServiceProvider;
        if (provider == null)
        {
            // Fallback: manually create with the available services
            trashVm = new TrashViewModel(_deleteService, ToastService,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<TrashViewModel>.Instance);
        }
        else
        {
            trashVm = provider.GetRequiredService<TrashViewModel>();
        }

        await trashVm.LoadDeletedAssetsAsync();
        CurrentView = trashVm;
    }

    /// <summary>
    /// Opens the file explorer to reveal the first selected asset (T8.19).
    /// </summary>
    private void RevealInFolder()
    {
        var asset = AssetGallery.SelectedAssets.FirstOrDefault();
        if (asset == null || string.IsNullOrEmpty(asset.StoragePath)) return;

        try
        {
            var dir = Path.GetDirectoryName(asset.StoragePath);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                if (OperatingSystem.IsWindows())
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{asset.StoragePath}\"") { UseShellExecute = true });
                }
                else
                {
                    Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to reveal asset in folder");
            ToastService.Show("Could not open file location", Services.ToastLevel.Warning);
        }
    }

    /// <summary>
    /// Copies the file path of the first selected asset to the clipboard (T8.19).
    /// </summary>
    private void CopyFilePath()
    {
        var asset = AssetGallery.SelectedAssets.FirstOrDefault();
        if (asset == null || string.IsNullOrEmpty(asset.StoragePath)) return;

        try
        {
            ClipboardService.SetText(asset.StoragePath);
            ToastService.Show("Path copied to clipboard", Services.ToastLevel.Success);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to copy file path to clipboard");
            ToastService.Show("Failed to copy path", Services.ToastLevel.Error);
        }
    }

    /// <summary>
    /// Copies the file path of the first selected asset to the clipboard (T8.19).
    /// Shows the filename as confirmation.
    /// </summary>
    private async Task CopyFileAsync()
    {
        var asset = AssetGallery.SelectedAssets.FirstOrDefault();
        if (asset == null || string.IsNullOrEmpty(asset.StoragePath) || !File.Exists(asset.StoragePath)) return;

        try
        {
            var name = Path.GetFileName(asset.StoragePath);
            ClipboardService.SetText(asset.StoragePath);
            ToastService.Show($"'{name}' path copied to clipboard", Services.ToastLevel.Success);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to copy file path to clipboard");
            ToastService.Show("Failed to copy path", Services.ToastLevel.Error);
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Adds the selected assets to a collection (placeholder for T8.19).
    /// Collections are currently read-only in the UI; this will be expanded in T8.18.
    /// </summary>
    private async Task AddToCollectionAsync()
    {
        var selected = AssetGallery.SelectedAssets.ToList();
        if (selected.Count == 0) return;

        // Placeholder: Show a toast indicating the action is not yet fully implemented
        ToastService.Show("Add to collection coming soon — collections management is in development", Services.ToastLevel.Info);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Cycles the rating on all selected assets (T8.22 bulk actions).
    /// Each asset gets its own next-rating value independently.
    /// </summary>
    private async Task RateSelectedAsync()
    {
        var selected = AssetGallery.SelectedAssets.ToList();
        if (selected.Count == 0) return;

        try
        {
            await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);
            var ids = selected.Select(a => a.Id).ToList();
            var dbAssets = await db.DigitalAssets
                .Where(a => ids.Contains(a.Id))
                .ToListAsync().ConfigureAwait(false);

            foreach (var dbAsset in dbAssets)
                dbAsset.Rating = (dbAsset.Rating + 1) % 6;

            await db.SaveChangesAsync().ConfigureAwait(false);

            // Update in-memory tiles so the UI reflects new ratings immediately
            foreach (var item in selected)
            {
                var dbMatch = dbAssets.FirstOrDefault(d => d.Id == item.Id);
                if (dbMatch != null)
                    item.Rating = dbMatch.Rating;
            }

            ToastService.Show(selected.Count == 1
                ? $"Rated '{selected[0].Title}' {dbAssets.FirstOrDefault()?.Rating}/5"
                : $"Rated {dbAssets.Count} asset(s)", Services.ToastLevel.Success);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to rate assets");
        }
    }

    /// <summary>
    /// Cycles the color label on all selected assets (T8.22 bulk actions).
    /// Each asset gets its own next-label value independently.
    /// </summary>
    private async Task SetLabelSelectedAsync()
    {
        var selected = AssetGallery.SelectedAssets.ToList();
        if (selected.Count == 0) return;

        try
        {
            await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);
            var ids = selected.Select(a => a.Id).ToList();
            var dbAssets = await db.DigitalAssets
                .Where(a => ids.Contains(a.Id))
                .ToListAsync().ConfigureAwait(false);

            var labels = Enum.GetValues<AssetLabel>();
            foreach (var dbAsset in dbAssets)
            {
                var currentIndex = Array.IndexOf(labels, dbAsset.Label);
                dbAsset.Label = labels[(currentIndex + 1) % labels.Length];
            }

            await db.SaveChangesAsync().ConfigureAwait(false);

            // Update in-memory tiles so the UI reflects new labels immediately
            foreach (var item in selected)
            {
                var dbMatch = dbAssets.FirstOrDefault(d => d.Id == item.Id);
                if (dbMatch != null)
                {
                    (item.ColorLabel, item.ColorBrush) = AssetListItem.MapLabelToDisplay(dbMatch.Label);
                }
            }

            var labelName = dbAssets.FirstOrDefault()?.Label.ToString() ?? "None";
            ToastService.Show(selected.Count == 1
                ? $"Label set to '{labelName}' for '{selected[0].Title}'"
                : $"Label set to '{labelName}' on {dbAssets.Count} asset(s)", Services.ToastLevel.Success);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set label");
        }
    }

    /// <summary>
    /// Sets the rating of all selected assets to a specific value (T8.21 keyboard digits, T8.22 bulk).
    /// The parameter is the rating value as a string ("0"-"5").
    /// </summary>
    private async Task SetRatingByKeyAsync(object? parameter)
    {
        if (parameter is not string ratingStr || !int.TryParse(ratingStr, out var rating))
            return;
        rating = Math.Clamp(rating, 0, 5);

        var selected = AssetGallery.SelectedAssets.ToList();
        if (selected.Count == 0) return;

        try
        {
            await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);
            var ids = selected.Select(a => a.Id).ToList();
            var dbAssets = await db.DigitalAssets
                .Where(a => ids.Contains(a.Id))
                .ToListAsync().ConfigureAwait(false);

            foreach (var dbAsset in dbAssets)
                dbAsset.Rating = rating;

            await db.SaveChangesAsync().ConfigureAwait(false);

            // Update in-memory tiles so the UI reflects new ratings immediately
            foreach (var item in selected)
            {
                var dbMatch = dbAssets.FirstOrDefault(d => d.Id == item.Id);
                if (dbMatch != null)
                    item.Rating = dbMatch.Rating;
            }

            ToastService.Show(selected.Count == 1
                ? (rating > 0 ? $"Rated '{selected[0].Title}' {rating}/5" : $"Rating cleared for '{selected[0].Title}'")
                : $"Set {dbAssets.Count} asset(s) to {rating}/5", Services.ToastLevel.Success);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set rating via keyboard");
        }
    }

    /// <summary>
    /// Cycles the flag on all selected assets (T8.22 bulk actions).
    /// Each asset gets its own next-flag value independently.
    /// </summary>
    private async Task SetFlagSelectedAsync()
    {
        var selected = AssetGallery.SelectedAssets.ToList();
        if (selected.Count == 0) return;

        try
        {
            await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);
            var ids = selected.Select(a => a.Id).ToList();
            var dbAssets = await db.DigitalAssets
                .Where(a => ids.Contains(a.Id))
                .ToListAsync().ConfigureAwait(false);

            foreach (var dbAsset in dbAssets)
            {
                dbAsset.Flag = dbAsset.Flag switch
                {
                    AssetFlag.Unflagged => AssetFlag.Pick,
                    AssetFlag.Pick => AssetFlag.Reject,
                    AssetFlag.Reject => AssetFlag.Unflagged,
                    _ => AssetFlag.Unflagged
                };
            }

            await db.SaveChangesAsync().ConfigureAwait(false);

            // Update in-memory tiles so the UI reflects new flags immediately
            foreach (var item in selected)
            {
                var dbMatch = dbAssets.FirstOrDefault(d => d.Id == item.Id);
                if (dbMatch != null)
                    item.IsFlagged = dbMatch.Flag != AssetFlag.Unflagged;
            }

            var flagName = dbAssets.FirstOrDefault()?.Flag.ToString() ?? "Unflagged";
            ToastService.Show(selected.Count == 1
                ? $"Flag set to '{flagName}' for '{selected[0].Title}'"
                : $"Flag set to '{flagName}' on {dbAssets.Count} asset(s)", Services.ToastLevel.Success);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set flag");
        }
    }

    // ─────────────────────────────────────────────────────────────────
    //  T8.21: Keyboard flag shortcuts (P = Pick, X = Reject)
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sets the flag on all selected assets to a specific value (T8.21 P/X keyboard shortcuts).
    /// </summary>
    private async Task SetFlagByKeyAsync(AssetFlag flag)
    {
        var selected = AssetGallery.SelectedAssets.ToList();
        if (selected.Count == 0) return;

        try
        {
            await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);
            var ids = selected.Select(a => a.Id).ToList();
            var dbAssets = await db.DigitalAssets
                .Where(a => ids.Contains(a.Id))
                .ToListAsync().ConfigureAwait(false);

            foreach (var dbAsset in dbAssets)
                dbAsset.Flag = flag;

            await db.SaveChangesAsync().ConfigureAwait(false);

            // Update in-memory tiles
            foreach (var item in selected)
            {
                var dbMatch = dbAssets.FirstOrDefault(d => d.Id == item.Id);
                if (dbMatch != null)
                    item.IsFlagged = dbMatch.Flag != AssetFlag.Unflagged;
            }

            var flagName = flag.ToString();
            ToastService.Show(selected.Count == 1
                ? $"Flag set to '{flagName}' for '{selected[0].Title}'"
                : $"Flag set to '{flagName}' on {dbAssets.Count} asset(s)", Services.ToastLevel.Success);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set flag via keyboard");
        }
    }

    // ─────────────────────────────────────────────────────────────────
    //  T8.21: F2 Rename asset
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Opens an input dialog to rename the selected asset's title (T8.21 F2 shortcut).
    /// Only works when exactly one asset is selected.
    /// </summary>
    private async Task RenameAssetAsync()
    {
        var asset = AssetGallery.SelectedAssets.FirstOrDefault();
        if (asset == null) return;

        var mainWindow = App.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
        if (mainWindow == null) return;

        var newTitle = await Views.InputDialog.ShowAsync(mainWindow,
            "Rename Asset", "Enter a new name for this asset:",
            confirmText: "Rename", defaultValue: asset.Title);

        if (string.IsNullOrWhiteSpace(newTitle) || newTitle == asset.Title) return;

        try
        {
            await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);
            var dbAsset = await db.DigitalAssets.FirstOrDefaultAsync(a => a.Id == asset.Id).ConfigureAwait(false);
            if (dbAsset == null) return;

            dbAsset.Title = newTitle.Trim();
            await db.SaveChangesAsync().ConfigureAwait(false);

            // Update in-memory tile
            asset.Title = dbAsset.Title;

            ToastService.Show($"Renamed to '{dbAsset.Title}'", Services.ToastLevel.Success);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to rename asset");
            ToastService.Show("Failed to rename asset", Services.ToastLevel.Error);
        }
    }

    // ─────────────────────────────────────────────────────────────────
    //  Phase 7: Permission-aware UI properties (T7.2, T7.3, T7.4, T7.5)
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// The current user's role name ("Viewer", "Editor", "Administrator"), or null.
    /// In standalone mode, returns "Administrator" (full access).
    /// </summary>
    public string? CurrentUserRole
    {
        get
        {
            if (_modeManager.IsStandalone)
                return "Administrator";
            return _modeManager.AuthSession?.CurrentUser?.Role;
        }
    }

    /// <summary>
    /// Whether the current user/session can access ingestion (requires asset:create).
    /// </summary>
    public bool CanIngest => EvaluatePermission("asset:create");

    /// <summary>
    /// Whether the current user/session can edit metadata (requires asset:update).
    /// </summary>
    public bool CanEditMetadata => EvaluatePermission("asset:update");

    /// <summary>
    /// Whether the current user/session can view audit logs (requires audit:read).
    /// </summary>
    public bool CanAudit => EvaluatePermission("audit:read");

    /// <summary>
    /// Whether the current user/session can access admin features (requires user:*).
    /// </summary>
    public bool CanAdminister => EvaluatePermission("user:*");

    /// <summary>
    /// Whether the current user/session can AI-tag assets (requires asset:update).
    /// </summary>
    public bool CanAiTag => _aiTaggingService != null && EvaluatePermission("asset:update");

    /// <summary>
    /// Human-readable session status text shown in the status bar.
    /// </summary>
    public string SessionStatusText
    {
        get
        {
            if (_modeManager.IsStandalone)
                return "Local mode — full access";

            var auth = _modeManager.AuthSession;
            if (auth == null || !auth.IsLoggedIn)
                return "Not logged in";

            var role = auth.CurrentUser?.Role ?? "?";
            if (auth.IsTokenExpired())
                return $"Session expired — {role} (relogin required)";

            return $"{auth.CurrentUser?.Username} — {role}";
        }
    }

    /// <summary>
    /// Tooltip text shown when hovering over disabled metadata editing controls.
    /// Explains why the control is disabled based on the current user's role and session state.
    /// (Phase 7 T7.2 — permission tooltips)
    /// </summary>
    public string? EditPermissionTooltip
    {
        get
        {
            if (_modeManager.IsStandalone)
                return null;

            if (!EvaluatePermission("asset:update"))
                return GetEditPermissionTooltipText();

            return null;
        }
    }

    /// <summary>
    /// Returns the human-readable tooltip text explaining why edit controls are disabled.
    /// Shared between the right-panel (EditPermissionTooltip) and MetadataEditorView.
    /// </summary>
    private string GetEditPermissionTooltipText()
    {
        return GetNavTooltipText("edit metadata", "Requires Editor or Administrator role");
    }

    /// <summary>
    /// Returns session-state-aware tooltip text for permission-gated navigation buttons.
    /// </summary>
    /// <param name="actionDescription">Lowercase verb phrase, e.g. "ingest assets", "view audit log".</param>
    /// <param name="requiredRole">The role requirement, e.g. "Requires Editor or Administrator role".</param>
    private string GetNavTooltipText(string actionDescription, string requiredRole)
    {
        var auth = _modeManager.AuthSession;
        if (auth == null || !auth.IsLoggedIn)
            return $"Sign in to {actionDescription}";

        if (auth.IsTokenExpired())
            return $"Session expired — re-login required to {actionDescription}";

        return requiredRole;
    }

    /// <summary>
    /// Tooltip for the Ingest navigation button. Returns <c>null</c> in standalone mode or when permission is granted.
    /// </summary>
    public string? IngestPermissionTooltip
        => !_modeManager.IsStandalone && !EvaluatePermission("asset:create") ? GetNavTooltipText("ingest assets", "Requires Editor or Administrator role") : null;

    /// <summary>
    /// Tooltip for the Metadata navigation button. Returns <c>null</c> in standalone mode or when permission is granted.
    /// </summary>
    public string? MetadataPermissionTooltip
        => !_modeManager.IsStandalone && !EvaluatePermission("asset:update") ? GetNavTooltipText("edit metadata", "Requires Editor or Administrator role") : null;

    /// <summary>
    /// Tooltip for the Audit navigation button. Returns <c>null</c> in standalone mode or when permission is granted.
    /// </summary>
    public string? AuditPermissionTooltip
        => !_modeManager.IsStandalone && !EvaluatePermission("audit:read") ? GetNavTooltipText("view audit log", "Requires Administrator role") : null;

    /// <summary>
    /// Tooltip for the Server Admin button. Returns <c>null</c> in standalone mode or when permission is granted.
    /// </summary>
    public string? AdminPermissionTooltip
        => !_modeManager.IsStandalone && !EvaluatePermission("user:*") ? GetNavTooltipText("manage server", "Requires Administrator role") : null;

    /// <summary>
    /// Tooltip for the AI Tag Selected button in the gallery toolbar.
    /// Shows a descriptive message when permission is granted, or a permission-denied
    /// message when the user lacks access. Returns <c>null</c> only when the service
    /// itself is unavailable (button should be hidden/disabled).
    /// </summary>
    public string? AiTagPermissionTooltip
    {
        get
        {
            if (_aiTaggingService == null)
                return null;

            if (_modeManager.IsStandalone)
                return "AI-tag selected image assets using local on-device inference";

            return !EvaluatePermission("asset:update")
                ? GetNavTooltipText("AI tag assets", "Requires Editor or Administrator role")
                : "AI-tag selected image assets using local on-device inference";
        }
    }

    private bool EvaluatePermission(string permission)
    {
        // Standalone mode: all permissions granted
        if (_modeManager.IsStandalone)
            return true;

        // Multi-user mode: check the current user's role
        var role = _modeManager.AuthSession?.CurrentUser?.Role;
        if (string.IsNullOrEmpty(role))
            return false;

        // Check token expiry: expired tokens = no permissions
        if (_modeManager.AuthSession!.IsTokenExpired())
            return false;

        return PermissionEvaluator.HasPermission(role, permission);
    }

    /// <summary>
    /// Called when connection state, login state, or mode changes.
    /// Fires PropertyChanged for all permission properties and re-evaluates command CanExecute.
    /// </summary>
    private async Task RefreshPermissionsAsync()
    {
        await _dispatcher.InvokeAsync(() =>
        {
            OnPropertyChanged(nameof(CurrentUserRole));
            OnPropertyChanged(nameof(CanIngest));
            OnPropertyChanged(nameof(CanEditMetadata));
            OnPropertyChanged(nameof(CanAudit));
            OnPropertyChanged(nameof(CanAdminister));
            OnPropertyChanged(nameof(CanAiTag));
            OnPropertyChanged(nameof(SessionStatusText));
            OnPropertyChanged(nameof(EditPermissionTooltip));
            OnPropertyChanged(nameof(IngestPermissionTooltip));
            OnPropertyChanged(nameof(MetadataPermissionTooltip));
            OnPropertyChanged(nameof(AuditPermissionTooltip));
            OnPropertyChanged(nameof(AdminPermissionTooltip));
            OnPropertyChanged(nameof(AiTagPermissionTooltip));

            // Update PropertyInspector per-edit permission (Phase 7 T7.2)
            PropertyInspector.CanEdit = EvaluatePermission("asset:update");

            // Gate MetadataEditorView editing controls (Phase 7 T7.2)
            // Set tooltip text FIRST, then toggle CanEdit which fires PropertyChanged for EditPermissionTooltip
            var canEdit = EvaluatePermission("asset:update");
            MetadataEditor.EditPermissionTooltip = canEdit
                ? string.Empty
                : GetEditPermissionTooltipText();
            MetadataEditor.CanEdit = canEdit;

            // T10.3: Refresh sidebar CRUD permissions
            Sidebar.RefreshPermissions();

            // Re-evaluate command CanExecute for permission-gated commands
            (ShowIngestionCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ShowMetadataEditorCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ShowAuditLogCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ShowServiceManagerCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ExportCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ImportMetadataCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (SavePresetCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (LoadPresetCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RotateClockwiseCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RotateCounterClockwiseCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (FlipHorizontalCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (FlipVerticalCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (AiTagSelectedCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (DeleteSelectedCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RateAssetCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (SetLabelCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (SetFlagCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (SetRatingCommand as RelayCommand)?.RaiseCanExecuteChanged();
        });

        // If token is expired, show a warning in the status bar
        if (!_modeManager.IsStandalone && _modeManager.AuthSession?.IsTokenExpired() == true)
        {
            _logger.LogWarning("Session token expired — user must re-login");
            StatusBar.StatusText = "Session expired — please log out and reconnect";
        }
    }

    /// <summary>
    /// Periodic timer tick — validates token with broker to detect role changes
    /// or account deactivation (Phase 7 T7.3, T7.5).
    ///
    /// Calls <c>ValidateTokenAsync</c> which queries the DB for the user's current
    /// role and active status. If the role changed, <c>CurrentUser</c> is updated
    /// and triggers <c>RefreshPermissionsAsync</c> automatically via PropertyChanged.
    /// If the account was deactivated, the session is cleared and ForceLogout fires.
    /// </summary>
    private async void OnSessionCheckTick(object? sender, EventArgs e)
    {
        if (_modeManager.IsStandalone)
            return;

        var auth = _modeManager.AuthSession;
        if (auth == null || !auth.IsLoggedIn)
            return;

        // Check token expiry first (fast, local check)
        if (auth.IsTokenExpired())
        {
            _logger.LogWarning("Session token expired (detected by timer)");        await _dispatcher.InvokeAsync(() =>
                {
                    StatusBar.StatusText = "Session expired — please log out and reconnect";
                });
            await RefreshPermissionsAsync();
            return;
        }

        // Phase 7 T7.5: Validate token with broker to detect role changes or deactivation.
        // ValidateTokenAsync now queries the DB on the broker side, so if an admin
        // changed the role or deactivated the account, this will detect it.
        try
        {
            var profile = await Connection.ValidateSessionAsync();

            if (profile != null)
            {
                // CurrentUser.Role was already updated by ValidateTokenAsync if it changed.
                // The PropertyChanged handler in the constructor fires RefreshPermissionsAsync automatically.
                _logger.LogDebug("Session check: user={Username}, role={Role}", profile.Username, profile.Role);

                // Update session status text in the status bar
                await _dispatcher.InvokeAsync(() =>
                {
                    OnPropertyChanged(nameof(SessionStatusText));
                });
            }
            // If profile is null, ForceLogout was already fired by ValidateSessionAsync
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Session validation failed during periodic check");
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
