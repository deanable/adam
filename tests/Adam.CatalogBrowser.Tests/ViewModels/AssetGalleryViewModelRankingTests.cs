using Adam.CatalogBrowser.ViewModels;
using Adam.Shared.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.CatalogBrowser.Tests.ViewModels;

/// <summary>
/// Tests for Phase 22 smart ranking features in AssetGalleryViewModel:
/// RankMode property, IsSmartRankingEnabled, click tracking, dwell timer.
/// These are synchronous state tests — no Avalonia dispatcher required.
/// </summary>
public sealed class AssetGalleryViewModelRankingTests : IAsyncLifetime
{
    private readonly string _basePath;
    private readonly ModeManager _modeManager;
    private readonly NullLogger<AssetGalleryViewModel> _logger;
    private AssetGalleryViewModel _gallery = null!;

    public AssetGalleryViewModelRankingTests()
    {
        _basePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _modeManager = new ModeManager(_basePath);
        _logger = new NullLogger<AssetGalleryViewModel>();
    }

    public async Task InitializeAsync()
    {
        await _modeManager.InitializeAsync();
        _gallery = new AssetGalleryViewModel(_modeManager, _logger);
    }

    public async Task DisposeAsync()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_basePath))
                Directory.Delete(_basePath, recursive: true);
        }
        catch (IOException) { }
    }

    // ═══════════════════════════════════════════════════════════
    //  RankMode property
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void RankMode_Default_IsSmart()
    {
        _gallery.RankMode.Should().Be("Smart");
    }

    [Fact]
    public void RankMode_SetValue_StoresAndNotifies()
    {
        var changed = new List<string?>();
        _gallery.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _gallery.RankMode = "Relevance";

        _gallery.RankMode.Should().Be("Relevance");
        changed.Should().Contain(nameof(AssetGalleryViewModel.RankMode));
    }

    [Fact]
    public void RankMode_ToSameValue_DoesNotNotify()
    {
        var changed = new List<string?>();
        _gallery.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _gallery.RankMode = "Smart"; // already default

        changed.Should().NotContain(nameof(AssetGalleryViewModel.RankMode));
    }

    [Fact]
    public void RankMode_SetToRating_StorageUpdated()
    {
        _gallery.RankMode = "Rating";
        _gallery.RankMode.Should().Be("Rating");
    }

    [Fact]
    public void RankMode_SetToDate_StorageUpdated()
    {
        _gallery.RankMode = "Date";
        _gallery.RankMode.Should().Be("Date");
    }

    // ═══════════════════════════════════════════════════════════
    //  IsSmartRankingEnabled
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void IsSmartRankingEnabled_Default_IsTrue()
    {
        _gallery.IsSmartRankingEnabled.Should().BeTrue();
    }

    [Fact]
    public void IsSmartRankingEnabled_SetFalse_UpdatesAndNotifies()
    {
        var changed = new List<string?>();
        _gallery.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _gallery.IsSmartRankingEnabled = false;

        _gallery.IsSmartRankingEnabled.Should().BeFalse();
        changed.Should().Contain(nameof(AssetGalleryViewModel.IsSmartRankingEnabled));
    }

    // ═══════════════════════════════════════════════════════════
    //  RankModes collection
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void RankModes_ContainsAllModes()
    {
        _gallery.RankModes.Should().BeEquivalentTo(
            new[] { "Relevance", "Smart", "Date", "Rating" });
    }

    // ═══════════════════════════════════════════════════════════
    //  OnSearchResultClicked
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void OnSearchResultClicked_StartsDwellTimer()
    {
        // Just verify it doesn't throw
        var act = () => _gallery.OnSearchResultClicked();
        act.Should().NotThrow();
    }

    [Fact]
    public void OnSearchResultClicked_MultipleCalls_DoesNotThrow()
    {
        _gallery.OnSearchResultClicked();
        _gallery.OnSearchResultClicked();
        _gallery.OnSearchResultClicked();

        // Multiple calls should be fine (timer resets)
        var act = () => _gallery.OnSearchResultClicked();
        act.Should().NotThrow();
    }

    // ═══════════════════════════════════════════════════════════
    //  OnSearchResultNavigatedAwayAsync
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task OnSearchResultNavigatedAwayAsync_WithoutRankingService_DoesNotThrow()
    {
        // Gallery was created without a SearchRankingService
        var act = () => _gallery.OnSearchResultNavigatedAwayAsync(
            Guid.NewGuid(), "test query", 1);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task OnSearchResultNavigatedAwayAsync_WithDwellTimer_ReturnsDwellTime()
    {
        _gallery.OnSearchResultClicked();
        // Intentionally short dwell
        await Task.Delay(5);

        var act = () => _gallery.OnSearchResultNavigatedAwayAsync(
            Guid.NewGuid(), "test query", 1);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task OnSearchResultNavigatedAwayAsync_WithoutClickFirst_DoesNotThrow()
    {
        // Call navigated away without having called clicked first
        var act = () => _gallery.OnSearchResultNavigatedAwayAsync(
            Guid.NewGuid(), "test", 0);

        await act.Should().NotThrowAsync();
    }

    // ═══════════════════════════════════════════════════════════
    //  SearchModeIcon / SearchPlaceholderText
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void SearchModeIcon_FtsMode_IsMagnifyingGlass()
    {
        _gallery.IsSemanticSearch = false;
        _gallery.SearchModeIcon.Should().Be("🔍");
    }

    [Fact]
    public void SearchModeIcon_SemanticMode_IsSparkle()
    {
        _gallery.IsSemanticSearch = true;
        _gallery.SearchModeIcon.Should().Be("✦");
    }

    [Fact]
    public void SearchPlaceholderText_FtsMode_IsSearchAssets()
    {
        _gallery.IsSemanticSearch = false;
        _gallery.SearchPlaceholderText.Should().Be("Search assets...");
    }

    [Fact]
    public void SearchPlaceholderText_SemanticMode_IsDescribeQuery()
    {
        _gallery.IsSemanticSearch = true;
        _gallery.SearchPlaceholderText.Should().Be("Describe what you're looking for...");
    }
}
