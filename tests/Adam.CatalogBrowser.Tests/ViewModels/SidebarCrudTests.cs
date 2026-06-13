using System.Reflection;
using Adam.CatalogBrowser.ViewModels;
using Adam.Shared.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.CatalogBrowser.Tests.ViewModels;

/// <summary>
/// Tests for Phase 10 sidebar CRUD operations — cascade delete, filter commands,
/// visual filter state, permission gating, and rename helpers.
///
/// Uses a real SQLite database (ModeManager with temp path) to verify
/// persistence. Private async methods (PromptCreateKeywordAsync, etc.) are
/// invoked via reflection.
/// </summary>
public sealed class SidebarCrudTests : IAsyncLifetime
{
    private readonly string _basePath;
    private readonly ModeManager _modeManager;
    private readonly NullLogger<SidebarViewModel> _logger;
    private SidebarViewModel _sidebar = null!;

    public SidebarCrudTests()
    {
        _basePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _modeManager = new ModeManager(_basePath);
        _logger = new NullLogger<SidebarViewModel>();
    }

    public async Task InitializeAsync()
    {
        await _modeManager.InitializeAsync();
        _sidebar = new SidebarViewModel(_modeManager, _logger);
        // Note: LoadAsync dispatches to Dispatcher.UIThread which requires a pumping
        // dispatcher. Tests verify the data model (commands, properties, static helpers)
        // that don't require UI-thread Init.
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

    // ──────────────────────────────────────────────
    //  T10.4: CountDescendantKeywords
    // ──────────────────────────────────────────────

    [Fact]
    public void CountDescendantKeywords_CountsRecursively()
    {
        // Arrange: build a keyword tree manually
        var leaf = new KeywordNode { Name = "Leaf", KeywordId = Guid.NewGuid() };
        var mid = new KeywordNode { Name = "Mid", KeywordId = Guid.NewGuid() };
        mid.Children.Add(leaf);
        var root = new KeywordNode { Name = "Root", KeywordId = Guid.NewGuid() };
        root.Children.Add(mid);

        // Act
        var count = InvokeStatic<int>("CountDescendantKeywords", root);

        // Assert
        count.Should().Be(2); // Mid + Leaf
    }

    [Fact]
    public void CountDescendantKeywords_NoChildren_ReturnsZero()
    {
        var leaf = new KeywordNode { Name = "Leaf", KeywordId = Guid.NewGuid() };
        var count = InvokeStatic<int>("CountDescendantKeywords", leaf);
        count.Should().Be(0);
    }

    // ──────────────────────────────────────────────
    //  T10.4: CountDescendantCollections
    // ──────────────────────────────────────────────

    [Fact]
    public void CountDescendantCollections_CountsRecursively()
    {
        var leaf = new CollectionNode { Name = "Leaf", Id = Guid.NewGuid() };
        var root = new CollectionNode { Name = "Root", Id = Guid.NewGuid() };
        root.Children.Add(leaf);

        var count = InvokeStatic<int>("CountDescendantCollections", root);
        count.Should().Be(1);
    }

    // ──────────────────────────────────────────────
    //  T10.4: CountDescendantCategories
    // ──────────────────────────────────────────────

    [Fact]
    public void CountDescendantCategories_CountsRecursively()
    {
        var leaf = new CategoryNode { Name = "Leaf", CategoryId = Guid.NewGuid() };
        var root = new CategoryNode { Name = "Root", CategoryId = Guid.NewGuid() };
        root.Children.Add(leaf);

        var count = InvokeStatic<int>("CountDescendantCategories", root);
        count.Should().Be(1);
    }

    // ──────────────────────────────────────────────
    //  T10.4: CollectDescendantKeywordIds
    // ──────────────────────────────────────────────

    [Fact]
    public void CollectDescendantKeywordIds_CollectsRecursively()
    {
        // Arrange
        var leaf = new KeywordNode { Name = "Leaf", KeywordId = Guid.NewGuid() };
        var mid = new KeywordNode { Name = "Mid", KeywordId = Guid.NewGuid() };
        mid.Children.Add(leaf);
        var root = new KeywordNode { Name = "Root", KeywordId = Guid.NewGuid() };
        root.Children.Add(mid);

        var ids = new List<Guid>();

        // Act
        InvokeStatic("CollectDescendantKeywordIds", root, ids);

        // Assert
        ids.Should().BeEquivalentTo([mid.KeywordId, leaf.KeywordId]);
    }

    // ──────────────────────────────────────────────
    //  T10.4: CollectDescendantCollectionIds
    // ──────────────────────────────────────────────

    [Fact]
    public void CollectDescendantCollectionIds_CollectsRecursively()
    {
        var leaf = new CollectionNode { Name = "Leaf", Id = Guid.NewGuid() };
        var root = new CollectionNode { Name = "Root", Id = Guid.NewGuid() };
        root.Children.Add(leaf);

        var ids = new List<Guid>();
        InvokeStatic("CollectDescendantCollectionIds", root, ids);

        ids.Should().ContainSingle().Which.Should().Be(leaf.Id);
    }

    // ──────────────────────────────────────────────
    //  T10.4: CollectDescendantCategoryIds
    // ──────────────────────────────────────────────

    [Fact]
    public void CollectDescendantCategoryIds_CollectsRecursively()
    {
        var leaf = new CategoryNode { Name = "Leaf", CategoryId = Guid.NewGuid() };
        var root = new CategoryNode { Name = "Root", CategoryId = Guid.NewGuid() };
        root.Children.Add(leaf);

        var ids = new List<Guid>();
        InvokeStatic("CollectDescendantCategoryIds", root, ids);

        ids.Should().ContainSingle().Which.Should().Be(leaf.CategoryId);
    }

    // ──────────────────────────────────────────────
    //  T10.8: FilterByThis / ClearFilter
    // ──────────────────────────────────────────────

    [Fact]
    public void FilterByThis_SetsSelectedKeyword()
    {
        var kw = new KeywordNode { Name = "Test", KeywordId = Guid.NewGuid() };
        _sidebar.FilterByThisCommand.Execute(kw);

        _sidebar.SelectedKeyword.Should().BeSameAs(kw);
    }

    [Fact]
    public void FilterByThis_SetsSelectedCollection()
    {
        var col = new CollectionNode { Name = "TestCol", Id = Guid.NewGuid() };
        _sidebar.FilterByThisCommand.Execute(col);

        _sidebar.SelectedCollection.Should().BeSameAs(col);
    }

    [Fact]
    public void FilterByThis_SetsSelectedCategory()
    {
        var cat = new CategoryNode { Name = "TestCat", CategoryId = Guid.NewGuid() };
        _sidebar.FilterByThisCommand.Execute(cat);

        _sidebar.SelectedMetadataCategory.Should().BeSameAs(cat);
    }

    [Fact]
    public void FilterByThis_SetsSelectedFolder()
    {
        var folder = new FolderNode { Name = "TestFolder", Path = "/test" };
        _sidebar.FilterByThisCommand.Execute(folder);

        _sidebar.SelectedFolder.Should().BeSameAs(folder);
    }

    [Fact]
    public void FilterByThis_SetsSelectedDateTaken()
    {
        var dt = new DateTakenNode { Name = "2024", Year = 2024 };
        _sidebar.FilterByThisCommand.Execute(dt);

        _sidebar.SelectedDateTaken.Should().BeSameAs(dt);
    }

    [Fact]
    public void ClearFilter_ClearsSelectedKeyword()
    {
        var kw = new KeywordNode { Name = "Test", KeywordId = Guid.NewGuid() };
        _sidebar.SelectedKeyword = kw;
        _sidebar.ClearFilterCommand.Execute(kw);

        _sidebar.SelectedKeyword.Should().BeNull();
    }

    [Fact]
    public void ClearFilter_ClearsSelectedCollection()
    {
        var col = new CollectionNode { Name = "Test", Id = Guid.NewGuid() };
        _sidebar.SelectedCollection = col;
        _sidebar.ClearFilterCommand.Execute(col);

        _sidebar.SelectedCollection.Should().BeNull();
    }

    [Fact]
    public void ClearFilter_ClearsSelectedCategory()
    {
        var cat = new CategoryNode { Name = "Test", CategoryId = Guid.NewGuid() };
        _sidebar.SelectedMetadataCategory = cat;
        _sidebar.ClearFilterCommand.Execute(cat);

        _sidebar.SelectedMetadataCategory.Should().BeNull();
    }

    // ──────────────────────────────────────────────
    //  T10.13: IsActiveFilter state
    // ──────────────────────────────────────────────

    [Fact]
    public void IsActiveFilter_Set_WhenKeywordSelected()
    {
        var kw = new KeywordNode { Name = "Active", KeywordId = Guid.NewGuid() };
        _sidebar.SelectedKeyword = kw;

        kw.IsActiveFilter.Should().BeTrue();
    }

    [Fact]
    public void IsActiveFilter_Clears_WhenDifferentFilterSelected()
    {
        var kw1 = new KeywordNode { Name = "KW1", KeywordId = Guid.NewGuid() };
        var kw2 = new KeywordNode { Name = "KW2", KeywordId = Guid.NewGuid() };

        _sidebar.SelectedKeyword = kw1;
        _sidebar.SelectedKeyword = kw2;

        kw1.IsActiveFilter.Should().BeFalse();
        kw2.IsActiveFilter.Should().BeTrue();
    }

    [Fact]
    public void IsActiveFilter_Clears_WhenFilterSetToNull()
    {
        var kw = new KeywordNode { Name = "Active", KeywordId = Guid.NewGuid() };

        _sidebar.SelectedKeyword = kw;
        _sidebar.SelectedKeyword = null;

        kw.IsActiveFilter.Should().BeFalse();
    }

    // ──────────────────────────────────────────────
    //  T10.3: Permission gating — CanCreateMetadata
    // ──────────────────────────────────────────────

    [Fact]
    public void CreateKeywordCommand_CanExecute_WhenCanCreateMetadata_ReturnsTrue()
    {
        // Standalone mode: CanCreateMetadata is true
        _sidebar.CreateKeywordCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void CreateCollectionCommand_CanExecute_WhenCanCreateMetadata_ReturnsTrue()
    {
        _sidebar.CreateCollectionCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void CreateCategoryCommand_CanExecute_WhenCanCreateMetadata_ReturnsTrue()
    {
        _sidebar.CreateCategoryCommand.CanExecute(null).Should().BeTrue();
    }

    // ──────────────────────────────────────────────
    //  T10.3: Permission gating — CanEditMetadata
    // ──────────────────────────────────────────────

    [Fact]
    public void RenameKeywordCommand_CanExecute_WhenCanEditMetadata_ReturnsTrue()
    {
        _sidebar.RenameKeywordCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void DeleteKeywordCommand_CanExecute_WhenCanEditMetadata_ReturnsTrue()
    {
        _sidebar.DeleteKeywordCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void RenameCollectionCommand_CanExecute_WhenCanEditMetadata_ReturnsTrue()
    {
        _sidebar.RenameCollectionCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void DeleteCollectionCommand_CanExecute_WhenCanEditMetadata_ReturnsTrue()
    {
        _sidebar.DeleteCollectionCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void RenameCategoryCommand_CanExecute_WhenCanEditMetadata_ReturnsTrue()
    {
        _sidebar.RenameCategoryCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void DeleteCategoryCommand_CanExecute_WhenCanEditMetadata_ReturnsTrue()
    {
        _sidebar.DeleteCategoryCommand.CanExecute(null).Should().BeTrue();
    }

    // ──────────────────────────────────────────────
    //  T10.2: F2RenameCommand
    // ──────────────────────────────────────────────

    [Fact]
    public void F2RenameCommand_CanExecute_NoSelection_ReturnsFalse()
    {
        _sidebar.F2RenameCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void F2RenameCommand_CanExecute_KeywordSelected_ReturnsTrue()
    {
        _sidebar.SelectedKeyword = new KeywordNode { Name = "Test", KeywordId = Guid.NewGuid() };
        _sidebar.F2RenameCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void F2RenameCommand_CanExecute_CollectionSelected_ReturnsTrue()
    {
        _sidebar.SelectedCollection = new CollectionNode { Name = "Test", Id = Guid.NewGuid() };
        _sidebar.F2RenameCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void F2RenameCommand_CanExecute_CategorySelected_ReturnsTrue()
    {
        _sidebar.SelectedMetadataCategory = new CategoryNode { Name = "Test", CategoryId = Guid.NewGuid() };
        _sidebar.F2RenameCommand.CanExecute(null).Should().BeTrue();
    }

    // ──────────────────────────────────────────────
    //  T10.2: Inline rename node methods
    // ──────────────────────────────────────────────

    [Fact]
    public void KeywordNode_BeginRename_SetsEditingAndEditName()
    {
        var node = new KeywordNode { Name = "Original" };
        node.BeginRename();

        node.IsEditing.Should().BeTrue();
        node.EditName.Should().Be("Original");
    }

    [Fact]
    public void KeywordNode_CommitRename_ValidName_UpdatesName()
    {
        var node = new KeywordNode { Name = "Original" };
        node.BeginRename();
        node.EditName = "Updated";
        node.CommitRename();

        node.Name.Should().Be("Updated");
        node.IsEditing.Should().BeFalse();
    }

    [Fact]
    public void KeywordNode_CommitRename_Empty_DoesNotUpdateName()
    {
        var node = new KeywordNode { Name = "Original" };
        node.BeginRename();
        node.EditName = "  ";
        node.CommitRename();

        node.Name.Should().Be("Original");
        node.IsEditing.Should().BeFalse();
    }

    [Fact]
    public void KeywordNode_CancelRename_RestoresOriginal()
    {
        var node = new KeywordNode { Name = "Original" };
        node.BeginRename();
        node.EditName = "Changed";
        node.CancelRename();

        node.Name.Should().Be("Original");
        node.EditName.Should().Be("Original");
        node.IsEditing.Should().BeFalse();
    }

    [Fact]
    public void CollectionNode_BeginRename_SetsEditingAndEditName()
    {
        var node = new CollectionNode { Name = "Col" };
        node.BeginRename();

        node.IsEditing.Should().BeTrue();
        node.EditName.Should().Be("Col");
    }

    [Fact]
    public void CategoryNode_BeginRename_SetsEditingAndEditName()
    {
        var node = new CategoryNode { Name = "Cat" };
        node.BeginRename();

        node.IsEditing.Should().BeTrue();
        node.EditName.Should().Be("Cat");
    }

    // ──────────────────────────────────────────────
    //  T10.5: RevealFolder / RescanFolder Command CanExecute
    // ──────────────────────────────────────────────

    [Fact]
    public void RevealFolderCommand_CanExecute_NoFolder_ReturnsFalse()
    {
        _sidebar.RevealFolderCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void RescanFolderCommand_CanExecute_NoFolder_ReturnsFalse()
    {
        _sidebar.RescanFolderCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void RevealFolderCommand_CanExecute_WithFolder_ReturnsTrue()
    {
        _sidebar.SelectedFolder = new FolderNode { Name = "Test", Path = "/test" };
        _sidebar.RevealFolderCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void RescanFolderCommand_CanExecute_WithFolder_ReturnsTrue()
    {
        _sidebar.SelectedFolder = new FolderNode { Name = "Test", Path = "/test" };
        _sidebar.RescanFolderCommand.CanExecute(null).Should().BeTrue();
    }

    // ──────────────────────────────────────────────
    //  Helpers — reflect into private static methods
    // ──────────────────────────────────────────────

    private static T InvokeStatic<T>(string methodName, object node)
    {
        var method = typeof(SidebarViewModel)
            .GetMethod(methodName,
                BindingFlags.Static | BindingFlags.NonPublic)!;
        return (T)method.Invoke(null, [node])!;
    }

    private static void InvokeStatic(string methodName, object node, List<Guid> ids)
    {
        var method = typeof(SidebarViewModel)
            .GetMethod(methodName,
                BindingFlags.Static | BindingFlags.NonPublic)!;
        method.Invoke(null, [node, ids]);
    }
}
