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
/// Tests for <see cref="CategoryTreeService"/> — standalone-mode CRUD
/// operations and cascade delete. LoadAsync dispatches to Dispatcher.UIThread
/// so it's tested via integration tests alongside SidebarViewModel.
/// </summary>
public sealed class CategoryTreeServiceTests : IAsyncLifetime
{
    private readonly string _basePath;
    private readonly ModeManager _modeManager;
    private CategoryTreeService _service = null!;

    public CategoryTreeServiceTests()
    {
        _basePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _modeManager = new ModeManager(_basePath);
    }

    public async Task InitializeAsync()
    {
        await _modeManager.InitializeAsync();
        _service = new CategoryTreeService(_modeManager, new NullLogger<CategoryTreeService>());
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
        var name = "TestCat_" + Guid.NewGuid().ToString("N")[..6];

        await _service.CreateAsync(name, parentId: null);

        await using var db = _modeManager.CreateDbContext();
        var saved = await db.Categories.FirstOrDefaultAsync(c => c.Name == name);
        saved.Should().NotBeNull();
        saved!.Name.Should().Be(name);
        saved.NormalizedName.Should().Be(name.ToUpperInvariant());
        saved.ParentId.Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_Standalone_WithParent_SetsParentId()
    {
        var parentName = "ParentCat_" + Guid.NewGuid().ToString("N")[..6];
        var childName = "ChildCat_" + Guid.NewGuid().ToString("N")[..6];

        await _service.CreateAsync(parentName, parentId: null);
        await using var db1 = _modeManager.CreateDbContext();
        var parent = await db1.Categories.FirstAsync(c => c.Name == parentName);

        await _service.CreateAsync(childName, parent.Id);

        await using var db2 = _modeManager.CreateDbContext();
        var child = await db2.Categories.FirstAsync(c => c.Name == childName);
        child.ParentId.Should().Be(parent.Id);
    }

    [Fact]
    public async Task CreateAsync_Standalone_TrimsName()
    {
        var name = "  Spaced Category  ";
        var trimmed = name.Trim();

        await _service.CreateAsync(name, parentId: null);

        await using var db = _modeManager.CreateDbContext();
        var saved = await db.Categories.FirstOrDefaultAsync(c => c.Name == trimmed);
        saved.Should().NotBeNull();
    }

    // ──────────────────────────────────────────────
    //  RenameAsync — standalone DB update
    // ──────────────────────────────────────────────

    [Fact]
    public async Task RenameAsync_Standalone_UpdatesNameAndNormalizedName()
    {
        var originalName = "OriginalCat_" + Guid.NewGuid().ToString("N")[..6];
        var newName = "RenamedCat_" + Guid.NewGuid().ToString("N")[..6];

        await _service.CreateAsync(originalName, parentId: null);
        await using var db1 = _modeManager.CreateDbContext();
        var cat = await db1.Categories.FirstAsync(c => c.Name == originalName);

        var node = new CategoryNode { Name = originalName, CategoryId = cat.Id };
        await _service.RenameAsync(node, newName);

        await using var db2 = _modeManager.CreateDbContext();
        var renamed = await db2.Categories.FirstAsync(c => c.Id == cat.Id);
        renamed.Name.Should().Be(newName);
        renamed.NormalizedName.Should().Be(newName.ToUpperInvariant());
    }

    [Fact]
    public async Task RenameAsync_NonExistentNode_DoesNotThrow()
    {
        var node = new CategoryNode { Name = "Ghost", CategoryId = Guid.NewGuid() };

        var act = () => _service.RenameAsync(node, "NewName");
        await act.Should().NotThrowAsync();
    }

    // ──────────────────────────────────────────────
    //  DeleteWithCascadeAsync — standalone cascade delete
    // ──────────────────────────────────────────────

    [Fact]
    public async Task DeleteWithCascadeAsync_LeafNode_DeletesSingle()
    {
        var name = "LeafCat_" + Guid.NewGuid().ToString("N")[..6];
        await _service.CreateAsync(name, parentId: null);
        await using var db1 = _modeManager.CreateDbContext();
        var cat = await db1.Categories.FirstAsync(c => c.Name == name);

        var node = new CategoryNode { Name = name, CategoryId = cat.Id };
        var deleted = await _service.DeleteWithCascadeAsync(node);

        deleted.Should().ContainSingle().Which.Should().Be(cat.Id);

        await using var db2 = _modeManager.CreateDbContext();
        var remaining = await db2.Categories.CountAsync(c => c.Id == cat.Id);
        remaining.Should().Be(0);
    }

    [Fact]
    public async Task DeleteWithCascadeAsync_ParentWithChild_DeletesBoth()
    {
        var parentName = "ParentCat_" + Guid.NewGuid().ToString("N")[..6];
        var childName = "ChildCat_" + Guid.NewGuid().ToString("N")[..6];

        // Create parent
        await _service.CreateAsync(parentName, parentId: null);
        await using var db1 = _modeManager.CreateDbContext();
        var parent = await db1.Categories.FirstAsync(c => c.Name == parentName);

        // Create child
        await _service.CreateAsync(childName, parent.Id);
        await using var db2 = _modeManager.CreateDbContext();
        var child = await db2.Categories.FirstAsync(c => c.Name == childName);

        // Build tree node for cascade
        var childNode = new CategoryNode { Name = childName, CategoryId = child.Id };
        var parentNode = new CategoryNode { Name = parentName, CategoryId = parent.Id };
        parentNode.Children.Add(childNode);

        var deleted = await _service.DeleteWithCascadeAsync(parentNode);

        deleted.Should().Contain(parent.Id);
        deleted.Should().Contain(child.Id);

        await using var db3 = _modeManager.CreateDbContext();
        (await db3.Categories.CountAsync()).Should().Be(0);
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
        var name = "DupCat_" + Guid.NewGuid().ToString("N")[..6];

        await _service.CreateAsync(name, parentId: null);
        await _service.CreateAsync(name, parentId: null);

        await using var db = _modeManager.CreateDbContext();
        var matches = await db.Categories.CountAsync(c => c.Name == name);
        matches.Should().Be(2);
    }
}
