using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Adam.CatalogBrowser.Services;
using Adam.Shared.Extractors;
using Adam.Shared.Services;
using Adam.Shared.Contracts;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Avalonia.Controls;
using Avalonia.Threading;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Adam.CatalogBrowser.Models.Sidebar;
// Resolves ambiguity with Adam.Shared.Contracts.CollectionNode (protobuf message)
using CollectionNode = Adam.CatalogBrowser.Models.Sidebar.CollectionNode;
// Resolves ambiguity with Adam.Shared.Services.SavedSearchService
using SavedSearchService = Adam.CatalogBrowser.Services.SavedSearchService;

namespace Adam.CatalogBrowser.ViewModels;

public class SidebarViewModel : INotifyPropertyChanged
{
    private readonly ModeManager _modeManager;
    private readonly ILogger<SidebarViewModel> _logger;
    // _folderScanService moved into FolderTreeService
    private readonly MediaFormatService _mediaFormatService;
    private readonly DateTakenTreeService _dateTakenTreeService;
    private readonly SavedSearchService _savedSearchService;
    private readonly FolderTreeService _folderTreeService;
    private readonly CollectionTreeService _collectionTreeService;
    private readonly KeywordTreeService _keywordTreeService;
    private readonly CategoryTreeService _categoryTreeService;
    private CategoryNode _selectedMediaFormat;
    private CategoryNode? _selectedMetadataCategory;
    private FolderNode? _selectedFolder;
    private CollectionNode? _selectedCollection;
    private KeywordNode? _selectedKeyword;
    // Collections, Keywords, MetadataCategories now owned by respective tree services
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private bool _isLoading;
    private DateTakenNode? _selectedDateTaken;
    private string? _activeSearchQueryText;
    private SavedSearchNode? _selectedSavedSearch;
    private SearchHistoryNode? _selectedRecentSearch;

    public SidebarViewModel(ModeManager modeManager, ILogger<SidebarViewModel> logger, MediaFormatService mediaFormatService, DateTakenTreeService dateTakenTreeService, SavedSearchService savedSearchService, FolderTreeService folderTreeService, CollectionTreeService collectionTreeService, KeywordTreeService keywordTreeService, CategoryTreeService categoryTreeService, FolderScanService? folderScanService = null)
    {
        _modeManager = modeManager;
        _logger = logger;
        _mediaFormatService = mediaFormatService;
        _dateTakenTreeService = dateTakenTreeService;
        _savedSearchService = savedSearchService;
        _selectedMediaFormat = MediaFormats[0];

        // T8.18 / T10.3: Sidebar CRUD commands with permission gating
        CreateCollectionCommand = new RelayCommand(async _ => await PromptCreateCollectionAsync(), _ => CanCreateMetadata);
        RenameCollectionCommand = new RelayCommand(async _ => await PromptRenameCollectionAsync(), _ => CanEditMetadata);
        DeleteCollectionCommand = new RelayCommand(async _ => await PromptDeleteCollectionAsync(), _ => CanEditMetadata);

        CreateKeywordCommand = new RelayCommand(async _ => await PromptCreateKeywordAsync(), _ => CanCreateMetadata);
        RenameKeywordCommand = new RelayCommand(async _ => await PromptRenameKeywordAsync(), _ => CanEditMetadata);
        DeleteKeywordCommand = new RelayCommand(async _ => await PromptDeleteKeywordAsync(), _ => CanEditMetadata);

        CreateCategoryCommand = new RelayCommand(async _ => await PromptCreateCategoryAsync(), _ => CanCreateMetadata);
        RenameCategoryCommand = new RelayCommand(async _ => await PromptRenameCategoryAsync(), _ => CanEditMetadata);
        DeleteCategoryCommand = new RelayCommand(async _ => await PromptDeleteCategoryAsync(), _ => CanEditMetadata);

        // T8.18: Context menu display — these receive the clicked node as parameter.
        // They set the selected item, then show a MenuFlyout at the clicked location.
        ShowKeywordMenuCommand = new RelayCommand(ShowKeywordContextMenu);
        ShowCategoryMenuCommand = new RelayCommand(ShowCategoryContextMenu);
        ShowFolderMenuCommand = new RelayCommand(ShowFolderContextMenu);

        // T10.2: Inline rename commands — called from SearchableTreeView when user commits rename
        CommitRenameCommand = new RelayCommand(CommitNodeRename);

        // T10.5: Folder-specific commands
        RevealFolderCommand = new RelayCommand(RevealFolder, _ => SelectedFolder != null && SelectedFolder.Path.Length > 0);
        RescanFolderCommand = new RelayCommand(async _ => await RescanFolderAsync(), _ => SelectedFolder != null && SelectedFolder.Path.Length > 0);

        // T10.1 / T10.11: Filter commands — used in context menus
        FilterByThisCommand = new RelayCommand(OnFilterByThis);
        ClearFilterCommand = new RelayCommand(OnClearFilter);

        // T10.2: F2 inline rename keyboard shortcut
        F2RenameCommand = new RelayCommand(_ => BeginRenameSelectedNode(), _ => SelectedKeyword != null || SelectedCollection != null || SelectedMetadataCategory != null);

        // Phase 19: Saved search & search history commands
        ExecuteSavedSearchCommand = new RelayCommand(OnExecuteSavedSearch, _ => SelectedSavedSearch != null);
        ExecuteRecentSearchCommand = new RelayCommand(OnExecuteRecentSearch, _ => SelectedRecentSearch != null);
        DeleteSavedSearchCommand = new RelayCommand(OnDeleteSavedSearch, _ => SelectedSavedSearch != null);
        TogglePinSavedSearchCommand = new RelayCommand(OnTogglePinSavedSearch, _ => SelectedSavedSearch != null);
        ClearRecentSearchesCommand = new RelayCommand(_ => ClearRecentSearches());
    }

