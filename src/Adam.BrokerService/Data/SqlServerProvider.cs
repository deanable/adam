using Microsoft.EntityFrameworkCore;
using Adam.Shared.Data;

namespace Adam.BrokerService.Data;

public static class SqlServerProvider
{
    public const string ProviderName = "sqlserver";

    public static DbContextOptionsBuilder<AppDbContext> Configure(DbContextOptionsBuilder<AppDbContext> builder, string connectionString)
    {
        return builder.UseSqlServer(connectionString, opts =>
        {
            opts.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
        });
    }
}
