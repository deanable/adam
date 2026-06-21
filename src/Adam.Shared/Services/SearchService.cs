using Adam.Shared.Data;
using Adam.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace Adam.Shared.Services;

public class SearchService
{
    private readonly AppDbContext _context;

    public SearchService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<DigitalAsset>> SearchAsync(
        string? query = null,
        AssetType? type = null,
        Guid? collectionId = null,
        string[]? tags = null,
        int? minRating = null,
        int? maxRating = null,
        DateTimeOffset? fromDate = null,
        DateTimeOffset? toDate = null,
        string sortBy = "FileName",
        string sortDir = "asc",
        int page = 1,
        int pageSize = 100,
        CancellationToken ct = default)
    {
        var q = _context.DigitalAssets
            .Include(a => a.Collection)
            .Include(a => a.MetadataProfile)
            .Include(a => a.Keywords)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var search = query.ToLowerInvariant();
            q = q.Where(a =>
                a.Title.ToLower().Contains(search) ||
                (a.Description != null && a.Description.ToLower().Contains(search)) ||
                a.FileName.ToLower().Contains(search) ||
                a.Keywords.Any(k => k.Name.ToLower().Contains(search)) ||
                (a.MetadataProfile != null && (
                    a.MetadataProfile.CameraMake != null && a.MetadataProfile.CameraMake.ToLower().Contains(search) ||
                    a.MetadataProfile.CameraModel != null && a.MetadataProfile.CameraModel.ToLower().Contains(search) ||
                    a.MetadataProfile.Creator != null && a.MetadataProfile.Creator.ToLower().Contains(search) ||
                    a.MetadataProfile.Copyright != null && a.MetadataProfile.Copyright.ToLower().Contains(search)
                ))
            );
        }

        if (type.HasValue)
            q = q.Where(a => a.Type == type.Value);

        if (collectionId.HasValue)
            q = q.Where(a => a.CollectionId == collectionId.Value);

        if (tags is { Length: > 0 })
        {
            q = q.Where(a => a.Keywords.Any(k => tags.Contains(k.Name)));
        }

        if (minRating.HasValue && maxRating.HasValue)
        {
            q = q.Where(a =>
                a.MetadataProfile != null &&
                a.MetadataProfile.Rating >= minRating.Value &&
                a.MetadataProfile.Rating <= maxRating.Value);
        }

        // Note: DateTimeOffset comparison operators (>=, <=) in Where clauses work on
        // PostgreSQL/SQL Server but are NOT supported by the SQLite EF Core provider.
        // In standalone (SQLite) mode, these filters are silently skipped.
        if (fromDate.HasValue)
        {
            var from = fromDate.Value.ToUniversalTime();
            q = q.Where(a => a.CreatedAt >= from);
        }

        if (toDate.HasValue)
        {
            var to = toDate.Value.ToUniversalTime();
            q = q.Where(a => a.CreatedAt <= to);
        }

        // DateTimeOffset ORDER BY is not supported by SQLite's EF Core provider.
        // Apply non-DateAdded sorts in the DB; for DateAdded, load all matching IDs
        // with their CreatedAt values, sort in memory, paginate, then load assets by ID.
        if (sortBy == "DateAdded")
        {
            // Query 1: Load just IDs with CreatedAt for ordering
            var ordering = await q
                .Select(a => new { a.Id, a.CreatedAt })
                .ToListAsync(ct);

            var orderedIds = (sortDir.ToLower() == "desc"
                    ? ordering.OrderByDescending(x => x.CreatedAt)
                    : ordering.OrderBy(x => x.CreatedAt))
                .Select(x => x.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            if (orderedIds.Count == 0)
                return [];

            // Query 2: Load full assets by page IDs, preserving order
            var idOrder = orderedIds.Select((id, idx) => new { id, idx })
                .ToDictionary(x => x.id, x => x.idx);
            var assets = await _context.DigitalAssets
                .Include(a => a.Collection)
                .Include(a => a.MetadataProfile)
                .Include(a => a.Keywords)
                .Where(a => orderedIds.Contains(a.Id))
                .ToListAsync(ct);

            return [.. assets.OrderBy(a => idOrder.GetValueOrDefault(a.Id))];
        }

        q = (sortBy, sortDir.ToLower()) switch
        {
            ("FileName", "desc") => q.OrderByDescending(a => a.FileName),
            ("FileName", _) => q.OrderBy(a => a.FileName),
            ("FileType", "desc") => q.OrderByDescending(a => a.Type),
            ("FileType", _) => q.OrderBy(a => a.Type),
            ("FileSize", "desc") => q.OrderByDescending(a => a.FileSize),
            ("FileSize", _) => q.OrderBy(a => a.FileSize),
            _ => q.OrderBy(a => a.FileName)
        };

        return await q
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }
}
