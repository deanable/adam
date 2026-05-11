using Avalonia.Controls;
using Avalonia.Data.Converters;
using Adam.CatalogBrowser.ViewModels;

namespace Adam.CatalogBrowser.Views;

public partial class MetadataEditorView : UserControl
{
    public MetadataEditorView()
    {
        InitializeComponent();
    }
}

public class InverseBoolConverter2 : IValueConverter
{
    public static readonly InverseBoolConverter2 Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is bool b ? !b : value;
    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is bool b ? !b : value;
}
