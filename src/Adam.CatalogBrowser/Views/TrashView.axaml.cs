using Avalonia.Controls;
using Avalonia.Data.Converters;
using Adam.CatalogBrowser.ViewModels;

namespace Adam.CatalogBrowser.Views;

public partial class TrashView : UserControl
{
    public TrashView()
    {
        InitializeComponent();
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is TrashViewModel vm && sender is ListBox listBox)
        {
            vm.UpdateSelection(listBox.SelectedItems?.Cast<object?>().ToList() ?? []);
        }
    }
}

public class InverseBoolConverter3 : IValueConverter
{
    public static readonly InverseBoolConverter3 Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is bool b ? !b : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is bool b ? !b : value;
}
