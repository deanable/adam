using Avalonia.Controls;
using Avalonia.Interactivity;
using Adam.CatalogBrowser.ViewModels;

namespace Adam.CatalogBrowser.Views;

public partial class AiTagReviewDialog : Window
{
    /// <summary>
    /// Parameterless constructor required by the Avalonia XAML compiler.
    /// Use <c>AiTagReviewDialog(AiTagReviewViewModel)</c> for normal usage.
    /// </summary>
    public AiTagReviewDialog()
    {
        InitializeComponent();
    }

    public AiTagReviewDialog(AiTagReviewViewModel vm) : this()
    {
        DataContext = vm;
        vm.CloseRequested += result =>
        {
            Close(result);
        };
    }
}
