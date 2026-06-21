using Adam.Shared.Data;
using Adam.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace Adam.Shared.Services;

/// <summary>
/// Standalone-mode service for SearchHistory operations.
/// In multi-user mode, the broker's <c>SearchHistoryHandler</c> handles these operations instead.
/// </summary>
public sealed class SearchHistoryService
{
    private readonly AppDbContext _context;

    public SearchHistoryService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<SearchHistoryEntry> RecordAsync(
        string queryText,
        string filtersJson = "{}",
        bool isSemantic = false,
        Guid? userId = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(queryText))
            throw new ArgumentException("Query text cannot be empty", nameof(queryText));

        var entry = new SearchHistoryEntry
        {
            Id = Guid.NewGuid(),
            QueryText = queryText.Trim(),
            FiltersJson = filtersJson,
            IsSemantic = isSemantic,
            ExecutedAt = DateTimeOffset.UtcNow,
            UserId = userId
        };

        _context.SearchHistoryEntries.Add(entry);

        // Auto-purge: keep only the last 200 entries for this user
        await PurgeOldEntriesAsync(userId, ct);

        await _context.SaveChangesAsync(ct);
        return entry;
    }

    public async Task<List<SearchHistoryEntry>> ListAsync(
        Guid? userId = null,
        int maxResults = 200,
        CancellationToken ct = default)
    {
        // Load to memory first, then sort — SQLite cannot ORDER BY DateTimeOffset
        var all = await _context.SearchHistoryEntries
            .Where(s => s.UserId == userId)
            .AsNoTracking()
            .ToListAsync(ct);
        return [.. all.OrderByDescending(s => s.ExecutedAt).Take(maxResults)];
    }

    public async Task ClearAsync(Guid? userId = null, CancellationToken ct = default)
    {
        var entries = await _context.SearchHistoryEntries
            .Where(s => s.UserId == userId)
            .ToListAsync(ct);

        _context.SearchHistoryEntries.RemoveRange(entries);
        await _context.SaveChangesAsync(ct);
    }

    private async Task PurgeOldEntriesAsync(Guid? userId, CancellationToken ct)
    {
        if (userId == null) return;

        // Single query: skip the 200 most recent entries and delete the rest
        // Load to memory first, then sort — SQLite cannot ORDER BY DateTimeOffset
        var all = await _context.SearchHistoryEntries
            .Where(s => s.UserId == userId)
            .ToListAsync(ct);
        var toDelete = all.OrderByDescending(s => s.ExecutedAt).Skip(200).ToList();

        if (toDelete.Count > 0)
            _context.SearchHistoryEntries.RemoveRange(toDelete);
    }
}
