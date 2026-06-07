namespace Adam.Shared.Services;

/// <summary>
/// Abstraction for Avalonia's dispatcher, allowing unit tests to provide
/// a synchronous dispatcher stub that avoids hanging on Dispatcher.UIThread.
/// </summary>
public interface IUiDispatcher
{
    Task InvokeAsync(Action action);
    void Post(Action action);
    bool CheckAccess();
}
