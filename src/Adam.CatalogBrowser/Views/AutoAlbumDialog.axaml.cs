using Avalonia.Controls;
using Avalonia.Input;
using Adam.CatalogBrowser.ViewModels;

namespace Adam.CatalogBrowser.Views;

public partial class AutoAlbumDialog : UserControl
{
    public AutoAlbumDialog()
    {
        InitializeComponent();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (DataContext is AutoAlbumViewModel vm)
        {
            switch (e.Key)
            {
                case Key.Enter:
                    if (vm.HasClusters)
                        vm.CreateAlbumsCommand.Execute(null);
                    break;
                case Key.Escape:
                    vm.CloseCommand.Execute(null);
                    break;
            }
        }
    }
}
