using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Adam.CatalogBrowser.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
}

/// <summary>
/// Converts a boolean to a background brush for the active mode toggle button.
/// True → #33FFFFFF (semi-transparent white highlight), False → Transparent.
/// </summary>
public class BoolToModeBgConverter : IValueConverter
{
    public static readonly BoolToModeBgConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b && b ? new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)) : Brushes.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts a boolean to a status indicator color: True → LimeGreen (connected), False → Gray (disconnected).
/// </summary>
public class BoolToStatusColorConverter : IValueConverter
{
    public static readonly BoolToStatusColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b && b ? Brushes.LimeGreen : Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
