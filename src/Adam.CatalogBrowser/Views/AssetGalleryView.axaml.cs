using Avalonia.Controls;
using Avalonia.Data.Converters;
using Adam.CatalogBrowser.ViewModels;

namespace Adam.CatalogBrowser.Views;

public partial class AssetGalleryView : UserControl
{
    public AssetGalleryView()
    {
        InitializeComponent();
    }
}

public class InverseBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is bool b ? !b : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is bool b ? !b : value;
}
