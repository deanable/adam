using Avalonia.Controls;
using Avalonia.Interactivity;
using Adam.CatalogBrowser.ViewModels;

namespace Adam.CatalogBrowser.Views;

/// <summary>
/// Code-behind for PluginManagerView. Handles window-close event
/// and provides the static ShowAsync helper for the modal dialog.
/// </summary>
public partial class PluginManagerView : UserControl
{
    public PluginManagerView()
    {
        InitializeComponent();
    }

    public PluginManagerView(PluginManagerViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    /// <summary>
    /// Shows the Plugin Manager as a modal dialog.
    /// </summary>
    public static async Task ShowAsync(Window? owner)
    {
        if (owner == null) return;

        var vm = App.ServiceProvider?.GetService(typeof(PluginManagerViewModel)) as PluginManagerViewModel;
        if (vm == null) return;

        var content = new PluginManagerView(vm);
        var window = new Window
        {
            Title = "Plugin Manager",
            Content = content,
            Width = 520,
            Height = 440,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            WindowDecorations = WindowDecorations.None
        };

        // Close the window when the ViewModel requests close
        vm.RequestClose += () =>
        {
            if (window.IsVisible)
                window.Close();
        };

        await window.ShowDialog(owner);
    }
}
