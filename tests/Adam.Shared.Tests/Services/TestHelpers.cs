using Adam.Shared.Data;
using Microsoft.EntityFrameworkCore;

namespace Adam.Shared.Tests.Services;

/// <summary>
/// A simple implementation of <see cref="IDbContextFactory{TContext}"/>
/// for <see cref="AppDbContext"/> that wraps pre-configured options.
/// Used by tests that need to inject an IDbContextFactory into services.
/// </summary>
public sealed class SimpleDbContextFactory : IDbContextFactory<AppDbContext>
{
    private readonly DbContextOptions<AppDbContext> _options;

    public SimpleDbContextFactory(DbContextOptions<AppDbContext> options)
    {
        _options = options;
    }

    public AppDbContext CreateDbContext()
    {
        return new AppDbContext(_options);
    }

    public async Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        return new AppDbContext(_options);
    }
}
