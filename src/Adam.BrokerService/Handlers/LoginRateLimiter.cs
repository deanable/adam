using System.Collections.Concurrent;

namespace Adam.BrokerService.Handlers;

/// <summary>
/// In-memory sliding-window rate limiter for login attempts.
/// Tracks attempts per username+IP combination and blocks when exceeded.
/// </summary>
public sealed class LoginRateLimiter
{
    private readonly int _maxAttempts;
    private readonly TimeSpan _windowDuration;
    private readonly ConcurrentDictionary<string, SlidingWindow> _windows = new();

    public LoginRateLimiter(int maxAttempts = 5, TimeSpan? windowDuration = null)
    {
        _maxAttempts = maxAttempts;
        _windowDuration = windowDuration ?? TimeSpan.FromMinutes(15);
    }

    /// <summary>
    /// Records a login attempt. Returns true if the attempt is allowed, false if rate-limited.
    /// </summary>
    public bool TryAttempt(string username, string ipAddress)
    {
        var key = $"{username.ToLowerInvariant()}:{ipAddress}";
        var window = _windows.GetOrAdd(key, _ => new SlidingWindow(_windowDuration));
        return window.TryIncrement(_maxAttempts);
    }

    /// <summary>
    /// Returns the number of remaining attempts for the given key.
    /// </summary>
    public int GetRemainingAttempts(string username, string ipAddress)
    {
        var key = $"{username.ToLowerInvariant()}:{ipAddress}";
        if (_windows.TryGetValue(key, out var window))
            return Math.Max(0, _maxAttempts - window.Count);
        return _maxAttempts;
    }

    /// <summary>
    /// Periodic cleanup entry point. Removes expired windows.
    /// </summary>
    public void PruneExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kvp in _windows)
        {
            if (kvp.Value.IsExpired(now))
                _windows.TryRemove(kvp.Key, out _);
        }
    }

    private sealed class SlidingWindow
    {
        private readonly TimeSpan _duration;
        private readonly List<DateTimeOffset> _attempts = new();
        private readonly object _lock = new();

        public SlidingWindow(TimeSpan duration)
        {
            _duration = duration;
        }

        public int Count
        {
            get
            {
                lock (_lock)
                {
                    Prune(DateTimeOffset.UtcNow);
                    return _attempts.Count;
                }
            }
        }

        public bool TryIncrement(int maxAttempts)
        {
            lock (_lock)
            {
                var now = DateTimeOffset.UtcNow;
                Prune(now);
                if (_attempts.Count >= maxAttempts)
                    return false;
                _attempts.Add(now);
                return true;
            }
        }

        public bool IsExpired(DateTimeOffset now)
        {
            lock (_lock)
            {
                if (_attempts.Count == 0) return true;
                return now - _attempts[^1] > _duration;
            }
        }

        private void Prune(DateTimeOffset now)
        {
            var cutoff = now - _duration;
            _attempts.RemoveAll(a => a < cutoff);
        }
    }
}
