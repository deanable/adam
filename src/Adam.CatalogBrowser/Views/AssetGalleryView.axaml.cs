using Avalonia.Controls;
using Avalonia.Data.Converters;
using Adam.CatalogBrowser.ViewModels;

namespace Adam.CatalogBrowser.Views;

public partial class AssetGalleryView : UserControl
{
    public AssetGalleryView()
    {
        InitializeComponent();
        GalleryScroller.ScrollChanged += OnScrollChanged;
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
}

public class InverseBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is bool b ? !b : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is bool b ? !b : value;
}
