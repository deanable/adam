using Adam.CatalogBrowser.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Adam.CatalogBrowser.Views;

public partial class ConfirmationDialog : Window
{
    public ConfirmationDialog()
    {
        InitializeComponent();
    }

    public ConfirmationDialog(ConfirmationDialogViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.RequestClose += () =>
        {
            Close(viewModel.Confirmed);
        };
    }

    /// <summary>
    /// Convenience method to show a confirmation dialog and await the result.
    /// </summary>
    /// <param name="owner">The parent window for the modal dialog.</param>
    /// <param name="title">Dialog title.</param>
    /// <param name="message">Confirmation message body.</param>
    /// <param name="confirmText">Text for the confirm button (default "Delete").</param>
    /// <param name="cancelText">Text for the cancel button (default "Cancel").</param>
    /// <param name="isDestructive">If true, the confirm button is styled red.</param>
    /// <returns>True if the user confirmed, false otherwise.</returns>
    public static async Task<bool> ShowAsync(
        Window owner,
        string title,
        string message,
        string confirmText = "Delete",
        string cancelText = "Cancel",
        bool isDestructive = true)
    {
        var vm = new ConfirmationDialogViewModel
        {
            Title = title,
            Message = message,
            ConfirmText = confirmText,
            CancelText = cancelText,
            IsDestructive = isDestructive
        };

        var dialog = new ConfirmationDialog(vm);
        var result = await dialog.ShowDialog<bool?>(owner);
        return result == true;
    }
}
