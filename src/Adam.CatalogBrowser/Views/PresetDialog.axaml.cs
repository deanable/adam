using Adam.CatalogBrowser.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Adam.CatalogBrowser.Views;

public partial class PresetDialog : Window
{
    public PresetDialog()
    {
        InitializeComponent();
    }

    public PresetDialog(PresetDialogViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        if (DataContext is PresetDialogViewModel vm)
        {
            vm.RequestClose += () => Close();
            vm.PresetApplied += _ => Close();
            _ = vm.LoadAsync();
        }
    }
}
