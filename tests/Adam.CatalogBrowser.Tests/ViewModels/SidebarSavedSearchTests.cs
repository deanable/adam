using System.Reflection;
using Adam.CatalogBrowser.ViewModels;
using Adam.Shared.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.CatalogBrowser.Tests.ViewModels;

/// <summary>
/// Tests for Phase 19 sidebar saved search and recent search functionality —
/// filter state transitions, command CanExecute, property round-trips,
/// IsActiveFilter lifecycle, and static helper methods.
///
/// Uses a real SQLite database via ModeManager for constructor parity
/// with production. LoadAsync is not called (requires Dispatcher.UIThread);
/// tests verify the data model layer that doesn't require a pumping dispatcher.
///
/// <para>
/// Integration tests for the FilterChanged → ApplyFilter(searchQuery:) pipeline
/// (sidebar + MainWindowViewModel + gallery wiring) are in
/// <see cref="MainWindowViewModelTests"/>.
/// </para>
/// </summary>
public sealed class SidebarSavedSearchTests : IAsyncLifetime
{
    private readonly string _basePath;
    private readonly ModeManager _modeManager;
    private readonly NullLogger<SidebarViewModel> _logger;
    private SidebarViewModel _sidebar = null!;

    public SidebarSavedSearchTests()
    {
        _basePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _modeManager = new ModeManager(_basePath);
        _logger = new NullLogger<SidebarViewModel>();
    }

