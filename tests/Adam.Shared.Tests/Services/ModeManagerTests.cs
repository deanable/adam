using Adam.Shared.Data;
using Adam.Shared.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Adam.Shared.Tests.Services;

/// <summary>
/// A simple <see cref="ILogger{T}"/> that captures all log entries in memory for
/// verification. Avoids NSubstitute's generic-type-argument matching limitations
/// with <see cref="ILogger.Log{TState}"/>.
/// </summary>
internal sealed class ListLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Entries.Add((logLevel, formatter(state, exception), exception));
    }
}

/// <summary>
/// Tests for <see cref="ModeManager.AddMissingColumnsAsync"/> — verifies that
/// schema migration operations on pre-existing databases are handled gracefully,
/// with proper exception logging and without throwing to callers.
/// </summary>
public sealed class ModeManagerTests : IDisposable
{
    private readonly string _basePath;

    public ModeManagerTests()
    {
        _basePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_basePath, recursive: true); } catch (IOException) { }
    }

    /// <summary>
    /// Verifies that <see cref="ModeManager.InitializeAsync"/> calls
    /// <see cref="ModeManager.AddMissingColumnsAsync"/> on a pre-existing
    /// database and completes without throwing, producing a valid schema.
    ///
    /// The first call creates the database (EnsureCreatedAsync → true).
    /// After removing the __EFMigrationsHistory table, the second call enters
    /// the EnsureCreatedAsync fallback path (returns false since tables exist),
    /// which triggers AddMissingColumnsAsync for SortOrder, UserPreferences,
    /// and index creation. All operations use IF NOT EXISTS / duplicate-column
    /// guards, so they succeed silently on a matching schema.
    /// </summary>
    [Fact]
    public async Task InitializeAsync_OnPreExistingDb_AddsMissingSchemaGracefully()
    {
        // Arrange
        var modeManager = new ModeManager(_basePath);

        // Act - First call: creates the database from scratch
        await modeManager.InitializeAsync();

        // Verify the DB was created
        File.Exists(modeManager.DbPath).Should().BeTrue();

        // Remove migration history so the next call goes through
        // the EnsureCreatedAsync → AddMissingColumnsAsync path
        await using (var db = modeManager.CreateDbContext())
        {
            await db.Database.ExecuteSqlRawAsync(
                "DROP TABLE IF EXISTS \"__EFMigrationsHistory\"");
        }

        // Act - Second call: EnsureCreatedAsync returns false (schema matches),
        // which triggers AddMissingColumnsAsync for the SortOrder column,
        // UserPreferences table, and composite indexes. Should not throw.
        await modeManager.InitializeAsync();

        // Assert - Schema is intact and queries work
        await using var verifyDb = modeManager.CreateDbContext();
        var conn = verifyDb.Database.GetDbConnection();
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();

        // The DigitalAssets table is still queryable
        cmd.CommandText = "SELECT COUNT(*) FROM \"DigitalAssets\"";
        var assetCount = (long)(await cmd.ExecuteScalarAsync())!;
        assetCount.Should().Be(0);

        // Verify SortOrder column was added (or already existed)
        cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('DigitalAssets') WHERE name='SortOrder'";
        var sortColCount = (long)(await cmd.ExecuteScalarAsync())!;
        sortColCount.Should().Be(1, "SortOrder column should exist on DigitalAssets");

        // Verify SortOrder index was created
        cmd.CommandText = "SELECT COUNT(*) FROM pragma_index_list('DigitalAssets') WHERE name='IX_DigitalAssets_SortOrder'";
        var sortIdxCount = (long)(await cmd.ExecuteScalarAsync())!;
        sortIdxCount.Should().Be(1, "IX_DigitalAssets_SortOrder index should exist");

        // Verify composite index was created
        cmd.CommandText = "SELECT COUNT(*) FROM pragma_index_list('DigitalAssets') WHERE name='IX_DigitalAssets_CollectionId_SortOrder'";
        var collIdxCount = (long)(await cmd.ExecuteScalarAsync())!;
        collIdxCount.Should().Be(1, "IX_DigitalAssets_CollectionId_SortOrder index should exist");

        // Verify UserPreferences table was created
        cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('UserPreferences') WHERE name='Id'";
        var prefTableCount = (long)(await cmd.ExecuteScalarAsync())!;
        prefTableCount.Should().Be(1, "UserPreferences table should exist");
    }

    /// <summary>
    /// Verifies that when <see cref="ModeManager.AddMissingColumnsAsync"/>
    /// encounters SQL failures, it catches the exception, logs a warning,
    /// and does not rethrow.
    ///
    /// The scenario: the UserPreferences table is replaced with a VIEW
    /// (same name). SQLite's shared table+view namespace causes
    /// <c>CREATE TABLE IF NOT EXISTS</c> to fail. The catch block in
    /// AddMissingColumnsAsync logs a warning and continues gracefully
    /// to the next operation.
    /// </summary>
    [Fact]
    public async Task AddMissingColumnsAsync_WhenSqlFails_LogsWarningAndContinues()
    {
        // Arrange
        var logger = new ListLogger<ModeManager>();
        var modeManager = new ModeManager(_basePath, logger: logger);

        // First call: creates the database
        await modeManager.InitializeAsync();

        // Remove migration history to enter the AddMissingColumnsAsync path
        await using (var db = modeManager.CreateDbContext())
        {
            await db.Database.ExecuteSqlRawAsync(
                "DROP TABLE IF EXISTS \"__EFMigrationsHistory\"");
        }

        // Replace the UserPreferences table with a VIEW of the same name.
        // SQLite shares the namespace between tables and views, so
        // 
        //   CREATE TABLE IF NOT EXISTS "UserPreferences" ...
        //
        // will FAIL because a VIEW with that name already exists.
        // This failure is caught by the general catch block that logs
        // at Warning level.
        await using (var db = modeManager.CreateDbContext())
        {
            await db.Database.ExecuteSqlRawAsync(
                "DROP TABLE IF EXISTS \"UserPreferences\"");
            await db.Database.ExecuteSqlRawAsync(
                "CREATE VIEW \"UserPreferences\" AS SELECT 1 AS \"Id\"");
        }

        // Clear captured entries from the first InitializeAsync
        logger.Entries.Clear();

        // Act - Second call triggers AddMissingColumnsAsync.
        // 1. ALTER TABLE "DigitalAssets" ADD COLUMN "SortOrder"
        //    → column already exists, caught silently by `when` filter
        // 2. CREATE TABLE IF NOT EXISTS "UserPreferences" ...
        //    → FAILS because a VIEW with that name exists
        //    → caught by general catch block, logged at Warning
        // 3. Remaining operations continue unaffected (DigitalAssets intact)
        await modeManager.InitializeAsync();

        // Assert - The method completed without throwing to the caller.
        // The view-conflict failure was logged at Warning level
        // with the exception details.
        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Warning &&
            e.Exception != null);

        // The database is still operational despite the UserPreferences error
        await using (var verifyDb = modeManager.CreateDbContext())
        {
            var count = await verifyDb.DigitalAssets.CountAsync();
            count.Should().Be(0);
        }
    }

    /// <summary>
    /// Verifies that when <see cref="ModeManager.AddMissingColumnsAsync"/>
    /// encounters the expected "duplicate column" exception for the SortOrder
    /// ALTER TABLE, it does NOT log it (since it's an expected condition).
    /// </summary>
    [Fact]
    public async Task AddMissingColumnsAsync_DuplicateColumn_DoesNotLogWarning()
    {
        // Arrange
        var logger = new ListLogger<ModeManager>();
        var modeManager = new ModeManager(_basePath, logger: logger);

        // First call: creates the database
        await modeManager.InitializeAsync();

        // Remove migration history
        await using (var db = modeManager.CreateDbContext())
        {
            await db.Database.ExecuteSqlRawAsync(
                "DROP TABLE IF EXISTS \"__EFMigrationsHistory\"");
        }

        // The SortOrder column already exists from EnsureCreatedAsync.
        // When AddMissingColumnsAsync runs again, the ALTER TABLE will
        // throw "duplicate column name: SortOrder", which is caught
        // silently by the `when (ex.Message.Contains("duplicate column"))` filter.
        // No manual ALTER TABLE needed — the column is already present.

        // Clear captured entries from the first InitializeAsync
        logger.Entries.Clear();

        // Act - Second call triggers AddMissingColumnsAsync.
        // The ALTER TABLE SortOrder will throw "duplicate column",
        // which is caught silently by the `when (ex.Message.Contains("duplicate column"))` filter.
        // The remaining operations (UserPreferences table, indexes) all succeed.
        await modeManager.InitializeAsync();

        // Assert - No warning/error logs were produced for the duplicate column
        // (it's caught by the when-filter without logging).
        // The indexes and UserPreferences table should succeed without warnings.
        logger.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
    }

    /// <summary>
    /// Verifies that when <see cref="ModeManager.AddMissingColumnsAsync"/> encounters
    /// index-creation failures (e.g. a read-only database), the failure is caught and
    /// logged at Warning level without throwing.
    ///
    /// After the scheme is created, the two SortOrder indexes are dropped and the
    /// database file is set to read-only. When AddMissingColumnsAsync runs, the
    /// <c>ALTER TABLE</c> and <c>CREATE TABLE IF NOT EXISTS</c> statements are
    /// no-ops (schema exists), but the dropped indexes trigger <c>CREATE INDEX</c>
    /// attempts on a read-only file, which fail and are caught by the dedicated
    /// catch blocks.
    /// </summary>
    [Fact]
    public async Task AddMissingColumnsAsync_WhenSortOrderIndexFails_LogsWarning()
    {
        // Arrange
        var logger = new ListLogger<ModeManager>();
        var modeManager = new ModeManager(_basePath, logger: logger);

        // First call: creates the database
        await modeManager.InitializeAsync();

        // Drop migration history to enter the EnsureCreatedAsync → AddMissingColumnsAsync path
        await using (var db = modeManager.CreateDbContext())
        {
            await db.Database.ExecuteSqlRawAsync(
                "DROP TABLE IF EXISTS \"__EFMigrationsHistory\"");
        }

        // Drop the two SortOrder indexes so AddMissingColumnsAsync tries to recreate them
        await using (var db = modeManager.CreateDbContext())
        {
            await db.Database.ExecuteSqlRawAsync(
                "DROP INDEX IF EXISTS \"IX_DigitalAssets_SortOrder\"");
            await db.Database.ExecuteSqlRawAsync(
                "DROP INDEX IF EXISTS \"IX_DigitalAssets_CollectionId_SortOrder\"");
        }

        // Close all connection pools and make the DB file read-only
        SqliteConnection.ClearAllPools();
        var dbFile = modeManager.DbPath;
        var originalAttributes = File.GetAttributes(dbFile);
        File.SetAttributes(dbFile, originalAttributes | FileAttributes.ReadOnly);

        // Clear captured entries from the first InitializeAsync
        logger.Entries.Clear();

        try
        {
            // Act - Second call triggers AddMissingColumnsAsync.
            // 1. ALTER TABLE "DigitalAssets" ADD COLUMN "SortOrder"
            //    → column already exists, caught silently by `when` filter
            // 2. CREATE TABLE IF NOT EXISTS "UserPreferences" + indexes
            //    → schema already exists, SQLite no-ops on existing objects
            // 3. CREATE INDEX "IX_DigitalAssets_SortOrder"
            //    → dropped above, tries to create → FAILS (read-only)
            //    → caught by general catch block, logged at Warning
            // 4. CREATE INDEX "IX_DigitalAssets_CollectionId_SortOrder"
            //    → dropped above, tries to create → FAILS (read-only)
            //    → caught by general catch block, logged at Warning
            await modeManager.InitializeAsync();

            // Assert - Warning logs were produced for the index-creation failures
            logger.Entries.Should().Contain(e =>
                e.Level == LogLevel.Warning &&
                e.Exception != null);
        }
        finally
        {
            // Restore file permissions for cleanup
            File.SetAttributes(dbFile, originalAttributes & ~FileAttributes.ReadOnly);
        }
    }

    /// <summary>
    /// Verifies that <see cref="ModeManager.InitializeAsync"/> can be called
    /// multiple times on the same database without throwing, even after
    /// AddMissingColumnsAsync has already applied its schema changes.
    /// All operations use IF NOT EXISTS / duplicate-column guards, so
    /// repeated calls are idempotent.
    /// </summary>
    [Fact]
    public async Task InitializeAsync_MultipleCalls_IsIdempotent()
    {
        // Arrange
        var modeManager = new ModeManager(_basePath);

        // Act - Call InitializeAsync three times
        await modeManager.InitializeAsync();
        await modeManager.InitializeAsync();
        await modeManager.InitializeAsync();

        // Assert - DB is operational after all calls
        await using var verifyDb = modeManager.CreateDbContext();
        var count = await verifyDb.DigitalAssets.CountAsync();
        count.Should().Be(0);

        // Verify schema elements exist
        var conn = verifyDb.Database.GetDbConnection();
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('DigitalAssets') WHERE name='SortOrder'";
        var sortColCount = (long)(await cmd.ExecuteScalarAsync())!;
        sortColCount.Should().Be(1);

        cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('UserPreferences') WHERE name='Id'";
        var prefCount = (long)(await cmd.ExecuteScalarAsync())!;
        prefCount.Should().Be(1);
    }
}
