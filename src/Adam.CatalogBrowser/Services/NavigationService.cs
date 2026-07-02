using Microsoft.Extensions.Logging;

namespace Adam.CatalogBrowser.Services;

/// <summary>
/// Encapsulates view lifecycle management including navigation to modal views,
/// back-stack tracking, and automatic disposal of previous views.
/// 
/// Handles three navigation patterns:
/// 1. <see cref="NavigateToAsync"/> — Create a new ViewModel, optionally initialize it asynchronously,
///    wire its CloseRequested event, and navigate to it.
/// 2. <see cref="NavigateToDirectAsync"/> — Resolve or create a ViewModel, optionally load it,
///    and navigate without back-stack tracking (e.g., gallery tabs).
/// 3. <see cref="GoBack"/> — Dispose the current view and restore the previous one from the back stack.
/// </summary>
public sealed class NavigationService
{
    private readonly ILogger<NavigationService>? _logger;
    private readonly Stack<Func<object?>> _backStack = new();
    private object? _currentView;

    public NavigationService(ILogger<NavigationService>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Fired when the current view changes. Wire this to the ViewModel's CurrentView setter.
    /// </summary>
    public event Action<object?>? ViewChanged;

    /// <summary>
    /// Navigate to a new ViewModel with optional async initialization.
    /// The previous view is preserved in the back stack for <see cref="GoBack"/>.
    /// If the ViewModel implements <see cref="ICloseable"/>, its CloseRequested event
    /// automatically triggers GoBack.
    /// </summary>
    public async Task NavigateToAsync<T>(
        Func<T> createVm,
        Func<T, Task>? initializeAsync = null)
        where T : notnull
    {
        var vm = createVm();

        // Push current view onto back stack — capture by value so GoBack returns the correct page
        var snapshot = _currentView;
        _backStack.Push(() => snapshot);

        // Wire CloseRequested to GoBack if the VM supports it
        if (vm is ICloseable closeable)
        {
            closeable.CloseRequested += GoBack;
        }

        // Run async initialization if provided
        if (initializeAsync != null)
        {
            await initializeAsync(vm);
        }

        _currentView = vm;
        ViewChanged?.Invoke(vm);
    }

    /// <summary>
    /// Navigate directly to a ViewModel without back-stack tracking.
    /// The current view is disposed if it implements <see cref="IDisposable"/>.
    /// Use for non-modal navigation (e.g., gallery tabs, settings).
    /// </summary>
    public void NavigateTo<T>(T vm)
        where T : notnull
    {
        // Dispose current view if it implements IDisposable
        if (_currentView is IDisposable disposable)
        {
            disposable.Dispose();
        }

        _currentView = vm;
        ViewChanged?.Invoke(vm);
    }

    /// <summary>
    /// Navigate directly to a ViewModel with async loading, without back-stack tracking.
    /// The current view is disposed before loading the new one.
    /// </summary>
    public async Task NavigateToDirectAsync<T>(
        Func<T> createVm,
        Func<T, Task>? initializeAsync = null)
        where T : notnull
    {
        // Dispose current view if it implements IDisposable
        if (_currentView is IDisposable disposable)
        {
            disposable.Dispose();
        }

        var vm = createVm();

        if (initializeAsync != null)
        {
            await initializeAsync(vm);
        }

        _currentView = vm;
        ViewChanged?.Invoke(vm);
    }

    /// <summary>
    /// Go back to the previous view in the back stack.
    /// Disposes the current view if it implements <see cref="IDisposable"/>.
    /// </summary>
    public void GoBack()
    {
        if (_backStack.Count == 0)
        {
            _logger?.LogWarning("GoBack called with empty back stack");
            return;
        }

        // Dispose current view if it implements IDisposable
        if (_currentView is IDisposable disposable)
        {
            disposable.Dispose();
        }

        var previousViewGetter = _backStack.Pop();
        _currentView = previousViewGetter();
        ViewChanged?.Invoke(_currentView);
    }

    /// <summary>
    /// Clears the back stack and disposes any views in it.
    /// </summary>
    public void ClearBackStack()
    {
        _backStack.Clear();
    }
}
