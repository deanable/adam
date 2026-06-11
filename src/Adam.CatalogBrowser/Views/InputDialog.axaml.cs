using Avalonia.Controls;
using Adam.CatalogBrowser.ViewModels;

namespace Adam.CatalogBrowser.Views;

/// <summary>
/// A simple text input dialog for sidebar CRUD operations (T8.18).
/// </summary>
public partial class InputDialog : Window
{
    public InputDialog()
    {
        InitializeComponent();
        // Focus the text box and select all text on open
        Loaded += (_, _) =>
        {
            InputTextBox.Focus();
            InputTextBox.SelectAll();
        };
    }

    /// <summary>
    /// Shows a modal input dialog and returns the text entered, or null if cancelled.
    /// </summary>
    public static async Task<string?> ShowAsync(Window? owner, string title, string message,
        string confirmText = "OK", string cancelText = "Cancel", string? defaultValue = null)
    {
        if (owner == null) return null;

        var vm = new InputDialogViewModel
        {
            Title = title,
            Message = message,
            ConfirmText = confirmText,
            CancelText = cancelText,
            DefaultValue = defaultValue ?? string.Empty,
            InputText = defaultValue ?? string.Empty
        };

        var dialog = new InputDialog
        {
            DataContext = vm
        };

        await dialog.ShowDialog(owner);
        return vm.Confirmed ? vm.InputText : null;
    }
}
