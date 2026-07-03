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
/// Tests for <see cref="CollectionTreeService"/> — standalone-mode tree building,
/// CRUD operations, and cascade delete. Multi-user mode requires a running
/// broker and is tested via integration tests.
/// </summary>
public sealed class CollectionTreeServiceTests : IAsyncLifetime
{
    private readonly string _basePath;
    private readonly ModeManager _modeManager;
    private CollectionTreeService _service = null!;

    public CollectionTreeServiceTests()
    {
        _basePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _modeManager = new ModeManager(_basePath);
    }

    public async Task InitializeAsync()
    {
        await _modeManager.InitializeAsync();
        _service = new CollectionTreeService(_modeManager, new NullLogger<CollectionTreeService>());
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
    //  BuildTree — static tree builder
    // ──────────────────────────────────────────────

    [Fact]
    public void BuildTree_FlatList_BuildsHierarchy()
    {
        var rootId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var grandchildId = Guid.NewGuid();

        var all = new List<CollectionNode>
        {
            new() { Id = rootId, Name = "Root" },
            new() { Id = childId, Name = "Child", ParentId = rootId },
            new() { Id = grandchildId, Name = "Grandchild", ParentId = childId }
        };

        var result = CollectionTreeService.BuildTree(all[0], all);

        result.Id.Should().Be(rootId);
        result.Children.Should().ContainSingle().Which.Id.Should().Be(childId);
        result.Children[0].Children.Should().ContainSingle().Which.Id.Should().Be(grandchildId);
    }

    [Fact]
    public void BuildTree_NoChildren_ReturnsRootAlone()
    {
        var rootId = Guid.NewGuid();
        var all = new List<CollectionNode>
        {
            new() { Id = rootId, Name = "Root" }
        };

        var result = CollectionTreeService.BuildTree(all[0], all);

        result.Children.Should().BeEmpty();
    }

    [Fact]
    public void BuildTree_MultipleRoots_PreservesSiblings()
    {
        var rootId = Guid.NewGuid();
        var childA = Guid.NewGuid();
        var childB = Guid.NewGuid();

        var all = new List<CollectionNode>
        {
            new() { Id = rootId, Name = "Root" },
            new() { Id = childA, Name = "A", ParentId = rootId },
            new() { Id = childB, Name = "B", ParentId = rootId }
        };

        var result = CollectionTreeService.BuildTree(all[0], all);

        result.Children.Should().HaveCount(2);
        result.Children.Should().Contain(c => c.Id == childA);
        result.Children.Should().Contain(c => c.Id == childB);
    }

    // ──────────────────────────────────────────────
    //  CreateAsync — standalone DB persistence
    // ──────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_Standalone_PersistsToDatabase()
    {
        var name = "Test Collection " + Guid.NewGuid().ToString("N")[..6];

        await _service.CreateAsync(name, parentId: null);

        await using var db = _modeManager.CreateDbContext();
        var saved = await db.Collections.FirstOrDefaultAsync(c => c.Name == name);
        saved.Should().NotBeNull();
        saved!.Name.Should().Be(name);
        saved.ParentId.Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_Standalone_WithParent_SetsParentId()
    {
        var parentName = "Parent " + Guid.NewGuid().ToString("N")[..6];
        var childName = "Child " + Guid.NewGuid().ToString("N")[..6];

        // Create parent first
        await _service.CreateAsync(parentName, parentId: null);
        await using var db1 = _modeManager.CreateDbContext();
        var parent = await db1.Collections.FirstAsync(c => c.Name == parentName);

        // Create child with parent reference
        await _service.CreateAsync(childName, parent.Id);

        await using var db2 = _modeManager.CreateDbContext();
        var child = await db2.Collections.FirstAsync(c => c.Name == childName);
        child.ParentId.Should().Be(parent.Id);
    }

    [Fact]
    public async Task CreateAsync_Standalone_TrimsName()
    {
        var name = "  Spaced Collection  ";
        var trimmed = name.Trim();

        await _service.CreateAsync(name, parentId: null);

        await using var db = _modeManager.CreateDbContext();
        var saved = await db.Collections.FirstOrDefaultAsync(c => c.Name == trimmed);
        saved.Should().NotBeNull();
    }

    // ──────────────────────────────────────────────
    //  RenameAsync — standalone DB update
    // ──────────────────────────────────────────────

    [Fact]
    public async Task RenameAsync_Standalone_UpdatesName()
    {
        var originalName = "Original " + Guid.NewGuid().ToString("N")[..6];
        var newName = "Renamed " + Guid.NewGuid().ToString("N")[..6];

        // Create the collection
        await _service.CreateAsync(originalName, parentId: null);
        await using var db1 = _modeManager.CreateDbContext();
        var col = await db1.Collections.FirstAsync(c => c.Name == originalName);

        // Create a node and rename it
        var node = new CollectionNode { Id = col.Id, Name = originalName };
        await _service.RenameAsync(node, newName);

        await using var db2 = _modeManager.CreateDbContext();
        var renamed = await db2.Collections.FirstAsync(c => c.Id == col.Id);
        renamed.Name.Should().Be(newName);
    }

    [Fact]
    public async Task RenameAsync_NonExistentNode_DoesNotThrow()
    {
        var node = new CollectionNode { Id = Guid.NewGuid(), Name = "Ghost" };

        var act = () => _service.RenameAsync(node, "NewName");
        await act.Should().NotThrowAsync();
    }

    // ──────────────────────────────────────────────
    //  DeleteWithCascadeAsync — standalone cascade delete
    // ──────────────────────────────────────────────

    [Fact]
    public async Task DeleteWithCascadeAsync_LeafNode_DeletesSingle()
    {
        var name = "Leaf " + Guid.NewGuid().ToString("N")[..6];
        await _service.CreateAsync(name, parentId: null);
        await using var db1 = _modeManager.CreateDbContext();
        var col = await db1.Collections.FirstAsync(c => c.Name == name);

        var node = new CollectionNode { Id = col.Id, Name = name };
        var deleted = await _service.DeleteWithCascadeAsync(node);

        deleted.Should().ContainSingle().Which.Should().Be(col.Id);

        await using var db2 = _modeManager.CreateDbContext();
        var remaining = await db2.Collections.CountAsync(c => c.Id == col.Id);
        remaining.Should().Be(0);
    }

    [Fact]
    public async Task DeleteWithCascadeAsync_ParentWithChild_DeletesBoth()
    {
        // Create parent
        var parentName = "Parent " + Guid.NewGuid().ToString("N")[..6];
        await _service.CreateAsync(parentName, parentId: null);
        await using var db1 = _modeManager.CreateDbContext();
        var parent = await db1.Collections.FirstAsync(c => c.Name == parentName);

        // Create child
        var childName = "Child " + Guid.NewGuid().ToString("N")[..6];
        await _service.CreateAsync(childName, parent.Id);
        await using var db2 = _modeManager.CreateDbContext();
        var child = await db2.Collections.FirstAsync(c => c.Name == childName);

        // Build tree node for cascade
        var childNode = new CollectionNode { Id = child.Id, Name = childName };
        var parentNode = new CollectionNode { Id = parent.Id, Name = parentName };
        parentNode.Children.Add(childNode);

        var deleted = await _service.DeleteWithCascadeAsync(parentNode);

        deleted.Should().Contain(parent.Id);
        deleted.Should().Contain(child.Id);

        await using var db3 = _modeManager.CreateDbContext();
        (await db3.Collections.CountAsync()).Should().Be(0);
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
        var name = "Duplicate " + Guid.NewGuid().ToString("N")[..6];

        await _service.CreateAsync(name, parentId: null);
        await _service.CreateAsync(name, parentId: null);

        await using var db = _modeManager.CreateDbContext();
        var matches = await db.Collections.CountAsync(c => c.Name == name);
        matches.Should().Be(2);
    }
}
