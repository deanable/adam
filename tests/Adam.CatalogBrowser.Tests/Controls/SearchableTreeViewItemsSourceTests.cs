using Adam.CatalogBrowser.Controls;
using FluentAssertions;

namespace Adam.CatalogBrowser.Tests.Controls;

/// <summary>
/// Tests the <see cref="SearchableTreeView"/> control's dependency-property
/// registrations (<see cref="SearchableTreeView.ItemsSourceProperty"/>,
/// <see cref="SearchableTreeView.SelectedItemProperty"/>,
/// <see cref="SearchableTreeView.DropCommandProperty"/>,
/// <see cref="SearchableTreeView.CountLabelProperty"/>),
/// the <see cref="DropPayload"/> model, and extended <see cref="FlatItem"/> model tests.
///
/// These tests operate on the static property metadata and plain data models only —
/// no Avalonia control instance is created, so they run without a headless platform.
/// (Full control-instantiation + data-flow tests require the Avalonia headless runtime
/// and are left for integration testing.)
/// </summary>
public class SearchableTreeViewItemsSourceTests
{
    // ──────────────────────────────────────────────
    //  DropPayload model
    // ──────────────────────────────────────────────

    [Fact]
    public void DropPayload_Properties_SetCorrectly()
    {
        var node = new object();
        var payload = new DropPayload
        {
            TargetNode = node,
            AssetIdsCsv = "id1,id2,id3"
        };

        payload.TargetNode.Should().BeSameAs(node);
        payload.AssetIdsCsv.Should().Be("id1,id2,id3");
    }

    [Fact]
    public void DropPayload_DefaultValues_AreDefault()
    {
        var payload = new DropPayload();

        payload.TargetNode.Should().BeNull();
        payload.AssetIdsCsv.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────
    //  Static DependencyProperty registrations
    // ──────────────────────────────────────────────

    [Fact]
    public void ItemsSourceProperty_IsRegistered()
    {
        var prop = SearchableTreeView.ItemsSourceProperty;

        prop.Should().NotBeNull();
        prop.Name.Should().Be(nameof(SearchableTreeView.ItemsSource));
        prop.OwnerType.Should().Be<SearchableTreeView>();
    }

    [Fact]
    public void SelectedItemProperty_IsRegistered()
    {
        var prop = SearchableTreeView.SelectedItemProperty;

        prop.Should().NotBeNull();
        prop.Name.Should().Be(nameof(SearchableTreeView.SelectedItem));
        prop.OwnerType.Should().Be<SearchableTreeView>();
    }

    [Fact]
    public void DropCommandProperty_IsRegistered()
    {
        var prop = SearchableTreeView.DropCommandProperty;

        prop.Should().NotBeNull();
        prop.Name.Should().Be(nameof(SearchableTreeView.DropCommand));
        prop.OwnerType.Should().Be<SearchableTreeView>();
    }

    [Fact]
    public void CountLabelProperty_IsRegistered()
    {
        var prop = SearchableTreeView.CountLabelProperty;

        prop.Should().NotBeNull();
        prop.Name.Should().Be(nameof(SearchableTreeView.CountLabel));
        prop.OwnerType.Should().Be<SearchableTreeView>();
    }

    // ──────────────────────────────────────────────
    //  Property metadata verification via reflection
    // ──────────────────────────────────────────────

    [Fact]
    public void ItemsSourceProperty_TypeIsIEnumerable()
    {
        SearchableTreeView.ItemsSourceProperty
            .PropertyType.Should().Be(typeof(System.Collections.IEnumerable));
    }

    // ──────────────────────────────────────────────
    //  Property metadata: default values
    // ──────────────────────────────────────────────

    [Fact]
    public void ItemsSourceProperty_DefaultValueIsNull()
    {
        SearchableTreeView.ItemsSourceProperty
            .GetDefaultValue(typeof(SearchableTreeView)).Should().BeNull();
    }

    [Fact]
    public void SelectedItemProperty_DefaultValueIsNull()
    {
        SearchableTreeView.SelectedItemProperty
            .GetDefaultValue(typeof(SearchableTreeView)).Should().BeNull();
    }

    [Fact]
    public void DropCommandProperty_DefaultValueIsNull()
    {
        SearchableTreeView.DropCommandProperty
            .GetDefaultValue(typeof(SearchableTreeView)).Should().BeNull();
    }

    [Fact]
    public void CountLabelProperty_DefaultValueIsEmptyString()
    {
        SearchableTreeView.CountLabelProperty
            .GetDefaultValue(typeof(SearchableTreeView)).Should().Be(string.Empty);
    }



    // ──────────────────────────────────────────────
    //  DropPayload edge cases
    // ──────────────────────────────────────────────

    [Fact]
    public void DropPayload_AssetIdsCsv_CanBeEmptyString()
    {
        var payload = new DropPayload
        {
            TargetNode = new object(),
            AssetIdsCsv = string.Empty
        };

        payload.AssetIdsCsv.Should().BeEmpty();
    }

    [Fact]
    public void DropPayload_AssetIdsCsv_CanBeLongString()
    {
        var csv = string.Join(",", Enumerable.Range(0, 100).Select(i => $"id{i}"));
        var payload = new DropPayload
        {
            TargetNode = new object(),
            AssetIdsCsv = csv
        };

        payload.AssetIdsCsv.Should().Be(csv);
        payload.AssetIdsCsv.Length.Should().BeGreaterThan(300);
    }
}
