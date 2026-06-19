using Avalonia.Controls;
using Avalonia.Input;
using Adam.CatalogBrowser.ViewModels;

namespace Adam.CatalogBrowser.Views;

public partial class FaceTaggingView : UserControl
{
    public FaceTaggingView()
    {
        InitializeComponent();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (DataContext is FaceTaggingViewModel vm)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    // Close view
                    break;
                case Key.F5:
                    vm.RefreshCommand.Execute(null);
                    break;
            }
        }
    }
}
