using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Adam.CatalogBrowser.ViewModels;

namespace Adam.CatalogBrowser.Views;

/// <summary>
/// Converts a non-null value to true (visible), null to false (collapsed).
/// </summary>
public class NullToBoolConverter : IValueConverter
{
    public static readonly NullToBoolConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value != null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts a boolean to a border brush for filmstrip items: true → accent, false → transparent.
/// </summary>
public class BoolToFilmstripBorderConverter : IValueConverter
{
    public static readonly BoolToFilmstripBorderConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b && b)
            return new SolidColorBrush(Color.FromRgb(25, 118, 210));
        return Brushes.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts a bool to a visibility: true → Visible, false → Collapsed.
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public static readonly BoolToVisibilityConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b;
}

/// <summary>
/// Converts a CompareViewMode enum to button text.
/// </summary>
public class CompareViewModeToButtonTextConverter : IValueConverter
{
    public static readonly CompareViewModeToButtonTextConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is CompareViewMode mode)
            return mode == CompareViewMode.Overlay ? "Side-by-side" : "Overlay";
        return "View mode";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts a diff status boolean to an icon: true → ✗ (different), false → ✓ (same).
/// </summary>
public class DiffStatusToIconConverter : IValueConverter
{
    public static readonly DiffStatusToIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b && b ? "✗" : "✓";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts a diff status boolean to a color: true → Orange (different), false → Green (same).
/// </summary>
public class DiffStatusToColorConverter : IValueConverter
{
    public static readonly DiffStatusToColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b && b)
            return new SolidColorBrush(Color.FromRgb(245, 127, 23));
        return new SolidColorBrush(Color.FromRgb(76, 175, 80));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
