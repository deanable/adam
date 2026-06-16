using Adam.Shared.Data;
using Adam.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace Adam.Shared.Services;

/// <summary>
/// Standalone-mode service for SavedSearch CRUD operations.
/// In multi-user mode, the broker's <c>SavedSearchHandler</c> handles these operations instead.
/// </summary>
public sealed class SavedSearchService
{
    private readonly AppDbContext _context;

    public SavedSearchService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<SavedSearch> CreateAsync(
        string name,
        string? queryText,
        string filtersJson,
        bool isPinned = false,
        Guid? userId = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be empty", nameof(name));

        // Check duplicate name per user
        var existing = await _context.SavedSearches
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Name == name, ct);
        if (existing != null)
            throw new InvalidOperationException("A saved search with this name already exists");

        var now = DateTimeOffset.UtcNow;
        var saved = new SavedSearch
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            QueryText = string.IsNullOrWhiteSpace(queryText) ? null : queryText.Trim(),
            FiltersJson = filtersJson,
            IsPinned = isPinned,
            UserId = userId,
            CreatedAt = now,
            ModifiedAt = now
        };

        _context.SavedSearches.Add(saved);
        await _context.SaveChangesAsync(ct);
        return saved;
    }

    public async Task<List<SavedSearch>> ListAsync(Guid? userId = null, CancellationToken ct = default)
    {
        return await _context.SavedSearches
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.IsPinned)
            .ThenBy(s => s.Name)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task<SavedSearch> UpdateAsync(
        Guid id,
        string name,
        string? queryText,
        string filtersJson,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be empty", nameof(name));

        var saved = await _context.SavedSearches.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (saved == null)
            throw new KeyNotFoundException("Saved search not found");

        saved.Name = name.Trim();
        saved.QueryText = string.IsNullOrWhiteSpace(queryText) ? null : queryText.Trim();
        saved.FiltersJson = filtersJson;
        saved.ModifiedAt = DateTimeOffset.UtcNow;

        await _context.SaveChangesAsync(ct);
        return saved;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var saved = await _context.SavedSearches.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (saved == null)
            throw new KeyNotFoundException("Saved search not found");

        _context.SavedSearches.Remove(saved);
        await _context.SaveChangesAsync(ct);
    }

    public async Task SetPinnedAsync(Guid id, bool isPinned, CancellationToken ct = default)
    {
        var saved = await _context.SavedSearches.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (saved == null)
            throw new KeyNotFoundException("Saved search not found");

        saved.IsPinned = isPinned;
        saved.ModifiedAt = DateTimeOffset.UtcNow;
        await _context.SaveChangesAsync(ct);
    }
}
