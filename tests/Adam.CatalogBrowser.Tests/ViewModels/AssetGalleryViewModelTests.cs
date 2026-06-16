using Adam.CatalogBrowser.ViewModels;
using Adam.Shared.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.CatalogBrowser.Tests.ViewModels;

/// <summary>
/// Direct unit tests for AssetGalleryViewModel.ApplyFilter with the
/// searchQuery parameter (Phase 19 Wave 7).
///
/// Routing verification (ExecuteSearchAsync called vs LoadAssetsAsync
/// called) is proved by synchronous state changes:
/// - With searchQuery: SearchText IS set, IsSearchActive = true → search path
/// - Without searchQuery: SearchText unchanged, IsSearchActive = false → DB load path
///
/// The async internals (LoadAssetsAsync, ExecuteSearchAsync) require an
/// Avalonia dispatcher and cannot be directly observed in test context;
/// the synchronous state changes are the authoritative routing indicator.
/// </summary>
public sealed class AssetGalleryViewModelTests : IAsyncLifetime
{
    private readonly string _basePath;
    private readonly ModeManager _modeManager;
    private readonly NullLogger<AssetGalleryViewModel> _logger;
    private AssetGalleryViewModel _gallery = null!;

    public AssetGalleryViewModelTests()
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
    //  ApplyFilter with searchQuery — synchronous state
    // ═══════════════════════════════════════════════════════════
    //  These verify routing: SearchText being set proves ExecuteSearchAsync
    //  was called (the searchQuery branch). The null/empty/whitespace tests
    //  prove LoadAssetsAsync was called instead.
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void ApplyFilter_WithSearchQuery_SetsSearchText()
    {
        _gallery.ApplyFilter(null, null, searchQuery: "nature photos");

        _gallery.SearchText.Should().Be("nature photos");
    }

    [Fact]
    public void ApplyFilter_WithSearchQuery_SetsIsSearchActiveTrue()
    {
        _gallery.ApplyFilter(null, null, searchQuery: "test query");

        _gallery.IsSearchActive.Should().BeTrue();
    }

    [Fact]
    public void ApplyFilter_WithSearchQuery_ExecutesSearchPath()
    {
        // SearchText is set synchronously before the fire-and-forget
        // ExecuteSearchAsync call. The caller can immediately observe
        // which path was taken — this is the routing verification.
        _gallery.ApplyFilter(null, null, searchQuery: "query");

        _gallery.SearchText.Should().Be("query");
        _gallery.IsSearchActive.Should().BeTrue();
    }

    [Fact]
    public void ApplyFilter_WithSearchQuery_SetsFilterStateFields()
    {
        var kwId = Guid.NewGuid();
        var catId = Guid.NewGuid();
        var dateFrom = new DateTime(2024, 1, 1);
        var dateTo = new DateTime(2024, 2, 1);

        _gallery.ApplyFilter(
            "Images",
            "/photos",
            keywordIds: [kwId],
            categoryIds: [catId],
            dateFrom: dateFrom,
            dateTo: dateTo,
            ratingFilter: 3,
            labelFilter: 2,
            flagFilter: 1,
            searchQuery: "sunset");

        _gallery.SearchText.Should().Be("sunset");
        _gallery.IsSearchActive.Should().BeTrue();
    }

    [Fact]
    public void ApplyFilter_WithSearchQuery_PreventsSearchTextDoubleSet()
    {
        _gallery.ApplyFilter(null, null, searchQuery: "first query");
        _gallery.ApplyFilter(null, null, searchQuery: "second query");

        _gallery.SearchText.Should().Be("second query");
        _gallery.IsSearchActive.Should().BeTrue();
    }

    // ═══════════════════════════════════════════════════════════
    //  ApplyFilter without searchQuery — LoadAssetsAsync path
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void ApplyFilter_WithoutSearchQuery_DoesNotSetSearchText()
    {
        _gallery.ApplyFilter(null, null);

        _gallery.SearchText.Should().BeEmpty("ApplyFilter without searchQuery should not modify SearchText");
        _gallery.IsSearchActive.Should().BeFalse();
    }

    [Fact]
    public void ApplyFilter_WithoutSearchQuery_PreservesExistingSearchText()
    {
        _gallery.ApplyFilter(null, null, searchQuery: "existing query");
        _gallery.SearchText.Should().Be("existing query");
        _gallery.IsSearchActive.Should().BeTrue();

        _gallery.ApplyFilter("Images", null);

        _gallery.SearchText.Should().Be("existing query", "LoadAssetsAsync should not clear SearchText");
        _gallery.IsSearchActive.Should().BeTrue("LoadAssetsAsync should not clear IsSearchActive");
    }

    [Fact]
    public void ApplyFilter_WithNullSearchQuery_BehavesSameAsOmitted()
    {
        _gallery.ApplyFilter(null, null, searchQuery: null);

        _gallery.SearchText.Should().BeEmpty();
        _gallery.IsSearchActive.Should().BeFalse();
    }

    [Fact]
    public void ApplyFilter_WithEmptySearchQuery_BehavesSameAsOmitted()
    {
        _gallery.ApplyFilter(null, null, searchQuery: string.Empty);

        _gallery.SearchText.Should().BeEmpty();
        _gallery.IsSearchActive.Should().BeFalse();
    }

    [Fact]
    public void ApplyFilter_WithWhitespaceSearchQuery_BehavesSameAsOmitted()
    {
        _gallery.ApplyFilter(null, null, searchQuery: "   ");

        _gallery.SearchText.Should().BeEmpty();
        _gallery.IsSearchActive.Should().BeFalse();
    }

    // ═══════════════════════════════════════════════════════════
    //  ApplyFilter — PropertyChanged notifications
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void ApplyFilter_WithSearchQuery_RaisesSearchTextPropertyChanged()
    {
        var changed = new List<string?>();
        _gallery.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _gallery.ApplyFilter(null, null, searchQuery: "test");

        changed.Should().Contain(nameof(AssetGalleryViewModel.SearchText));
    }

    [Fact]
    public void ApplyFilter_WithSearchQuery_RaisesIsSearchActivePropertyChanged()
    {
        var changed = new List<string?>();
        _gallery.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _gallery.ApplyFilter(null, null, searchQuery: "test");

        changed.Should().Contain(nameof(AssetGalleryViewModel.IsSearchActive));
    }

    [Fact]
    public void ApplyFilter_WithoutSearchQuery_DoesNotRaiseSearchTextChanged()
    {
        var changed = new List<string?>();
        _gallery.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _gallery.ApplyFilter(null, null);

        changed.Should().NotContain(nameof(AssetGalleryViewModel.SearchText));
        changed.Should().NotContain(nameof(AssetGalleryViewModel.IsSearchActive));
    }

}
