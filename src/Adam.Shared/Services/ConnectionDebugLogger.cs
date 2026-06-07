using System.Diagnostics;

namespace Adam.Shared.Services;

/// <summary>
/// Dedicated verbose logger for connection debugging. Writes every step of the
/// connection lifecycle to a file so you can trace exactly what happened —
/// TCP connect, TLS handshake, reconnection attempts, send/receive, and errors.
///
/// Configure the output directory by setting <see cref="LogDirectory"/> before
/// any connection activity (e.g., at app startup via <see cref="Reset"/>).
/// </summary>
public static class ConnectionDebugLogger
{
    private static string? _logDirectory;
    private static string? _logFilePath;
    private static string _logFileName = "connection-debug.log";
    private static readonly object _lock = new();

    /// <summary>
    /// Directory where the connection debug log file is written.
    /// Defaults to <c>%LOCALAPPDATA%/Adam/</c> on Windows,
    /// <c>~/.local/share/Adam/</c> on Linux/macOS.
    /// </summary>
    public static string LogDirectory
    {
        get
        {
            if (_logDirectory != null) return _logDirectory;

            var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(baseDir))
                baseDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) ?? ".",
                    ".local", "share");

            _logDirectory = Path.Combine(baseDir, "Adam");
            _logFilePath = Path.Combine(_logDirectory, _logFileName);
            return _logDirectory;
        }
        set
        {
            _logDirectory = value;
            _logFilePath = null; // will be recomputed on next access
        }
    }

    /// <summary>
    /// Full path to the debug log file.
    /// </summary>
    public static string LogFilePath
    {
        get
        {
            if (_logFilePath != null) return _logFilePath;
            _ = LogDirectory; // ensure initialized
            _logFilePath = Path.Combine(_logDirectory!, _logFileName);
            return _logFilePath;
        }
    }

    /// <summary>
    /// Clears the log file and writes the opening banner.
    /// Call once on app startup to avoid unbounded file growth.
    /// Use <paramref name="logName"/> to give each process its own file
    /// (e.g. "catalog" → <c>connection-debug-catalog.log</c>). The default
    /// derives the name from the current process name.
    /// </summary>
    public static void Reset(string? logName = null)
    {
        lock (_lock)
        {
            try
            {
                logName ??= GetDefaultLogName();
                _logFileName = $"connection-debug-{logName}.log";
                _logFilePath = null; // recompute on next access

                var dir = LogDirectory;
                Directory.CreateDirectory(dir);
                File.WriteAllText(LogFilePath,
                    $"=== Connection debug log started {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} ==={Environment.NewLine}" +
                    $"=== Machine: {Environment.MachineName}, OS: {Environment.OSVersion}, Process: {Process.GetCurrentProcess().ProcessName} (PID={Environment.ProcessId}) ==={Environment.NewLine}" +
                    $"=== Log file: {LogFilePath} ==={Environment.NewLine}");
            }
            catch
            {
                // best effort — don't let logging cause issues
            }
        }
    }

    private static string GetDefaultLogName()
    {
        try
        {
            var name = Process.GetCurrentProcess().ProcessName;
            // Clean the name for file-system safety
            var clean = new string(name.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
            return string.IsNullOrWhiteSpace(clean) ? "unknown" : clean.ToLowerInvariant();
        }
        catch
        {
            return "unknown";
        }
    }

    private static string Fmt(string level, string message)
    {
        var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var threadId = Environment.CurrentManagedThreadId;
        return $"{timestamp} [T{threadId:D3}] [{level}] {message}";
    }

    private static void Write(string level, string message)
    {
        lock (_lock)
        {
            try
            {
                var dir = LogDirectory;
                Directory.CreateDirectory(dir);
                File.AppendAllText(LogFilePath, Fmt(level, message) + Environment.NewLine);
            }
            catch
            {
                // best effort
            }
        }
    }

    /// <summary>Fine-grained trace step (e.g. entering a method, TCP connect started).</summary>
    public static void Trace(string message) => Write("TRACE", message);

    /// <summary>Notable lifecycle event (e.g. connected, disconnected, reconnecting).</summary>
    public static void Info(string message) => Write("INFO ", message);

    /// <summary>Unexpected but recoverable condition.</summary>
    public static void Warn(string message) => Write("WARN ", message);

    /// <summary>Failure that prevented the operation from completing.</summary>
    public static void Error(string message) => Write("ERROR", message);

    /// <summary>Failure with exception details.</summary>
    public static void Error(Exception ex, string message)
    {
        var stack = ex.StackTrace?.Replace(Environment.NewLine, Environment.NewLine + "  ");
        Write("ERROR",
            $"{message}: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}  Stack:{Environment.NewLine}  {stack}");
    }
}
