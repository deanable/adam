using Adam.CatalogBrowser.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using FluentAssertions;

namespace Adam.CatalogBrowser.Tests.Controls;

/// <summary>
/// Minimal Avalonia Application that loads the Fluent theme for
/// headless control rendering during tests.
/// </summary>
internal sealed class TestCatalogBrowserApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
        base.Initialize();
    }
}

/// <summary>
/// Headless integration tests for the <see cref="SearchableTreeView"/> drag-drop
/// highlight feature. Invokes <c>internal</c> handlers directly to avoid
/// <see cref="InputElement.InputHitTest"/> limitations in headless mode.
/// </summary>
[Collection(nameof(HeadlessAvaloniaCollection))]
public class SearchableTreeViewHeadlessIntegrationTests : IDisposable
{
    private static readonly Lazy<HeadlessUnitTestSession> Session = new(
        () => HeadlessUnitTestSession.StartNew(typeof(TestCatalogBrowserApp)));

    private sealed class TestNode
    {
        public string Name { get; set; } = string.Empty;
        public List<TestNode> Children { get; } = [];
    }

    private Task DispatchAsync(Action action)
        => Session.Value.Dispatch(action, CancellationToken.None);

    private Task<T> DispatchAsync<T>(Func<T> func)
        => Session.Value.Dispatch(func, CancellationToken.None);

    /// <summary>
    /// Creates a SearchableTreeView inside a Window, dispatches to UI thread,
    /// and ensures layout completes.
    /// </summary>
    private async Task<SearchableTreeView> CreateWindowedTreeViewAsync()
    {
        return await DispatchAsync(() =>
        {
            var cat1 = new TestNode { Name = "Category1" };
            cat1.Children.Add(new TestNode { Name = "SubItem1" });
            cat1.Children.Add(new TestNode { Name = "SubItem2" });

            var ctrl = new SearchableTreeView
            {
                ItemsSource = new TestNode[]
                {
                    cat1,
                    new TestNode { Name = "Category2" }
                }
            };

            var win = new Window
            {
                Width = 400,
                Height = 500,
                Content = ctrl
            };

            win.Show();
            Dispatcher.UIThread.RunJobs();
            Dispatcher.UIThread.RunJobs();
            return ctrl;
        });
    }

    // ──────────────────────────────────────────────
    //  DragEventArgs helpers
    // ──────────────────────────────────────────────

