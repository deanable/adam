using Adam.Shared.Data;
using Adam.Shared.Models;
using Adam.Shared.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.Shared.Tests.Services;

public sealed class AccessLogCleanupServiceTests : IDisposable
{
    private readonly string _basePath;
    private readonly ModeManager _modeManager;
    private readonly AccessLogCleanupService _sut;

    public AccessLogCleanupServiceTests()
    {
        _basePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _modeManager = new ModeManager(_basePath);
        _modeManager.InitializeAsync().GetAwaiter().GetResult();

        _sut = new AccessLogCleanupService(
            _modeManager,
            logger: NullLogger<AccessLogCleanupService>.Instance);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_basePath, recursive: true); } catch { }
    }

    /// <summary>
    /// Seeds a user and access log entries. Returns the user ID for FK references.
    /// The caller must dispose the returned context.
    /// </summary>
    private async Task<(Guid UserId, AppDbContext Db)> SeedUserAndLogsAsync(params (string action, int daysAgo)[] entries)
    {
        var db = await _modeManager.CreateDbContextAsync();

        var userId = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id = userId,
            Username = "testuser",
            Email = "test@example.com",
            PasswordHash = "hash",
            RoleId = Guid.Parse("00000000-0000-0000-0000-000000000003"),
            IsActive = true
        });
        await db.SaveChangesAsync();

        foreach (var (action, daysAgo) in entries)
        {
            db.AccessLogs.Add(new AccessLog
            {
                Id = Guid.NewGuid(),
                Action = action,
                EntityType = "Asset",
                UserId = userId,
                Timestamp = DateTimeOffset.UtcNow.AddDays(-daysAgo) // stored as DateTimeOffset, service compares via UtcDateTime
            });
        }
        await db.SaveChangesAsync();

        return (userId, db);
    }

    [Fact]
    public async Task PruneAsync_DefaultRetention_PrunesOldEntries()
    {
        // Arrange
        var (_, db) = await SeedUserAndLogsAsync(
            ("Login", 60),
            ("Export", 45),
            ("View", 5));

        // Verify seed
        (await db.AccessLogs.CountAsync()).Should().Be(3);
        db.Dispose();

        // Act
        var deleted = await _sut.PruneAsync(retentionDays: 30);

        // Assert
        deleted.Should().Be(2);

        await using var verify = await _modeManager.CreateDbContextAsync();
        (await verify.AccessLogs.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task PruneAsync_RetentionZero_DoesNothing()
    {
        // Arrange
        var (_, db) = await SeedUserAndLogsAsync(("Login", 100));
        db.Dispose();

        // Act
        var deleted = await _sut.PruneAsync(retentionDays: 0);

        // Assert
        deleted.Should().Be(0);

        await using var verify = await _modeManager.CreateDbContextAsync();
        (await verify.AccessLogs.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task PruneAsync_NoOldEntries_ReturnsZero()
    {
        // Arrange
        var (_, db) = await SeedUserAndLogsAsync(("View", 1));
        db.Dispose();

        // Act
        var deleted = await _sut.PruneAsync(retentionDays: 30);

        // Assert
        deleted.Should().Be(0);
    }

    [Fact]
    public void GetRetentionDays_NoConfig_ReturnsDefault()
    {
        var local = new AccessLogCleanupService(
            _modeManager,
            logger: NullLogger<AccessLogCleanupService>.Instance);

        local.GetRetentionDays().Should().Be(30);
    }

    [Fact]
    public void GetRetentionDays_WithConfig_ReturnsConfiguredValue()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AccessLog:RetentionDays"] = "7"
            })
            .Build();

        var local = new AccessLogCleanupService(
            _modeManager, config,
            NullLogger<AccessLogCleanupService>.Instance);

        local.GetRetentionDays().Should().Be(7);
    }
}
