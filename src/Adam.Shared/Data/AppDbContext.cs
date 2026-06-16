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
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<AccessLog> AccessLogs => Set<AccessLog>();
    public DbSet<ModeConfiguration> ModeConfigurations => Set<ModeConfiguration>();
    public DbSet<WatchedFolder> WatchedFolders => Set<WatchedFolder>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<SavedSearch> SavedSearches => Set<SavedSearch>();
    public DbSet<SearchHistoryEntry> SearchHistoryEntries => Set<SearchHistoryEntry>();
    public DbSet<AssetEmbedding> AssetEmbeddings => Set<AssetEmbedding>();

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
            e.Property(x => x.Version).HasDefaultValue(1).IsConcurrencyToken();
            // Composite unique index on (ChecksumSha256, IsDeleted) works across all
            // providers (SQLite, PostgreSQL, SQL Server) without dialect-specific SQL.
            // Each checksum can have at most one active row AND at most one deleted
            // row, which is sufficient for duplicate detection and restore.
            e.HasIndex(x => new { x.ChecksumSha256, x.IsDeleted }).IsUnique();
            e.HasIndex(x => x.Type);
            e.HasIndex(x => x.StoragePath);
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => x.ModifiedAt);
            e.HasIndex(x => x.MimeType);
            e.HasIndex(x => x.FileSize);
            e.HasIndex(x => x.FileName);
            e.HasIndex(x => new { x.Type, x.CreatedAt });
            e.HasIndex(x => x.SortOrder);
            e.HasIndex(x => new { x.CollectionId, x.SortOrder });
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
            e.Property(x => x.IsSmart).HasDefaultValue(false);
            e.Property(x => x.SmartQueryJson).HasMaxLength(4000);
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
            e.HasIndex(x => x.DateTaken);
            e.HasIndex(x => x.Rating);
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
            e.Property(x => x.IsAiGenerated).HasDefaultValue(false);
            // Composite unique: same keyword name can exist under different parents
            e.HasIndex(x => new { x.NormalizedName, x.ParentId }).IsUnique();
            e.HasOne(x => x.Parent).WithMany(k => k.Children).HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Category>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.Property(x => x.NormalizedName).IsRequired().HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.IsAiGenerated).HasDefaultValue(false);
            // Composite unique: same category name can exist under different parents
            e.HasIndex(x => new { x.NormalizedName, x.ParentId }).IsUnique();
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
            e.Ignore(x => x.Permissions); // computed from RolePermissions; not stored as a column
            e.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<RolePermission>(e =>
        {
            e.HasKey(x => new { x.RoleId, x.Permission });
            e.Property(x => x.Permission).IsRequired().HasMaxLength(100);
            e.HasOne(x => x.Role)
                .WithMany(r => r.RolePermissions)
                .HasForeignKey(x => x.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
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

        modelBuilder.Entity<Comment>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Body).IsRequired().HasMaxLength(5000);
            e.Property(x => x.Version).HasDefaultValue(1).IsConcurrencyToken();
            e.Property(x => x.IsDeleted).HasDefaultValue(false);
            e.HasIndex(x => x.AssetId);
            e.HasIndex(x => x.CreatedAt);
            e.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ParentComment).WithMany(c => c.Replies).HasForeignKey(x => x.ParentCommentId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<WatchedFolder>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Path).IsRequired().HasMaxLength(2000);
            e.HasIndex(x => x.Path).IsUnique();
            e.Property(x => x.IsEnabled).IsRequired();
        });

        modelBuilder.Entity<SavedSearch>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.Property(x => x.QueryText).HasMaxLength(2000);
            e.Property(x => x.FiltersJson).IsRequired();
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => new { x.UserId, x.Name }).IsUnique();
        });

        modelBuilder.Entity<SearchHistoryEntry>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.QueryText).IsRequired().HasMaxLength(2000);
            e.Property(x => x.FiltersJson).IsRequired();
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.ExecutedAt);
        });

        modelBuilder.Entity<AssetEmbedding>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.TextEmbedding).IsRequired();
            e.Property(x => x.ImageEmbedding);
            e.Property(x => x.ModelVersion).IsRequired().HasMaxLength(100);
            e.HasOne(x => x.Asset).WithOne().HasForeignKey<AssetEmbedding>(x => x.AssetId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.AssetId).IsUnique();
        });

        SeedData(modelBuilder);
    }

    private static void SeedData(ModelBuilder modelBuilder)
    {
        var viewerId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var editorId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var adminId  = Guid.Parse("00000000-0000-0000-0000-000000000003");

        modelBuilder.Entity<Role>().HasData(
            new Role { Id = viewerId, Name = "Viewer" },
            new Role { Id = editorId, Name = "Editor" },
            new Role { Id = adminId,  Name = "Administrator" }
        );

        modelBuilder.Entity<RolePermission>().HasData(
            // Viewer
            new RolePermission { RoleId = viewerId, Permission = "asset:read" },
            new RolePermission { RoleId = viewerId, Permission = "collection:read" },
            // Editor
            new RolePermission { RoleId = editorId, Permission = "asset:read" },
            new RolePermission { RoleId = editorId, Permission = "asset:create" },
            new RolePermission { RoleId = editorId, Permission = "asset:update" },
            new RolePermission { RoleId = editorId, Permission = "collection:read" },
            new RolePermission { RoleId = editorId, Permission = "collection:update" },
            // Administrator
            new RolePermission { RoleId = adminId, Permission = "asset:*" },
            new RolePermission { RoleId = adminId, Permission = "collection:*" },
            new RolePermission { RoleId = adminId, Permission = "user:*" },
            new RolePermission { RoleId = adminId, Permission = "role:*" },
            new RolePermission { RoleId = adminId, Permission = "audit:read" }
        );
    }
}
