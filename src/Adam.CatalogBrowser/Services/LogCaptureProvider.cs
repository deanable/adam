using System.Collections.ObjectModel;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;

namespace Adam.CatalogBrowser.Services;

/// <summary>
/// An <see cref="ILoggerProvider"/> that captures all log messages into an
/// <see cref="ObservableCollection{T}"/> so they can be displayed in the UI
/// (e.g., terminal output from sc.exe, netsh, and elevated process logs).
///
/// Register via <c>builder.AddProvider(new LogCaptureProvider(capture))</c>.
/// </summary>
public sealed class LogCaptureProvider : ILoggerProvider
{
    private readonly ObservableCollection<string> _capture;

    public LogCaptureProvider(ObservableCollection<string> capture)
    {
        _capture = capture;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new CaptureLogger(categoryName, _capture);
    }

    public void Dispose()
    {
    }

    private sealed class CaptureLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly ObservableCollection<string> _capture;

        public CaptureLogger(string categoryName, ObservableCollection<string> capture)
        {
            _categoryName = categoryName;
            _capture = capture;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var level = logLevel switch
            {
                LogLevel.Trace => "TRCE",
                LogLevel.Debug => "DBUG",
                LogLevel.Information => "INFO",
                LogLevel.Warning => "WARN",
                LogLevel.Error => "FAIL",
                LogLevel.Critical => "CRIT",
                _ => "    "
            };

            // Shorten the category to just the class name for readability
            var category = _categoryName;
            var lastDot = category.LastIndexOf('.');
            if (lastDot >= 0 && lastDot < category.Length - 1)
                category = category[(lastDot + 1)..];

            var entry = $"[{timestamp}|{level}|{category}] {message}";

            // Dispatch to UI thread since ObservableCollection must be modified
            // on the UI thread (Avalonia's ItemsControl subscribes to CollectionChanged).
            // Log can be called from background threads (sc.exe/netsh/Process output).
            if (Dispatcher.UIThread.CheckAccess())
            {
                AddEntry(entry);
            }
            else
            {
                Dispatcher.UIThread.Post(() => AddEntry(entry));
            }
        }

        private void AddEntry(string entry)
        {
            // Keep max 2000 entries in memory
            if (_capture.Count >= 2000)
                _capture.RemoveAt(0);
            _capture.Add(entry);
        }
    }
}
