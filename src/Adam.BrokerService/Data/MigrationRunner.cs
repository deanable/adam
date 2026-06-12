using Adam.Shared.Data;
using Adam.Shared.Models;
using Adam.Shared.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Adam.BrokerService.Data;

public sealed class MigrationRunner
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MigrationRunner> _logger;

    public MigrationRunner(IServiceProvider serviceProvider, ILogger<MigrationRunner> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        _logger.LogInformation("Running database migrations...");

        try
        {
            await db.Database.MigrateAsync(ct);
            await SeedDefaultAdminAsync(db, _logger, ct);
            _logger.LogInformation("Database ready.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database migration failed");
            throw;
        }
    }

    private static async Task SeedDefaultAdminAsync(AppDbContext db, ILogger logger, CancellationToken ct)
    {
        if (await db.Users.AnyAsync(ct))
        {
            logger.LogInformation("Seed: Users already exist — skipping default admin creation.");
            return;
        }

        var adminRoleId = Guid.Parse("00000000-0000-0000-0000-000000000003");
        var role = await db.Roles.FindAsync([adminRoleId], ct);
        if (role == null)
        {
            logger.LogWarning("Seed: Administrator role not found — cannot create default admin.");
            return;
        }

        var admin = new User
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000000"),
            Username = "admin",
            Email = "admin@adam.local",
            PasswordHash = PasswordHelper.HashPassword("admin"),
            RoleId = adminRoleId,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.Users.Add(admin);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Seed: Default admin user created (username='admin', role='{Role}').", role.Name);
    }
}
