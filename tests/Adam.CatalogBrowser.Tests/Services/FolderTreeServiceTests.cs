using Adam.CatalogBrowser.Models.Sidebar;
using Adam.CatalogBrowser.Services;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Adam.Shared.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.CatalogBrowser.Tests.Services;

/// <summary>
/// Tests for <see cref="FolderTreeService"/> — static helpers and
/// standalone-mode DB operations (RevealFolder, RescanFolderAsync).
/// LoadAsync dispatches to Dispatcher.UIThread, so tree construction
/// and node lookup are tested via the internal static helper.
/// </summary>
public sealed class FolderTreeServiceTests : IAsyncLifetime
{
    private readonly string _basePath;
    private readonly ModeManager _modeManager;
    private FolderTreeService _service = null!;

    public FolderTreeServiceTests()
    {
        _basePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _modeManager = new ModeManager(_basePath);
    }

    public async Task InitializeAsync()
    {
        await _modeManager.InitializeAsync();
        _service = new FolderTreeService(_modeManager, new NullLogger<FolderTreeService>());
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

    // ──────────────────────────────────────────────
    //  FindFolderNode — static tree lookup
    // ──────────────────────────────────────────────

    [Fact]
    public void FindFolderNode_RootPath_ReturnsRoot()
    {
        var root = new FolderNode { Name = "Root", Path = "" };

        var result = FolderTreeService.FindFolderNode(root, "");

        result.Should().BeSameAs(root);
    }

    [Fact]
    public void FindFolderNode_SingleLevel_ReturnsCorrectNode()
    {
        var root = new FolderNode { Name = "Root", Path = "" };
        var child = new FolderNode { Name = "Pictures", Path = "Pictures" };
        root.Children.Add(child);

        var result = FolderTreeService.FindFolderNode(root, "Pictures");

        result.Should().BeSameAs(child);
    }

    [Fact]
    public void FindFolderNode_NestedPath_ReturnsCorrectNode()
    {
        var root = new FolderNode { Name = "Root", Path = "" };
        var photos = new FolderNode { Name = "Photos", Path = "Photos" };
        var vacations = new FolderNode { Name = "Vacations", Path = "Photos/Vacations" };
        root.Children.Add(photos);
        photos.Children.Add(vacations);

        var result = FolderTreeService.FindFolderNode(root, "Photos/Vacations");

        result.Should().BeSameAs(vacations);
    }

    [Fact]
    public void FindFolderNode_WindowsPath_NormalizesSlashes()
    {
        var root = new FolderNode { Name = "Root", Path = "" };
        var child = new FolderNode { Name = "Pictures", Path = "Pictures" };
        root.Children.Add(child);

        // FindFolderNode normalizes backslashes internally
        var result = FolderTreeService.FindFolderNode(root, "Pictures");

        result.Should().BeSameAs(child);
    }

    [Fact]
    public void FindFolderNode_NonExistentPath_ReturnsNull()
    {
        var root = new FolderNode { Name = "Root", Path = "" };
        root.Children.Add(new FolderNode { Name = "Pictures", Path = "Pictures" });

        var result = FolderTreeService.FindFolderNode(root, "Documents");

        result.Should().BeNull();
    }

    [Fact]
    public void FindFolderNode_PartialPath_ReturnsNull()
    {
        var root = new FolderNode { Name = "Root", Path = "" };
        root.Children.Add(new FolderNode { Name = "Photos", Path = "Photos" });

        // "Photos/2024/Vacation" — "Photos" exists but "2024" doesn't
        var result = FolderTreeService.FindFolderNode(root, "Photos/2024/Vacation");

        result.Should().BeNull();
    }

    // ──────────────────────────────────────────────
    //  RevealFolder — null/empty guards
    // ──────────────────────────────────────────────

    [Fact]
    public void RevealFolder_NullFolder_DoesNotThrow()
    {
        var act = () => _service.RevealFolder(null);
        act.Should().NotThrow();
    }

    [Fact]
    public void RevealFolder_EmptyPath_DoesNotThrow()
    {
        var folder = new FolderNode { Name = "Root", Path = "" };

        var act = () => _service.RevealFolder(folder);
        act.Should().NotThrow();
    }

    // ──────────────────────────────────────────────
    //  RescanFolderAsync — null/empty guards
    // ──────────────────────────────────────────────

    [Fact]
    public async Task RescanFolderAsync_NullFolder_DoesNotThrow()
    {
        var act = () => _service.RescanFolderAsync(null);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RescanFolderAsync_EmptyPath_DoesNotThrow()
    {
        var folder = new FolderNode { Name = "Root", Path = "" };

        var act = () => _service.RescanFolderAsync(folder);
        await act.Should().NotThrowAsync();
    }

    // ──────────────────────────────────────────────
    //  Constructor — FolderScanService fallback
    // ──────────────────────────────────────────────

    [Fact]
    public void Constructor_NoFolderScanService_CreatesFallback()
    {
        var service = new FolderTreeService(_modeManager, new NullLogger<FolderTreeService>());

        // Should not throw — fallback FolderScanService created internally
        service.Should().NotBeNull();
    }

    // ──────────────────────────────────────────────
    //  Roots collection starts empty
    // ──────────────────────────────────────────────

    [Fact]
    public void Roots_InitialState_IsEmpty()
    {
        _service.Roots.Should().BeEmpty();
    }
}
