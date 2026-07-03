using Adam.CatalogBrowser.Models.Sidebar;
using Adam.CatalogBrowser.Services;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Adam.Shared.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.CatalogBrowser.Tests.Services;

/// <summary>
/// Tests for <see cref="KeywordTreeService"/> — standalone-mode CRUD
/// operations and cascade delete. LoadAsync dispatches to Dispatcher.UIThread
/// so it's tested via integration tests alongside SidebarViewModel.
/// </summary>
public sealed class KeywordTreeServiceTests : IAsyncLifetime
{
    private readonly string _basePath;
    private readonly ModeManager _modeManager;
    private KeywordTreeService _service = null!;

    public KeywordTreeServiceTests()
    {
        _basePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _modeManager = new ModeManager(_basePath);
    }

    public async Task InitializeAsync()
    {
        await _modeManager.InitializeAsync();
        _service = new KeywordTreeService(_modeManager, new NullLogger<KeywordTreeService>());
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
    //  CreateAsync — standalone DB persistence
    // ──────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_Standalone_PersistsToDatabase()
    {
        var name = "TestKeyword_" + Guid.NewGuid().ToString("N")[..6];

        await _service.CreateAsync(name, parentId: null);

        await using var db = _modeManager.CreateDbContext();
        var saved = await db.Keywords.FirstOrDefaultAsync(k => k.Name == name);
        saved.Should().NotBeNull();
        saved!.Name.Should().Be(name);
        saved.NormalizedName.Should().Be(name.ToUpperInvariant());
        saved.ParentId.Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_Standalone_WithParent_SetsParentId()
    {
        var parentName = "ParentKW_" + Guid.NewGuid().ToString("N")[..6];
        var childName = "ChildKW_" + Guid.NewGuid().ToString("N")[..6];

        await _service.CreateAsync(parentName, parentId: null);
        await using var db1 = _modeManager.CreateDbContext();
        var parent = await db1.Keywords.FirstAsync(k => k.Name == parentName);

        await _service.CreateAsync(childName, parent.Id);

        await using var db2 = _modeManager.CreateDbContext();
        var child = await db2.Keywords.FirstAsync(k => k.Name == childName);
        child.ParentId.Should().Be(parent.Id);
    }

    [Fact]
    public async Task CreateAsync_Standalone_TrimsName()
    {
        var name = "  Spaced Keyword  ";
        var trimmed = name.Trim();

        await _service.CreateAsync(name, parentId: null);

        await using var db = _modeManager.CreateDbContext();
        var saved = await db.Keywords.FirstOrDefaultAsync(k => k.Name == trimmed);
        saved.Should().NotBeNull();
    }

    // ──────────────────────────────────────────────
    //  RenameAsync — standalone DB update
    // ──────────────────────────────────────────────

    [Fact]
    public async Task RenameAsync_Standalone_UpdatesNameAndNormalizedName()
    {
        var originalName = "OriginalKW_" + Guid.NewGuid().ToString("N")[..6];
        var newName = "RenamedKW_" + Guid.NewGuid().ToString("N")[..6];

        await _service.CreateAsync(originalName, parentId: null);
        await using var db1 = _modeManager.CreateDbContext();
        var kw = await db1.Keywords.FirstAsync(k => k.Name == originalName);

        var node = new KeywordNode { Name = originalName, KeywordId = kw.Id };
        await _service.RenameAsync(node, newName);

        await using var db2 = _modeManager.CreateDbContext();
        var renamed = await db2.Keywords.FirstAsync(k => k.Id == kw.Id);
        renamed.Name.Should().Be(newName);
        renamed.NormalizedName.Should().Be(newName.ToUpperInvariant());
    }

    [Fact]
    public async Task RenameAsync_NonExistentNode_DoesNotThrow()
    {
        var node = new KeywordNode { Name = "Ghost", KeywordId = Guid.NewGuid() };

        var act = () => _service.RenameAsync(node, "NewName");
        await act.Should().NotThrowAsync();
    }

    // ──────────────────────────────────────────────
    //  DeleteWithCascadeAsync — standalone cascade delete
    // ──────────────────────────────────────────────

    [Fact]
    public async Task DeleteWithCascadeAsync_LeafNode_DeletesSingle()
    {
        var name = "LeafKW_" + Guid.NewGuid().ToString("N")[..6];
        await _service.CreateAsync(name, parentId: null);
        await using var db1 = _modeManager.CreateDbContext();
        var kw = await db1.Keywords.FirstAsync(k => k.Name == name);

        var node = new KeywordNode { Name = name, KeywordId = kw.Id };
        var deleted = await _service.DeleteWithCascadeAsync(node);

        deleted.Should().ContainSingle().Which.Should().Be(kw.Id);

        await using var db2 = _modeManager.CreateDbContext();
        var remaining = await db2.Keywords.CountAsync(k => k.Id == kw.Id);
        remaining.Should().Be(0);
    }

    [Fact]
    public async Task DeleteWithCascadeAsync_ParentWithChild_DeletesBoth()
    {
        var parentName = "ParentKW_" + Guid.NewGuid().ToString("N")[..6];
        var childName = "ChildKW_" + Guid.NewGuid().ToString("N")[..6];

        // Create parent
        await _service.CreateAsync(parentName, parentId: null);
        await using var db1 = _modeManager.CreateDbContext();
        var parent = await db1.Keywords.FirstAsync(k => k.Name == parentName);

        // Create child
        await _service.CreateAsync(childName, parent.Id);
        await using var db2 = _modeManager.CreateDbContext();
        var child = await db2.Keywords.FirstAsync(k => k.Name == childName);

        // Build tree node for cascade
        var childNode = new KeywordNode { Name = childName, KeywordId = child.Id };
        var parentNode = new KeywordNode { Name = parentName, KeywordId = parent.Id };
        parentNode.Children.Add(childNode);

        var deleted = await _service.DeleteWithCascadeAsync(parentNode);

        deleted.Should().Contain(parent.Id);
        deleted.Should().Contain(child.Id);

        await using var db3 = _modeManager.CreateDbContext();
        (await db3.Keywords.CountAsync()).Should().Be(0);
    }

    // ──────────────────────────────────────────────
    //  Roots collection starts empty
    // ──────────────────────────────────────────────

    [Fact]
    public void Roots_InitialState_IsEmpty()
    {
        _service.Roots.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────
    //  CreateAsync — duplicate name allowed
    // ──────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_DuplicateName_AllowsMultiple()
    {
        var name = "DupKW_" + Guid.NewGuid().ToString("N")[..6];

        await _service.CreateAsync(name, parentId: null);
        await _service.CreateAsync(name, parentId: null);

        await using var db = _modeManager.CreateDbContext();
        var matches = await db.Keywords.CountAsync(k => k.Name == name);
        matches.Should().Be(2);
    }
}
