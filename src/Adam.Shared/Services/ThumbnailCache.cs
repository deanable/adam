using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace Adam.Shared.Services;

/// <summary>
/// Thread-safe LRU memory cache for decoded thumbnails.
/// Bounded by estimated byte size (default 256 MB). Evicts least-recently-used entries when full.
/// </summary>
public sealed class ThumbnailCache : IDisposable
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private readonly LinkedList<string> _lruOrder = new();
    private readonly ReaderWriterLockSlim _lock = new();
    private long _currentSizeBytes;
    private readonly long _maxSizeBytes;

    /// <summary>
    /// Creates a new ThumbnailCache with the specified maximum size.
    /// </summary>
    /// <param name="maxSizeBytes">Maximum cache size in bytes. Default is 256 MB.</param>
    public ThumbnailCache(long maxSizeBytes = 256L * 1024 * 1024)
    {
        _maxSizeBytes = maxSizeBytes;
    }

    /// <summary>
    /// Number of entries currently in the cache.
    /// </summary>
    public int Count
    {
        get
        {
            _lock.EnterReadLock();
            try { return _cache.Count; }
            finally { _lock.ExitReadLock(); }
        }
    }

    /// <summary>
    /// Current estimated size in bytes.
    /// </summary>
    public long CurrentSizeBytes
    {
        get
        {
            _lock.EnterReadLock();
            try { return Interlocked.Read(ref _currentSizeBytes); }
            finally { _lock.ExitReadLock(); }
        }
    }

    /// <summary>
    /// Tries to get a cached thumbnail bitmap by its cache key (typically the thumbnail file path).
    /// Returns true and updates the LRU order on hit; returns false on miss.
    /// The returned object is caller-owned and must not be disposed by the cache.
    /// </summary>
    public bool TryGet(string key, out object? bitmap)
    {
        _lock.EnterUpgradeableReadLock();
        try
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                _lock.EnterWriteLock();
                try
                {
                    // Move to end (most recently used)
                    _lruOrder.Remove(entry.Node);
                    _lruOrder.AddLast(entry.Node);
                }
                finally { _lock.ExitWriteLock(); }

                bitmap = entry.Bitmap;
                return true;
            }
        }
        finally { _lock.ExitUpgradeableReadLock(); }

        bitmap = null;
        return false;
    }

    /// <summary>
    /// Adds or updates a thumbnail bitmap in the cache. Evicts LRU entries if necessary.
    /// The cache does NOT take ownership of the bitmap — the caller must ensure the bitmap
    /// remains alive while it is in the cache.
    /// </summary>
    public void Add(string key, object bitmap, long estimatedSizeBytes)
    {
        _lock.EnterWriteLock();
        try
        {
            if (_cache.TryGetValue(key, out var existing))
            {
                // Update existing entry — replace bitmap and size, move to end
                Interlocked.Add(ref _currentSizeBytes, -existing.SizeBytes);
                _lruOrder.Remove(existing.Node);
                _lruOrder.AddLast(existing.Node);
                _cache[key] = new CacheEntry(bitmap, estimatedSizeBytes, existing.Node);
                Interlocked.Add(ref _currentSizeBytes, estimatedSizeBytes);
                return;
            }

            // Evict LRU entries until we have room
            while (_currentSizeBytes + estimatedSizeBytes > _maxSizeBytes && _lruOrder.Count > 0)
            {
                var oldest = _lruOrder.First!.Value;
                _lruOrder.RemoveFirst();
                if (_cache.TryRemove(oldest, out var evicted))
                {
                    Interlocked.Add(ref _currentSizeBytes, -evicted.SizeBytes);
                }
            }

            var node = _lruOrder.AddLast(key);
            _cache[key] = new CacheEntry(bitmap, estimatedSizeBytes, node);
            Interlocked.Add(ref _currentSizeBytes, estimatedSizeBytes);
        }
        finally { _lock.ExitWriteLock(); }
    }

    /// <summary>
    /// Removes a single entry by key. Used when the caller disposes the bitmap
    /// so the cache doesn't return a dead reference.
    /// </summary>
    public bool Remove(string key)
    {
        _lock.EnterWriteLock();
        try
        {
            if (_cache.TryRemove(key, out var removed))
            {
                _lruOrder.Remove(removed.Node);
                Interlocked.Add(ref _currentSizeBytes, -removed.SizeBytes);
                return true;
            }
            return false;
        }
        finally { _lock.ExitWriteLock(); }
    }

    /// <summary>
    /// Clears all entries from the cache. Does NOT dispose the cached bitmaps —
    /// the caller is responsible for disposing them if needed.
    /// </summary>
    public void Clear()
    {
        _lock.EnterWriteLock();
        try
        {
            _cache.Clear();
            _lruOrder.Clear();
            Interlocked.Exchange(ref _currentSizeBytes, 0);
        }
        finally { _lock.ExitWriteLock(); }
    }

    /// <summary>
    /// Estimates the memory footprint of a decoded bitmap given its pixel dimensions.
    /// Uses width × height × 4 bytes (RGBA).
    /// </summary>
    public static long EstimateBitmapSize(int pixelWidth, int pixelHeight)
        => (long)pixelWidth * pixelHeight * 4;

    public void Dispose()
    {
        Clear();
        _lock.Dispose();
    }

    private sealed record CacheEntry(object Bitmap, long SizeBytes, LinkedListNode<string> Node);
}
