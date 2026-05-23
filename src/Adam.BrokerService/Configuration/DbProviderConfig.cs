using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Adam.Shared.Data;

namespace Adam.BrokerService.Configuration;

public sealed class DbProviderConfig
{
    public string Provider { get; set; } = "sqlite";
    public string ConnectionString { get; set; } = "Data Source=catalog.db";

    public static DbProviderConfig FromConfiguration(IConfiguration config)
    {
        return new DbProviderConfig
        {
            Provider = config.GetValue<string>("DbProvider") ?? "sqlite",
            ConnectionString = config.GetValue<string>("DbConnection") ?? $"Data Source={config.GetValue<string>("DbPath") ?? "catalog.db"}"
        };
    }

    public void Configure(DbContextOptionsBuilder builder)
    {
        switch (Provider.ToLowerInvariant())
        {
            case "sqlite":
                builder.UseSqlite(ConnectionString);
                break;
            case "postgresql":
            case "postgres":
                builder.UseNpgsql(ConnectionString, npgsql =>
                    npgsql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(30), null));
                break;
            case "sqlserver":
            case "mssql":
                builder.UseSqlServer(ConnectionString, sql =>
                    sql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(30), null));
                break;
            default:
                throw new ArgumentException($"Unsupported database provider: {Provider}");
        }
    }
}
