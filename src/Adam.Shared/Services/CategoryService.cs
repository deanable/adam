using Adam.Shared.Data;
using Adam.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace Adam.Shared.Services;

/// <summary>
/// Domain logic for associating categories with assets. Extracted from
/// <see cref="AppDbContext"/> so the context remains pure data-access and this
/// behaviour is independently testable. Shares keyword normalization with
/// <see cref="KeywordService"/>.
/// </summary>
public sealed class CategoryService(AppDbContext db)
{
    public async Task AssociateCategoriesAsync(DigitalAsset asset, IEnumerable<string> categoryNames, bool isAiGenerated = false, CancellationToken ct = default)
    {
        if (categoryNames == null) return;

        var names = categoryNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct()
            .ToList();

        if (names.Count == 0) return;

        foreach (var name in names)
        {
            var normalized = KeywordService.NormalizeKeyword(name);
            var category = db.ChangeTracker.Entries<Category>()
                .Where(e => e.State == EntityState.Added)
                .Select(e => e.Entity)
                .FirstOrDefault(c => c.NormalizedName == normalized);

            category ??= await db.Categories.FirstOrDefaultAsync(c => c.NormalizedName == normalized, ct);
            if (category == null)
            {
                category = new Category
                {
                    Id = Guid.NewGuid(),
                    Name = name,
                    NormalizedName = normalized,
                    IsAiGenerated = isAiGenerated
                };
                db.Categories.Add(category);
            }
            if (!asset.Categories.Contains(category))
            {
                asset.Categories.Add(category);
            }
        }
    }
}
