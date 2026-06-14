using Adam.CatalogBrowser.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Adam.CatalogBrowser.Views;

public partial class ImportDialog : Window
{
    public ImportDialog()
    {
        InitializeComponent();
    }

    public ImportDialog(ImportViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        if (DataContext is ImportViewModel vm)
        {
            vm.RequestClose += () => Close();
            vm.ImportCompleted += () =>
            {
                // Keep the dialog open so the user can see the result
                // They can click Close when done
            };

            vm.BrowseFileFunc = async () =>
            {
                var options = new FilePickerOpenOptions
                {
                    Title = "Select CSV Metadata File",
                    AllowMultiple = false,
                    FileTypeFilter =
                    [
                        new FilePickerFileType("CSV Files") { Patterns = ["*.csv"] },
                        new FilePickerFileType("All Files") { Patterns = ["*"] }
                    ]
                };
                var result = await StorageProvider.OpenFilePickerAsync(options);
                return result.Count > 0 ? result[0].Path.LocalPath : null;
            };
        }
    }
}
