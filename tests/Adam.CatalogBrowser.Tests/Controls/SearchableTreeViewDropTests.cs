using Adam.CatalogBrowser.Controls;
using Avalonia.Input;
using FluentAssertions;

namespace Adam.CatalogBrowser.Tests.Controls;

/// <summary>
/// Tests the <see cref="SearchableTreeView.ReadDropText"/> method,
/// which reads asset ID text from a <see cref="IDataTransfer"/> during
/// a drag-drop operation. The method uses <c>Contains(DataFormat.Text)</c>
/// on the <see cref="IDataTransfer"/> interface and <c>TryGetText()</c>
/// on the concrete <see cref="DataTransfer"/> class instead of
/// <c>TryGetRaw</c> which may return <c>byte[]</c> after crossing the
/// OLE <c>IDataObject</c> boundary.
///
/// These tests operate on plain <see cref="DataTransfer"/> objects only —
/// no Avalonia control instance is required.
/// </summary>
public class SearchableTreeViewDropTests
{
    [Fact]
    public void ReadDropText_WithTextData_ReturnsText()
    {
        // Arrange
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateText("asset-1,asset-2,asset-3"));

        // Act
        var result = SearchableTreeView.ReadDropText(data);

        // Assert
        result.Should().Be("asset-1,asset-2,asset-3");
    }

    [Fact]
    public void ReadDropText_SingleGuid_ReturnsGuidString()
    {
        // Arrange
        var id = Guid.NewGuid().ToString();
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateText(id));

        // Act
        var result = SearchableTreeView.ReadDropText(data);

        // Assert
        result.Should().Be(id);
    }

    [Fact]
    public void ReadDropText_NullDataTransfer_ReturnsNull()
    {
        // Act
        var result = SearchableTreeView.ReadDropText(null);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ReadDropText_EmptyDataTransfer_ReturnsNull()
    {
        // Arrange
        var data = new DataTransfer();

        // Act
        var result = SearchableTreeView.ReadDropText(data);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ReadDropText_WithEmptyText_ReturnsEmptyString()
    {
        // Arrange — Contains(DataFormat.Text) returns true because there IS
        // a text item, and TryGetText() returns the empty string itself.
        // The caller (OnDrop) uses string.IsNullOrEmpty which catches both.
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateText(""));

        // Act
        var result = SearchableTreeView.ReadDropText(data);

        // Assert
        result.Should().Be("");
    }

    [Fact]
    public void ReadDropText_MultipleItems_FindsFirstText()
    {
        // Arrange
        var data = new DataTransfer();
        data.Add(new DataTransferItem());
        data.Add(DataTransferItem.CreateText("asset-id"));

        // Act
        var result = SearchableTreeView.ReadDropText(data);

        // Assert
        result.Should().Be("asset-id");
    }

    [Fact]
    public void ReadDropText_WithNonTextItems_ReturnsNull()
    {
        // Arrange
        var data = new DataTransfer();
        data.Add(new DataTransferItem());

        // Act
        var result = SearchableTreeView.ReadDropText(data);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ReadDropText_LongCsv_ReturnsFullString()
    {
        // Arrange
        var guids = Enumerable.Range(0, 50).Select(_ => Guid.NewGuid().ToString());
        var csv = string.Join(",", guids);
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateText(csv));

        // Act
        var result = SearchableTreeView.ReadDropText(data);

        // Assert
        result.Should().Be(csv);
    }
}
