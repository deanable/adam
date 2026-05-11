using Avalonia.Controls;
using Avalonia.Data.Converters;
using System.Globalization;

namespace Adam.CatalogBrowser.Views;

public partial class AdminPanelView : UserControl
{
    public AdminPanelView()
    {
        InitializeComponent();
    }
}

public class RadioButtonConverter : IValueConverter
{
    public static readonly RadioButtonConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value?.ToString() == parameter?.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? parameter?.ToString() : Avalonia.Data.BindingOperations.DoNothing;
    }
}
