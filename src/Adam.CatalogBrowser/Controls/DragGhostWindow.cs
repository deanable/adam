using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Adam.CatalogBrowser.Controls;

/// <summary>
/// A small, borderless overlay window that follows the cursor during a
/// drag-drop operation, showing a thumbnail preview of the dragged asset(s)
/// and a count badge.
/// </summary>
public sealed class DragGhostWindow : Window
{
    private readonly Border _previewBorder;
    private readonly Border _imageContainer;
    private readonly TextBlock _countBadge;    /// <summary>
    /// Whether the ghost window is currently shown and tracking.
    /// </summary>
    public bool IsDragging { get; private set; }

    public DragGhostWindow()
    {
        WindowDecorations = WindowDecorations.None;
        ShowInTaskbar = false;
        Topmost = true;
        CanResize = false;
        SizeToContent = SizeToContent.Manual;
        Width = 80;
        Height = 80;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Blur };
        IsHitTestVisible = false;

        // Semi-transparent border with the ghost preview
        _previewBorder = new Border
        {
            Width = 72,
            Height = 72,
            Background = new SolidColorBrush(Color.FromArgb(220, 50, 50, 50)),
            CornerRadius = new CornerRadius(8),
            BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };

        // Inner grid: image area + count badge overlay
        var innerGrid = new Grid();

        _imageContainer = new Border
        {
            Width = 56,
            Height = 56,
            Background = new SolidColorBrush(Color.FromArgb(100, 0, 0, 0)),
            CornerRadius = new CornerRadius(4),
            ClipToBounds = true,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Child = new TextBlock
            {
                Text = "\U0001F4C1", // file folder emoji as fallback
                FontSize = 24,
                Foreground = Brushes.White,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
            }
        };

        innerGrid.Children.Add(_imageContainer);

        // Count badge (top-right corner)
        _countBadge = new TextBlock
        {
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(220, 25, 118, 210)),
            Padding = new Thickness(4, 1, 4, 2),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Margin = new Thickness(0, -6, -6, 0),
        };

        innerGrid.Children.Add(_countBadge);

        _previewBorder.Child = innerGrid;

        Content = _previewBorder;
    }

    /// <summary>
    /// Show the drag ghost at the specified screen position.
    /// </summary>
    /// <param name="screenPosition">The initial cursor position in screen coordinates.</param>
    /// <param name="count">Number of assets being dragged.</param>
    /// <param name="thumbnailPath">Optional path to a thumbnail image to show.</param>
    public void ShowGhost(PixelPoint screenPosition, int count, string? thumbnailPath = null)
    {
        IsDragging = true;

        // Update the count badge
        _countBadge.Text = count > 1 ? count.ToString() : string.Empty;
        _countBadge.IsVisible = count > 1;

        // Try to load a thumbnail image; dispose previous one first
        if (_imageContainer.Child is Image oldImage && oldImage.Source is Bitmap oldBitmap)
        {
            oldBitmap.Dispose();
        }

        if (!string.IsNullOrEmpty(thumbnailPath) && System.IO.File.Exists(thumbnailPath))
        {
            try
            {
                var bitmap = new Bitmap(thumbnailPath);
                _imageContainer.Child = new Image
                {
                    Source = bitmap,
                    Stretch = Stretch.UniformToFill,
                    Width = 56,
                    Height = 56
                };
                _imageContainer.Background = Brushes.Transparent;
            }
            catch
            {
                // Fallback: keep the existing placeholder
            }
        }

        // Position the ghost offset from the cursor so it doesn't obscure it
        var offsetPosition = new PixelPoint(
            screenPosition.X + 16,
            screenPosition.Y + 16);

        Position = offsetPosition;
        Show();
    }

    /// <summary>
    /// Update the ghost's screen position to follow the cursor.
    /// </summary>
    public void UpdatePosition(PixelPoint screenPosition)
    {
        if (!IsDragging) return;

        Position = new PixelPoint(
            screenPosition.X + 16,
            screenPosition.Y + 16);
    }

    /// <summary>
    /// Hide the drag ghost when the drag operation completes.
    /// Also disposes any loaded thumbnail bitmap and restores
    /// the placeholder for the next drag.
    /// </summary>
    public void HideGhost()
    {
        IsDragging = false;

        // Dispose the thumbnail bitmap and restore placeholder
        if (_imageContainer.Child is Image oldImage && oldImage.Source is Bitmap oldBitmap)
        {
            oldBitmap.Dispose();
        }

        _imageContainer.Child = new TextBlock
        {
            Text = "\U0001F4C1",
            FontSize = 24,
            Foreground = Brushes.White,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };
        _imageContainer.Background = new SolidColorBrush(Color.FromArgb(100, 0, 0, 0));

        Hide();
    }
}
