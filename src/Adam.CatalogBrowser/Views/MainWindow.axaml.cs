using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Adam.CatalogBrowser.Controls;
using Adam.CatalogBrowser.ViewModels;

namespace Adam.CatalogBrowser.Views;

public partial class MainWindow : Window
{
    // ── Cached flyout for sidebar tree context menus (T8.18) ──
    private MenuFlyout? _collectionContextMenu;
    private MenuFlyout? _keywordContextMenu;
    private MenuFlyout? _categoryContextMenu;
    private MenuFlyout? _folderContextMenu;

    public MainWindow()
    {
        InitializeComponent();

        // Wire context menus after the window and its children are ready
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        // Collections TreeView — inline
        if (CollectionsTreeView != null)
        {
            CollectionsTreeView.ContextRequested += OnCollectionContextRequested;
            CollectionsTreeView.PointerPressed += OnPlainTreeViewPointerPressed;
            CollectionsTreeView.KeyDown += OnPlainTreeViewKeyDown;
        }

        // Folders TreeView (T10.5)
        if (FoldersTreeView != null)
        {
            FoldersTreeView.ContextRequested += OnFolderContextRequested;
            FoldersTreeView.PointerPressed += OnPlainTreeViewPointerPressed;
            FoldersTreeView.KeyDown += OnPlainTreeViewKeyDown;
        }

        // Keywords SearchableTreeView — access inner TreeViewBox
        if (KeywordsTreeView?.TreeViewBoxAccessor is { } kwTree)
        {
            kwTree.ContextRequested += OnKeywordContextRequested;
        }

        // Categories SearchableTreeView — access inner TreeViewBox
        if (CategoriesTreeView?.TreeViewBoxAccessor is { } catTree)
        {
            catTree.ContextRequested += OnCategoryContextRequested;
        }

        // T10.2: Wire rename completed command for SearchableTreeView instances
        if (KeywordsTreeView != null)
            KeywordsTreeView.RenameCompletedCommand = VM?.Sidebar?.CommitRenameCommand;
        if (CategoriesTreeView != null)
            CategoriesTreeView.RenameCompletedCommand = VM?.Sidebar?.CommitRenameCommand;

        // T8.21: Ctrl+F → focus keyword search box
        if (VM is { } vm)
        {
            vm.RequestFocusSearch += () => KeywordsTreeView?.FocusSearch();
        }
    }

    // ──────────────────────────────────────────────
    //  Context menu helpers
    // ──────────────────────────────────────────────

    private static MenuFlyout BuildSidebarContextMenu(
        string newHeader, ICommand newCmd,
        string renameHeader, ICommand renameCmd,
        string deleteHeader, ICommand deleteCmd)
    {
        var flyout = new MenuFlyout();
        flyout.Items.Add(new MenuItem { Header = newHeader, Command = newCmd });
        flyout.Items.Add(new MenuItem { Header = renameHeader, Command = renameCmd });
        flyout.Items.Add(new Separator());
        flyout.Items.Add(new MenuItem { Header = deleteHeader, Command = deleteCmd });
        return flyout;
    }

    /// <summary>
    /// Gets the ViewModel from the window's DataContext.
    /// </summary>
    private MainWindowViewModel? VM => DataContext as MainWindowViewModel;

    /// <summary>
    /// Finds the right-clicked TreeViewItem and returns its DataContext.
    /// </summary>
    private static object? GetClickedNode(ContextRequestedEventArgs e)
    {
        var source = e.Source as Visual;
        var treeItem = source?.FindAncestorOfType<TreeViewItem>();
        return treeItem?.DataContext;
    }

    // ──────────────────────────────────────────────
    //  Collections context menu
    // ──────────────────────────────────────────────

    private void OnCollectionContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        var node = GetClickedNode(e);
        if (node is not CollectionNode) return;
        var vm = VM?.Sidebar;
        if (vm == null) return;

        _collectionContextMenu ??= BuildSidebarContextMenu(
            "New Collection", vm.CreateCollectionCommand,
            "Rename", vm.RenameCollectionCommand,
            "Delete", vm.DeleteCollectionCommand);