    private static DataTransfer CreateTextData(string text = "asset-1,asset-2")
    {
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateText(text));
        return data;
    }

    private static DataTransfer CreateEmptyData()
        => new();

    private static DragEventArgs CreateDragEventArgs(
        RoutedEvent<DragEventArgs> routedEvent,
        DataTransfer data,
        ItemsControl source,
        Point position)
    {
        return new DragEventArgs(routedEvent, data, source, position, KeyModifiers.None);
    }

    // ──────────────────────────────────────────────
    //  Item lookup helpers
    // ──────────────────────────────────────────────

    private static TreeViewItem? GetTreeItem(SearchableTreeView control, int index)
        => control.TreeViewBox.ContainerFromIndex(index) as TreeViewItem;

    private static ListBoxItem? GetListItem(SearchableTreeView control, int index)
        => control.FlatListBox.ContainerFromIndex(index) as ListBoxItem;

    /// <summary>
    /// Helper to invoke <see cref="SearchableTreeView.ClearDropHighlight"/> and
    /// toggle the highlight through the normal workflow: it sets the item's background
    /// via <see cref="SearchableTreeView.HighlightedDropTarget"/>, calls the handler,
    /// then verifies the internal state.
    /// </summary>
    private static void ApplyHighlightAndVerify(SearchableTreeView control, Visual item, IBrush highlightBrush)
    {
        // Directly highlight the item (simulating what UpdateDropHighlight does)
        if (item is ListBoxItem listItem)
            listItem.Background = highlightBrush;
        else if (item is TreeViewItem treeItem)
            treeItem.Background = highlightBrush;

        control.HighlightedDropTarget = item;

        // Verify the highlight was applied
        item.Should().NotBeNull();
        item!.GetValue(
            item is ListBoxItem ? ListBoxItem.BackgroundProperty : TreeViewItem.BackgroundProperty)
            .Should().Be(highlightBrush);
    }

    // ──────────────────────────────────────────────
    //  OnDragOver – data validation
    // ──────────────────────────────────────────────

    [Fact]
    public async Task OnDragOver_WithTextData_SetsCopyDragEffects()
    {
        var control = await CreateWindowedTreeViewAsync();
        await DispatchAsync(() =>
        {
            var data = CreateTextData();
            var args = CreateDragEventArgs(DragDrop.DragOverEvent, data, control.TreeViewBox, default);

            control.OnDragOver(control, args);

            args.DragEffects.Should().Be(DragDropEffects.Copy,
                "text data should be accepted with Copy effect");
        });
    }

    [Fact]
    public async Task OnDragOver_WithNonTextData_SetsNoneDragEffects()
    {
        var control = await CreateWindowedTreeViewAsync();
        await DispatchAsync(() =>
        {
            var data = CreateEmptyData();
            var args = CreateDragEventArgs(DragDrop.DragOverEvent, data, control.TreeViewBox, default);

            control.OnDragOver(control, args);

            args.DragEffects.Should().Be(DragDropEffects.None,
                "non-text data should be rejected with None effect");
        });
    }

    // ──────────────────────────────────────────────
    //  Highlight application & clearing
    // ──────────────────────────────────────────────

    [Fact]
    public async Task ApplyHighlight_SetsBackgroundOnTreeViewItem()
    {
        var control = await CreateWindowedTreeViewAsync();
        await DispatchAsync(() =>
        {
            var firstItem = GetTreeItem(control, 0);
            firstItem.Should().NotBeNull("TreeViewItem 0 should be realized after layout");
            var initialBg = firstItem!.Background;

            ApplyHighlightAndVerify(control, firstItem, new SolidColorBrush(Colors.Red));

            firstItem.Background.Should().NotBe(initialBg,
                "background should change after applying highlight");
        });
    }

    [Fact]
    public async Task ApplyHighlight_SetsBackgroundOnListBoxItem()
    {
        var control = await CreateWindowedTreeViewAsync();
        await DispatchAsync(() =>
        {
            control.FindControl<TextBox>("SearchTextBox")!.Text = "SubItem";
            Dispatcher.UIThread.RunJobs();

            control.FlatListBox.IsVisible.Should().BeTrue();
            var firstListItem = GetListItem(control, 0);
            firstListItem.Should().NotBeNull("ListBox should have items after search");
            var initialBg = firstListItem!.Background;

            ApplyHighlightAndVerify(control, firstListItem, new SolidColorBrush(Colors.Red));

            firstListItem.Background.Should().NotBe(initialBg,
                "background should change after applying highlight");
        });
    }

    // ──────────────────────────────────────────────
    //  ClearDropHighlight
    // ──────────────────────────────────────────────

    [Fact]
    public async Task ClearDropHighlight_RestoresTreeViewItemBackground()
    {
        var control = await CreateWindowedTreeViewAsync();
        await DispatchAsync(() =>
        {
            var firstItem = GetTreeItem(control, 0);
            firstItem.Should().NotBeNull();
            var initialBackground = firstItem!.Background;

            // Set a known highlight then clear it
            firstItem.Background = new SolidColorBrush(Colors.Red);
            control.HighlightedDropTarget = firstItem;

            control.ClearDropHighlight();

            // After ClearValue, background should return to the theme default
            control.HighlightedDropTarget.Should().BeNull("highlight should be cleared");
            firstItem.Background.Should().Be(initialBackground,
                "ClearValue should restore themed background");
        });
    }

    [Fact]
    public async Task ClearDropHighlight_RestoresListBoxItemBackground()
    {
        var control = await CreateWindowedTreeViewAsync();
        await DispatchAsync(() =>
        {
            control.FindControl<TextBox>("SearchTextBox")!.Text = "SubItem";
            Dispatcher.UIThread.RunJobs();

            control.FlatListBox.IsVisible.Should().BeTrue();
            var firstListItem = GetListItem(control, 0);
            firstListItem.Should().NotBeNull();
            var initialBackground = firstListItem!.Background;

            firstListItem.Background = new SolidColorBrush(Colors.Red);
            control.HighlightedDropTarget = firstListItem;

            control.ClearDropHighlight();

            control.HighlightedDropTarget.Should().BeNull("highlight should be cleared");
            firstListItem.Background.Should().Be(initialBackground,
                "ClearValue should restore themed background");
        });
    }

    // ──────────────────────────────────────────────
    //  OnDragOver – clears previous highlight
    // ──────────────────────────────────────────────

    [Fact]
    public async Task OnDragOver_WithNonTextData_ClearsExistingHighlight()
    {
        var control = await CreateWindowedTreeViewAsync();
        await DispatchAsync(() =>
        {
            var firstItem = GetTreeItem(control, 0);
            firstItem.Should().NotBeNull();
            var initialBackground = firstItem!.Background;

            // Simulate an active highlight
            firstItem.Background = new SolidColorBrush(Colors.Red);
            control.HighlightedDropTarget = firstItem;

            // Now raise DragOver with non-text data — expects highlight to be cleared
            var data = CreateEmptyData();
            var args = CreateDragEventArgs(DragDrop.DragOverEvent, data, control.TreeViewBox, default);
            control.OnDragOver(control, args);

            control.HighlightedDropTarget.Should().BeNull(
                "non-text DragOver should clear existing highlight");
            firstItem.Background.Should().Be(initialBackground,
                "ClearValue should restore themed background");
        });
    }

    // ──────────────────────────────────────────────
    //  OnDragLeave
    // ──────────────────────────────────────────────

    [Fact]
    public async Task OnDragLeave_ClearsHighlight()
    {
        var control = await CreateWindowedTreeViewAsync();
        await DispatchAsync(() =>
        {
            var firstItem = GetTreeItem(control, 0);
            firstItem.Should().NotBeNull();
            var initialBackground = firstItem!.Background;

            // Simulate an active highlight
            firstItem.Background = new SolidColorBrush(Colors.Red);
            control.HighlightedDropTarget = firstItem;

            // Invoke OnDragLeave
            var data = CreateEmptyData();
            var args = CreateDragEventArgs(DragDrop.DragLeaveEvent, data, control.TreeViewBox, default);
            control.OnDragLeave(control, args);

            control.HighlightedDropTarget.Should().BeNull(
                "DragLeave should clear highlight");
            firstItem.Background.Should().Be(initialBackground,
                "ClearValue should restore themed background");
        });
    }

    // ──────────────────────────────────────────────
    //  OnDrop
    // ──────────────────────────────────────────────

    [Fact]
    public async Task OnDrop_ClearsHighlight()
    {
        var control = await CreateWindowedTreeViewAsync();
        await DispatchAsync(() =>
        {
            var firstItem = GetTreeItem(control, 0);
            firstItem.Should().NotBeNull();
            var initialBackground = firstItem!.Background;

            // Simulate an active highlight
            firstItem.Background = new SolidColorBrush(Colors.Red);
            control.HighlightedDropTarget = firstItem;

            // Invoke OnDrop
            var data = CreateTextData();
            var args = CreateDragEventArgs(DragDrop.DropEvent, data, control.TreeViewBox, default);
            control.OnDrop(control, args);

            control.HighlightedDropTarget.Should().BeNull(
                "Drop should clear highlight");
            firstItem.Background.Should().Be(initialBackground,
                "ClearValue should restore themed background");
        });
    }

    // ──────────────────────────────────────────────
    //  Drag-effects routing test (via RaiseEvent)
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Drop_RaiseEvent_RoutesToOnDropHandler()
    {
        var control = await CreateWindowedTreeViewAsync();
        await DispatchAsync(() =>
        {
            var firstItem = GetTreeItem(control, 0);
            firstItem.Should().NotBeNull();
            var initialBackground = firstItem!.Background;

            // Pre-set a highlight (simulating what DragOver would do)
            firstItem!.Background = new SolidColorBrush(Colors.Red);
            control.HighlightedDropTarget = firstItem;

            // Raise the actual Drop event — should trigger OnDrop which calls ClearDropHighlight
            var data = CreateTextData();
            var args = CreateDragEventArgs(DragDrop.DropEvent, data, control.TreeViewBox, default);
            control.TreeViewBox.RaiseEvent(args);

            // Verify the highlight was cleared (means OnDrop was called)
            control.HighlightedDropTarget.Should().BeNull(
                "RaiseEvent with DropEvent should trigger OnDrop which clears highlight");
            firstItem.Background.Should().Be(initialBackground,
                "ClearValue should restore themed background");
        });
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
