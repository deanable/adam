using Adam.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace Adam.Shared.Services;

/// <summary>
/// Helper for two-query pagination with <see cref="DigitalAsset"/> entities.
/// Used to work around SQLite's inability to ORDER BY <c>DateTimeOffset</c>
/// in EF Core queries.
/// </summary>
public static class PaginationHelper
{
    /// <summary>
    /// Loads <see cref="DigitalAsset"/> entities with the specified IDs from the query
    /// and returns them in the exact order of <paramref name="sortedIds"/>.
    /// </summary>
    /// <param name="query">The <see cref="IQueryable{DigitalAsset}"/> to load entities from.</param>
    /// <param name="sortedIds">The ordered list of IDs to load, in the desired output order.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Entities in the exact order of <paramref name="sortedIds"/>, or an empty list if <paramref name="sortedIds"/> is empty.</returns>
    public static async Task<List<DigitalAsset>> LoadInOrderAsync(
        IQueryable<DigitalAsset> query,
        List<Guid> sortedIds,
        CancellationToken ct = default)
    {
        if (sortedIds.Count == 0)
            return [];

        var assets = await query
            .Where(a => sortedIds.Contains(a.Id))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var order = sortedIds
            .Select((id, i) => (id, i))
            .ToDictionary(x => x.id, x => x.i);

        return [.. assets.OrderBy(a => order[a.Id])];
    }
}