        if (sender is Control ctl)
        {
            _collectionContextMenu.ShowAt(ctl);
            e.Handled = true;
        }
    }

    // ──────────────────────────────────────────────
    //  Keywords context menu
    // ──────────────────────────────────────────────

    private void OnKeywordContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        var node = GetClickedNode(e);
        if (node is not KeywordNode) return;
        var vm = VM?.Sidebar;
        if (vm == null) return;

        _keywordContextMenu ??= BuildSidebarContextMenu(
            "New Keyword", vm.CreateKeywordCommand,
            "Rename", vm.RenameKeywordCommand,
            "Delete", vm.DeleteKeywordCommand);

        if (sender is Control ctl)
        {
            _keywordContextMenu.ShowAt(ctl);
            e.Handled = true;
        }
    }

    // ──────────────────────────────────────────────
    //  Categories context menu
    // ──────────────────────────────────────────────

    private void OnCategoryContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        var node = GetClickedNode(e);
        if (node is not CategoryNode) return;
        var vm = VM?.Sidebar;
        if (vm == null) return;

        _categoryContextMenu ??= BuildSidebarContextMenu(
            "New Category", vm.CreateCategoryCommand,
            "Rename", vm.RenameCategoryCommand,
            "Delete", vm.DeleteCategoryCommand);

        if (sender is Control ctl)
        {
            _categoryContextMenu.ShowAt(ctl);
            e.Handled = true;
        }
    }

    // ──────────────────────────────────────────────
    //  Folders context menu (T10.5)
    // ──────────────────────────────────────────────

    private void OnFolderContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        var node = GetClickedNode(e);
        if (node is not FolderNode) return;
        var vm = VM?.Sidebar;
        if (vm == null) return;

        _folderContextMenu ??= BuildFolderContextMenu(vm);

        if (sender is Control ctl)
        {
            _folderContextMenu.ShowAt(ctl);
            e.Handled = true;
        }
    }

    private static MenuFlyout BuildFolderContextMenu(SidebarViewModel vm)
    {
        var flyout = new MenuFlyout();
        flyout.Items.Add(new MenuItem { Header = "Reveal in Explorer", Command = vm.RevealFolderCommand });
        flyout.Items.Add(new MenuItem { Header = "Re-scan Folder", Command = vm.RescanFolderCommand });
        return flyout;
    }

    // ──────────────────────────────────────────────
    //  Plain TreeView inline rename (T10.2)
    // ──────────────────────────────────────────────

    private void OnPlainTreeViewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount != 2) return;

        var source = e.Source as Visual;
        var treeItem = source?.FindAncestorOfType<TreeViewItem>();
        if (treeItem?.DataContext == null) return;

        var node = treeItem.DataContext;
        var beginRename = node.GetType().GetMethod("BeginRename",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        if (beginRename != null)
        {
            beginRename.Invoke(node, null);
            Dispatcher.UIThread.Post(() =>
            {
                var textBox = treeItem.FindDescendantOfType<TextBox>();
                textBox?.Focus();
                textBox?.SelectAll();
            }, DispatcherPriority.Input);
        }
    }

    private void OnPlainTreeViewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter && e.Key != Key.Escape) return;

        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as TextBox;
        if (focused == null) return;

        var treeItem = focused.FindAncestorOfType<TreeViewItem>();
        if (treeItem?.DataContext == null) return;

        var node = treeItem.DataContext;
        var isEditingProp = node.GetType().GetProperty("IsEditing");
        if (isEditingProp == null || !(bool)(isEditingProp.GetValue(node) ?? false)) return;

        var vm = VM?.Sidebar;
        if (vm == null) return;

        if (e.Key == Key.Enter)
        {
            // Fire the command — it handles both CommitRename() and DB persistence.
            vm.CommitRenameCommand.Execute(node);
            e.Handled = true;
        }
        else // Escape
        {
            var cancelRename = node.GetType().GetMethod("CancelRename",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            cancelRename?.Invoke(node, null);
            e.Handled = true;
        }
    }
}

/// <summary>
/// Converts a boolean to a background brush for the active mode toggle button.
/// True → #33FFFFFF (semi-transparent white highlight), False → Transparent.
/// </summary>
public class BoolToModeBgConverter : IValueConverter
{
    public static readonly BoolToModeBgConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b && b ? new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)) : Brushes.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts a boolean to a status indicator color: True → LimeGreen (connected), False → Gray (disconnected).
/// </summary>
public class BoolToStatusColorConverter : IValueConverter
{
    public static readonly BoolToStatusColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b && b ? Brushes.LimeGreen : Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
