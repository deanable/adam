using Microsoft.EntityFrameworkCore;
using Adam.Shared.Models;

namespace Adam.Shared.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        foreach (var entry in ChangeTracker.Entries<DigitalAsset>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                entry.Entity.ModifiedAt = DateTimeOffset.UtcNow;
                if (entry.State == EntityState.Modified)
                    entry.Entity.Version++;
            }
            else if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = DateTimeOffset.UtcNow;
                entry.Entity.ModifiedAt = DateTimeOffset.UtcNow;
            }
        }

        return base.SaveChangesAsync(ct);
    }

    public DbSet<DigitalAsset> DigitalAssets => Set<DigitalAsset>();
    public DbSet<Collection> Collections => Set<Collection>();
    public DbSet<MetadataProfile> MetadataProfiles => Set<MetadataProfile>();
    public DbSet<RatingInfo> RatingInfos => Set<RatingInfo>();
    public DbSet<Keyword> Keywords => Set<Keyword>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<AccessLog> AccessLogs => Set<AccessLog>();
    public DbSet<ModeConfiguration> ModeConfigurations => Set<ModeConfiguration>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DigitalAsset>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.FileName).IsRequired().HasMaxLength(500);
            e.Property(x => x.FileExtension).IsRequired().HasMaxLength(50);
            e.Property(x => x.MimeType).IsRequired().HasMaxLength(255);
            e.Property(x => x.FileSize).IsRequired();
            e.Property(x => x.ChecksumSha256).IsRequired().HasMaxLength(64);
            e.Property(x => x.StoragePath).IsRequired();
            e.Property(x => x.OriginalPath).IsRequired().HasMaxLength(2000);
            e.Property(x => x.Title).IsRequired().HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(2000);
            e.Property(x => x.Type).IsRequired();
            e.Property(x => x.IsDeleted).HasDefaultValue(false);
            e.Property(x => x.Version).HasDefaultValue(1);
            e.HasIndex(x => x.ChecksumSha256).IsUnique().HasFilter("NOT IsDeleted");
            e.HasOne(x => x.Collection).WithMany(c => c.Assets).HasForeignKey(x => x.CollectionId);
            e.HasOne(x => x.MetadataProfile).WithOne(m => m.DigitalAsset).HasForeignKey<MetadataProfile>(m => m.DigitalAssetId);
            e.HasQueryFilter(x => !x.IsDeleted);

            e.HasMany(x => x.Keywords)
                .WithMany(k => k.Assets)
                .UsingEntity<Dictionary<string, object>>(
                    "AssetKeywords",
                    j => j.HasOne<Keyword>().WithMany().HasForeignKey("KeywordsId"),
                    j => j.HasOne<DigitalAsset>().WithMany().HasForeignKey("DigitalAssetsId"),
                    j =>
                    {
                        j.HasKey("DigitalAssetsId", "KeywordsId");
                        j.HasIndex("KeywordsId").HasDatabaseName("IX_AssetKeywords_KeywordId");
                        j.HasIndex("DigitalAssetsId").HasDatabaseName("IX_AssetKeywords_AssetId");
                    });

            e.HasMany(x => x.Categories)
                .WithMany(c => c.Assets)
                .UsingEntity<Dictionary<string, object>>(
                    "AssetCategories",
                    j => j.HasOne<Category>().WithMany().HasForeignKey("CategoriesId"),
                    j => j.HasOne<DigitalAsset>().WithMany().HasForeignKey("DigitalAssetsId"),
                    j =>
                    {
                        j.HasKey("DigitalAssetsId", "CategoriesId");
                        j.HasIndex("CategoriesId").HasDatabaseName("IX_AssetCategories_CategoryId");
                        j.HasIndex("DigitalAssetsId").HasDatabaseName("IX_AssetCategories_AssetId");
                    });
        });

        modelBuilder.Entity<Collection>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(1000);
            e.HasOne(x => x.Parent).WithMany(c => c.Children).HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.Name, x.ParentId }).IsUnique();
        });

        modelBuilder.Entity<MetadataProfile>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.CameraMake).HasMaxLength(200);
            e.Property(x => x.CameraModel).HasMaxLength(200);
            e.Property(x => x.LensModel).HasMaxLength(200);
            e.Property(x => x.ExposureTime).HasMaxLength(50);
            e.Property(x => x.Orientation).HasMaxLength(50);
        });

        modelBuilder.Entity<RatingInfo>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.DigitalAsset).WithOne().HasForeignKey<RatingInfo>(x => x.DigitalAssetId);
        });

        modelBuilder.Entity<Keyword>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.Property(x => x.NormalizedName).IsRequired().HasMaxLength(200);
            e.HasIndex(x => x.NormalizedName).IsUnique();
            e.HasOne(x => x.Parent).WithMany(k => k.Children).HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Category>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.Property(x => x.NormalizedName).IsRequired().HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(1000);
            e.HasIndex(x => x.NormalizedName).IsUnique();
            e.HasOne(x => x.Parent).WithMany(c => c.Children).HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Username).IsRequired().HasMaxLength(100);
            e.Property(x => x.Email).IsRequired().HasMaxLength(255);
            e.Property(x => x.PasswordHash).IsRequired();
            e.HasIndex(x => x.Username).IsUnique();
            e.HasIndex(x => x.Email).IsUnique();
            e.HasOne(x => x.Role).WithMany(r => r.Users).HasForeignKey(x => x.RoleId);
        });

        modelBuilder.Entity<Role>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(50);
            e.Property(x => x.Permissions).HasConversion(
                v => string.Join(',', v),
                v => v.Split(',', StringSplitOptions.RemoveEmptyEntries));
            e.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<AccessLog>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Action).IsRequired().HasMaxLength(50);
            e.Property(x => x.EntityType).IsRequired().HasMaxLength(50);
            e.HasOne(x => x.User).WithMany(u => u.AccessLogs).HasForeignKey(x => x.UserId);
            e.HasIndex(x => x.Timestamp);
        });

        modelBuilder.Entity<ModeConfiguration>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Mode).IsRequired().HasMaxLength(20);
            e.Property(x => x.DbProvider).IsRequired().HasMaxLength(20);
            e.Property(x => x.ServiceEndpoint).HasMaxLength(255);
        });

        SeedData(modelBuilder);
    }

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

    public async Task AssociateCategoriesAsync(DigitalAsset asset, IEnumerable<string> categoryNames, CancellationToken ct = default)
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
            var normalized = NormalizeKeyword(name);
            var category = ChangeTracker.Entries<Category>()
                .Where(e => e.State == EntityState.Added)
                .Select(e => e.Entity)
                .FirstOrDefault(c => c.NormalizedName == normalized);

            category ??= await Categories.FirstOrDefaultAsync(c => c.NormalizedName == normalized, ct);
            if (category == null)
            {
                category = new Category
                {
                    Id = Guid.NewGuid(),
                    Name = name,
                    NormalizedName = normalized
                };
                Categories.Add(category);
            }
            if (!asset.Categories.Contains(category))
            {
                asset.Categories.Add(category);
            }
        }
    }

    private async Task<Keyword> EnsureKeywordHierarchyAsync(string hierarchicalName, CancellationToken ct)
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
            var parentId = parent?.Id;

            current = ChangeTracker.Entries<Keyword>()
                .Where(e => e.State == EntityState.Added)
                .Select(e => e.Entity)
                .FirstOrDefault(k => k.NormalizedName == normalized);

            current ??= await Keywords.FirstOrDefaultAsync(
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
                Keywords.Add(current);
            }

            parent = current;
        }

        return current!;
    }

    private static string NormalizeKeyword(string name)
    {
        var normalized = name.ToLowerInvariant().Trim();
        // Collapse multiple spaces into single space
        while (normalized.Contains("  "))
            normalized = normalized.Replace("  ", " ");
        return normalized;
    }

    private static void SeedData(ModelBuilder modelBuilder)
    {
        var viewerId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var editorId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var adminId = Guid.Parse("00000000-0000-0000-0000-000000000003");

        modelBuilder.Entity<Role>().HasData(
            new Role { Id = viewerId, Name = "Viewer", Permissions = ["asset:read", "collection:read"] },
            new Role { Id = editorId, Name = "Editor", Permissions = ["asset:read", "asset:create", "asset:update", "collection:read", "collection:update"] },
            new Role { Id = adminId, Name = "Administrator", Permissions = ["asset:*", "collection:*", "user:*", "role:*", "audit:read"] }
        );
    }
}
