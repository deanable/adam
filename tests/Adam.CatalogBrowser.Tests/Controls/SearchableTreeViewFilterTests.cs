using System.Collections;
using System.Reflection;
using Adam.CatalogBrowser.Controls;
using Adam.CatalogBrowser.ViewModels;
using FluentAssertions;

namespace Adam.CatalogBrowser.Tests.Controls;

/// <summary>
/// Tests the private static <c>FlattenAndFilter</c> method of
/// <see cref="SearchableTreeView"/>.  The method walks a tree of objects
/// with <c>Name</c> / <c>Children</c> / <c>Count</c> (or <c>AssetCount</c>)
/// properties via reflection and collects matching <see cref="FlatItem"/> records.
///
/// Uses reflection to invoke the private method — avoids needing the full
/// Avalonia control tree and UI thread.
/// </summary>
public class SearchableTreeViewFilterTests
{
    private static readonly Type TargetType = typeof(SearchableTreeView);

    private static readonly MethodInfo FlattenAndFilterMethod =
        TargetType.GetMethod("FlattenAndFilter",
            BindingFlags.Static | BindingFlags.NonPublic)!;

    /// <summary>
    /// Invokes <c>SearchableTreeView.FlattenAndFilter(object, string, List&lt;string&gt;, List&lt;FlatItem&gt;)</c>
    /// and returns the resulting <see cref="FlatItem"/> list.
    /// </summary>
    private static List<FlatItem> InvokeFlattenAndFilter(
        object node, string searchText, List<string>? parentPath = null)
    {
        var results = new List<FlatItem>();
        var path = parentPath ?? [];
        FlattenAndFilterMethod.Invoke(null, [node, searchText, path, results]);
        return results;
    }

    // ──────────────────────────────────────────────
    //  Helper node types
    // ──────────────────────────────────────────────

    private sealed class TestNode
    {
        public string Name { get; set; } = string.Empty;
        public List<TestNode> Children { get; } = [];
    }

    private sealed class TestNodeWithCount
    {
        public string Name { get; set; } = string.Empty;
        public int Count { get; set; }
        public List<TestNodeWithCount> Children { get; } = [];
    }

    private sealed class TestNodeWithAssetCount
    {
        public string Name { get; set; } = string.Empty;
        public int AssetCount { get; set; }
        public List<TestNodeWithAssetCount> Children { get; } = [];
    }

    // ──────────────────────────────────────────────
    //  Basic filtering
    // ──────────────────────────────────────────────

    [Fact]
    public void FlattenAndFilter_MatchingName_ReturnsFlatItem()
    {
        var node = new TestNode { Name = "Nature" };
        var results = InvokeFlattenAndFilter(node, "Nat");

        results.Should().ContainSingle();
        results[0].Name.Should().Be("Nature");
        results[0].DisplayPath.Should().Be("Nature");
    }

    [Fact]
    public void FlattenAndFilter_NonMatchingName_ReturnsEmpty()
    {
        var node = new TestNode { Name = "Nature" };
        var results = InvokeFlattenAndFilter(node, "Urban");

        results.Should().BeEmpty();
    }

    [Fact]
    public void FlattenAndFilter_CaseInsensitive_ReturnsMatch()
    {
        var node = new TestNode { Name = "Nature" };
        var results = InvokeFlattenAndFilter(node, "nature");

        results.Should().ContainSingle();
        results[0].Name.Should().Be("Nature");
    }

    [Fact]
    public void FlattenAndFilter_PartialMatch_ReturnsMatch()
    {
        var node = new TestNode { Name = "Sunset Beach" };
        var results = InvokeFlattenAndFilter(node, "Sun");

        results.Should().ContainSingle();
        results[0].Name.Should().Be("Sunset Beach");
    }

    [Fact]
    public void FlattenAndFilter_EmptySearchText_ReturnsAll()
    {
        // Note: RebuildFilteredList guards against empty search text,
        // but FlattenAndFilter itself receives non-empty strings.
        // Searching with empty string — contains("", ...) returns true
        var node = new TestNode { Name = "Anything" };
        var results = InvokeFlattenAndFilter(node, "");

        results.Should().ContainSingle();
    }

    // ──────────────────────────────────────────────
    //  Hierarchy / path building
    // ──────────────────────────────────────────────

    [Fact]
    public void FlattenAndFilter_NestedNode_BuildsDisplayPath()
    {
        // Tree: Root > Nature > Trees
        var leaf = new TestNode { Name = "Trees" };
        var parent = new TestNode { Name = "Nature", Children = { leaf } };
        var root = new TestNode { Name = "Root", Children = { parent } };

        // Search from "root" with "Trees"
        var results = InvokeFlattenAndFilter(root, "Trees");

        results.Should().ContainSingle();
        results[0].DisplayPath.Should().Be("Root > Nature > Trees");
    }

    [Fact]
    public void FlattenAndFilter_MultipleMatchingSiblings_ReturnsAll()
    {
        var child1 = new TestNode { Name = "Beach" };
        var child2 = new TestNode { Name = "Mountain" };
        var child3 = new TestNode { Name = "Forest" };
        var root = new TestNode { Name = "Locations", Children = { child1, child2, child3 } };

        var results = InvokeFlattenAndFilter(root, "o");

        results.Should().HaveCount(3); // "Locations", "Mountain", and "Forest" all contain 'o'
        results.Select(r => r.Name).Should().BeEquivalentTo(["Locations", "Mountain", "Forest"]);
    }

