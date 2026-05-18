using Microsoft.Extensions.Logging;

namespace Adam.CatalogBrowser.Services;

public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly StreamWriter _writer;

    public FileLoggerProvider(string filePath)
    {
        _writer = new StreamWriter(filePath, append: false) { AutoFlush = true };
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, _writer);

    public void Dispose() => _writer.Dispose();
}

public sealed class FileLogger(string categoryName, StreamWriter writer) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var now = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var level = logLevel.ToString()[..5];
        var msg = formatter(state, exception);
        lock (writer)
        {
            writer.WriteLine($"{now} [{level}] {categoryName}{Environment.NewLine}  {msg}");
            if (exception != null)
                writer.WriteLine($"  {exception}");
        }
    }
}

public static class FileLoggerExtensions
{
    public static ILoggingBuilder AddFile(this ILoggingBuilder builder, string filePath)
    {
        builder.AddProvider(new FileLoggerProvider(filePath));
        return builder;
    }
}
