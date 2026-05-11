using Adam.BrokerService.Configuration;
using Adam.Shared.Data;
using Adam.Shared.Models;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;
using Testcontainers.MsSql;

namespace Adam.BrokerService.Tests.Integration;

public sealed class DbProviderMatrixTests
{
    [Theory]
    [InlineData("sqlite", "Data Source=:memory:")]
    public async Task Can_create_and_query_database(string provider, string connectionString)
    {
        using var sqliteConn = provider == "sqlite" && connectionString == "Data Source=:memory:"
            ? new SqliteConnection(connectionString)
            : null;

        if (sqliteConn != null)
            await sqliteConn.OpenAsync();

        var config = new DbProviderConfig
        {
            Provider = provider,
            ConnectionString = sqliteConn != null ? connectionString : connectionString
        };

        var options = new DbContextOptionsBuilder<AppDbContext>();
        if (sqliteConn != null)
            options.UseSqlite(sqliteConn);
        else
            config.Configure(options);

        await using var db = new AppDbContext(options.Options);
        await db.Database.EnsureCreatedAsync();

        var collectionId = Guid.NewGuid();
        db.Collections.Add(new Collection { Id = collectionId, Name = "Test" });
        await db.SaveChangesAsync();

        db.DigitalAssets.Add(new DigitalAsset
        {
            Id = Guid.NewGuid(),
            FileName = "test.jpg",
            FileExtension = ".jpg",
            MimeType = "image/jpeg",
            FileSize = 1024,
            ChecksumSha256 = new string('b', 64),
            StoragePath = "/test/test.jpg",
            Title = "Provider Test",
            Type = AssetType.Image,
            CollectionId = collectionId
        });
        await db.SaveChangesAsync();

        var assets = await db.DigitalAssets.ToListAsync();
        assets.Should().HaveCount(1);
        assets[0].Title.Should().Be("Provider Test");
    }

    [Fact]
    public async Task DbProviderConfig_reads_from_configuration()
    {
        var configData = new Dictionary<string, string?>
        {
            ["DbProvider"] = "postgresql",
            ["DbConnection"] = "Host=localhost;Database=adam_test"
        };

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        var dbConfig = DbProviderConfig.FromConfiguration(config);
        dbConfig.Provider.Should().Be("postgresql");
        dbConfig.ConnectionString.Should().Be("Host=localhost;Database=adam_test");
    }

    [Fact(Skip = "Requires Docker")]
    public async Task Can_use_postgresql_with_testcontainers()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("adam_test")
            .WithUsername("adam")
            .WithPassword("adam_test_pass")
            .Build();

        await postgres.StartAsync();

        var config = new DbProviderConfig
        {
            Provider = "postgresql",
            ConnectionString = postgres.GetConnectionString()
        };

        var options = new DbContextOptionsBuilder<AppDbContext>();
        config.Configure(options);

        await using var db = new AppDbContext(options.Options);
        await db.Database.EnsureCreatedAsync();

        var count = await db.DigitalAssets.CountAsync();
        count.Should().Be(0);

        await postgres.StopAsync();
    }

    [Fact(Skip = "Requires Docker")]
    public async Task Can_use_sqlserver_with_testcontainers()
    {
        await using var mssql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
            .Build();

        await mssql.StartAsync();

        var config = new DbProviderConfig
        {
            Provider = "sqlserver",
            ConnectionString = mssql.GetConnectionString()
        };

        var options = new DbContextOptionsBuilder<AppDbContext>();
        config.Configure(options);

        await using var db = new AppDbContext(options.Options);
        await db.Database.EnsureCreatedAsync();

        var count = await db.DigitalAssets.CountAsync();
        count.Should().Be(0);

        await mssql.StopAsync();
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("postgresql")]
    [InlineData("sqlserver")]
    public void DbProviderConfig_supports_all_providers(string provider)
    {
        var config = new DbProviderConfig
        {
            Provider = provider,
            ConnectionString = "Data Source=test.db"
        };

        var options = new DbContextOptionsBuilder<AppDbContext>();
        config.Configure(options);

        var db = new AppDbContext(options.Options);
        db.Should().NotBeNull();
    }
}
