using Adam.CatalogBrowser.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Adam.CatalogBrowser.Views;

public partial class LoginDialog : Window
{
    private readonly LoginDialogViewModel? _viewModel;

    public LoginDialog()
    {
        InitializeComponent();
    }

    public LoginDialog(LoginDialogViewModel viewModel) : this()
    {
        _viewModel = viewModel;
        DataContext = viewModel;

        // Auto-close the dialog when login succeeds
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LoginDialogViewModel.LoginSucceeded) && viewModel.LoginSucceeded)
            {
                Close(true);
            }
        };
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        // Focus the username field on open so users can start typing immediately
        Dispatcher.UIThread.Post(() =>
        {
            UsernameTextBox?.Focus();
        }, DispatcherPriority.Input);
    }
}
