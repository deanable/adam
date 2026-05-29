using System.Collections;
using System.Reflection;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Adam.CatalogBrowser.Controls;

/// <summary>
/// Wraps a tree node for flat-list display during search.
/// </summary>
public class FlatItem
{
    public object Node { get; init; } = null!;
    public string DisplayPath { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int Count { get; init; }
}

/// <summary>
/// Payload for the <see cref="SearchableTreeView.DropCommand"/>,
/// containing the target node and the CSV of dropped asset IDs.
/// </summary>
public class DropPayload
{
    public object TargetNode { get; init; } = null!;
    public string AssetIdsCsv { get; init; } = string.Empty;
}

/// <summary>
/// A user control that wraps a TreeView with a search box.
///
/// <para><b>No search:</b> shows full tree hierarchy using a TreeView
/// with hardcoded rendering (Name + Count).</para>
///
/// <para><b>With search:</b> recursively flattens the tree, filters nodes
/// whose Name contains the search text (case-insensitive), and shows them
/// as a flat ListBox with full path display.</para>
///
/// <para><b>Drag-drop target:</b> bind <see cref="DropCommand"/> to receive
/// dropped items. Drag data expected to have <c>"AssetIds"</c> key with a
/// comma-separated list of GUIDs. The command parameter is a
/// <see cref="DropPayload"/> containing the target node and asset IDs.</para>
/// </summary>
public partial class SearchableTreeView : UserControl
{
    private bool _isUpdatingSelection;

    // ──────────────────────────────────────────────
    //  Drag-over highlight
    // ──────────────────────────────────────────────

    /// <summary>
    /// The visual container (TreeViewItem or ListBoxItem) currently under the
    /// drag cursor. Its background is tinted while the drag is over it.
    /// </summary>
    private Visual? _highlightedDropTarget;

    private static readonly IBrush DropHighlightBrush =
        new SolidColorBrush(Color.FromArgb(50, 25, 118, 210));

    internal void ClearDropHighlight()
    {
        if (_highlightedDropTarget == null) return;

        if (_highlightedDropTarget is ListBoxItem listItem)
            listItem.ClearValue(ListBoxItem.BackgroundProperty);
        else if (_highlightedDropTarget is TreeViewItem treeItem)
            treeItem.ClearValue(TreeViewItem.BackgroundProperty);

        _highlightedDropTarget = null;
    }

    /// <summary>
    /// Gets or sets the currently highlighted drop target, for testing.
    /// </summary>
    internal Visual? HighlightedDropTarget
    {
        get => _highlightedDropTarget;
        set => _highlightedDropTarget = value;
    }

    /// <summary>
    /// Finds the visual container item at the given position and highlights it.
    /// </summary>
    private void UpdateDropHighlight(ItemsControl itemsControl, Point position)
    {
        // Find the visual item (TreeViewItem or ListBoxItem) at position
        var hit = itemsControl.InputHitTest(position);
        Visual? targetVisual = null;
        if (hit != null)
        {
            var current = hit as Visual;
            while (current != null)
            {
                if (current is ListBoxItem or TreeViewItem)
                {
                    targetVisual = current;
                    break;
                }
                current = current.GetVisualParent();
            }
        }

        // Same as current highlight — nothing to do
        if (targetVisual == _highlightedDropTarget) return;

        // Clear previous highlight
        ClearDropHighlight();

        // Apply new highlight
        if (targetVisual != null)
        {
            if (targetVisual is ListBoxItem listItem)
                listItem.Background = DropHighlightBrush;
            else if (targetVisual is TreeViewItem treeItem)
                treeItem.Background = DropHighlightBrush;

            _highlightedDropTarget = targetVisual;
        }
    }

