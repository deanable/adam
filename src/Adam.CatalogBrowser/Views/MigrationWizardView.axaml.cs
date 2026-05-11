using Avalonia.Controls;
using Avalonia.Data.Converters;
using System.Globalization;

namespace Adam.CatalogBrowser.Views;

public partial class MigrationWizardView : UserControl
{
    public MigrationWizardView()
    {
        InitializeComponent();
    }
}

public class BoolOppositeConverter : IValueConverter
{
    public static readonly BoolOppositeConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b ? !b : value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b ? !b : value;
    }
}
