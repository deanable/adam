using System.Text.RegularExpressions;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace Adam.Shared.Services;

/// <summary>
/// Domain logic for associating keywords with assets and maintaining the
/// hierarchical keyword tree. Extracted from <see cref="AppDbContext"/> so the
/// context remains pure data-access and this behaviour is independently testable.
/// </summary>
public sealed class KeywordService(AppDbContext db)
{
    public async Task AssociateKeywordsAsync(DigitalAsset asset, IEnumerable<string> keywordNames, CancellationToken ct = default)
    {
        if (keywordNames == null) return;

        var names = keywordNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct()
            .ToList();

        if (names.Count == 0) return;

        foreach (var name in names)
        {
            var leafKeyword = await EnsureKeywordHierarchyAsync(name, ct);
            if (!asset.Keywords.Contains(leafKeyword))
            {
                asset.Keywords.Add(leafKeyword);
                leafKeyword.UsageCount++;
            }
        }
    }

    public async Task<Keyword> EnsureKeywordHierarchyAsync(string hierarchicalName, CancellationToken ct = default)
    {
        var parts = hierarchicalName.Split(new[] { '|', '>' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();

        if (parts.Count == 0)
            throw new ArgumentException("Keyword name cannot be empty", nameof(hierarchicalName));

        Keyword? parent = null;
        Keyword? current = null;

        foreach (var part in parts)
        {
            var normalized = NormalizeKeyword(part);

            current = db.ChangeTracker.Entries<Keyword>()
                .Where(e => e.State == EntityState.Added)
                .Select(e => e.Entity)
                .FirstOrDefault(k => k.NormalizedName == normalized);

            current ??= await db.Keywords.FirstOrDefaultAsync(
                k => k.NormalizedName == normalized, ct);

            if (current == null)
            {
                current = new Keyword
                {
                    Id = Guid.NewGuid(),
                    Name = part,
                    NormalizedName = normalized,
                    ParentId = parent?.Id
                };
                db.Keywords.Add(current);
            }

            parent = current;
        }

        return current!;
    }

    internal static string NormalizeKeyword(string name)
        => Regex.Replace(name.Trim().ToLowerInvariant(), @"\s+", " ");
}
