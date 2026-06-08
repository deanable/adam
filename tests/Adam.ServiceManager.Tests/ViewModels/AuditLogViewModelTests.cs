using System.ComponentModel;
using Adam.ServiceManager.ViewModels;
using Adam.Shared.Models;
using Adam.Shared.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.ServiceManager.Tests.ViewModels;

/// <summary>
/// Tests for <see cref="AuditLogViewModel"/> — the audit log viewer in ServiceManager.
/// Uses <see cref="SyncUiDispatcher"/> to avoid hanging on Avalonia's dispatcher.
/// </summary>
public sealed class AuditLogViewModelTests : IDisposable
{
    private readonly string _basePath;
    private readonly ModeManager _modeManager;
    private readonly AuditLogViewModel _vm;
    private readonly SyncUiDispatcher _dispatcher;
    private bool _disposed;

    public AuditLogViewModelTests()
    {
        _basePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _modeManager = new ModeManager(_basePath);
        _modeManager.InitializeAsync().GetAwaiter().GetResult();
        _dispatcher = new SyncUiDispatcher();
        _vm = new AuditLogViewModel(_modeManager, new NullLogger<AuditLogViewModel>(), _dispatcher);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_basePath))
                Directory.Delete(_basePath, recursive: true);
        }
        catch (IOException) { }
    }

    // ──────────────────────────────────────────────
    //  Constructor
    // ──────────────────────────────────────────────

    [Fact]
    public void Constructor_CommandsAreInitialized()
    {
        _vm.RefreshCommand.Should().NotBeNull();
        _vm.ClearLogCommand.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_LogMessages_ContainsInitializationEntry()
    {
        _vm.LogMessages.Should().NotBeEmpty();
        _vm.LogMessages[0].Should().Contain("Audit Log initialized");
    }

    [Fact]
    public void Constructor_DefaultState_IsNotLoading()
    {
        _vm.IsLoading.Should().BeFalse();
    }

    [Fact]
    public void Constructor_DefaultState_StatusTextIsEmpty()
    {
        _vm.StatusText.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_DefaultState_LogsIsEmpty()
    {
        _vm.Logs.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────
    //  Property round-trips
    // ──────────────────────────────────────────────

    [Fact]
    public void IsLoading_RoundTrips()
    {
        _vm.IsLoading = true;
        _vm.IsLoading.Should().BeTrue();
        _vm.IsLoading = false;
        _vm.IsLoading.Should().BeFalse();
    }

    [Fact]
    public void StatusText_RoundTrips()
    {
        _vm.StatusText = "Test status";
        _vm.StatusText.Should().Be("Test status");
    }

    [Fact]
    public void FilterAction_RoundTrips()
    {
        _vm.FilterAction = "Create";
        _vm.FilterAction.Should().Be("Create");
    }

    [Fact]
    public void FilterEntityType_RoundTrips()
    {
        _vm.FilterEntityType = "User";
        _vm.FilterEntityType.Should().Be("User");
    }

    [Fact]
    public void FilterFrom_RoundTrips()
    {
        var dt = DateTimeOffset.UtcNow;
        _vm.FilterFrom = dt;
        _vm.FilterFrom.Should().Be(dt);
        _vm.FilterFrom = null;
        _vm.FilterFrom.Should().BeNull();
    }

    [Fact]
    public void FilterTo_RoundTrips()
    {
        var dt = DateTimeOffset.UtcNow;
        _vm.FilterTo = dt;
        _vm.FilterTo.Should().Be(dt);
        _vm.FilterTo = null;
        _vm.FilterTo.Should().BeNull();
    }

    // ──────────────────────────────────────────────
    //  PropertyChanged notifications
    // ──────────────────────────────────────────────

    [Fact]
    public void StatusText_Setter_RaisesPropertyChanged()
    {
        var changes = new List<string?>();
        _vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        _vm.StatusText = "Updated";
        changes.Should().Contain(nameof(AuditLogViewModel.StatusText));
    }

    [Fact]
    public void IsLoading_Setter_RaisesPropertyChanged()
    {
        var changes = new List<string?>();
        _vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        _vm.IsLoading = true;
        changes.Should().Contain(nameof(AuditLogViewModel.IsLoading));
    }

    [Fact]
    public void FilterAction_Setter_RaisesPropertyChanged()
    {
        var changes = new List<string?>();
        _vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        _vm.FilterAction = "Create";
        changes.Should().Contain(nameof(AuditLogViewModel.FilterAction));
    }

    [Fact]
    public void FilterEntityType_Setter_RaisesPropertyChanged()
    {
        var changes = new List<string?>();
        _vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        _vm.FilterEntityType = "User";
        changes.Should().Contain(nameof(AuditLogViewModel.FilterEntityType));
    }

    // ──────────────────────────────────────────────
    //  ClearLogCommand
    // ──────────────────────────────────────────────

    [Fact]
    public void ClearLogCommand_ClearsLogMessages()
    {
        _vm.LogMessages.Add("Test message");
        _vm.ClearLogCommand.Execute(null);
        _vm.LogMessages.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────
    //  LoadLogsAsync
    // ──────────────────────────────────────────────

    [Fact]
    public async Task LoadLogsAsync_WhenDatabaseEmpty_LeavesLogsEmpty()
    {
        await _vm.LoadLogsAsync();
        _vm.Logs.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadLogsAsync_LoadsLogEntries()
    {
        // Seed access logs
        await SeedAccessLogEntryAsync("Create", "User", "Created user admin");
        await SeedAccessLogEntryAsync("Update", "Asset", "Updated metadata");

        await _vm.LoadLogsAsync();

        _vm.Logs.Should().HaveCount(2);
        _vm.Logs.Should().Contain(l => l.Action == "Create" && l.EntityType == "User");
        _vm.Logs.Should().Contain(l => l.Action == "Update" && l.EntityType == "Asset");
    }

    [Fact]
    public async Task LoadLogsAsync_SetsIsLoadingFlags()
    {
        await _vm.LoadLogsAsync();
        _vm.IsLoading.Should().BeFalse();
    }

    [Fact]
    public async Task LoadLogsAsync_AddsLogEntries()
    {
        await _vm.LoadLogsAsync();
        _vm.LogMessages.Should().Contain(m => m.Contains("Loading audit logs..."));
        _vm.LogMessages.Should().Contain(m => m.Contains("Querying database..."));
        _vm.LogMessages.Should().Contain(m => m.Contains("Loaded 0 log entr"));
    }

    [Fact]
    public async Task LoadLogsAsync_ClearsPreviousDataBeforeReload()
    {
        await SeedAccessLogEntryAsync("Create", "User", "entry1");
        await _vm.LoadLogsAsync();
        _vm.Logs.Should().HaveCount(1);

        // Reload should clear and re-populate
        await _vm.LoadLogsAsync();
        _vm.Logs.Should().HaveCount(1);
    }

    // ──────────────────────────────────────────────
    //  LoadLogsAsync — filtering
    // ──────────────────────────────────────────────

    [Fact]
    public async Task LoadLogsAsync_FilterByAction_ReturnsOnlyMatching()
    {
        await SeedAccessLogEntryAsync("Create", "User", "Created user");
        await SeedAccessLogEntryAsync("Delete", "User", "Deleted user");
        await SeedAccessLogEntryAsync("Update", "Asset", "Updated asset");

        _vm.FilterAction = "Create";
        await _vm.LoadLogsAsync();

        _vm.Logs.Should().HaveCount(1);
        _vm.Logs[0].Action.Should().Be("Create");
    }

    [Fact]
    public async Task LoadLogsAsync_FilterByEntityType_ReturnsOnlyMatching()
    {
        await SeedAccessLogEntryAsync("Create", "User", "Created user");
        await SeedAccessLogEntryAsync("Create", "Asset", "Created asset");
        await SeedAccessLogEntryAsync("Create", "Collection", "Created collection");

        _vm.FilterEntityType = "Asset";
        await _vm.LoadLogsAsync();

        _vm.Logs.Should().HaveCount(1);
        _vm.Logs[0].EntityType.Should().Be("Asset");
    }

    [Fact]
    public async Task LoadLogsAsync_FilterByFrom_ReturnsEntriesAfterDate()
    {
        // Create an older entry (yesterday) and a newer one (today)
        var yesterday = DateTimeOffset.UtcNow.AddDays(-1);
        var now = DateTimeOffset.UtcNow;

        await SeedAccessLogEntryAsync("Create", "User", "old", yesterday);
        await SeedAccessLogEntryAsync("Create", "User", "new", now);

        _vm.FilterFrom = now.AddHours(-1);  // Only the recent one
        await _vm.LoadLogsAsync();

        _vm.Logs.Should().HaveCount(1);
        _vm.Logs[0].Details.Should().Be("new");
    }

    [Fact]
    public async Task LoadLogsAsync_FilterByTo_ReturnsEntriesBeforeDate()
    {
        var yesterday = DateTimeOffset.UtcNow.AddDays(-1);
        var now = DateTimeOffset.UtcNow;

        await SeedAccessLogEntryAsync("Create", "User", "old", yesterday);
        await SeedAccessLogEntryAsync("Create", "User", "new", now);

        _vm.FilterTo = yesterday.AddHours(1);  // Only the old one
        await _vm.LoadLogsAsync();

        _vm.Logs.Should().HaveCount(1);
        _vm.Logs[0].Details.Should().Be("old");
    }

    [Fact]
    public async Task LoadLogsAsync_RefreshingFilters_ClearsAndReloads()
    {
        await SeedAccessLogEntryAsync("Create", "User", "entry1");

        // Load without filter
        await _vm.LoadLogsAsync();
        _vm.Logs.Should().HaveCount(1);

        // Apply filter and reload
        _vm.FilterAction = "Delete";
        await _vm.LoadLogsAsync();

        _vm.Logs.Should().BeEmpty();
        _vm.LogMessages.Should().Contain(m => m.Contains("Filtering by action: 'Delete'"));
    }

    // ──────────────────────────────────────────────
    //  PropertyChanged — full notification set
    // ──────────────────────────────────────────────

    [Fact]
    public async Task LoadLogsAsync_RaisesPropertyChanged()
    {
        var changes = new List<string?>();
        _vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        await _vm.LoadLogsAsync();

        changes.Should().Contain(nameof(AuditLogViewModel.IsLoading));
    }

    // ──────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────

    private async Task SeedAccessLogEntryAsync(string action, string entityType, string details, DateTimeOffset? timestamp = null)
    {
        await using var db = _modeManager.CreateDbContext();
        var adminRole = await db.Roles.FirstAsync(r => r.Name == "Administrator");
        var adminUser = db.Users.FirstOrDefault(u => u.Username == "admin");
        if (adminUser == null)
        {
            adminUser = new User
            {
                Id = Guid.NewGuid(),
                Username = "admin",
                Email = "admin@test.com",
                PasswordHash = PasswordHelper.HashPassword("admin"),
                RoleId = adminRole.Id,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.Users.Add(adminUser);
            await db.SaveChangesAsync();
        }

        var log = new AccessLog
        {
            Id = Guid.NewGuid(),
            UserId = adminUser.Id,
            Action = action,
            EntityType = entityType,
            Details = details,
            Timestamp = timestamp ?? DateTimeOffset.UtcNow
        };
        db.AccessLogs.Add(log);
        await db.SaveChangesAsync();
    }
}
