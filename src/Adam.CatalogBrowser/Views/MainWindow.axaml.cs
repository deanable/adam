using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Media;
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
