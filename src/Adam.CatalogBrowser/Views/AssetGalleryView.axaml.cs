using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Adam.CatalogBrowser.Controls;
using Adam.CatalogBrowser.Models;
using Adam.CatalogBrowser.ViewModels;

namespace Adam.CatalogBrowser.Views;

public partial class AssetGalleryView : UserControl
{
    // ──────────────────────────────────────────────
    //  Drag ghost (overlay preview window, reused across drags)
    // ──────────────────────────────────────────────

    private readonly DragGhostWindow _dragGhost = new();
    private readonly DispatcherTimer _dragTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(30)
    };

    public AssetGalleryView()
    {
        InitializeComponent();
        GalleryScroller.ScrollChanged += OnScrollChanged;
        SetupItemTransitions();

        // Drag source: initiate drag on pointer press with selected asset IDs.
        // The sidebar's SearchableTreeView reads the text data to process bulk
        // keyword/category assignments via the BulkOperationQueue.
        GridViewBox.PointerPressed += OnGalleryPointerPressed;
        GridViewBox.PointerMoved += OnGalleryPointerMoved;
        ListViewBox.PointerPressed += OnGalleryPointerPressed;
        ListViewBox.PointerMoved += OnGalleryPointerMoved;

        // T20.2: Wire loupe open on double-click
        GridViewBox.DoubleTapped += OnGalleryDoubleTapped;
        ListViewBox.DoubleTapped += OnGalleryDoubleTapped;

        // Wire context menus programmatically (T8.16) — avoids
        // FindAncestor Window bindings that break in MenuFlyout popup roots.
        GridViewBox.ContextRequested += OnContextRequested;
        ListViewBox.ContextRequested += OnContextRequested;

        _dragTimer.Tick += OnDragTimerTick;

        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        _dragTimer.Stop();
        _dragGhost.HideGhost();
        _dragGhost.Close();
    }

    private static Style CreateItemTransitionStyle()
    {
        var style = new Style(x => x.OfType<ListBoxItem>());
        style.Add(new Setter
        {
            Property = ListBoxItem.TransitionsProperty,
            Value = new Transitions
            {
                new BrushTransition
                {
                    Property = ListBoxItem.BackgroundProperty,
                    Duration = TimeSpan.FromMilliseconds(150)
                }
            }
        });
        return style;
    }

    private static Style CreateAccentTransitionStyle()
    {
        var style = new Style(x => x.OfType<Border>().Class("LeftAccent"));
        style.Add(new Setter
        {
            Property = Border.TransitionsProperty,
            Value = new Transitions
            {
                new BrushTransition
                {
                    Property = Border.BackgroundProperty,
                    Duration = TimeSpan.FromMilliseconds(150)
                }
            }
        });
        return style;
    }

    private void SetupItemTransitions()
    {
        ListViewBox?.Styles.Add(CreateItemTransitionStyle());
        GridViewBox?.Styles.Add(CreateItemTransitionStyle());
        ListViewBox?.Styles.Add(CreateAccentTransitionStyle());
    }

    private async void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (DataContext is not AssetGalleryViewModel vm) return;

        // T21.1: Update viewport tracking for visibility-based thumbnail loading
        vm.UpdateViewport(
            GalleryScroller.Offset.Y,
            GalleryScroller.Viewport.Height,
            GalleryScroller.Viewport.Width);

        // Infinite scroll: trigger load more at 80% scroll threshold
        var scrollableHeight = GalleryScroller.Extent.Height - GalleryScroller.Viewport.Height;
        if (scrollableHeight <= 0) return;

        var threshold = scrollableHeight * 0.8;
        if (GalleryScroller.Offset.Y >= threshold)
        {
            await vm.LoadMoreAsync();
        }
    }

    // ──────────────────────────────────────────────
    //  Drag source (Avalonia 12 DoDragDropAsync API)
    //  The OS handles the drag threshold, so we initiate the drag
    //  directly from PointerPressed with the selected asset IDs as text.
    //  A ghost overlay appears at the press position as a visual preview
    //  showing the primary asset thumbnail and selection count.
    //  On Windows, PointerMoved events are suppressed during an OS drag,
    //  so a DispatcherTimer polls Win32 GetCursorPos to track the cursor.
    // ──────────────────────────────────────────────

    private async void OnGalleryPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not AssetGalleryViewModel vm) return;

        var selectedAssets = vm.SelectedAssets.ToList();
        if (selectedAssets.Count == 0) return;

        var selectedIds = selectedAssets
            .Select(a => a.Id.ToString())
            .ToList();

        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateText(string.Join(",", selectedIds)));

        // Show the drag ghost at the initial cursor position
        if (sender is Control sourceControl)
        {
            var startPos = e.GetPosition(sourceControl);
            var screenPos = sourceControl.PointToScreen(startPos);

            var primaryAsset = selectedAssets.FirstOrDefault();

            _dragGhost.ShowGhost(screenPos, selectedAssets.Count, primaryAsset?.ThumbnailPath);
        }

        // Start polling cursor position during the OLE drag
        // (PointerMoved events are suppressed on Windows)
        _dragTimer.Start();

        try
        {
            await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Copy);
        }
        finally
        {
            // Stop the timer and hide the ghost after drag completes
            _dragTimer.Stop();
            _dragGhost.HideGhost();
        }
    }

    /// <summary>
    /// Polls cursor position via Win32 API to move the ghost overlay.
    /// Runs on a DispatcherTimer (~30ms interval) during the drag.
    /// Only runs on Windows (the P/Invoke would throw on macOS/Linux).
    /// On non-Windows platforms, the PointerMoved handler (<see cref="OnGalleryPointerMoved"/>)
    /// provides cursor tracking since pointer events are not suppressed there.
    /// </summary>
    private void OnDragTimerTick(object? sender, EventArgs e)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (!_dragGhost.IsDragging) return;
        var cursorPos = Win32CursorHelper.GetScreenCursorPosition();
        // Guard against the (0,0) fallback if GetCursorPos fails.
        // On multi-monitor setups, (0,0) could theoretically be a valid
        // cursor position (e.g. a secondary display above/left of primary),
        // but missing one frame at 30ms is negligible.
        if (cursorPos.X == 0 && cursorPos.Y == 0) return;
        _dragGhost.UpdatePosition(cursorPos);
    }

    /// <summary>
    /// Track cursor position during drag to move the ghost overlay.
    /// During an OS drag-drop operation, pointer events may still fire
    /// on the source control depending on the platform; if they do,
    /// the ghost will follow the cursor in real time.
    /// On Windows, these events are suppressed, so the DispatcherTimer
    /// (OnDragTimerTick) handles cursor tracking instead.
    /// </summary>
    private void OnGalleryPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragGhost.IsDragging) return;
        if (sender is not Control sourceControl) return;

        var pos = e.GetPosition(sourceControl);
        var screenPos = sourceControl.PointToScreen(pos);
        _dragGhost.UpdatePosition(screenPos);
    }

    // ──────────────────────────────────────────────
    //  Context menu (T8.16)
    // ──────────────────────────────────────────────

    /// <summary>
    /// Handles right-click / context key on gallery items.
    /// Selects the right-clicked item (if not already selected) and shows
    /// a fully programmatic MenuFlyout whose commands are bound directly
    /// to the MainWindowViewModel — no XAML FindAncestor bindings needed.
    /// </summary>
    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not ListBox listBox) return;

        // Find which item was right-clicked
        var source = e.Source as Control;
        var listBoxItem = source?.FindAncestorOfType<ListBoxItem>();
        if (listBoxItem == null) return;

        var window = this.FindAncestorOfType<Window>();
        if (window?.DataContext is not MainWindowViewModel vm) return;

        // If the right-clicked item isn't already selected, select only it.
        // This preserves multi-selection when right-clicking within an existing selection.
        var clickedItem = listBoxItem.DataContext;
        if (clickedItem != null && listBox.SelectedItems != null)
        {
            var alreadySelected = listBox.SelectedItems
                .Cast<object>()
                .Any(x => ReferenceEquals(x, clickedItem));

            if (!alreadySelected)
            {
                listBox.SelectedItems.Clear();
                listBox.SelectedItems.Add(clickedItem);
            }
        }

        var flyout = BuildContextMenu(vm);
        flyout.ShowAt(listBoxItem);
        e.Handled = true;
    }

    /// <summary>
    /// Builds the gallery item context menu with commands from MainWindowViewModel.
    /// Features rate sub-menu with exact values (T14.5).
    /// Label and flag are simple cycle items (no parameterized commands available yet).
    /// All commands are wired directly as ICommand references — no XAML bindings.
    /// </summary>
    private static MenuFlyout BuildContextMenu(MainWindowViewModel vm)
    {
        var flyout = new MenuFlyout();

        // T14.5: Rate sub-menu with exact values via CommandParameter
        var rateSub = new MenuItem { Header = "Rate" };
        rateSub.Items.Add(new MenuItem { Header = "Unrate (0)", Command = vm.SetRatingCommand, CommandParameter = "0" });
        rateSub.Items.Add(new MenuItem { Header = "\u2605 1", Command = vm.SetRatingCommand, CommandParameter = "1" });
        rateSub.Items.Add(new MenuItem { Header = "\u2605\u2605 2", Command = vm.SetRatingCommand, CommandParameter = "2" });
        rateSub.Items.Add(new MenuItem { Header = "\u2605\u2605\u2605 3", Command = vm.SetRatingCommand, CommandParameter = "3" });
        rateSub.Items.Add(new MenuItem { Header = "\u2605\u2605\u2605\u2605 4", Command = vm.SetRatingCommand, CommandParameter = "4" });
        rateSub.Items.Add(new MenuItem { Header = "\u2605\u2605\u2605\u2605\u2605 5", Command = vm.SetRatingCommand, CommandParameter = "5" });
        flyout.Items.Add(rateSub);

        flyout.Items.Add(new MenuItem { Header = "Cycle Label", Command = vm.SetLabelCommand });
        flyout.Items.Add(new MenuItem { Header = "Cycle Flag", Command = vm.SetFlagCommand });
        flyout.Items.Add(new Separator());
        flyout.Items.Add(new MenuItem { Header = "AI Tag", Command = vm.AiTagSelectedCommand });
        flyout.Items.Add(new MenuItem { Header = "Export...", Command = vm.ExportCommand });
        flyout.Items.Add(new Separator());
        flyout.Items.Add(new MenuItem { Header = "Rotate 90\u00B0 CW", Command = vm.RotateClockwiseCommand });
        flyout.Items.Add(new MenuItem { Header = "Rotate 90\u00B0 CCW", Command = vm.RotateCounterClockwiseCommand });
        flyout.Items.Add(new MenuItem { Header = "Flip Horizontal", Command = vm.FlipHorizontalCommand });
        flyout.Items.Add(new MenuItem { Header = "Flip Vertical", Command = vm.FlipVerticalCommand });
        flyout.Items.Add(new Separator());
        flyout.Items.Add(new MenuItem { Header = "Reveal in Folder", Command = vm.RevealInFolderCommand });
        flyout.Items.Add(new MenuItem { Header = "Copy File Path", Command = vm.CopyFilePathCommand });
        flyout.Items.Add(new MenuItem { Header = "Copy File", Command = vm.CopyFileCommand });
        flyout.Items.Add(new Separator());
        flyout.Items.Add(new MenuItem { Header = "Delete...", Command = vm.DeleteSelectedCommand });

        return flyout;
    }

    // ──────────────────────────────────────────────
    //  Selection
    // ──────────────────────────────────────────────

    /// <summary>
    /// T20.2: Opens the loupe view when an asset is double-clicked.
    /// </summary>
    private void OnGalleryDoubleTapped(object? sender, TappedEventArgs e)
    {
        var source = e.Source as Control;
        var listBoxItem = source?.FindAncestorOfType<ListBoxItem>();
        if (listBoxItem?.DataContext is not AssetListItem asset) return;

        if (DataContext is AssetGalleryViewModel vm)
            vm.RequestOpenAsset(asset);
    }

    private void OnGallerySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not AssetGalleryViewModel vm) return;
        if (sender is not ListBox listBox) return;

        var items = listBox.SelectedItems ?? Array.Empty<object?>();
        vm.UpdateSelection(items.Cast<object?>().ToList());
    }
}

