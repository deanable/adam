using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Styling;
using Adam.CatalogBrowser.ViewModels;

namespace Adam.CatalogBrowser.Views;

public partial class AssetGalleryView : UserControl
{
    public AssetGalleryView()
    {
        InitializeComponent();
        GalleryScroller.ScrollChanged += OnScrollChanged;
        SetupItemTransitions();
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
