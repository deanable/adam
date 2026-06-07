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
using Avalonia.Threading;
using LiquidVision.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Adam.CatalogBrowser.ViewModels;

public class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly ModeManager _modeManager;
    private readonly MetadataWritebackService _writeback;
    private readonly AiTaggingService? _aiTaggingService;
    private readonly BulkOperationQueue _bulkQueue;
    private object? _currentView;

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
        AiTaggingService? aiTaggingService = null,
        bool startUp = true)
    {
        _logger = logger;
        _modeManager = modeManager;
        _writeback = writeback;
        _bulkQueue = bulkQueue;
        Sidebar = sidebar;
        AssetGallery = assetGallery;
        Ingestion = ingestion;
        MetadataEditor = metadataEditor;
        AuditLog = auditLog;
        PropertyInspector = propertyInspector;
        Connection = connection;
        StatusBar = statusBar;
        _currentView = assetGallery;

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
        });

        ShowMetadataEditorCommand = new RelayCommand(async _ =>
        {
            if (PropertyInspector.SelectedAsset != null)
                await metadataEditor.LoadAssetAsync(PropertyInspector.SelectedAsset.Id);
            CurrentView = metadataEditor;
        });

        ShowAuditLogCommand = new RelayCommand(_ => CurrentView = auditLog);
        ShowServiceManagerCommand = new RelayCommand(_ => LaunchServiceManager());
        ExportCommand = new RelayCommand(_ => ShowExportDialog());

        RotateClockwiseCommand = new RelayCommand(async _ => await RotateAsync(ImageAdjustmentService.Rotate90Cw));
        RotateCounterClockwiseCommand = new RelayCommand(async _ => await RotateAsync(ImageAdjustmentService.Rotate90Ccw));
        FlipHorizontalCommand = new RelayCommand(async _ => await RotateAsync(ImageAdjustmentService.ToggleFlipHorizontal));
        FlipVerticalCommand = new RelayCommand(async _ => await RotateAsync(ImageAdjustmentService.ToggleFlipVertical));

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

            assetGallery.ApplyFilter(mediaFormat, folderPath, keywordIds, categoryIds, dateFrom, dateTo);
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
        };

        if (startUp)
        {
            ConnectionDebugLogger.Info("[STARTUP] MainWindowViewModel startup sequence beginning");
            StatusBar.IsInitialLoading = true;
            _ = Task.Run(async () =>
            {
                try
                {
                    var config = App.Config;
                    ConnectionDebugLogger.Info($"[STARTUP] Config mode={config.Mode}, host={config.ServiceHost}:{config.ServicePort}");

                    if (config.Mode == "MultiUser")
                    {
                        ConnectionDebugLogger.Info($"[STARTUP] Initializing multi-user mode (host={config.ServiceHost}:{config.ServicePort})");
                        await _modeManager.InitializeMultiUserAsync(config.ServiceHost, config.ServicePort);
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            Connection.IsServiceMode = true;
                            Connection.ServiceHost = config.ServiceHost;
                            Connection.ServicePort = config.ServicePort;
                        });

                        if (_modeManager.BrokerClient != null && _modeManager.AuthSession != null)
                        {
                            ConnectionDebugLogger.Info("[STARTUP] Auto-connecting to broker...");
                            await _modeManager.BrokerClient.ConnectAsync();
                            var authenticated = await Dispatcher.UIThread.InvokeAsync(() =>
                                TryShowLoginDialogAsync(_modeManager.AuthSession, config.ServiceHost, config.ServicePort));

                            if (authenticated)
                            {
                                ConnectionDebugLogger.Info("[STARTUP] Auto-connect succeeded, authenticated");
                                await Dispatcher.UIThread.InvokeAsync(() =>
                                {
                                    Connection.IsConnectedToService = true;
                                    Connection.ServiceConnectionStatus = $"Connected to {config.ServiceHost}:{config.ServicePort}";
                                });
                            }
                            else
                            {
                                ConnectionDebugLogger.Warn("[STARTUP] Auto-connect succeeded but authentication cancelled/failed");
                                await _modeManager.BrokerClient.DisconnectAsync();
                                await Dispatcher.UIThread.InvokeAsync(() => Connection.ServiceConnectionStatus = "Login cancelled — connect manually");
                            }
                        }
                        else
                        {
                            ConnectionDebugLogger.Error("[STARTUP] BrokerClient or AuthSession is null after InitializeMultiUserAsync");
                        }
                    }
                    else
                    {
                        ConnectionDebugLogger.Info("[STARTUP] Initializing standalone/local mode");
                        await _modeManager.InitializeAsync();
                    }

                    await Sidebar.LoadAsync();
                    await AssetGallery.LoadAssetsAsync();
                    await PropertyInspector.LoadTagAutoCompleteSourceAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[Startup] FAILED to load sidebar and gallery");
                }
                finally
                {
                    Dispatcher.UIThread.Post(() => StatusBar.IsInitialLoading = false);
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
    public PropertyInspectorViewModel PropertyInspector { get; }
    public ConnectionViewModel Connection { get; }
    public StatusBarViewModel StatusBar { get; }

    public ICommand ShowGalleryCommand { get; }
    public ICommand ShowIngestionCommand { get; }
    public ICommand ShowMetadataEditorCommand { get; }
    public ICommand ShowAuditLogCommand { get; }
    public ICommand ShowServiceManagerCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand RotateClockwiseCommand { get; }
    public ICommand RotateCounterClockwiseCommand { get; }
    public ICommand FlipHorizontalCommand { get; }
    public ICommand FlipVerticalCommand { get; }
    public ICommand AssignKeywordDropCommand { get; }
    public ICommand AssignCategoryDropCommand { get; }
    public ICommand AiTagSelectedCommand { get; }

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
        var dialog = new Views.ExportDialog();
        if (dialog.DataContext is ExportDialogViewModel vm) vm.SelectedAssets = selected;
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
                await Dispatcher.UIThread.InvokeAsync(() => item.ThumbnailPath = thumbnailPath);
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
            Dispatcher.UIThread.Post(() =>
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
        {
            if (e.PropertyName == nameof(AiTaggingService.DownloadProgress))
            {
                Dispatcher.UIThread.Post(() =>
                {
                    var pct = aiTaggingService.DownloadProgress;
                    StatusBar.IsModelDownloading = pct > 0 && pct < 1.0;
                    StatusBar.ModelDownloadPercentage = pct * 100;
                    if (pct > 0 && pct < 1.0)
                        StatusBar.StatusText = $"Downloading AI model ({pct * 100:F0}%)";
                    else if (pct >= 1.0)
                        StatusBar.StatusText = "AI model ready";
                });
            }
            else if (e.PropertyName == nameof(AiTaggingService.IsInitialized) && aiTaggingService.IsInitialized)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    StatusBar.IsModelDownloading = false;
                });
            }
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
