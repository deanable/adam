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
        DateTime? fromDate = null,
        DateTime? toDate = null,
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

        if (fromDate.HasValue)
            q = q.Where(a => a.CreatedAt >= fromDate.Value);

        if (toDate.HasValue)
            q = q.Where(a => a.CreatedAt <= toDate.Value);

        q = (sortBy, sortDir.ToLower()) switch
        {
            ("FileName", "desc") => q.OrderByDescending(a => a.FileName),
            ("FileName", _) => q.OrderBy(a => a.FileName),
            ("DateAdded", "desc") => q.OrderByDescending(a => a.CreatedAt),
            ("DateAdded", _) => q.OrderBy(a => a.CreatedAt),
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
