using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;

namespace Adam.ServiceManager.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
}

/// <summary>
/// Converts a boolean (tab selected) to a background brush for tab buttons.
/// True → semi-transparent white highlight, False → transparent.
/// </summary>
public class TabBgConverter : IValueConverter
{
    public static readonly TabBgConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b && b
            ? new SolidColorBrush(Color.FromArgb(40, 255, 255, 255))
            : Brushes.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
