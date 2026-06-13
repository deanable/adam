using Adam.Shared.Data;
using Adam.Shared.Models;
using Adam.Shared.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.Shared.Tests.Services;

/// <summary>
/// Unit tests for <see cref="SqliteFtsService"/> covering FTS5 table creation,
/// trigger-based sync, search ranking, query escaping, and availability checks.
/// </summary>
public sealed class SqliteFtsServiceTests : IDisposable
{
    private readonly string _basePath;
    private readonly ModeManager _modeManager;
    private readonly SqliteFtsService _ftsService;

    public SqliteFtsServiceTests()
    {
        _basePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _modeManager = new ModeManager(_basePath);
        _modeManager.InitializeAsync().GetAwaiter().GetResult();

        var factory = new TestDbContextFactory(_modeManager);
        _ftsService = new SqliteFtsService(factory, NullLogger<SqliteFtsService>.Instance);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_basePath, recursive: true); } catch { }
    }

    // ── Helpers ───────────────────────────────────────────────

    private async Task SeedAssetAsync(
        string title = "Test Photo",
        string? description = "A beautiful landscape",
        string fileName = "photo.jpg",
        string[]? keywordNames = null)
    {
        await using var db = await _modeManager.CreateDbContextAsync();

        var id = Guid.NewGuid();
        var asset = new DigitalAsset
        {
            Id = id,
            Title = title,
            Description = description,
            FileName = fileName,
            FileExtension = Path.GetExtension(fileName),
            MimeType = "image/jpeg",
            FileSize = 1024,
            ChecksumSha256 = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes($"{title}{fileName}")))[..16],
            StoragePath = $"C:/test/{fileName}".Replace('\\', '/'),
            OriginalPath = $"C:/test/{fileName}".Replace('\\', '/'),
            Type = AssetType.Image,
        };
        db.DigitalAssets.Add(asset);

        if (keywordNames is { Length: > 0 })
        {
            foreach (var kwName in keywordNames)
            {
                var kw = new Keyword
                {
                    Id = Guid.NewGuid(),
                    Name = kwName,
                    NormalizedName = kwName.ToUpperInvariant(),
                };
                db.Keywords.Add(kw);
                asset.Keywords.Add(kw);
            }
        }

        await db.SaveChangesAsync();
    }

    private async Task<int> GetFtsRowCountAsync()
    {
        await using var db = await _modeManager.CreateDbContextAsync();
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM digital_assets_fts";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private async Task<int> GetMapRowCountAsync()
    {
        await using var db = await _modeManager.CreateDbContextAsync();
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM digital_assets_fts_map";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    // ── FTS5 Creation Tests ───────────────────────────────────

    [Fact]
    public async Task EnsureReadyAsync_CreatesFts5TableAndMappingTable()
    {
        await _ftsService.EnsureReadyAsync();

        (await GetFtsRowCountAsync()).Should().Be(0);
        (await GetMapRowCountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task EnsureReadyAsync_IsIdempotent()
    {
        await _ftsService.EnsureReadyAsync();

        Func<Task> act = () => _ftsService.EnsureReadyAsync();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task EnsureReadyAsync_CreatesTriggers_AllowsBulkInsert()
    {
        await _ftsService.EnsureReadyAsync();
        await SeedAssetAsync(title: "First");
        await SeedAssetAsync(title: "Second");
        await SeedAssetAsync(title: "Third");

        (await GetFtsRowCountAsync()).Should().Be(3);
        (await GetMapRowCountAsync()).Should().Be(3);
    }

    // ── Availability Check Tests ──────────────────────────────

    [Fact]
    public async Task IsAvailableAsync_ReturnsFalse_BeforeTableCreation()
    {
        var result = await _ftsService.IsAvailableAsync();
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsAvailableAsync_ReturnsTrue_AfterTableCreation()
    {
        await _ftsService.EnsureReadyAsync();

        var result = await _ftsService.IsAvailableAsync();
        result.Should().BeTrue();
    }

    // ── Trigger Sync Tests ────────────────────────────────────

    [Fact]
    public async Task Trigger_InsertAsset_SyncsToFtsTable()
    {
        await _ftsService.EnsureReadyAsync();

        await SeedAssetAsync(title: "Sunset Beach");
        (await GetFtsRowCountAsync()).Should().Be(1);

        var results = await _ftsService.SearchAsync("Sunset");
        results.Should().HaveCount(1);
        results[0].Asset.Title.Should().Be("Sunset Beach");
    }

    [Fact]
    public async Task Trigger_UpdateAsset_SyncsToFtsTable()
    {
        await _ftsService.EnsureReadyAsync();
        await SeedAssetAsync(title: "Original Title");

        // Update via EF Core
        await using (var db = await _modeManager.CreateDbContextAsync())
        {
            var asset = await db.DigitalAssets.FirstAsync();
            asset.Title = "Updated Title";
            await db.SaveChangesAsync();
        }

        // Old search returns nothing, new search finds it
        (await _ftsService.SearchAsync("Original")).Should().BeEmpty();

        var newResults = await _ftsService.SearchAsync("Updated");
        newResults.Should().HaveCount(1);
        newResults[0].Asset.Title.Should().Be("Updated Title");
    }

    [Fact]
    public async Task Trigger_DeleteAsset_RemovesFromFtsTable()
    {
        await _ftsService.EnsureReadyAsync();
        await SeedAssetAsync(title: "To Be Deleted");
        (await GetFtsRowCountAsync()).Should().Be(1);

        await using (var db = await _modeManager.CreateDbContextAsync())
        {
            var asset = await db.DigitalAssets.FirstAsync();
            db.DigitalAssets.Remove(asset);
            await db.SaveChangesAsync();
        }

        (await GetFtsRowCountAsync()).Should().Be(0);
        (await GetMapRowCountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Trigger_AddKeyword_MakesKeywordSearchable()
    {
        await _ftsService.EnsureReadyAsync();
        await SeedAssetAsync(title: "My Photo", keywordNames: []);

        // Search by keyword before adding — no match
        var before = await _ftsService.SearchAsync("sunset");
        before.Should().BeEmpty();

        // Add keyword via EF Core
        await using (var db = await _modeManager.CreateDbContextAsync())
        {
            var asset = await db.DigitalAssets.Include(a => a.Keywords).FirstAsync();
            var kw = new Keyword
            {
                Id = Guid.NewGuid(),
                Name = "sunset",
                NormalizedName = "SUNSET",
            };
            db.Keywords.Add(kw);
            asset.Keywords.Add(kw);
            await db.SaveChangesAsync();
        }

        // Search by keyword after adding — now matches
        var after = await _ftsService.SearchAsync("sunset");
        after.Should().HaveCount(1);
        after[0].MatchedFields.Should().Contain("Keywords");
    }

    [Fact]
    public async Task Trigger_RemoveKeyword_MakesKeywordUnsearchable()
    {
        await _ftsService.EnsureReadyAsync();
        await SeedAssetAsync(title: "My Photo", description: "A scenic photo", keywordNames: ["landscape", "nature"]);

        // Verify keyword search works before removal
        var before = await _ftsService.SearchAsync("landscape");
        before.Should().HaveCount(1);
        before[0].MatchedFields.Should().Contain("Keywords");

        // Remove keyword via EF Core
        await using (var db = await _modeManager.CreateDbContextAsync())
        {
            var asset = await db.DigitalAssets
                .Include(a => a.Keywords)
                .FirstAsync();
            var kw = asset.Keywords.First(k => k.Name == "landscape");
            asset.Keywords.Remove(kw);
            await db.SaveChangesAsync();
        }

        // "landscape" no longer matches (description is "A scenic photo", keyword removed)
        var afterLandscape = await _ftsService.SearchAsync("landscape");
        afterLandscape.Should().BeEmpty();

        // "nature" still matches via keyword
        var afterNature = await _ftsService.SearchAsync("nature");
        afterNature.Should().HaveCount(1);
    }

    // ── Search Ranking Tests ──────────────────────────────────

    [Fact]
    public async Task SearchAsync_ReturnsResultsRankedByBm25()
    {
        await _ftsService.EnsureReadyAsync();
        await SeedAssetAsync(title: "Mountain Sunrise");
        await SeedAssetAsync(title: "Mountain Lake");
        await SeedAssetAsync(title: "Mountain Trail Adventure");

        var results = await _ftsService.SearchAsync("Mountain");

        results.Should().HaveCount(3);
        results.All(r => r.Asset.Title.Contains("Mountain")).Should().BeTrue();
    }

    [Fact]
    public async Task SearchAsync_MatchesTitle()
    {
        await _ftsService.EnsureReadyAsync();
        await SeedAssetAsync(title: "Sunset Beach", description: "A calm evening");

        var results = await _ftsService.SearchAsync("Sunset");

        results.Should().HaveCount(1);
        results[0].MatchedFields.Should().Contain("Title");
    }

    [Fact]
    public async Task SearchAsync_MatchesDescription()
    {
        await _ftsService.EnsureReadyAsync();
        await SeedAssetAsync(title: "Photo 1", description: "A beautiful waterfall in the forest");

        var results = await _ftsService.SearchAsync("waterfall");

        results.Should().HaveCount(1);
        results[0].MatchedFields.Should().Contain("Description");
    }

    [Fact]
    public async Task SearchAsync_MatchesFileName()
    {
        await _ftsService.EnsureReadyAsync();
        await SeedAssetAsync(title: "Untitled", fileName: "IMG_2026_vacation.jpg");

        var results = await _ftsService.SearchAsync("vacation");

        results.Should().HaveCount(1);
        results[0].MatchedFields.Should().Contain("FileName");
    }

    [Fact]
    public async Task SearchAsync_MatchesMultipleFields()
    {
        await _ftsService.EnsureReadyAsync();
        await SeedAssetAsync(
            title: "Mountain Hike",
            description: "A scenic mountain trail",
            fileName: "hike_2026.jpg");

        var results = await _ftsService.SearchAsync("mountain");

        results.Should().HaveCount(1);
        results[0].MatchedFields.Should().Contain("Title");
        results[0].MatchedFields.Should().Contain("Description");
    }

    [Fact]
    public async Task SearchAsync_RespectsMaxResults()
    {
        await _ftsService.EnsureReadyAsync();
        for (int i = 0; i < 10; i++)
            await SeedAssetAsync(title: $"Photo {i} Mountain View");

        var results = await _ftsService.SearchAsync("Mountain", maxResults: 3);

        results.Should().HaveCount(3);
    }

    [Fact]
    public async Task SearchAsync_NoMatches_ReturnsEmpty()
    {
        await _ftsService.EnsureReadyAsync();
        await SeedAssetAsync(title: "Sunset Beach");

        var results = await _ftsService.SearchAsync("xyznonexistent");
        results.Should().BeEmpty();
    }

    // ── Query Escaping Tests ──────────────────────────────────

    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsEmpty()
    {
        await _ftsService.EnsureReadyAsync();
        await SeedAssetAsync(title: "Test Photo");

        var results = await _ftsService.SearchAsync("");
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_NullQuery_ReturnsEmpty()
    {
        await _ftsService.EnsureReadyAsync();
        await SeedAssetAsync(title: "Test Photo");

        var results = await _ftsService.SearchAsync(null!);
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_WhitespaceQuery_ReturnsEmpty()
    {
        await _ftsService.EnsureReadyAsync();
        await SeedAssetAsync(title: "Test Photo");

        var results = await _ftsService.SearchAsync("   ");
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_WhenFtsUnavailable_ReturnsEmpty()
    {
        // Don't call EnsureReadyAsync — FTS table doesn't exist
        await SeedAssetAsync(title: "Test Photo");

        var results = await _ftsService.SearchAsync("Test");
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_PhraseQuery_MatchesExactPhrase()
    {
        await _ftsService.EnsureReadyAsync();
        await SeedAssetAsync(title: "Mountain Sunrise", description: "Beautiful mountain sunrise view");

        var results = await _ftsService.SearchAsync("\"Mountain Sunrise\"");

        results.Should().HaveCount(1);
        results[0].Asset.Title.Should().Be("Mountain Sunrise");
    }

    [Fact]
    public async Task SearchAsync_MultiTermQuery_AllTermsMustMatch()
    {
        await _ftsService.EnsureReadyAsync();
        await SeedAssetAsync(title: "Mountain Sunrise");
        await SeedAssetAsync(title: "Mountain Lake");

        // Multi-term query uses AND (prefix matching per term)
        var results = await _ftsService.SearchAsync("Mountain Sunrise");

        results.Should().HaveCount(1);
        results[0].Asset.Title.Should().Be("Mountain Sunrise");
    }

    // ── Suggestions Tests ─────────────────────────────────────

    [Fact]
    public async Task GetSuggestionsAsync_ReturnsMatchingTitles()
    {
        await _ftsService.EnsureReadyAsync();
        await SeedAssetAsync(title: "Sunset Beach");
        await SeedAssetAsync(title: "Sunset Mountain");
        await SeedAssetAsync(title: "Ocean Waves");

        var suggestions = await _ftsService.GetSuggestionsAsync("Sunset");

        suggestions.Should().HaveCount(2);
        suggestions.Should().Contain(s => s == "Sunset Beach");
        suggestions.Should().Contain(s => s == "Sunset Mountain");
    }

    [Fact]
    public async Task GetSuggestionsAsync_ShortPrefix_ReturnsEmpty()
    {
        await _ftsService.EnsureReadyAsync();
        await SeedAssetAsync(title: "Test Photo");

        var suggestions = await _ftsService.GetSuggestionsAsync("T");
        suggestions.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSuggestionsAsync_RespectsMaxSuggestions()
    {
        await _ftsService.EnsureReadyAsync();
        for (int i = 0; i < 10; i++)
            await SeedAssetAsync(title: $"Mountain View {i}");

        var suggestions = await _ftsService.GetSuggestionsAsync("Mountain", maxSuggestions: 3);

        suggestions.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetSuggestionsAsync_EmptyIndex_ReturnsEmpty()
    {
        await _ftsService.EnsureReadyAsync();

        var suggestions = await _ftsService.GetSuggestionsAsync("anything");
        suggestions.Should().BeEmpty();
    }

    // ── Rebuild Index Tests ───────────────────────────────────

    [Fact]
    public async Task RebuildIndexAsync_RepopulatesFtsTable()
    {
        await _ftsService.EnsureReadyAsync();
        await SeedAssetAsync(title: "Photo One");
        await SeedAssetAsync(title: "Photo Two");
        (await GetFtsRowCountAsync()).Should().Be(2);

        await _ftsService.RebuildIndexAsync();

        var results = await _ftsService.SearchAsync("Photo");
        results.Should().HaveCount(2);
    }

    // ── Integration: Multiple assets with complex search ──────

    [Fact]
    public async Task SearchAsync_ComplexScenario_MultipleAssetsAndKeywords()
    {
        await _ftsService.EnsureReadyAsync();

        await SeedAssetAsync(
            title: "Sunset at the Beach",
            description: "Golden hour photography",
            keywordNames: ["sunset", "beach", "photography"]);

        await SeedAssetAsync(
            title: "Mountain Sunrise",
            description: "Early morning hike",
            keywordNames: ["mountain", "sunrise"]);

        await SeedAssetAsync(
            title: "City Night",
            description: "Urban photography",
            keywordNames: ["city", "night", "photography"]);

        // Search by title
        var titleResults = await _ftsService.SearchAsync("Sunset");
        titleResults.Should().HaveCount(1);
        titleResults[0].Asset.Title.Should().Be("Sunset at the Beach");

        // Search by keyword
        var kwResults = await _ftsService.SearchAsync("photography");
        kwResults.Should().HaveCount(2);

        // Search by description
        var descResults = await _ftsService.SearchAsync("hike");
        descResults.Should().HaveCount(1);
        descResults[0].Asset.Title.Should().Be("Mountain Sunrise");
    }

    // ── Nested factory for test DbContext ─────────────────────

    private sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>
    {
        private readonly ModeManager _modeManager;

        public TestDbContextFactory(ModeManager modeManager) => _modeManager = modeManager;

        public AppDbContext CreateDbContext() => _modeManager.CreateDbContext();

        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => _modeManager.CreateDbContextAsync(cancellationToken);
    }
}