    public async Task InitializeAsync()
    {
        await _modeManager.InitializeAsync();
        _sidebar = new SidebarViewModel(_modeManager, _logger);
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
    //  FilterByThis — SavedSearchNode / SearchHistoryNode
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void FilterByThis_SavedSearchNode_SetsSelectedSavedSearch()
    {
        var ss = new SavedSearchNode { Name = "My Saved Search", SearchId = Guid.NewGuid(), QueryText = "nature" };
        _sidebar.FilterByThisCommand.Execute(ss);

        _sidebar.SelectedSavedSearch.Should().BeSameAs(ss);
    }

    [Fact]
    public void FilterByThis_SearchHistoryNode_SetsSelectedRecentSearch()
    {
        var rs = new SearchHistoryNode { QueryText = "sunset", EntryId = Guid.NewGuid(), ExecutedAt = DateTimeOffset.UtcNow };
        _sidebar.FilterByThisCommand.Execute(rs);

        _sidebar.SelectedRecentSearch.Should().BeSameAs(rs);
    }

    // ═══════════════════════════════════════════════════════════
    //  ClearFilter — SavedSearchNode / SearchHistoryNode
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void ClearFilter_SavedSearchNode_ClearsSelectedSavedSearch()
    {
        var ss = new SavedSearchNode { Name = "Test Search", SearchId = Guid.NewGuid() };
        _sidebar.SelectedSavedSearch = ss;

        _sidebar.ClearFilterCommand.Execute(ss);

        _sidebar.SelectedSavedSearch.Should().BeNull();
    }

    [Fact]
    public void ClearFilter_SearchHistoryNode_ClearsSelectedRecentSearch()
    {
        var rs = new SearchHistoryNode { QueryText = "test", EntryId = Guid.NewGuid(), ExecutedAt = DateTimeOffset.UtcNow };
        _sidebar.SelectedRecentSearch = rs;

        _sidebar.ClearFilterCommand.Execute(rs);

        _sidebar.SelectedRecentSearch.Should().BeNull();
    }

    // ═══════════════════════════════════════════════════════════
    //  ActiveSearchQueryText transitions
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void SelectedSavedSearch_Setter_WhenSet_UpdatesActiveSearchQueryText()
    {
        var ss = new SavedSearchNode { Name = "Saved", SearchId = Guid.NewGuid(), QueryText = "mountains" };

        _sidebar.SelectedSavedSearch = ss;

        _sidebar.ActiveSearchQueryText.Should().Be("mountains");
    }

    [Fact]
    public void SelectedSavedSearch_Setter_WhenCleared_ResetsActiveSearchQueryTextToNull()
    {
        var ss = new SavedSearchNode { Name = "Saved", SearchId = Guid.NewGuid(), QueryText = "mountains" };
        _sidebar.SelectedSavedSearch = ss;
        _sidebar.ActiveSearchQueryText.Should().Be("mountains");

        _sidebar.SelectedSavedSearch = null;

        _sidebar.ActiveSearchQueryText.Should().BeNull();
    }

    [Fact]
    public void SelectedRecentSearch_Setter_WhenSet_UpdatesActiveSearchQueryText()
    {
        var rs = new SearchHistoryNode { QueryText = "ocean", EntryId = Guid.NewGuid(), ExecutedAt = DateTimeOffset.UtcNow };

        _sidebar.SelectedRecentSearch = rs;

        _sidebar.ActiveSearchQueryText.Should().Be("ocean");
    }

    [Fact]
    public void SelectedRecentSearch_Setter_WhenCleared_ResetsActiveSearchQueryTextToNull()
    {
        var rs = new SearchHistoryNode { QueryText = "ocean", EntryId = Guid.NewGuid(), ExecutedAt = DateTimeOffset.UtcNow };
        _sidebar.SelectedRecentSearch = rs;
        _sidebar.ActiveSearchQueryText.Should().Be("ocean");

        _sidebar.SelectedRecentSearch = null;

        _sidebar.ActiveSearchQueryText.Should().BeNull();
    }

    [Fact]
    public void ActiveSearchQueryText_PrefersSavedSearch_OverRecentSearch()
    {
        var ss = new SavedSearchNode { Name = "Saved", SearchId = Guid.NewGuid(), QueryText = "preferred" };
        var rs = new SearchHistoryNode { QueryText = "other", EntryId = Guid.NewGuid(), ExecutedAt = DateTimeOffset.UtcNow };

        // Set recent search first, then saved search — saved should win
        _sidebar.SelectedRecentSearch = rs;
        _sidebar.SelectedSavedSearch = ss;

        _sidebar.ActiveSearchQueryText.Should().Be("preferred");
    }

    [Fact]
    public void ActiveSearchQueryText_WhenSwitchingFromSavedToKeyword_Clears()
    {
        var ss = new SavedSearchNode { Name = "Saved", SearchId = Guid.NewGuid(), QueryText = "saved-text" };
        _sidebar.SelectedSavedSearch = ss;
        _sidebar.ActiveSearchQueryText.Should().Be("saved-text");

        var kw = new KeywordNode { Name = "Nature", KeywordId = Guid.NewGuid() };
        _sidebar.SelectedKeyword = kw;

        _sidebar.ActiveSearchQueryText.Should().BeNull("switching to a keyword filter should clear the search query text");
    }

    // ═══════════════════════════════════════════════════════════
    //  ExecuteSavedSearchCommand / ExecuteRecentSearchCommand
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void ExecuteSavedSearchCommand_WithSavedSearchNode_SetsSelectedSavedSearch()
    {
        var ss = new SavedSearchNode { Name = "Execute Test", SearchId = Guid.NewGuid(), QueryText = "forest" };

        _sidebar.ExecuteSavedSearchCommand.Execute(ss);

        _sidebar.SelectedSavedSearch.Should().BeSameAs(ss);
        _sidebar.ActiveSearchQueryText.Should().Be("forest");
    }

    [Fact]
    public void ExecuteRecentSearchCommand_WithSearchHistoryNode_SetsSelectedRecentSearch()
    {
        var rs = new SearchHistoryNode { QueryText = "desert", EntryId = Guid.NewGuid(), ExecutedAt = DateTimeOffset.UtcNow };

        _sidebar.ExecuteRecentSearchCommand.Execute(rs);

        _sidebar.SelectedRecentSearch.Should().BeSameAs(rs);
        _sidebar.ActiveSearchQueryText.Should().Be("desert");
    }

    // ═══════════════════════════════════════════════════════════
    //  Command CanExecute
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void ExecuteSavedSearchCommand_CanExecute_NoSelection_ReturnsFalse()
    {
        _sidebar.ExecuteSavedSearchCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void ExecuteSavedSearchCommand_CanExecute_WhenSelectedSavedSearchIsSet_ReturnsTrue()
    {
        _sidebar.SelectedSavedSearch = new SavedSearchNode { Name = "Test", SearchId = Guid.NewGuid() };

        _sidebar.ExecuteSavedSearchCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void ExecuteRecentSearchCommand_CanExecute_NoSelection_ReturnsFalse()
    {
        _sidebar.ExecuteRecentSearchCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void ExecuteRecentSearchCommand_CanExecute_WhenSelectedRecentSearchIsSet_ReturnsTrue()
    {
        _sidebar.SelectedRecentSearch = new SearchHistoryNode { QueryText = "test", EntryId = Guid.NewGuid(), ExecutedAt = DateTimeOffset.UtcNow };

        _sidebar.ExecuteRecentSearchCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void DeleteSavedSearchCommand_CanExecute_NoSelection_ReturnsFalse()
    {
        _sidebar.DeleteSavedSearchCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void DeleteSavedSearchCommand_CanExecute_WhenSelected_ReturnsTrue()
    {
        _sidebar.SelectedSavedSearch = new SavedSearchNode { Name = "Del", SearchId = Guid.NewGuid() };
        _sidebar.DeleteSavedSearchCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void TogglePinSavedSearchCommand_CanExecute_NoSelection_ReturnsFalse()
    {
        _sidebar.TogglePinSavedSearchCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void TogglePinSavedSearchCommand_CanExecute_WhenSelected_ReturnsTrue()
    {
        _sidebar.SelectedSavedSearch = new SavedSearchNode { Name = "Pin", SearchId = Guid.NewGuid() };
        _sidebar.TogglePinSavedSearchCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void ClearRecentSearchesCommand_CanExecute_AlwaysReturnsTrue()
    {
        // ClearRecentSearchesCommand uses RelayCommand with no CanExecute predicate
        _sidebar.ClearRecentSearchesCommand.CanExecute(null).Should().BeTrue();
    }

    // ═══════════════════════════════════════════════════════════
    //  IsActiveFilter — SavedSearchNode
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void IsActiveFilter_Set_WhenSavedSearchSelected()
    {
        var ss = new SavedSearchNode { Name = "Active SS", SearchId = Guid.NewGuid() };

        _sidebar.SelectedSavedSearch = ss;

        ss.IsActiveFilter.Should().BeTrue();
    }

    [Fact]
    public void IsActiveFilter_Clears_WhenDifferentSavedSearchSelected()
    {
        var ss1 = new SavedSearchNode { Name = "SS1", SearchId = Guid.NewGuid() };
        var ss2 = new SavedSearchNode { Name = "SS2", SearchId = Guid.NewGuid() };

        _sidebar.SelectedSavedSearch = ss1;
        _sidebar.SelectedSavedSearch = ss2;

        ss1.IsActiveFilter.Should().BeFalse();
        ss2.IsActiveFilter.Should().BeTrue();
    }

    [Fact]
    public void IsActiveFilter_Clears_WhenSavedSearchDeselected()
    {
        var ss = new SavedSearchNode { Name = "SS", SearchId = Guid.NewGuid() };

        _sidebar.SelectedSavedSearch = ss;
        _sidebar.SelectedSavedSearch = null;

        ss.IsActiveFilter.Should().BeFalse();
    }

    // ═══════════════════════════════════════════════════════════
    //  IsActiveFilter — SearchHistoryNode
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void IsActiveFilter_Set_WhenRecentSearchSelected()
    {
        var rs = new SearchHistoryNode { QueryText = "beach", EntryId = Guid.NewGuid(), ExecutedAt = DateTimeOffset.UtcNow };

        _sidebar.SelectedRecentSearch = rs;

        rs.IsActiveFilter.Should().BeTrue();
    }

    [Fact]
    public void IsActiveFilter_Clears_WhenDifferentRecentSearchSelected()
    {
        var rs1 = new SearchHistoryNode { QueryText = "query1", EntryId = Guid.NewGuid(), ExecutedAt = DateTimeOffset.UtcNow.AddMinutes(-10) };
        var rs2 = new SearchHistoryNode { QueryText = "query2", EntryId = Guid.NewGuid(), ExecutedAt = DateTimeOffset.UtcNow };

        _sidebar.SelectedRecentSearch = rs1;
        _sidebar.SelectedRecentSearch = rs2;

        rs1.IsActiveFilter.Should().BeFalse();
        rs2.IsActiveFilter.Should().BeTrue();
    }

    [Fact]
    public void IsActiveFilter_Clears_WhenRecentSearchDeselected()
    {
        var rs = new SearchHistoryNode { QueryText = "query", EntryId = Guid.NewGuid(), ExecutedAt = DateTimeOffset.UtcNow };

        _sidebar.SelectedRecentSearch = rs;
        _sidebar.SelectedRecentSearch = null;

        rs.IsActiveFilter.Should().BeFalse();
    }

    [Fact]
    public void IsActiveFilter_Clears_WhenSwitchingFromRecentSearchToSavedSearch()
    {
        var rs = new SearchHistoryNode { QueryText = "recent", EntryId = Guid.NewGuid(), ExecutedAt = DateTimeOffset.UtcNow };
        var ss = new SavedSearchNode { Name = "Saved", SearchId = Guid.NewGuid(), QueryText = "saved" };

        _sidebar.SelectedRecentSearch = rs;
        _sidebar.SelectedSavedSearch = ss;

        rs.IsActiveFilter.Should().BeFalse("recent search should clear when saved search becomes active");
        ss.IsActiveFilter.Should().BeTrue();
    }

    // ═══════════════════════════════════════════════════════════
    //  ClearSavedSearchActiveStates / ClearRecentSearchActiveStates
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void ClearSavedSearchActiveStates_ClearsAllSavedSearchNodes()
    {
        // Add both pinned and unpinned saved searches to the sidebar's collection
        var ss1 = new SavedSearchNode { Name = "SS1", SearchId = Guid.NewGuid(), IsPinned = true };
        var ss2 = new SavedSearchNode { Name = "SS2", SearchId = Guid.NewGuid() };
        ss1.IsActiveFilter = true;
        ss2.IsActiveFilter = true;

        _sidebar.SavedSearches.Add(ss1);
        _sidebar.SavedSearches.Add(ss2);

        // Invoke the private ClearSavedSearchActiveStates method via reflection
        InvokeInstanceMethod("ClearSavedSearchActiveStates");

        ss1.IsActiveFilter.Should().BeFalse();
        ss2.IsActiveFilter.Should().BeFalse();
    }

    [Fact]
    public void ClearRecentSearchActiveStates_ClearsAllSearchHistoryNodes()
    {
        var rs1 = new SearchHistoryNode { QueryText = "q1", EntryId = Guid.NewGuid(), ExecutedAt = DateTimeOffset.UtcNow };
        var rs2 = new SearchHistoryNode { QueryText = "q2", EntryId = Guid.NewGuid(), ExecutedAt = DateTimeOffset.UtcNow };
        rs1.IsActiveFilter = true;
        rs2.IsActiveFilter = true;

        _sidebar.RecentSearches.Add(rs1);
        _sidebar.RecentSearches.Add(rs2);

        InvokeInstanceMethod("ClearRecentSearchActiveStates");

        rs1.IsActiveFilter.Should().BeFalse();
        rs2.IsActiveFilter.Should().BeFalse();
    }

    [Fact]
    public void ClearSavedSearchActiveStates_EmptyCollection_DoesNotThrow()
    {
        // SavedSearches starts empty — call should be a no-op
        var act = () => InvokeInstanceMethod("ClearSavedSearchActiveStates");
        act.Should().NotThrow();
    }

    [Fact]
    public void ClearRecentSearchActiveStates_EmptyCollection_DoesNotThrow()
    {
        var act = () => InvokeInstanceMethod("ClearRecentSearchActiveStates");
        act.Should().NotThrow();
    }

    // ═══════════════════════════════════════════════════════════
    //  ClearActiveFilterStates — includes saved/recent search
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void ClearActiveFilterStates_ClearsSavedAndRecentSearchStatesInFlatLists()
    {
        var ss = new SavedSearchNode { Name = "SS", SearchId = Guid.NewGuid() };
        var rs = new SearchHistoryNode { QueryText = "q", EntryId = Guid.NewGuid(), ExecutedAt = DateTimeOffset.UtcNow };
        ss.IsActiveFilter = true;
        rs.IsActiveFilter = true;
        _sidebar.SavedSearches.Add(ss);
        _sidebar.RecentSearches.Add(rs);

        // ClearActiveFilterStates is called internally by OnFilterChanged.
        // Simulate by setting a different filter (keyword) which triggers the full clear.
        var kw = new KeywordNode { Name = "Nature", KeywordId = Guid.NewGuid() };
        _sidebar.SelectedKeyword = kw;

        ss.IsActiveFilter.Should().BeFalse("ClearActiveFilterStates should clear saved search nodes");
        rs.IsActiveFilter.Should().BeFalse("ClearActiveFilterStates should clear recent search nodes");
    }

    // ═══════════════════════════════════════════════════════════
    //  SavedSearchNode property round-trips
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void SavedSearchNode_PropertyRoundTrips()
    {
        var node = new SavedSearchNode();

        node.SearchId = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
        node.Name = "My Saved Search";
        node.QueryText = "nature photos";
        node.IsPinned = false;

        node.SearchId.Should().Be(Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890"));
        node.Name.Should().Be("My Saved Search");
        node.QueryText.Should().Be("nature photos");
        node.IsPinned.Should().BeFalse();
    }

    [Fact]
    public void SavedSearchNode_PinIcon_WhenPinned_ReturnsPinEmoji()
    {
        var node = new SavedSearchNode { Name = "Test", SearchId = Guid.NewGuid(), IsPinned = true };

        node.PinIcon.Should().Be("\U0001F4CC");
    }

    [Fact]
    public void SavedSearchNode_PinIcon_WhenNotPinned_ReturnsEmpty()
    {
        var node = new SavedSearchNode { Name = "Test", SearchId = Guid.NewGuid(), IsPinned = false };

        node.PinIcon.Should().BeEmpty();
    }

    [Fact]
    public void SavedSearchNode_PinIcon_UpdatesOnIsPinnedChange()
    {
        var node = new SavedSearchNode { Name = "Test", SearchId = Guid.NewGuid(), IsPinned = false };
        node.PinIcon.Should().BeEmpty();

        node.IsPinned = true;

        node.PinIcon.Should().Be("\U0001F4CC");
    }

    [Fact]
    public void SavedSearchNode_IsActiveFilter_RaisesPropertyChanged()
    {
        var node = new SavedSearchNode { Name = "Test", SearchId = Guid.NewGuid() };
        var changed = new List<string?>();
        node.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        node.IsActiveFilter = true;

        changed.Should().Contain(nameof(SavedSearchNode.IsActiveFilter));
    }

    [Fact]
    public void SavedSearchNode_Name_RaisesPropertyChanged()
    {
        var node = new SavedSearchNode { SearchId = Guid.NewGuid() };
        var changed = new List<string?>();
        node.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        node.Name = "Updated";

        changed.Should().Contain(nameof(SavedSearchNode.Name));
    }

    // ═══════════════════════════════════════════════════════════
    //  SearchHistoryNode property round-trips
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void SearchHistoryNode_PropertyRoundTrips()
    {
        var node = new SearchHistoryNode();

        node.EntryId = Guid.Parse("b2c3d4e5-f6a7-8901-bcde-f12345678901");
        node.QueryText = "sunset landscape";
        var now = DateTimeOffset.UtcNow;
        node.ExecutedAt = now;

        node.EntryId.Should().Be(Guid.Parse("b2c3d4e5-f6a7-8901-bcde-f12345678901"));
        node.QueryText.Should().Be("sunset landscape");
        node.ExecutedAt.Should().Be(now);
    }

    [Fact]
    public void SearchHistoryNode_TimeAgo_JustNow()
    {
        var node = new SearchHistoryNode
        {
            QueryText = "test",
            EntryId = Guid.NewGuid(),
            ExecutedAt = DateTimeOffset.UtcNow.AddSeconds(-15)
        };

        node.TimeAgo.Should().Be("just now");
    }

    [Fact]
    public void SearchHistoryNode_TimeAgo_MinutesAgo()
    {
        var node = new SearchHistoryNode
        {
            QueryText = "test",
            EntryId = Guid.NewGuid(),
            ExecutedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        };

        node.TimeAgo.Should().Be("5 min ago");
    }

    [Fact]
    public void SearchHistoryNode_TimeAgo_HoursAgo()
    {
        var node = new SearchHistoryNode
        {
            QueryText = "test",
            EntryId = Guid.NewGuid(),
            ExecutedAt = DateTimeOffset.UtcNow.AddHours(-3)
        };

        node.TimeAgo.Should().Be("3h ago");
    }

    [Fact]
    public void SearchHistoryNode_TimeAgo_DaysAgo()
    {
        var node = new SearchHistoryNode
        {
            QueryText = "test",
            EntryId = Guid.NewGuid(),
            ExecutedAt = DateTimeOffset.UtcNow.AddDays(-2)
        };

        node.TimeAgo.Should().Be("2d ago");
    }

    [Fact]
    public void SearchHistoryNode_TimeAgo_OlderThanWeek_ShowsDate()
    {
        // Use a fixed date to avoid test timing issues
        var fixedDate = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);
        var node = new SearchHistoryNode
        {
            QueryText = "test",
            EntryId = Guid.NewGuid(),
            ExecutedAt = fixedDate
        };

        node.TimeAgo.Should().Be("Jan 15");
    }

    [Fact]
    public void SearchHistoryNode_ExecutedAt_Setter_UpdatesTimeAgo()
    {
        var node = new SearchHistoryNode
        {
            QueryText = "test",
            EntryId = Guid.NewGuid(),
            ExecutedAt = DateTimeOffset.UtcNow.AddMinutes(-30)
        };
        var initialTimeAgo = node.TimeAgo;
        initialTimeAgo.Should().Be("30 min ago");

        node.ExecutedAt = DateTimeOffset.UtcNow.AddMinutes(-1);

        node.TimeAgo.Should().Be("1 min ago");
    }

    [Fact]
    public void SearchHistoryNode_IsActiveFilter_RaisesPropertyChanged()
    {
        var node = new SearchHistoryNode { QueryText = "test", EntryId = Guid.NewGuid(), ExecutedAt = DateTimeOffset.UtcNow };
        var changed = new List<string?>();
        node.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        node.IsActiveFilter = true;

        changed.Should().Contain(nameof(SearchHistoryNode.IsActiveFilter));
    }

    [Fact]
    public void SearchHistoryNode_QueryText_RaisesPropertyChanged()
    {
        var node = new SearchHistoryNode { EntryId = Guid.NewGuid(), ExecutedAt = DateTimeOffset.UtcNow };
        var changed = new List<string?>();
        node.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        node.QueryText = "updated query";

        changed.Should().Contain(nameof(SearchHistoryNode.QueryText));
    }

    // ═══════════════════════════════════════════════════════════
    //  FilterChanged event
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void SelectedSavedSearch_Setter_RaisesFilterChanged()
    {
        var fired = false;
        _sidebar.FilterChanged += () => fired = true;

        var ss = new SavedSearchNode { Name = "Test", SearchId = Guid.NewGuid() };
        _sidebar.SelectedSavedSearch = ss;

        fired.Should().BeTrue();
    }

    [Fact]
    public void SelectedRecentSearch_Setter_RaisesFilterChanged()
    {
        var fired = false;
        _sidebar.FilterChanged += () => fired = true;

        var rs = new SearchHistoryNode { QueryText = "test", EntryId = Guid.NewGuid(), ExecutedAt = DateTimeOffset.UtcNow };
        _sidebar.SelectedRecentSearch = rs;

        fired.Should().BeTrue();
    }

    [Fact]
    public void SelectedSavedSearch_Setter_SameValue_DoesNotRaiseFilterChanged()
    {
        var ss = new SavedSearchNode { Name = "Test", SearchId = Guid.NewGuid() };
        _sidebar.SelectedSavedSearch = ss;

        var fired = false;
        _sidebar.FilterChanged += () => fired = true;

        // Set the same value again — the setter should short-circuit
        _sidebar.SelectedSavedSearch = ss;

        fired.Should().BeFalse("setting the same saved search should not re-fire FilterChanged");
    }

    [Fact]
    public void SelectedRecentSearch_Setter_SameValue_DoesNotRaiseFilterChanged()
    {
        var rs = new SearchHistoryNode { QueryText = "test", EntryId = Guid.NewGuid(), ExecutedAt = DateTimeOffset.UtcNow };
        _sidebar.SelectedRecentSearch = rs;

        var fired = false;
        _sidebar.FilterChanged += () => fired = true;

        _sidebar.SelectedRecentSearch = rs;

        fired.Should().BeFalse("setting the same recent search should not re-fire FilterChanged");
    }

    // ═══════════════════════════════════════════════════════════
    //  PropertyChanged notifications
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void SavedSearches_Setter_RaisesPropertyChanged()
    {
        var changed = new List<string?>();
        _sidebar.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        // Trigger the private setter via reflection
        var field = typeof(SidebarViewModel)
            .GetField("_savedSearches", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var newCollection = new System.Collections.ObjectModel.ObservableCollection<SavedSearchNode>();
        // The setter is auto-property with private set; we trigger it via the backing field
        // Actually, we need to invoke the setter. Let's use reflection to set the property.
        var prop = typeof(SidebarViewModel).GetProperty(nameof(SidebarViewModel.SavedSearches))!;
        prop.SetValue(_sidebar, newCollection);

        changed.Should().Contain(nameof(SidebarViewModel.SavedSearches));
    }

    [Fact]
    public void RecentSearches_Setter_RaisesPropertyChanged()
    {
        var changed = new List<string?>();
        _sidebar.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        var prop = typeof(SidebarViewModel).GetProperty(nameof(SidebarViewModel.RecentSearches))!;
        prop.SetValue(_sidebar, new System.Collections.ObjectModel.ObservableCollection<SearchHistoryNode>());

        changed.Should().Contain(nameof(SidebarViewModel.RecentSearches));
    }

    [Fact]
    public void SelectedSavedSearch_Setter_RaisesPropertyChanged()
    {
        var changed = new List<string?>();
        _sidebar.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _sidebar.SelectedSavedSearch = new SavedSearchNode { Name = "T", SearchId = Guid.NewGuid() };

        changed.Should().Contain(nameof(SidebarViewModel.SelectedSavedSearch));
    }

    [Fact]
    public void SelectedRecentSearch_Setter_RaisesPropertyChanged()
    {
        var changed = new List<string?>();
        _sidebar.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _sidebar.SelectedRecentSearch = new SearchHistoryNode { QueryText = "t", EntryId = Guid.NewGuid(), ExecutedAt = DateTimeOffset.UtcNow };

        changed.Should().Contain(nameof(SidebarViewModel.SelectedRecentSearch));
    }

    // ═══════════════════════════════════════════════════════════
    //  Helpers — reflection for private instance methods
    // ═══════════════════════════════════════════════════════════

    private void InvokeInstanceMethod(string methodName)
    {
        var method = typeof(SidebarViewModel)
            .GetMethod(methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
        method!.Invoke(_sidebar, null);
    }
}