    // ──────────────────────────────────────────────
    //  Dependency properties
    // ──────────────────────────────────────────────

    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<SearchableTreeView, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<object?> SelectedItemProperty =
        AvaloniaProperty.Register<SearchableTreeView, object?>(nameof(SelectedItem),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<ICommand?> DropCommandProperty =
        AvaloniaProperty.Register<SearchableTreeView, ICommand?>(nameof(DropCommand));

    /// <summary>
    /// Optional label to show next to the count (e.g. "Assets" or "Count").
    /// Leave empty to just show the number.
    /// </summary>
    public static readonly StyledProperty<string> CountLabelProperty =
        AvaloniaProperty.Register<SearchableTreeView, string>(nameof(CountLabel), string.Empty);

    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public ICommand? DropCommand
    {
        get => GetValue(DropCommandProperty);
        set => SetValue(DropCommandProperty, value);
    }

    public string CountLabel
    {
        get => GetValue(CountLabelProperty);
        set => SetValue(CountLabelProperty, value);
    }

    // ──────────────────────────────────────────────
    //  Constructor
    // ──────────────────────────────────────────────

    public SearchableTreeView()
    {
        InitializeComponent();

        SearchTextBox.TextChanged += OnSearchTextChanged;
        TreeViewBox.SelectionChanged += OnTreeViewSelectionChanged;
        FlatListBox.SelectionChanged += OnFlatListSelectionChanged;

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);

        ItemsSourceProperty.Changed.AddClassHandler<SearchableTreeView>((s, _) => s.RebuildFilteredList());
        SelectedItemProperty.Changed.AddClassHandler<SearchableTreeView>((s, _) => s.SyncSelection());
    }

    // ──────────────────────────────────────────────
    //  Search / filtering
    // ──────────────────────────────────────────────

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
        => RebuildFilteredList();

    private void RebuildFilteredList()
    {
        var searchText = SearchTextBox.Text?.Trim();
        var isSearching = !string.IsNullOrEmpty(searchText);

        TreeViewBox.IsVisible = !isSearching;
        FlatListBox.IsVisible = isSearching;

        if (isSearching)
        {
            var flatItems = new List<FlatItem>();
            foreach (var root in GetItems())
                FlattenAndFilter(root, searchText!, [], flatItems);

            FlatListBox.ItemsSource = flatItems;
        }
        else
        {
            FlatListBox.ItemsSource = null;
        }
    }

    private IEnumerable GetItems() => ItemsSource ?? Enumerable.Empty<object>();

    private static void FlattenAndFilter(object node, string searchText, List<string> parentPath, List<FlatItem> results)
    {
        var name = GetNodeName(node);
        var path = new List<string>(parentPath) { name };
        var caseSearch = searchText.AsSpan();

        if (name.Contains(caseSearch, StringComparison.OrdinalIgnoreCase))
        {
            results.Add(new FlatItem
            {
                Node = node,
                DisplayPath = string.Join(" > ", path),
                Name = name,
                Count = GetNodeCount(node)
            });
        }

        foreach (var child in GetChildren(node))
            FlattenAndFilter(child, searchText, path, results);
    }

    // ──────────────────────────────────────────────
    //  Selection sync
    // ──────────────────────────────────────────────

