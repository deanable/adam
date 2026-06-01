using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Adam.ServiceManager.ViewModels;
using System.Globalization;

namespace Adam.ServiceManager.Views;

public partial class ServiceManagerView : UserControl
{
    public ServiceManagerView()
    {
        InitializeComponent();
    }
}

/// <summary>
/// Converts <see cref="ServiceHealth"/> to a solid fill brush for the traffic-light indicator.
/// </summary>
public class ServiceHealthConverter : IValueConverter
{
    public static readonly ServiceHealthConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ServiceHealth health)
        {
            return health switch
            {
                ServiceHealth.Green => new SolidColorBrush(Color.Parse("#2E7D32")),   // Dark green
                ServiceHealth.Amber => new SolidColorBrush(Color.Parse("#F57F17")),   // Amber/orange
                ServiceHealth.Red => new SolidColorBrush(Color.Parse("#C62828")),     // Dark red
                _ => new SolidColorBrush(Color.Parse("#9E9E9E"))                      // Grey fallback
            };
        }
        return new SolidColorBrush(Color.Parse("#9E9E9E"));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Converts a boolean (IsElevated) to a badge background color.
/// Green for elevated, amber for not elevated.
/// </summary>
public class AdminBadgeBackgroundConverter : IValueConverter
{
    public static readonly AdminBadgeBackgroundConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isElevated)
        {
            return isElevated
                ? new SolidColorBrush(Color.Parse("#2E7D32"))   // Green
                : new SolidColorBrush(Color.Parse("#E65100"));  // Orange
        }
        return new SolidColorBrush(Color.Parse("#9E9E9E"));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Converts a boolean (IsElevated) to a display string.
/// "Administrator" when true, "Standard User" when false.
/// </summary>
public class AdminBadgeTextConverter : IValueConverter
{
    public static readonly AdminBadgeTextConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool isElevated && isElevated
            ? "Administrator"
            : "Standard User";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Converts a string to a boolean indicating whether it's not null or empty.
/// Useful for showing/hiding status message borders.
/// </summary>
public class NotEmptyConverter : IValueConverter
{
    public static readonly NotEmptyConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is string s && !string.IsNullOrEmpty(s);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
