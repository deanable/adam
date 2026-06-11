using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Adam.CatalogBrowser.Services;

/// <summary>
/// Severity level for toast notifications.
/// </summary>
public enum ToastLevel
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>
/// Represents a single transient notification.
/// </summary>
public class ToastNotification : INotifyPropertyChanged
{
    private string _message = string.Empty;
    private ToastLevel _level = ToastLevel.Info;
    private double _opacity = 1.0;
    private bool _isDismissed;

    public Guid Id { get; } = Guid.NewGuid();
    public DateTime CreatedAt { get; } = DateTime.UtcNow;

    public string Message
    {
        get => _message;
        set { _message = value; OnPropertyChanged(); }
    }

    public ToastLevel Level
    {
        get => _level;
        set { _level = value; OnPropertyChanged(); OnPropertyChanged(nameof(BackgroundColor)); }
    }

    public double Opacity
    {
        get => _opacity;
        set { _opacity = value; OnPropertyChanged(); }
    }

    public bool IsDismissed
    {
        get => _isDismissed;
        set { _isDismissed = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Background color based on the toast level (used by the view).
    /// </summary>
    public string BackgroundColor => Level switch
    {
        ToastLevel.Success => "#2E7D32",
        ToastLevel.Warning => "#E65100",
        ToastLevel.Error => "#C62828",
        _ => "#1565C0"
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>
/// Singleton service that manages a collection of transient toast notifications.
/// ViewModels enqueue notifications, and the UI layer displays them.
/// Notifications auto-dismiss after a configurable duration.
/// </summary>
public class ToastService : INotifyPropertyChanged
{
    private readonly ObservableCollection<ToastNotification> _notifications = [];
    private readonly TimeSpan _defaultDuration;

    public ToastService(TimeSpan? defaultDuration = null)
    {
        _defaultDuration = defaultDuration ?? TimeSpan.FromSeconds(4);
        Notifications = new ReadOnlyObservableCollection<ToastNotification>(_notifications);
    }

    /// <summary>
    /// The current list of active toast notifications.
    /// </summary>
    public ReadOnlyObservableCollection<ToastNotification> Notifications { get; } = null!;

    /// <summary>
    /// True when there is at least one active notification.
    /// </summary>
    public bool HasNotifications => _notifications.Count > 0;

    /// <summary>
    /// Enqueue a new toast notification.
    /// </summary>
    public void Show(string message, ToastLevel level = ToastLevel.Info, TimeSpan? duration = null)
    {
        var toast = new ToastNotification
        {
            Message = message,
            Level = level
        };

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _notifications.Add(toast);
            OnPropertyChanged(nameof(HasNotifications));
            OnPropertyChanged(nameof(Notifications));

            // Auto-dismiss after the specified duration
            var dismissAt = DateTime.UtcNow + (duration ?? _defaultDuration);
            _ = DismissAfterAsync(toast, dismissAt);
        });
    }

    /// <summary>
    /// Dismiss a specific notification immediately.
    /// </summary>
    public void Dismiss(ToastNotification toast)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            toast.IsDismissed = true;
            _notifications.Remove(toast);
            OnPropertyChanged(nameof(HasNotifications));
        });
    }

    /// <summary>
    /// Dismiss all active notifications.
    /// </summary>
    public void DismissAll()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            foreach (var n in _notifications)
                n.IsDismissed = true;
            _notifications.Clear();
            OnPropertyChanged(nameof(HasNotifications));
        });
    }

    private async Task DismissAfterAsync(ToastNotification toast, DateTime dismissAt)
    {
        var delay = dismissAt - DateTime.UtcNow;
        if (delay > TimeSpan.Zero)
            await Task.Delay(delay);

        Dismiss(toast);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
