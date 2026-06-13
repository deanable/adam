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

        // DateTaken TreeView — filter context menu (T10.11 / T10.12)
        if (DateTakenTree != null)
        {
            DateTakenTree.ContextRequested += OnDateTakenContextRequested;
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

    /// <summary>
    /// Builds a context menu for sidebar tree nodes with CRUD + Filter items (T10.1, T10.11).
    /// </summary>
    private static MenuFlyout BuildSidebarContextMenu(
        ICommand createCmd, ICommand renameCmd, ICommand deleteCmd, object node,
        ICommand filterByThisCmd, ICommand clearFilterCmd,
        string createHdr = "New", string renameHdr = "Rename", string deleteHdr = "Delete",
        string filterHdr = "Filter by this", string clearHdr = "Clear filter")
    {
        var flyout = new MenuFlyout();
        flyout.Items.Add(new MenuItem { Header = createHdr, Command = createCmd });
        flyout.Items.Add(new MenuItem { Header = renameHdr, Command = renameCmd });
        flyout.Items.Add(new Separator());
        flyout.Items.Add(new MenuItem { Header = deleteHdr, Command = deleteCmd });
        flyout.Items.Add(new Separator());
        flyout.Items.Add(new MenuItem { Header = filterHdr, Command = filterByThisCmd, CommandParameter = node });
        flyout.Items.Add(new MenuItem { Header = clearHdr, Command = clearFilterCmd, CommandParameter = node });
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
    //  Collections context menu (T10.1, T10.11)
    // ──────────────────────────────────────────────

    private void OnCollectionContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        var node = GetClickedNode(e);
        if (node is not CollectionNode col) return;
        var vm = VM?.Sidebar;
        if (vm == null) return;

        var flyout = BuildSidebarContextMenu(
            vm.CreateCollectionCommand, vm.RenameCollectionCommand, vm.DeleteCollectionCommand, col,
            vm.FilterByThisCommand, vm.ClearFilterCommand,
            createHdr: "New Collection");

        if (sender is Control ctl)
        {
            flyout.ShowAt(ctl);
            e.Handled = true;
        }
    }

    // ──────────────────────────────────────────────
    //  Keywords context menu (T10.1, T10.11)
    // ──────────────────────────────────────────────

    private void OnKeywordContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        var node = GetClickedNode(e);
        if (node is not KeywordNode kw) return;
        var vm = VM?.Sidebar;
        if (vm == null) return;

        var flyout = BuildSidebarContextMenu(
            vm.CreateKeywordCommand, vm.RenameKeywordCommand, vm.DeleteKeywordCommand, kw,
            vm.FilterByThisCommand, vm.ClearFilterCommand,
            createHdr: "New Keyword");

        if (sender is Control ctl)
        {
            flyout.ShowAt(ctl);
            e.Handled = true;
        }
    }

    // ──────────────────────────────────────────────
    //  Categories context menu (T10.1, T10.11)
    // ──────────────────────────────────────────────

    private void OnCategoryContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        var node = GetClickedNode(e);
        if (node is not CategoryNode cat) return;
        var vm = VM?.Sidebar;
        if (vm == null) return;

        var flyout = BuildSidebarContextMenu(
            vm.CreateCategoryCommand, vm.RenameCategoryCommand, vm.DeleteCategoryCommand, cat,
            vm.FilterByThisCommand, vm.ClearFilterCommand,
            createHdr: "New Category");

        if (sender is Control ctl)
        {
            flyout.ShowAt(ctl);
            e.Handled = true;
        }
    }

    // ──────────────────────────────────────────────
    //  Folders context menu (T10.1, T10.5, T10.11)
    // ──────────────────────────────────────────────

    private void OnFolderContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        var node = GetClickedNode(e);
        if (node is not FolderNode folder) return;
        var vm = VM?.Sidebar;
        if (vm == null) return;

        var flyout = new MenuFlyout();
        flyout.Items.Add(new MenuItem { Header = "Reveal in Explorer", Command = vm.RevealFolderCommand });
        flyout.Items.Add(new MenuItem { Header = "Re-scan Folder", Command = vm.RescanFolderCommand });
        flyout.Items.Add(new Separator());
        flyout.Items.Add(new MenuItem { Header = "Filter by this", Command = vm.FilterByThisCommand, CommandParameter = folder });
        flyout.Items.Add(new MenuItem { Header = "Clear filter", Command = vm.ClearFilterCommand, CommandParameter = folder });

        if (sender is Control ctl)
        {
            flyout.ShowAt(ctl);
            e.Handled = true;
        }
    }

    // ──────────────────────────────────────────────
    //  DateTaken context menu (T10.11, T10.12)
    // ──────────────────────────────────────────────

    private void OnDateTakenContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        var node = GetClickedNode(e);
        if (node is not DateTakenNode dt) return;
        var vm = VM?.Sidebar;
        if (vm == null) return;

        var flyout = new MenuFlyout();
        flyout.Items.Add(new MenuItem { Header = "Filter by this", Command = vm.FilterByThisCommand, CommandParameter = dt });
        flyout.Items.Add(new MenuItem { Header = "Clear filter", Command = vm.ClearFilterCommand, CommandParameter = dt });

        if (sender is Control ctl)
        {
            flyout.ShowAt(ctl);
            e.Handled = true;
        }
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
/// Shared bool inverter for use across all CatalogBrowser views.
/// Convert: true → false, false → true. ConvertBack also supported.
/// </summary>
public class InverseBoolConverter : IValueConverter
{
    public static readonly InverseBoolConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is bool b ? !b : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is bool b ? !b : value;
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
/// Converts a boolean to FontWeight: True → Bold (active filter), False → Normal.
/// </summary>
public class BoolToFontWeightConverter : IValueConverter
{
    public static readonly BoolToFontWeightConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b && b ? FontWeight.Bold : FontWeight.Normal;
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
