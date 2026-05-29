using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Adam.CatalogBrowser.Controls;
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
    //  Selection
    // ──────────────────────────────────────────────

    private void OnGallerySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not AssetGalleryViewModel vm) return;
        if (sender is not ListBox listBox) return;

        var items = listBox.SelectedItems ?? Array.Empty<object?>();
        vm.UpdateSelection(items.Cast<object?>().ToList());
    }
}

public class InverseBoolConverter : IValueConverter
{
    public static readonly InverseBoolConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is bool b ? !b : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is bool b ? !b : value;
}
