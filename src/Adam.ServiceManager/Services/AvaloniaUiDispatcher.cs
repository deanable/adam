using Adam.Shared.Services;
using Avalonia.Threading;

namespace Adam.ServiceManager.Services;

/// <summary>
/// Default dispatcher that delegates to <see cref="Dispatcher.UIThread"/>.
/// </summary>
public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public Task InvokeAsync(Action action)
    {
        var tcs = new TaskCompletionSource();
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                action();
                tcs.TrySetResult();
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });
        return tcs.Task;
    }

    public void Post(Action action)
    {
        Dispatcher.UIThread.Post(action);
    }

    public bool CheckAccess() => Dispatcher.UIThread.CheckAccess();
}