    [Fact]
    public void FlattenAndFilter_ParentAndChildMatch_BothReturned()
    {
        var child = new TestNode { Name = "Nature" };
        var root = new TestNode { Name = "Nature", Children = { child } };

        var results = InvokeFlattenAndFilter(root, "Nature");

        results.Should().HaveCount(2);
        results.Select(r => r.Name).Should().AllBe("Nature");
        results.Select(r => r.DisplayPath).Should().BeEquivalentTo([
            "Nature",
            "Nature > Nature"
        ]);
    }

    [Fact]
    public void FlattenAndFilter_NodeWithoutName_ShowsEmptyPath()
    {
        var node = new TestNode { Name = "" };
        var results = InvokeFlattenAndFilter(node, "");

        results.Should().ContainSingle();
        results[0].DisplayPath.Should().Be("");
    }

    // ──────────────────────────────────────────────
    //  Count property reflection
    // ──────────────────────────────────────────────

    [Fact]
    public void FlattenAndFilter_NodeWithCount_PopulatesCount()
    {
        var node = new TestNodeWithCount { Name = "Nature", Count = 42 };
        var results = InvokeFlattenAndFilter(node, "Nat");

        results.Should().ContainSingle();
        results[0].Count.Should().Be(42);
    }

    [Fact]
    public void FlattenAndFilter_NodeWithAssetCount_PopulatesCount()
    {
        var node = new TestNodeWithAssetCount { Name = "Nature", AssetCount = 99 };
        var results = InvokeFlattenAndFilter(node, "Nat");

        results.Should().ContainSingle();
        results[0].Count.Should().Be(99);
    }

    [Fact]
    public void FlattenAndFilter_NodeWithoutCountProperty_ReturnsZero()
    {
        var node = new TestNode { Name = "Nature" }; // no Count or AssetCount
        var results = InvokeFlattenAndFilter(node, "Nat");

        results.Should().ContainSingle();
        results[0].Count.Should().Be(0);
    }

    // ──────────────────────────────────────────────
    //  Real model integration
    // ──────────────────────────────────────────────

    [Fact]
    public void FlattenAndFilter_KeywordNode_WorksWithReflection()
    {
        var child = new KeywordNode { Name = "Summer", AssetCount = 7 };
        var root = new KeywordNode { Name = "Seasons", AssetCount = 3, Children = { child } };

        // Search for "Summer"
        var results = InvokeFlattenAndFilter(root, "Summer");

        results.Should().ContainSingle();
        results[0].Name.Should().Be("Summer");
        results[0].Count.Should().Be(7);
        results[0].DisplayPath.Should().Be("Seasons > Summer");
    }

    [Fact]
    public void FlattenAndFilter_CategoryNode_WorksWithReflection()
    {
        var child = new CategoryNode { Name = "Nature", Count = 15 };
        var root = new CategoryNode { Name = "All Categories", Count = 100, Children = { child } };

        var results = InvokeFlattenAndFilter(root, "Nature");

        results.Should().ContainSingle();
        results[0].Name.Should().Be("Nature");
        results[0].Count.Should().Be(15);
        results[0].DisplayPath.Should().Be("All Categories > Nature");
    }

    [Fact]
    public void FlattenAndFilter_KeywordNode_NestedSearch_FindsDescendant()
    {
        var grandchild = new KeywordNode { Name = "Bird", AssetCount = 5 };
        var child = new KeywordNode { Name = "Animals", Children = { grandchild } };
        var root = new KeywordNode { Name = "Keywords", Children = { child } };

        var results = InvokeFlattenAndFilter(root, "Bird");

        results.Should().ContainSingle();
        results[0].Name.Should().Be("Bird");
        results[0].DisplayPath.Should().Be("Keywords > Animals > Bird");
    }

    [Fact]
    public void FlattenAndFilter_DeepNestedSearch_DoesNotFindNonMatching()
    {
        var grandchild = new KeywordNode { Name = "Bird", AssetCount = 5 };
        var child = new KeywordNode { Name = "Animals", Children = { grandchild } };
        var root = new KeywordNode { Name = "Keywords", Children = { child } };

        var results = InvokeFlattenAndFilter(root, "Fish");

        results.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────
    //  FlatItem model
    // ──────────────────────────────────────────────

    [Fact]
    public void FlatItem_Properties_SetCorrectly()
    {
        var node = new TestNode { Name = "Test" };
        var item = new FlatItem
        {
            Node = node,
            DisplayPath = "Root > Test",
            Name = "Test",
            Count = 10
        };

        item.Node.Should().BeSameAs(node);
        item.DisplayPath.Should().Be("Root > Test");
        item.Name.Should().Be("Test");
        item.Count.Should().Be(10);
    }

    [Fact]
    public void FlatItem_DefaultValues_AreDefault()
    {
        var item = new FlatItem();

        item.Node.Should().BeNull();
        item.DisplayPath.Should().BeEmpty();
        item.Name.Should().BeEmpty();
        item.Count.Should().Be(0);
    }
}
