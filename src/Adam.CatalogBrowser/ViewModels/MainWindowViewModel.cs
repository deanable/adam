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
    private readonly DispatcherTimer _sessionCheckTimer;

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

        // Phase 7: Session check timer — checks token expiry every 60s (T7.3)
        _sessionCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _sessionCheckTimer.Tick += OnSessionCheckTick;
        _sessionCheckTimer.Start();

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
        ExportCommand = new RelayCommand(_ => ShowExportDialog(), _ => CanEditMetadata);

        RotateClockwiseCommand = new RelayCommand(async _ => await RotateAsync(ImageAdjustmentService.Rotate90Cw), _ => CanEditMetadata);
        RotateCounterClockwiseCommand = new RelayCommand(async _ => await RotateAsync(ImageAdjustmentService.Rotate90Ccw), _ => CanEditMetadata);
        FlipHorizontalCommand = new RelayCommand(async _ => await RotateAsync(ImageAdjustmentService.ToggleFlipHorizontal), _ => CanEditMetadata);
        FlipVerticalCommand = new RelayCommand(async _ => await RotateAsync(ImageAdjustmentService.ToggleFlipVertical), _ => CanEditMetadata);

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
            await Dispatcher.UIThread.InvokeAsync(() =>
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
    /// Returns <c>null</c> when the full condition (service available + permission granted) is met.
    /// Returns session-state text when permission is lacking but the service is available.
    /// Returns <c>null</c> when the service itself is unavailable (button remains hidden).
    /// </summary>
    public string? AiTagPermissionTooltip
    {
        get
        {
            if (_aiTaggingService == null)
                return null;

            if (_modeManager.IsStandalone)
                return null;

            return !EvaluatePermission("asset:update") ? GetNavTooltipText("AI tag assets", "Requires Editor or Administrator role") : null;
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
        await Dispatcher.UIThread.InvokeAsync(() =>
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

            // Re-evaluate command CanExecute for permission-gated commands
            (ShowIngestionCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ShowMetadataEditorCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ShowAuditLogCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ShowServiceManagerCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ExportCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RotateClockwiseCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RotateCounterClockwiseCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (FlipHorizontalCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (FlipVerticalCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (AiTagSelectedCommand as RelayCommand)?.RaiseCanExecuteChanged();
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
            _logger.LogWarning("Session token expired (detected by timer)");
            await Dispatcher.UIThread.InvokeAsync(() =>
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
                await Dispatcher.UIThread.InvokeAsync(() =>
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