    private void OnTreeViewSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingSelection) return;
        SelectedItem = TreeViewBox.SelectedItem;
    }

    private void OnFlatListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingSelection) return;
        if (FlatListBox.SelectedItem is FlatItem flat)
            SelectedItem = flat.Node;
    }

    private void SyncSelection()
    {
        _isUpdatingSelection = true;
        try
        {
            if (FlatListBox.IsVisible && FlatListBox.ItemsSource is IEnumerable<FlatItem> flatItems && SelectedItem != null)
            {
                var match = flatItems.FirstOrDefault(f => f.Node == SelectedItem);
                FlatListBox.SelectedItem = match;
            }
        }
        finally
        {
            _isUpdatingSelection = false;
        }
    }

    // ──────────────────────────────────────────────
    //  Drag-drop (target)
    // ──────────────────────────────────────────────

    internal void OnDragOver(object? sender, DragEventArgs e)
    {
        var hasText = e.DataTransfer.Contains(DataFormat.Text);
        e.DragEffects = hasText ? DragDropEffects.Copy : DragDropEffects.None;
        // Note: intentionally NOT setting e.Handled here, matching the
        // IngestionView pattern, so parent containers can also observe
        // the DragOver event if needed.

        // Only show the drop highlight when the data is actually accepted
        if (hasText)
        {
            if (TreeViewBox.IsVisible)
                UpdateDropHighlight(TreeViewBox, e.GetPosition(TreeViewBox));
            else if (FlatListBox.IsVisible)
                UpdateDropHighlight(FlatListBox, e.GetPosition(FlatListBox));
            else
                ClearDropHighlight();
        }
        else
        {
            ClearDropHighlight();
        }
    }

    internal void OnDragLeave(object? sender, DragEventArgs e)
    {
        ClearDropHighlight();
    }

    internal void OnDrop(object? sender, DragEventArgs e)
    {
        ClearDropHighlight();

        var idsCsv = ReadDropText(e.DataTransfer);

        if (string.IsNullOrEmpty(idsCsv))
        {
            return;
        }

        object? targetNode = null;

        if (TreeViewBox.IsVisible)
            targetNode = FindNodeAtPosition(TreeViewBox, e.GetPosition(TreeViewBox));
        else if (FlatListBox.IsVisible)
            targetNode = FindNodeAtPosition(FlatListBox, e.GetPosition(FlatListBox));

        if (targetNode != null)
        {
            var payload = new DropPayload
            {
                TargetNode = targetNode,
                AssetIdsCsv = idsCsv
            };
            if (DropCommand?.CanExecute(payload) == true)
                DropCommand.Execute(payload);
        }
    }

    private static object? FindNodeAtPosition(ItemsControl itemsControl, Point position)
    {
        var hit = itemsControl.InputHitTest(position);
        if (hit == null) return null;

        var current = hit as Visual;
        while (current != null)
        {
            if (current is ListBoxItem listItem)
                return listItem.DataContext is FlatItem flat ? flat.Node : listItem.DataContext;

            if (current is TreeViewItem treeItem)
                return treeItem.DataContext;

            current = current.GetVisualParent();
        }

        return null;
    }

    // ──────────────────────────────────────────────
    //  Drop-text reading (extracted for testability)
    // ──────────────────────────────────────────────

    /// <summary>
    /// Reads text data from the drag payload. The gallery creates the data
    /// as <c>DataTransferItem.CreateText()</c> so we use <see cref="DataTransfer.TryGetText"/>
    /// instead of <see cref="DataTransferItem.TryGetRaw"/> which may return <c>byte[]</c>
    /// after crossing the OLE <c>IDataObject</c> boundary.
    /// </summary>
    internal static string? ReadDropText(IDataTransfer? dataTransfer)
    {
        if (dataTransfer == null) return null;

        // Use Contains on IDataTransfer (unambiguous interface method),
        // then cast to DataTransfer to call TryGetText (not on the interface).
        if (dataTransfer.Contains(DataFormat.Text) && dataTransfer is DataTransfer dt)
        {
            return dt.TryGetText();
        }

        return null;
    }

    // ──────────────────────────────────────────────
    //  Reflection-based helpers
    // ──────────────────────────────────────────────

    private static string GetNodeName(object item)
    {
        var prop = item.GetType().GetProperty("Name");
        return prop?.GetValue(item) as string ?? string.Empty;
    }

    private static IEnumerable GetChildren(object item)
    {
        if (item == null) return Enumerable.Empty<object>();
        var prop = item.GetType().GetProperty("Children");
        return prop?.GetValue(item) as IEnumerable ?? Enumerable.Empty<object>();
    }

    private static int GetNodeCount(object item)
    {
        // Try Count property first (CategoryNode), then AssetCount (KeywordNode, FolderNode, etc.)
        var countProp = item.GetType().GetProperty("Count");
        if (countProp?.GetValue(item) is int count)
            return count;

        var assetCountProp = item.GetType().GetProperty("AssetCount");
        if (assetCountProp?.GetValue(item) is int assetCount)
            return assetCount;

        return 0;
    }
}
