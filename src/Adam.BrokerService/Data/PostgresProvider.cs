using Microsoft.EntityFrameworkCore;
using Adam.Shared.Data;

namespace Adam.BrokerService.Data;

public static class PostgresProvider
{
    public const string ProviderName = "postgresql";

    public static DbContextOptionsBuilder<AppDbContext> Configure(DbContextOptionsBuilder<AppDbContext> builder, string connectionString)
    {
        return builder.UseNpgsql(connectionString, opts =>
        {
            opts.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
            opts.MigrationsHistoryTable("__EFMigrationsHistory", "public");
        });
    }
}