    public ObservableCollection<FolderNode> Folders => _folderTreeService.Roots;

    public ObservableCollection<CollectionNode> Collections => _collectionTreeService.Roots;

    public ObservableCollection<KeywordNode> Keywords => _keywordTreeService.Roots;

    public ObservableCollection<CategoryNode> MediaFormats => _mediaFormatService.MediaFormats;

    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(); }
    }

    public ObservableCollection<CategoryNode> MetadataCategories => _categoryTreeService.Roots;

    public ObservableCollection<DateTakenNode> DateTakenTree => _dateTakenTreeService.DateTakenTree;

    // T14.5: Advanced filter properties
    private int _selectedRating;
    private int _selectedLabel;
    private int _selectedFlag;
    private bool _isRatingFilterActive;
    private bool _isLabelFilterActive;
    private bool _isFlagFilterActive;

    /// <summary>
    /// Rating filter: 0 = Any, 1 = Unrated, 2..6 = 1..5 stars.
    /// </summary>
    public int SelectedRatingFilter
    {
        get => _selectedRating;
        set
        {
            if (_selectedRating == value) return;
            _selectedRating = value;
            _isRatingFilterActive = value > 0;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsRatingFilterActive));
            OnFilterChanged();
        }
    }

    public bool IsRatingFilterActive => _isRatingFilterActive;

    /// <summary>
    /// Label filter: 0 = Any, 1 = None, 2..6 = Red..Purple.
    /// </summary>
    public int SelectedLabelFilter
    {
        get => _selectedLabel;
        set
        {
            if (_selectedLabel == value) return;
            _selectedLabel = value;
            _isLabelFilterActive = value > 0;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsLabelFilterActive));
            OnFilterChanged();
        }
    }

    public bool IsLabelFilterActive => _isLabelFilterActive;

    /// <summary>
    /// Flag filter: 0 = Any, 1 = Unflagged, 2 = Pick, 3 = Reject.
    /// </summary>
    public int SelectedFlagFilter
    {
        get => _selectedFlag;
        set
        {
            if (_selectedFlag == value) return;
            _selectedFlag = value;
            _isFlagFilterActive = value > 0;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsFlagFilterActive));
            OnFilterChanged();
        }
    }

    public bool IsFlagFilterActive => _isFlagFilterActive;

    // T14.5: Advanced filter option lists
    public ObservableCollection<string> RatingFilterOptions { get; } =
    [
        "Any rating",
        "Unrated",
        "★ 1",
        "★★ 2",
        "★★★ 3",
        "★★★★ 4",
        "★★★★★ 5"
    ];

    public ObservableCollection<string> LabelFilterOptions { get; } =
    [
        "Any label",
        "None",
        "Red",
        "Green",
        "Blue",
        "Yellow",
        "Purple"
    ];

    public ObservableCollection<string> FlagFilterOptions { get; } =
    [
        "Any flag",
        "Unflagged",
        "Pick",
        "Reject"
    ];

    public CategoryNode SelectedMediaFormat
    {
        get => _selectedMediaFormat;
        set { _selectedMediaFormat = value; OnPropertyChanged(); OnMediaFormatChanged(); }
    }

    public CategoryNode? SelectedMetadataCategory
    {
        get => _selectedMetadataCategory;
        set { _selectedMetadataCategory = value; OnPropertyChanged(); OnFilterChanged(); }
    }

    public FolderNode? SelectedFolder
    {
        get => _selectedFolder;
        set { _selectedFolder = value; OnPropertyChanged(); OnFilterChanged(); }
    }

    public CollectionNode? SelectedCollection
    {
        get => _selectedCollection;
        set { _selectedCollection = value; OnPropertyChanged(); OnFilterChanged(); }
    }

    public KeywordNode? SelectedKeyword
    {
        get => _selectedKeyword;
        set { _selectedKeyword = value; OnPropertyChanged(); OnFilterChanged(); }
    }

    public DateTakenNode? SelectedDateTaken
    {
        get => _selectedDateTaken;
        set
        {
            if (_selectedDateTaken != value)
            {
                _selectedDateTaken = value;
                OnPropertyChanged();
                OnFilterChanged();
            }
        }
    }

    /// <summary>
    /// The currently active search query text, set when a saved search or
    /// quick search is selected from the sidebar. Used by MainWindowViewModel
    /// to pass the query to ApplyFilter (Phase 19 Wave 7).
    /// </summary>
    public string? ActiveSearchQueryText
    {
        get => _activeSearchQueryText;
        set
        {
            _activeSearchQueryText = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<SavedSearchNode> SavedSearches => _savedSearchService.SavedSearches;

    public ObservableCollection<SearchHistoryNode> RecentSearches => _savedSearchService.RecentSearches;

    public SavedSearchNode? SelectedSavedSearch
    {
        get => _selectedSavedSearch;
        set
        {
            if (_selectedSavedSearch == value) return;

            // Clear previous selection's IsActiveFilter
            if (_selectedSavedSearch != null)
                _selectedSavedSearch.IsActiveFilter = false;

            _selectedSavedSearch = value;

            // Update ActiveSearchQueryText
            ActiveSearchQueryText = value?.QueryText;

            // Mutual exclusion: selecting a saved search deselects recent search
            if (value != null && _selectedRecentSearch != null)
            {
                _selectedRecentSearch.IsActiveFilter = false;
                _selectedRecentSearch = null;
                OnPropertyChanged(nameof(SelectedRecentSearch));
            }

            OnPropertyChanged();
            OnFilterChanged();
        }
    }

    public SearchHistoryNode? SelectedRecentSearch
    {
        get => _selectedRecentSearch;
        set
        {
            if (_selectedRecentSearch == value) return;

            // Clear previous selection's IsActiveFilter
            if (_selectedRecentSearch != null)
                _selectedRecentSearch.IsActiveFilter = false;

            _selectedRecentSearch = value;

            // Update ActiveSearchQueryText
            ActiveSearchQueryText = value?.QueryText;

            // Mutual exclusion: selecting a recent search deselects saved search
            if (value != null && _selectedSavedSearch != null)
            {
                _selectedSavedSearch.IsActiveFilter = false;
                _selectedSavedSearch = null;
                OnPropertyChanged(nameof(SelectedSavedSearch));
            }

            OnPropertyChanged();
            OnFilterChanged();
        }
    }

    // ── T8.18: Sidebar CRUD commands ──
    public ICommand CreateCollectionCommand { get; }
    public ICommand RenameCollectionCommand { get; }
    public ICommand DeleteCollectionCommand { get; }
    public ICommand CreateKeywordCommand { get; }
    public ICommand RenameKeywordCommand { get; }
    public ICommand DeleteKeywordCommand { get; }
    public ICommand CreateCategoryCommand { get; }
    public ICommand RenameCategoryCommand { get; }
    public ICommand DeleteCategoryCommand { get; }

    // ── T8.18: Context menu display commands ──
    // These accept the clicked node as parameter and show a ContextFlyout.
    // They are wired via SearchableTreeView.NodeContextMenuCommand.
    public ICommand ShowKeywordMenuCommand { get; }
    public ICommand ShowCategoryMenuCommand { get; }
    public ICommand ShowFolderMenuCommand { get; }

    // ── T10.2: Inline rename commit ──
    public ICommand CommitRenameCommand { get; }

    // ── T10.1/T10.11: Filter commands ──
    public ICommand FilterByThisCommand { get; }
    public ICommand ClearFilterCommand { get; }

    // ── T10.2: F2 rename keyboard shortcut ──
    public ICommand F2RenameCommand { get; }

    // ── T10.5: Folder commands ──
    public ICommand RevealFolderCommand { get; }
    public ICommand RescanFolderCommand { get; }

    // ── Phase 19: Saved search & search history commands ──
    public ICommand ExecuteSavedSearchCommand { get; }
    public ICommand ExecuteRecentSearchCommand { get; }
    public ICommand DeleteSavedSearchCommand { get; }
    public ICommand TogglePinSavedSearchCommand { get; }
    public ICommand ClearRecentSearchesCommand { get; }

    // ── T10.3: Permission gating ──
    public bool CanEditMetadata => _modeManager.IsStandalone || EvaluatePermission("asset:update");
    public bool CanCreateMetadata => _modeManager.IsStandalone || EvaluatePermission("collection:create") || EvaluatePermission("asset:create");

    /// <summary>
    /// Raises PropertyChanged for permission properties and re-evaluates CRUD command CanExecute.
    /// Called by MainWindowViewModel.RefreshPermissionsAsync after login/logout/role change.
    /// </summary>
    public void RefreshPermissions()
    {
        OnPropertyChanged(nameof(CanEditMetadata));
        OnPropertyChanged(nameof(CanCreateMetadata));
        (CreateCollectionCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (RenameCollectionCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (DeleteCollectionCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (CreateKeywordCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (RenameKeywordCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (DeleteKeywordCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (CreateCategoryCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (RenameCategoryCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (DeleteCategoryCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    public event Action? FilterChanged;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        await Dispatcher.UIThread.InvokeAsync(() => IsLoading = true);
        _logger.LogInformation("[LoadAsync] Acquiring load lock...");
        await _loadLock.WaitAsync(ct).ConfigureAwait(false);
        _logger.LogInformation("[LoadAsync] Lock acquired. Starting parallel loads...");
        try
        {
            await Task.WhenAll(
                _folderTreeService.LoadAsync(ct),
                _collectionTreeService.LoadAsync(ct),
                _keywordTreeService.LoadAsync(ct),
                _mediaFormatService.LoadAsync(ct),
                _categoryTreeService.LoadAsync(ct),
                _dateTakenTreeService.LoadAsync(ct),
                _savedSearchService.LoadSavedSearchesAsync(ct),
                _savedSearchService.LoadRecentSearchesAsync(ct)).ConfigureAwait(false);
            _logger.LogInformation("[LoadAsync] All parallel loads completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LoadAsync] One or more parallel loads failed. Exception type={ExType}, Message={Message}", ex.GetType().Name, ex.Message);
            throw;
        }
        finally
        {
            _loadLock.Release();
            _logger.LogInformation("[LoadAsync] Lock released");
            await Dispatcher.UIThread.InvokeAsync(() => IsLoading = false);
        }
    }



    private void OnMediaFormatChanged() => FilterChanged?.Invoke();

    // T10.13: Track previously active filter nodes so IsActiveFilter can be
    // cleared even when the node isn't in the loaded tree (e.g. tests without
    // LoadAsync, or nodes removed from the tree before deselection).
    private INotifyPropertyChanged? _previousActiveFilterNode;

    private void OnFilterChanged()
    {
        // T10.13: Clear the previous node's IsActiveFilter directly.
        // This handles cases where the node isn't in the loaded tree
        // (ClearActiveFilterStates walks the tree only).
        if (_previousActiveFilterNode != null)
        {
            switch (_previousActiveFilterNode)
            {
                case KeywordNode kw: kw.IsActiveFilter = false; break;
                case CategoryNode cat: cat.IsActiveFilter = false; break;
                case CollectionNode col: col.IsActiveFilter = false; break;
                case FolderNode f: f.IsActiveFilter = false; break;
                case DateTakenNode dt: dt.IsActiveFilter = false; break;
                case SavedSearchNode ss: ss.IsActiveFilter = false; break;
                case SearchHistoryNode rs: rs.IsActiveFilter = false; break;
            }
        }

        // Also walk the tree and flat lists to catch any other stale nodes
        ClearActiveFilterStates();

        // Phase 19: If switching to a non-search filter, clear saved/recent search selections
        // so the priority chain below picks the correct active filter.
        if (SelectedKeyword != null || SelectedCollection != null || SelectedFolder != null ||
            SelectedMetadataCategory != null || SelectedDateTaken != null)
        {
            if (_selectedSavedSearch != null)
            {
                _selectedSavedSearch.IsActiveFilter = false;
                _selectedSavedSearch = null;
                OnPropertyChanged(nameof(SelectedSavedSearch));
            }
            if (_selectedRecentSearch != null)
            {
                _selectedRecentSearch.IsActiveFilter = false;
                _selectedRecentSearch = null;
                OnPropertyChanged(nameof(SelectedRecentSearch));
            }
        }

        // Determine new active filter (Phase 19: saved/recent search takes priority)
        INotifyPropertyChanged? newActive = null;
        if (SelectedSavedSearch != null) newActive = SelectedSavedSearch;
        else if (SelectedRecentSearch != null) newActive = SelectedRecentSearch;
        else if (SelectedKeyword != null) newActive = SelectedKeyword;
        else if (SelectedMetadataCategory != null) newActive = SelectedMetadataCategory;
        else if (SelectedCollection != null) newActive = SelectedCollection;
        else if (SelectedFolder != null) newActive = SelectedFolder;
        else if (SelectedDateTaken != null) newActive = SelectedDateTaken;

        if (newActive != null)
        {
            // Clear ActiveSearchQueryText for non-search filters (keyword, collection, etc.)
            if (newActive is not SavedSearchNode and not SearchHistoryNode)
                ActiveSearchQueryText = null;

            switch (newActive)
            {
                case SavedSearchNode ss: ss.IsActiveFilter = true; break;
                case SearchHistoryNode rs: rs.IsActiveFilter = true; break;
                case KeywordNode kw: kw.IsActiveFilter = true; break;
                case CategoryNode cat: cat.IsActiveFilter = true; break;
                case CollectionNode col: col.IsActiveFilter = true; break;
                case FolderNode f: f.IsActiveFilter = true; break;
                case DateTakenNode dt: dt.IsActiveFilter = true; break;
            }
            _previousActiveFilterNode = newActive;
        }
        else
        {
            if (_activeSearchQueryText != null)
                ActiveSearchQueryText = null;
            _previousActiveFilterNode = null;
        }

        FilterChanged?.Invoke();
    }

    /// <summary>
    /// Clears IsActiveFilter on all tree nodes recursively and flat lists.
    /// </summary>
    private void ClearActiveFilterStates()
    {
        void ClearRecursive(System.Collections.IEnumerable children)
        {
            foreach (var child in children)
            {
                switch (child)
                {
                    case KeywordNode kw:
                        kw.IsActiveFilter = false;
                        ClearRecursive(kw.Children);
                        break;
                    case CategoryNode cat:
                        cat.IsActiveFilter = false;
                        ClearRecursive(cat.Children);
                        break;
                    case CollectionNode col:
                        col.IsActiveFilter = false;
                        ClearRecursive(col.Children);
                        break;
                    case FolderNode folder:
                        folder.IsActiveFilter = false;
                        ClearRecursive(folder.Children);
                        break;
                    case DateTakenNode dt:
                        dt.IsActiveFilter = false;
                        ClearRecursive(dt.Children);
                        break;
                }
            }
        }

        ClearRecursive(Keywords.FirstOrDefault()?.Children ?? Enumerable.Empty<KeywordNode>());
        ClearRecursive(MetadataCategories.FirstOrDefault()?.Children ?? Enumerable.Empty<CategoryNode>());
        ClearRecursive(Collections.FirstOrDefault()?.Children ?? Enumerable.Empty<CollectionNode>());
        ClearRecursive(Folders.FirstOrDefault()?.Children ?? Enumerable.Empty<FolderNode>());
        ClearRecursive(DateTakenTree.FirstOrDefault()?.Children ?? Enumerable.Empty<DateTakenNode>());

        // Phase 19: Clear saved and recent search active states from flat lists
        ClearSavedSearchActiveStates();
        ClearRecentSearchActiveStates();
    }


    /// <summary>
    /// Returns the main application window, or null if unavailable.
    /// </summary>
    private static Avalonia.Controls.Window? GetOwnerWindow()
        => App.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

    /// <summary>
    /// Sends a request to the broker and returns the response envelope.
    /// Returns null if the broker is unavailable or disconnected.
    /// </summary>
    private async Task<Envelope?> SendBrokerRequestAsync<T>(T request, MessageTypeCode messageType, CancellationToken ct = default)
        where T : IProtoSerializable
    {
        var broker = _modeManager.BrokerClient;
        if (broker == null || !broker.IsConnected)
            return null;

        var authToken = _modeManager.AuthSession?.Token ?? string.Empty;
        var envelope = new Envelope
        {
            AuthToken = authToken,
            CorrelationId = Guid.NewGuid().ToString(),
            MessageType = messageType,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(request))
        };

        return await broker.SendAsync(envelope, ct);
    }

    // ──────────────────────────────────────────────
    //  T8.18: Context menu builder helpers
    // ──────────────────────────────────────────────

    /// <summary>
    /// Builds a standard context menu for sidebar tree nodes.
    /// </summary>
    internal static MenuFlyout BuildNodeContextMenu(
        ICommand createCommand,
        ICommand renameCommand,
        ICommand deleteCommand,
        string createHeader = "New",
        string renameHeader = "Rename",
        string deleteHeader = "Delete")
    {
        var flyout = new MenuFlyout();
        flyout.Items.Add(new MenuItem { Header = createHeader, Command = createCommand });
        flyout.Items.Add(new MenuItem { Header = renameHeader, Command = renameCommand });
        flyout.Items.Add(new Separator());
        flyout.Items.Add(new MenuItem { Header = deleteHeader, Command = deleteCommand });
        return flyout;
    }

    /// <summary>
    /// Handles right-click on a keyword tree node — sets selection and shows context menu.
    /// </summary>
    private void ShowKeywordContextMenu(object? parameter)
    {
        if (parameter is KeywordNode kw)
            SelectedKeyword = kw;
        // The flyout itself is shown by the SearchableTreeView code-behind
        // via the NodeContextMenuCommand. We just handle selection here.
    }

    /// <summary>
    /// Handles right-click on a category tree node — sets selection and shows context menu.
    /// </summary>
    private void ShowCategoryContextMenu(object? parameter)
    {
        if (parameter is CategoryNode cat)
            SelectedMetadataCategory = cat;
    }

    /// <summary>
    /// Handles right-click on a folder tree node — sets selection and shows context menu.
    /// </summary>
    private void ShowFolderContextMenu(object? parameter)
    {
        if (parameter is FolderNode folder)
            SelectedFolder = folder;
    }

    // ──────────────────────────────────────────────
    //  T10.1: Filter-by-this / Clear-filter commands
    // ──────────────────────────────────────────────

    private void OnFilterByThis(object? parameter)
    {
        switch (parameter)
        {
            case SavedSearchNode ss:
                SelectedSavedSearch = ss;
                break;
            case SearchHistoryNode rs:
                SelectedRecentSearch = rs;
                break;
            case KeywordNode kw:
                SelectedKeyword = kw;
                break;
            case CategoryNode cat:
                SelectedMetadataCategory = cat;
                break;
            case CollectionNode col:
                SelectedCollection = col;
                break;
            case FolderNode folder:
                SelectedFolder = folder;
                break;
            case DateTakenNode dt:
                SelectedDateTaken = dt;
                break;
        }
    }

    private void OnClearFilter(object? parameter)
    {
        switch (parameter)
        {
            case SavedSearchNode:
                SelectedSavedSearch = null;
                break;
            case SearchHistoryNode:
                SelectedRecentSearch = null;
                break;
            case KeywordNode:
                SelectedKeyword = null;
                break;
            case CategoryNode:
                SelectedMetadataCategory = null;
                break;
            case CollectionNode:
                SelectedCollection = null;
                break;
            case FolderNode:
                SelectedFolder = null;
                break;
            case DateTakenNode:
                SelectedDateTaken = null;
                break;
        }
    }

    // ──────────────────────────────────────────────
    //  T10.2: F2 inline rename keyboard shortcut
    // ──────────────────────────────────────────────

    private void BeginRenameSelectedNode()
    {
        var node = (object?)SelectedKeyword ?? (object?)SelectedCollection ?? SelectedMetadataCategory;
        if (node == null) return;

        var beginMethod = node.GetType().GetMethod("BeginRename",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        beginMethod?.Invoke(node, null);

        // Notify UI that node state changed
        OnPropertyChanged(nameof(Keywords));
        OnPropertyChanged(nameof(Collections));
        OnPropertyChanged(nameof(MetadataCategories));
    }

    /// <summary>
    /// Handles inline rename commit from SearchableTreeView TextBox (T10.2).
    /// Receives the node as parameter, calls CommitRename() and persists to DB.
    /// </summary>
    private async void CommitNodeRename(object? parameter)
    {
        try
        {
            switch (parameter)
            {
                case KeywordNode kw when kw.IsEditing:
                {
                    var oldName = kw.Name;
                    kw.CommitRename();
                    if (kw.Name != oldName)
                        await PersistKeywordRenameAsync(kw);
                    break;
                }
                case CategoryNode cat when cat.IsEditing:
                {
                    var oldName = cat.Name;
                    cat.CommitRename();
                    if (cat.Name != oldName)
                        await PersistCategoryRenameAsync(cat);
                    break;
                }
                case CollectionNode col when col.IsEditing:
                {
                    var oldName = col.Name;
                    col.CommitRename();
                    if (col.Name != oldName)
                        await PersistCollectionRenameAsync(col);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist inline rename");
        }
    }

    private async Task PersistKeywordRenameAsync(KeywordNode kw)
    {
        await _keywordTreeService.RenameAsync(kw, kw.Name);
    }

    private async Task PersistCategoryRenameAsync(CategoryNode cat)
    {
        await _categoryTreeService.RenameAsync(cat, cat.Name);
    }

    private async Task PersistCollectionRenameAsync(CollectionNode col)
    {
        await _collectionTreeService.RenameAsync(col, col.Name);
    }

    /// <summary>
    /// Opens the selected folder in the system file explorer (T10.5).
    /// </summary>
    private void RevealFolder(object? parameter)
    {
        FolderNode? folder = parameter as FolderNode;
        if (folder == null || string.IsNullOrEmpty(folder.Path))
            folder = SelectedFolder;
        _folderTreeService.RevealFolder(folder);
    }

    /// <summary>
    /// Triggers a re-scan/ingest of the selected folder (T10.5, T15.4).
    /// </summary>
    private async Task RescanFolderAsync()
    {
        if (SelectedFolder == null || string.IsNullOrEmpty(SelectedFolder.Path)) return;
        await _folderTreeService.RescanFolderAsync(SelectedFolder);
        await LoadAsync();
    }

    private bool EvaluatePermission(string permission)
    {
        if (_modeManager.IsStandalone) return true;
        var session = _modeManager.AuthSession;
        if (session == null || !session.IsLoggedIn) return false;
        if (session.IsTokenExpired()) return false;
        var role = session.CurrentUser?.Role;
        if (string.IsNullOrEmpty(role)) return false;        return Shared.Services.PermissionEvaluator.HasPermission(role, permission);
    }

    /// <summary>
    /// Shows an input dialog and returns the entered text.
    /// </summary>
    private static async Task<string?> ShowInputDialog(string title, string message, string okButton, string? defaultValue = null)
    {
        return await Views.InputDialog.ShowAsync(
            App.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null,
            title,
            message,
            okButton,
            "Cancel",
            defaultValue: defaultValue);
    }

    // ──────────────────────────────────────────────
    //  T8.18: Sidebar CRUD operations (standalone)
    // ──────────────────────────────────────────────

    private async Task PromptCreateCollectionAsync()
    {
        var parentId = SelectedCollection?.Id;
        var name = await ShowInputDialog("New Collection", "Enter collection name:", "Create");
        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            await _collectionTreeService.CreateAsync(name.Trim(), parentId);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create collection");
        }
    }

    private async Task PromptRenameCollectionAsync()
    {
        if (SelectedCollection == null) return;

        var newName = await ShowInputDialog("Rename Collection", $"Rename '{SelectedCollection.Name}' to:", "Rename", SelectedCollection.Name);
        if (string.IsNullOrWhiteSpace(newName)) return;

        try
        {
            await _collectionTreeService.RenameAsync(SelectedCollection, newName.Trim());
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rename collection");
        }
    }

    private async Task PromptDeleteCollectionAsync()
    {
        if (SelectedCollection == null) return;

        var owner = GetOwnerWindow();
        if (owner == null) return;

        // T10.4: Count descendants for cascade confirmation
        var descIds = new List<object>();
        SelectedCollection?.CollectDescendantIds(descIds);
        var descendantCount = descIds.Count;
        var message = descendantCount > 0
            ? $"Are you sure you want to delete '{SelectedCollection.Name}' and all {descendantCount} sub-collections?\n\n" +
              $"The collection(s) will be removed but the assets within them will not be deleted."
            : $"Are you sure you want to delete '{SelectedCollection.Name}'?\n\n" +
              $"This will remove the collection but not the assets within it.";

        var confirmed = await Views.ConfirmationDialog.ShowAsync(owner, "Delete Collection",
            message, "Delete", "Cancel", isDestructive: true);
        if (!confirmed) return;

        try
        {
            await _collectionTreeService.DeleteWithCascadeAsync(SelectedCollection);
            SelectedCollection = null;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete collection (cascade)");
        }
    }

    private async Task PromptCreateKeywordAsync()
    {
        var parentId = SelectedKeyword?.KeywordId;
        var name = await ShowInputDialog("New Keyword", "Enter keyword name:", "Create");
        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            await _keywordTreeService.CreateAsync(name.Trim(), parentId);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create keyword");
        }
    }

    private async Task PromptRenameKeywordAsync()
    {
        if (SelectedKeyword == null) return;

        var newName = await ShowInputDialog("Rename Keyword", $"Rename '{SelectedKeyword.Name}' to:", "Rename", SelectedKeyword.Name);
        if (string.IsNullOrWhiteSpace(newName)) return;

        try
        {
            await _keywordTreeService.RenameAsync(SelectedKeyword, newName.Trim());
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rename keyword");
        }
    }

    private async Task PromptDeleteKeywordAsync()
    {
        if (SelectedKeyword == null) return;

        var owner = GetOwnerWindow();
        if (owner == null) return;

        // T10.4: Count descendants for cascade confirmation
        var descIds = new List<object>(); SelectedKeyword?.CollectDescendantIds(descIds);
        var descendantCount = descIds.Count;
        var message = descendantCount > 0
            ? $"Are you sure you want to delete '{SelectedKeyword.Name}' and all {descendantCount} sub-keywords?\n\n" +
              $"The keyword(s) will be removed from all assets. This action cannot be undone."
            : $"Are you sure you want to delete '{SelectedKeyword.Name}'?\n\n" +
              $"The keyword will be removed from all assets. This action cannot be undone.";

        var confirmed = await Views.ConfirmationDialog.ShowAsync(owner, "Delete Keyword",
            message, "Delete", "Cancel", isDestructive: true);
        if (!confirmed) return;

        try
        {
            await _keywordTreeService.DeleteWithCascadeAsync(SelectedKeyword);
            SelectedKeyword = null;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete keyword (cascade)");
        }
    }

    private async Task PromptCreateCategoryAsync()
    {
        var parentId = SelectedMetadataCategory?.CategoryId;
        var name = await ShowInputDialog("New Category", "Enter category name:", "Create");
        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            await _categoryTreeService.CreateAsync(name.Trim(), parentId);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create category");
        }
    }

    private async Task PromptRenameCategoryAsync()
    {
        if (SelectedMetadataCategory == null) return;

        var newName = await ShowInputDialog("Rename Category", $"Rename '{SelectedMetadataCategory.Name}' to:", "Rename", SelectedMetadataCategory.Name);
        if (string.IsNullOrWhiteSpace(newName)) return;

        try
        {
            await _categoryTreeService.RenameAsync(SelectedMetadataCategory, newName.Trim());
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rename category");
        }
    }

    private async Task PromptDeleteCategoryAsync()
    {
        if (SelectedMetadataCategory == null) return;

        var owner = GetOwnerWindow();
        if (owner == null) return;

        // T10.4: Count descendants for cascade confirmation
        var descIds = new List<object>(); SelectedMetadataCategory?.CollectDescendantIds(descIds);
        var descendantCount = descIds.Count;
        var message = descendantCount > 0
            ? $"Are you sure you want to delete '{SelectedMetadataCategory.Name}' and all {descendantCount} sub-categories?\n\n" +
              $"The category(s) will be removed from all assets. This action cannot be undone."
            : $"Are you sure you want to delete '{SelectedMetadataCategory.Name}'?\n\n" +
              $"The category will be removed from all assets. This action cannot be undone.";

        var confirmed = await Views.ConfirmationDialog.ShowAsync(owner, "Delete Category",
            message, "Delete", "Cancel", isDestructive: true);
        if (!confirmed) return;

        try
        {
            await _categoryTreeService.DeleteWithCascadeAsync(SelectedMetadataCategory);
            SelectedMetadataCategory = null;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete category (cascade)");
        }
    }// ──────────────────────────────────────────────
    //  Phase 19: Saved search & search history
    // ──────────────────────────────────────────────

    private void OnExecuteSavedSearch(object? parameter)
    {
        if (parameter is SavedSearchNode ss)
            SelectedSavedSearch = ss;
    }

    private void OnExecuteRecentSearch(object? parameter)
    {
        if (parameter is SearchHistoryNode rs)
            SelectedRecentSearch = rs;
    }

    private async void OnDeleteSavedSearch(object? parameter)
    {
        if (SelectedSavedSearch == null) return;
        await _savedSearchService.DeleteSavedSearchAsync(SelectedSavedSearch.SearchId);
        OnPropertyChanged(nameof(SavedSearches));
        SelectedSavedSearch = null;
    }

    private async void OnTogglePinSavedSearch(object? parameter)
    {
        if (SelectedSavedSearch == null) return;
        SelectedSavedSearch.IsPinned = !SelectedSavedSearch.IsPinned;
        // Persist pin state
        await _savedSearchService.PersistPinStateAsync(SelectedSavedSearch.SearchId, SelectedSavedSearch.IsPinned);
    }

    private void ClearRecentSearches()
    {
        _savedSearchService.ClearRecentSearches();
        OnPropertyChanged(nameof(RecentSearches));
        SelectedRecentSearch = null;
    }

    /// <summary>
    /// Clears IsActiveFilter on all SavedSearchNode items in the flat list.
    /// </summary>
    private void ClearSavedSearchActiveStates()
    {
        _savedSearchService.ClearSavedSearchActiveStates();
    }

    /// <summary>
    /// Clears IsActiveFilter on all SearchHistoryNode items in the flat list.
    /// </summary>
    private void ClearRecentSearchActiveStates()
    {
        _savedSearchService.ClearRecentSearchActiveStates();
    }

    // ──────────────────────────────────────────────
    //  Static tree traversal helpers (used by tests via reflection)
    // ──────────────────────────────────────────────

    /// <summary>
    /// Counts all descendant keywords recursively (excluding the root).
    /// </summary>
    private static int CountDescendantKeywords(KeywordNode root)
    {
        var count = 0;
        foreach (var child in root.Children)
        {
            count++;
            count += CountDescendantKeywords(child);
        }
        return count;
    }

    /// <summary>
    /// Counts all descendant collections recursively (excluding the root).
    /// </summary>
    private static int CountDescendantCollections(CollectionNode root)
    {
        var count = 0;
        foreach (var child in root.Children)
        {
            count++;
            count += CountDescendantCollections(child);
        }
        return count;
    }

    /// <summary>
    /// Counts all descendant categories recursively (excluding the root).
    /// </summary>
    private static int CountDescendantCategories(CategoryNode root)
    {
        var count = 0;
        foreach (var child in root.Children)
        {
            count++;
            count += CountDescendantCategories(child);
        }
        return count;
    }

    /// <summary>
    /// Collects all descendant keyword IDs recursively into the provided list.
    /// </summary>
    private static void CollectDescendantKeywordIds(KeywordNode root, List<Guid> ids)
    {
        foreach (var child in root.Children)
        {
            ids.Add(child.KeywordId);
            CollectDescendantKeywordIds(child, ids);
        }
    }

    /// <summary>
    /// Collects all descendant collection IDs recursively into the provided list.
    /// </summary>
    private static void CollectDescendantCollectionIds(CollectionNode root, List<Guid> ids)
    {
        foreach (var child in root.Children)
        {
            ids.Add(child.Id);
            CollectDescendantCollectionIds(child, ids);
        }
    }

    /// <summary>
    /// Collects all descendant category IDs recursively into the provided list.
    /// </summary>
    private static void CollectDescendantCategoryIds(CategoryNode root, List<Guid> ids)
    {
        foreach (var child in root.Children)
        {
            ids.Add(child.CategoryId);
            CollectDescendantCategoryIds(child, ids);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
