using System.Text.Json;
using Adam.Shared.Data;
using Adam.Shared.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.Shared.Tests.Services;

/// <summary>
/// Unit tests for <see cref="UserPreferenceService"/> panel persistence round-trips.
/// Verifies that the <c>metadata.expandedPanels</c> key with a <c>HashSet&lt;string&gt;</c>
/// value survives SetAsync → GetAsync cycles through SQLite.
/// </summary>
public sealed class UserPreferenceServiceTests : IAsyncLifetime
{
    private const string ExpandedPanelsKey = "metadata.expandedPanels";

    private readonly string _basePath;
    private readonly ModeManager _modeManager;
    private UserPreferenceService _service = null!;

    public UserPreferenceServiceTests()
    {
        _basePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _modeManager = new ModeManager(_basePath);
    }

    public async Task InitializeAsync()
    {
        await _modeManager.InitializeAsync();
        var connStr = _modeManager.CreateDbContext().Database.GetDbConnection().ConnectionString;
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connStr)
            .Options;
        var factory = new SimpleDbContextFactory(options);
        _service = new UserPreferenceService(factory, NullLogger<UserPreferenceService>.Instance);
    }

    public async Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_basePath))
                Directory.Delete(_basePath, recursive: true);
        }
        catch (IOException) { }
    }

    // ═══════════════════════════════════════════════════════════
    //  HashSet<string> round-trip
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task SetAsync_HashSet_RoundTrips()
    {
        var panels = new HashSet<string> { "folders", "keywords", "A", "B", "C" };

        await _service.SetAsync(ExpandedPanelsKey, panels);
        var loaded = await _service.GetAsync<HashSet<string>>(ExpandedPanelsKey);

        loaded.Should().NotBeNull();
        loaded!.Should().BeEquivalentTo(panels);
    }

    [Fact]
    public async Task SetAsync_EmptySet_RoundTrips()
    {
        var panels = new HashSet<string>();

        await _service.SetAsync(ExpandedPanelsKey, panels);
        var loaded = await _service.GetAsync<HashSet<string>>(ExpandedPanelsKey);

        loaded.Should().NotBeNull();
        loaded.Should().BeEmpty();
    }

    [Fact]
    public async Task SetAsync_SingleElement_RoundTrips()
    {
        var panels = new HashSet<string> { "A" };

        await _service.SetAsync(ExpandedPanelsKey, panels);
        var loaded = await _service.GetAsync<HashSet<string>>(ExpandedPanelsKey);

        loaded.Should().NotBeNull();
        loaded.Should().ContainSingle("A");
    }

    [Fact]
    public async Task SetAsync_MetadataEditorOnly_RoundTrips()
    {
        var panels = new HashSet<string> { "A", "B", "C", "D", "E", "F", "G", "H" };

        await _service.SetAsync(ExpandedPanelsKey, panels);
        var loaded = await _service.GetAsync<HashSet<string>>(ExpandedPanelsKey);

        loaded.Should().NotBeNull();
        loaded.Should().BeEquivalentTo(panels);
    }

    [Fact]
    public async Task SetAsync_SidebarOnly_RoundTrips()
    {
        var panels = new HashSet<string>
        {
            "folders", "collections", "savedSearches", "recentSearches",
            "keywords", "mediaFormat", "categories", "dateTaken",
            "rating", "label", "flag", "aiModel",
            "metadata", "comments", "tags"
        };

        await _service.SetAsync(ExpandedPanelsKey, panels);
        var loaded = await _service.GetAsync<HashSet<string>>(ExpandedPanelsKey);

        loaded.Should().NotBeNull();
        loaded.Should().BeEquivalentTo(panels);
    }

    // ═══════════════════════════════════════════════════════════
    //  Overwrite
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task SetAsync_OverwriteReplacesValue()
    {
        var first = new HashSet<string> { "A", "B" };
        var second = new HashSet<string> { "C", "D" };

        await _service.SetAsync(ExpandedPanelsKey, first);
        await _service.SetAsync(ExpandedPanelsKey, second);
        var loaded = await _service.GetAsync<HashSet<string>>(ExpandedPanelsKey);

        loaded.Should().NotBeNull();
        loaded.Should().BeEquivalentTo(second);
        loaded.Should().NotContain("A");
    }

    // ═══════════════════════════════════════════════════════════
    //  Reset + GetOrDefault
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task ResetAsync_RemovesKey()
    {
        await _service.SetAsync(ExpandedPanelsKey, new HashSet<string> { "A" });
        await _service.ResetAsync(ExpandedPanelsKey);

        var loaded = await _service.GetAsync<HashSet<string>>(ExpandedPanelsKey);
        loaded.Should().BeNull();
    }

    [Fact]
    public async Task GetOrDefaultAsync_WhenMissing_ReturnsDefault()
    {
        var result = await _service.GetOrDefaultAsync(ExpandedPanelsKey, new HashSet<string> { "default" });
        result.Should().BeEquivalentTo(new HashSet<string> { "default" });
    }

    [Fact]
    public async Task GetOrDefaultAsync_WhenPresent_ReturnsSavedValue()
    {
        await _service.SetAsync(ExpandedPanelsKey, new HashSet<string> { "saved" });

        var result = await _service.GetOrDefaultAsync(ExpandedPanelsKey, new HashSet<string> { "default" });
        result.Should().BeEquivalentTo(new HashSet<string> { "saved" });
    }

    // ═══════════════════════════════════════════════════════════
    //  Serialization format
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task PersistentStorage_UsesJsonArrayFormat()
    {
        var panels = new HashSet<string> { "folders", "keywords", "A" };
        await _service.SetAsync(ExpandedPanelsKey, panels);

        // Read raw JSON from the DB to verify format
        await using var db = _modeManager.CreateDbContext();
        var pref = await db.UserPreferences
            .Where(p => p.Key == ExpandedPanelsKey)
            .FirstOrDefaultAsync();

        pref.Should().NotBeNull();
        pref!.ValueJson.Should().Be("[\r\n  \"folders\",\r\n  \"keywords\",\r\n  \"A\"\r\n]");
    }

    // ═══════════════════════════════════════════════════════════
    //  LoadAsync cache hydration
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task LoadAsync_PopulatesCache_AndGetReturnsCachedValue()
    {
        // Seed directly via SetAsync
        var original = new HashSet<string> { "A", "B" };
        await _service.SetAsync(ExpandedPanelsKey, original);

        // Create a fresh service (empty cache)
        var connStr = _modeManager.CreateDbContext().Database.GetDbConnection().ConnectionString;
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connStr)
            .Options;
        var factory = new SimpleDbContextFactory(options);
        var freshService = new UserPreferenceService(factory, NullLogger<UserPreferenceService>.Instance);

        // LoadAsync hydrates cache, then GetAsync reads from cache
        await freshService.LoadAsync();
        var loaded = await freshService.GetAsync<HashSet<string>>(ExpandedPanelsKey);

        loaded.Should().NotBeNull();
        loaded!.Should().BeEquivalentTo(original);
    }

    [Fact]
    public async Task LoadAsync_IsIdempotent()
    {
        await _service.SetAsync(ExpandedPanelsKey, new HashSet<string> { "A" });
        await _service.LoadAsync();
        await _service.LoadAsync(); // second call should be no-op via _loaded guard

        var loaded = await _service.GetAsync<HashSet<string>>(ExpandedPanelsKey);
        loaded.Should().ContainSingle("A");
    }
}
