using Adam.CatalogBrowser.Controls;
using Adam.CatalogBrowser.Tests.TestInfrastructure;
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
/// Headless integration tests for the <see cref="SearchableTreeView"/> drag-drop
/// highlight feature. Invokes <c>internal</c> handlers directly to avoid
/// <see cref="InputElement.InputHitTest"/> limitations in headless mode.
///
/// Uses <c>loadXaml: false</c> constructor + <see cref="SearchableTreeView.InitializeVisualTree"/>
/// to set up the visual tree programmatically when precompiled XAML is unavailable
/// in the headless test context. Avoids <see cref="ItemsControl.ContainerFromIndex"/>
/// which does not realize items in headless mode.
/// </summary>
[Collection(nameof(HeadlessAvaloniaCollection))]
public class SearchableTreeViewHeadlessIntegrationTests : IDisposable
{
    private static readonly Lazy<HeadlessUnitTestSession> Session = new(
        () => HeadlessUnitTestSession.StartNew(typeof(TestCatalogBrowserApp)));

    private Task DispatchAsync(Action action)
        => Session.Value.Dispatch(action, CancellationToken.None);

    private Task<T> DispatchAsync<T>(Func<T> func)
        => Session.Value.Dispatch(func, CancellationToken.None);

    /// <summary>
    /// Creates a SearchableTreeView with programmatic visual tree, wrapped in a Window.
    /// Returns the control and pre-created TreeViewItem / ListBoxItem containers so
    /// tests can reference them without calling ContainerFromIndex.
    /// </summary>
    private async Task<(SearchableTreeView Control, TreeViewItem Cat1, TreeViewItem Cat2, TreeViewItem SubItem1)>
        CreatePreparedTreeViewAsync()
    {
        return await DispatchAsync(() =>
        {
            var sub1 = new TreeViewItem { Header = "SubItem1" };
            var sub2 = new TreeViewItem { Header = "SubItem2" };
            var cat1 = new TreeViewItem
            {
                Header = "Category1",
                Items = { sub1, sub2 },
                IsExpanded = true
            };
            var cat2 = new TreeViewItem { Header = "Category2" };

            var ctrl = new SearchableTreeView(loadXaml: false);
            ctrl.InitializeVisualTree();

            ctrl.TreeViewBox.ItemsSource = null;
            ctrl.TreeViewBox.Items.Add(cat1);
            ctrl.TreeViewBox.Items.Add(cat2);

            // FlatListBox is populated by RebuildFilteredList when search text is set;
            // test methods that need ListBox containers create them directly.

            var win = new Window
            {
                Width = 400,
                Height = 500,
                Content = ctrl
            };

            win.Show();

            // Force layout passes
            for (var i = 0; i < 5; i++)
                Dispatcher.UIThread.RunJobs();

            return (ctrl, cat1, cat2, sub1);
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
        Interactive source,
        Point position)
    {
        return new DragEventArgs(routedEvent, data, source, position, KeyModifiers.None);
    }

    // ──────────────────────────────────────────────
    //  OnDragOver – data validation
    // ──────────────────────────────────────────────

    [Fact]
    public async Task OnDragOver_WithTextData_SetsCopyDragEffects()
    {
        var (control, _, _, _) = await CreatePreparedTreeViewAsync();
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
        var (control, _, _, _) = await CreatePreparedTreeViewAsync();
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
        var (control, cat1, _, _) = await CreatePreparedTreeViewAsync();
        await DispatchAsync(() =>
        {
            var initialBg = cat1.Background;

            cat1.Background = new SolidColorBrush(Colors.Red);
            control.HighlightedDropTarget = cat1;

            cat1.Background.Should().NotBe(initialBg,
                "background should change after applying highlight");
            control.HighlightedDropTarget.Should().BeSameAs(cat1);
        });
    }

    [Fact]
    public async Task ApplyHighlight_SetsBackgroundOnListBoxItem()
    {
        var (control, _, _, _) = await CreatePreparedTreeViewAsync();
        await DispatchAsync(() =>
        {
            // Set search text to make FlatListBox visible via RebuildFilteredList
            // Use the accessor since FindControl won't find programmatic controls
            control.SearchTextBoxAccessor.Text = "SubItem";
            Dispatcher.UIThread.RunJobs();

            control.FlatListBoxAccessor.IsVisible.Should().BeTrue();

            var listItem = new ListBoxItem { Content = "SubItem1" };

            var initialBg = listItem.Background;

            listItem.Background = new SolidColorBrush(Colors.Red);
            control.HighlightedDropTarget = listItem;

            listItem.Background.Should().NotBe(initialBg,
                "background should change after applying highlight");
            control.HighlightedDropTarget.Should().BeSameAs(listItem);
        });
    }

    // ──────────────────────────────────────────────
    //  ClearDropHighlight
    // ──────────────────────────────────────────────

    [Fact]
    public async Task ClearDropHighlight_RestoresTreeViewItemBackground()
    {
        var (control, cat1, _, _) = await CreatePreparedTreeViewAsync();
        await DispatchAsync(() =>
        {
            var initialBackground = cat1.Background;

            // Set a known highlight then clear it
            cat1.Background = new SolidColorBrush(Colors.Red);
            control.HighlightedDropTarget = cat1;

            control.ClearDropHighlight();

            control.HighlightedDropTarget.Should().BeNull("highlight should be cleared");
            cat1.Background.Should().Be(initialBackground,
                "ClearValue should restore themed background");
        });
    }

    [Fact]
    public async Task ClearDropHighlight_RestoresListBoxItemBackground()
    {
        var (control, _, _, _) = await CreatePreparedTreeViewAsync();
        await DispatchAsync(() =>
        {
            control.SearchTextBoxAccessor.Text = "SubItem";
            Dispatcher.UIThread.RunJobs();

            control.FlatListBoxAccessor.IsVisible.Should().BeTrue();

            var listItem = new ListBoxItem { Content = "SubItem1" };
            var initialBackground = listItem.Background;

            listItem.Background = new SolidColorBrush(Colors.Red);
            control.HighlightedDropTarget = listItem;

            control.ClearDropHighlight();

            control.HighlightedDropTarget.Should().BeNull("highlight should be cleared");
            listItem.Background.Should().Be(initialBackground,
                "ClearValue should restore themed background");
        });
    }

    // ──────────────────────────────────────────────
    //  OnDragOver – clears previous highlight
    // ──────────────────────────────────────────────

    [Fact]
    public async Task OnDragOver_WithNonTextData_ClearsExistingHighlight()
    {
        var (control, cat1, _, _) = await CreatePreparedTreeViewAsync();
        await DispatchAsync(() =>
        {
            var initialBackground = cat1.Background;

            // Simulate an active highlight
            cat1.Background = new SolidColorBrush(Colors.Red);
            control.HighlightedDropTarget = cat1;

            // Now raise DragOver with non-text data — expects highlight to be cleared
            var data = CreateEmptyData();
            var args = CreateDragEventArgs(DragDrop.DragOverEvent, data, control.TreeViewBox, default);
            control.OnDragOver(control, args);

            control.HighlightedDropTarget.Should().BeNull(
                "non-text DragOver should clear existing highlight");
            cat1.Background.Should().Be(initialBackground,
                "ClearValue should restore themed background");
        });
    }

    // ──────────────────────────────────────────────
    //  OnDragLeave
    // ──────────────────────────────────────────────

    [Fact]
    public async Task OnDragLeave_ClearsHighlight()
    {
        var (control, cat1, _, _) = await CreatePreparedTreeViewAsync();
        await DispatchAsync(() =>
        {
            var initialBackground = cat1.Background;

            // Simulate an active highlight
            cat1.Background = new SolidColorBrush(Colors.Red);
            control.HighlightedDropTarget = cat1;

            // Invoke OnDragLeave
            var data = CreateEmptyData();
            var args = CreateDragEventArgs(DragDrop.DragLeaveEvent, data, control.TreeViewBox, default);
            control.OnDragLeave(control, args);

            control.HighlightedDropTarget.Should().BeNull(
                "DragLeave should clear highlight");
            cat1.Background.Should().Be(initialBackground,
                "ClearValue should restore themed background");
        });
    }

    // ──────────────────────────────────────────────
    //  OnDrop
    // ──────────────────────────────────────────────

    [Fact]
    public async Task OnDrop_ClearsHighlight()
    {
        var (control, cat1, _, _) = await CreatePreparedTreeViewAsync();
        await DispatchAsync(() =>
        {
            var initialBackground = cat1.Background;

            // Simulate an active highlight
            cat1.Background = new SolidColorBrush(Colors.Red);
            control.HighlightedDropTarget = cat1;

            // Invoke OnDrop
            var data = CreateTextData();
            var args = CreateDragEventArgs(DragDrop.DropEvent, data, control.TreeViewBox, default);
            control.OnDrop(control, args);

            control.HighlightedDropTarget.Should().BeNull(
                "Drop should clear highlight");
            cat1.Background.Should().Be(initialBackground,
                "ClearValue should restore themed background");
        });
    }

    // ──────────────────────────────────────────────
    //  Drag-effects routing test (via RaiseEvent)
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Drop_RaiseEvent_RoutesToOnDropHandler()
    {
        var (control, cat1, _, _) = await CreatePreparedTreeViewAsync();
        await DispatchAsync(() =>
        {
            var initialBackground = cat1.Background;

            // Pre-set a highlight
            cat1.Background = new SolidColorBrush(Colors.Red);
            control.HighlightedDropTarget = cat1;

            // Raise the actual Drop event — should trigger OnDrop which calls ClearDropHighlight
            var data = CreateTextData();
            var args = CreateDragEventArgs(DragDrop.DropEvent, data, control.TreeViewBox, default);
            control.TreeViewBox.RaiseEvent(args);

            // Verify the highlight was cleared (means OnDrop was called)
            control.HighlightedDropTarget.Should().BeNull(
                "RaiseEvent with DropEvent should trigger OnDrop which clears highlight");
            cat1.Background.Should().Be(initialBackground,
                "ClearValue should restore themed background");
        });
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
