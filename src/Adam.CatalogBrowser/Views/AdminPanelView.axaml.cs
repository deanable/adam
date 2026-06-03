using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Adam.CatalogBrowser.Views;

public partial class AdminPanelView : UserControl
{
    public AdminPanelView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
