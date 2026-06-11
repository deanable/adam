using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Adam.Shared.Services;

namespace Adam.CatalogBrowser.ViewModels;

/// <summary>
/// ViewModel for the simple text-input dialog used by T8.18 sidebar CRUD
/// (new/rename operations for Collections, Keywords, Categories).
/// </summary>
public class InputDialogViewModel : INotifyPropertyChanged
{
    private string _inputText = string.Empty;

    public string Title { get; set; } = "Input";
    public string Message { get; set; } = string.Empty;
    public string ConfirmText { get; set; } = "OK";
    public string CancelText { get; set; } = "Cancel";
    public string DefaultValue { get; set; } = string.Empty;

    public string InputText
    {
        get => _inputText;
        set { _inputText = value; OnPropertyChanged(); }
    }

    public ICommand ConfirmCommand { get; }
    public ICommand CancelCommand { get; }

    /// <summary>
    /// Set to true when the user confirms.
    /// </summary>
    public bool Confirmed { get; private set; }

    public InputDialogViewModel()
    {
        ConfirmCommand = new RelayCommand(_ =>
        {
            Confirmed = true;
            // Close will be handled by the dialog code-behind
        });

        CancelCommand = new RelayCommand(_ =>
        {
            Confirmed = false;
            // Close will be handled by the dialog code-behind
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
