using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Adam.CatalogBrowser.Views;

public partial class CommentPanelView : UserControl
{
    public CommentPanelView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
