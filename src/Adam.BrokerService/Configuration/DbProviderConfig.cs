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
                builder.UseNpgsql(ConnectionString);
                break;
            case "sqlserver":
            case "mssql":
                builder.UseSqlServer(ConnectionString);
                break;
            default:
                throw new ArgumentException($"Unsupported database provider: {Provider}");
        }
    }
}
