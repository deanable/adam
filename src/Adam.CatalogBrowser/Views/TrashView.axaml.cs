using Avalonia.Controls;
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

