using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Adam.CatalogBrowser.ViewModels;
using Adam.Shared.Services;
using System.IO;

namespace Adam.CatalogBrowser.Views;

public partial class DuplicateReviewView : UserControl
{
    public DuplicateReviewView()
    {
        InitializeComponent();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (DataContext is DuplicateReviewViewModel vm)
        {
            switch (e.Key)
            {
                case Key.Left:
                    if (vm.CanNavigatePrevious)
                        vm.PreviousGroupCommand.Execute(null);
                    break;
                case Key.Right:
                    if (vm.CanNavigateNext)
                        vm.NextGroupCommand.Execute(null);
                    break;
                case Key.Escape:
                    vm.CloseCommand.Execute(null);
                    break;
            }
        }
    }
}
