using Adam.CatalogBrowser.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Adam.CatalogBrowser.Views;

public partial class ExportDialog : Window
{
    public ExportDialog()
    {
        InitializeComponent();
    }

    public ExportDialog(ExportDialogViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        if (DataContext is ExportDialogViewModel vm)
        {
            vm.BrowseFolderFunc = async () =>
            {
                var options = new FolderPickerOpenOptions
                {
                    Title = "Select Export Destination",
                    AllowMultiple = false
                };
                var result = await StorageProvider.OpenFolderPickerAsync(options);
                return result.Count > 0 ? result[0].Path.LocalPath : string.Empty;
            };
        }
    }
}
