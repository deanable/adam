using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Adam.Shared.Data;

/// <summary>
/// Design-time factory for EF Core migration tooling (dotnet ef migrations add).
/// Uses a temporary SQLite database so migrations can be generated without a real database.
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseSqlite("Data Source=:memory:");

        return new AppDbContext(optionsBuilder.Options);
    }
}
