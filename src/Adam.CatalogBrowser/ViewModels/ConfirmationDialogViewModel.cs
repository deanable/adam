using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Adam.Shared.Services;

namespace Adam.CatalogBrowser.ViewModels;

public class ConfirmationDialogViewModel : INotifyPropertyChanged
{
    private string _title = "Confirm";
    private string _message = "Are you sure?";
    private string _confirmText = "Confirm";
    private string _cancelText = "Cancel";
    private bool _isDestructive = true;
    private bool _confirmed;

    public ConfirmationDialogViewModel()
    {
        ConfirmCommand = new RelayCommand(_ => { Confirmed = true; RequestClose?.Invoke(); });
        CancelCommand = new RelayCommand(_ => { Confirmed = false; RequestClose?.Invoke(); });
    }

    public string Title
    {
        get => _title;
        set { _title = value; OnPropertyChanged(); }
    }

    public string Message
    {
        get => _message;
        set { _message = value; OnPropertyChanged(); }
    }

    public string ConfirmText
    {
        get => _confirmText;
        set { _confirmText = value; OnPropertyChanged(); }
    }

    public string CancelText
    {
        get => _cancelText;
        set { _cancelText = value; OnPropertyChanged(); }
    }

    public bool IsDestructive
    {
        get => _isDestructive;
        set { _isDestructive = value; OnPropertyChanged(); }
    }

    public bool Confirmed
    {
        get => _confirmed;
        private set { _confirmed = value; OnPropertyChanged(); }
    }

    public ICommand ConfirmCommand { get; }
    public ICommand CancelCommand { get; }

    /// <summary>
    /// Fired when the user clicks Confirm or Cancel, signalling the window to close.
    /// </summary>
    public event Action? RequestClose;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
