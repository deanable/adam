using Adam.CatalogBrowser.Controls;
using Adam.CatalogBrowser.Services;
using Adam.CatalogBrowser.ViewModels;
using System.Reflection;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Adam.Shared.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.CatalogBrowser.Tests.ViewModels;

/// <summary>
/// Tests for the drag-drop command handlers on <see cref="MainWindowViewModel"/>:
/// <c>OnAssignKeywordDrop</c>, <c>OnAssignCategoryDrop</c>, and the
/// helper method <c>ParseAssetIds</c>.  These handlers are invoked via the
/// <see cref="MainWindowViewModel.AssignKeywordDropCommand"/> and
/// <see cref="MainWindowViewModel.AssignCategoryDropCommand"/> public commands.
/// </summary>
public sealed class DropCommandHandlersTests : IAsyncLifetime
{
    private readonly string _basePath;
    private readonly ModeManager _modeManager;
    private readonly NullLogger<BulkOperationQueue> _queueLogger;
    private readonly NullLogger<MainWindowViewModel> _vmLogger;
    private readonly NullLogger<SidebarViewModel> _sidebarLogger;
    private readonly NullLogger<AssetGalleryViewModel> _galleryLogger;
    private readonly NullLogger<IngestionViewModel> _ingestionLogger;
    private BulkOperationQueue _bulkQueue = null!;
    private MainWindowViewModel _vm = null!;

    public DropCommandHandlersTests()
    {
        _basePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _modeManager = new ModeManager(_basePath);
        _queueLogger = new NullLogger<BulkOperationQueue>();
        _vmLogger = new NullLogger<MainWindowViewModel>();
        _sidebarLogger = new NullLogger<SidebarViewModel>();
        _galleryLogger = new NullLogger<AssetGalleryViewModel>();
        _ingestionLogger = new NullLogger<IngestionViewModel>();
    }

    public async Task InitializeAsync()
    {
        await _modeManager.InitializeAsync();

        _bulkQueue = new BulkOperationQueue(_modeManager, _queueLogger);
        var sidebar = new SidebarViewModel(_modeManager, _sidebarLogger);
        var gallery = new AssetGalleryViewModel(_modeManager, _galleryLogger);
        var ingestion = new IngestionViewModel(_modeManager, _ingestionLogger);
        var metadataEditor = new MetadataEditorViewModel(_modeManager);
        var auditLog = new AuditLogViewModel(_modeManager);
        var propertyInspector = new PropertyInspectorViewModel(new NullLogger<PropertyInspectorViewModel>(), _modeManager, new Adam.Shared.Services.MetadataWritebackService());
        var connection = new ConnectionViewModel(new NullLogger<ConnectionViewModel>(), _modeManager);
        var statusBar = new StatusBarViewModel(_bulkQueue);

        _vm = new MainWindowViewModel(
            _vmLogger, _modeManager, new Adam.Shared.Services.MetadataWritebackService(), sidebar, gallery,
            ingestion, metadataEditor,
            auditLog, _bulkQueue,
            propertyInspector, connection, statusBar,
            new DeleteService(_modeManager), new ToastService());

        // Suppress the startup fire-and-forget's IsInitialLoading = false
        // dispatch (it would hang without a pumping dispatcher).
        _vm.StatusBar.IsInitialLoading = false;
    }

    public async Task DisposeAsync()
    {
        await _bulkQueue.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_basePath))
                Directory.Delete(_basePath, recursive: true);
        }
        catch (IOException) { }
    }

    // ──────────────────────────────────────────────
    //  ParseAssetIds
    // ──────────────────────────────────────────────

    [Fact]
    public void ParseAssetIds_ValidCsv_ReturnsGuids()
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var csv = string.Join(",", ids.Select(id => id.ToString()));

        var result = InvokeParseAssetIds(csv);

        result.Should().BeEquivalentTo(ids);
    }

    [Fact]
    public void ParseAssetIds_SingleId_ReturnsSingleGuid()
    {
        var id = Guid.NewGuid();
        var result = InvokeParseAssetIds(id.ToString());
        result.Should().ContainSingle().Which.Should().Be(id);
    }

    [Fact]
    public void ParseAssetIds_Duplicates_ReturnsDistinct()
    {
        var id = Guid.NewGuid();
        var csv = $"{id},{id},{id}";
        var result = InvokeParseAssetIds(csv);
        result.Should().ContainSingle().Which.Should().Be(id);
    }

    [Fact]
    public void ParseAssetIds_EmptyString_ReturnsEmpty()
    {
        var result = InvokeParseAssetIds("");
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseAssetIds_InvalidGuid_ReturnsEmpty()
    {
        var result = InvokeParseAssetIds("not-a-guid");
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseAssetIds_MixedValidAndInvalid_ReturnsEmpty()
    {
        // If any GUID is invalid, the catch clause returns empty
        var id = Guid.NewGuid();
        var result = InvokeParseAssetIds($"{id},bad-guid");
        result.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────
    //  AssignKeywordDropCommand
    // ──────────────────────────────────────────────

    [Fact]
    public async Task AssignKeywordDropCommand_WithKeywordNode_EnqueuesKeywordOperation()
    {
        // Arrange: seed an asset and listen for queue completion
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _bulkQueue.AllCompleted += (_, _) => tcs.TrySetResult();

        var assetId = Guid.NewGuid();
        SeedAssetDirect(assetId);

        // Create a keyword node with valid ID
        var keywordNode = new KeywordNode
        {
            Name = "DropKeyword",
            KeywordId = Guid.NewGuid()
        };

        var payload = new DropPayload
        {
            TargetNode = keywordNode,
            AssetIdsCsv = assetId.ToString()
        };

        // Act
        _vm.AssignKeywordDropCommand.Execute(payload);
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(15));

        // Assert: the asset should now have the keyword
        await using var db = _modeManager.CreateDbContext();
        var asset = await db.DigitalAssets
            .Include(a => a.Keywords)
            .FirstAsync(a => a.Id == assetId);

        asset.Keywords.Should().ContainSingle(k => k.Name == "DropKeyword");
    }

    [Fact]
    public void AssignKeywordDropCommand_WithInvalidKeywordId_DoesNotEnqueue()
    {
        // Empty Guid is invalid — should be ignored
        var keywordNode = new KeywordNode
        {
            Name = "InvalidKeyword",
            KeywordId = Guid.Empty
        };

        var payload = new DropPayload
        {
            TargetNode = keywordNode,
            AssetIdsCsv = Guid.NewGuid().ToString()
        };

        // Should not throw
        _vm.AssignKeywordDropCommand.Execute(payload);
    }

    [Fact]
    public void AssignKeywordDropCommand_WithNonKeywordTarget_DoesNotEnqueue()
    {
        // A CategoryNode dropped on the keyword command — wrong type, ignored
        var categoryNode = new CategoryNode
        {
            Name = "Nature",
            CategoryId = Guid.NewGuid()
        };

        var payload = new DropPayload
        {
            TargetNode = categoryNode,
            AssetIdsCsv = Guid.NewGuid().ToString()
        };

        _vm.AssignKeywordDropCommand.Execute(payload);
    }

    [Fact]
    public void AssignKeywordDropCommand_WithNullPayload_DoesNotThrow()
    {
        _vm.AssignKeywordDropCommand.Execute(null);
    }

    [Fact]
    public void AssignKeywordDropCommand_WithInvalidAssetIdsCsv_DoesNotEnqueue()
    {
        var keywordNode = new KeywordNode
        {
            Name = "TestKeyword",
            KeywordId = Guid.NewGuid()
        };

        var payload = new DropPayload
        {
            TargetNode = keywordNode,
            AssetIdsCsv = "not-a-guid"
        };

        _vm.AssignKeywordDropCommand.Execute(payload);
    }

    // ──────────────────────────────────────────────
    //  AssignCategoryDropCommand
    // ──────────────────────────────────────────────

    [Fact]
    public async Task AssignCategoryDropCommand_WithCategoryNode_EnqueuesCategoryOperation()
    {
        // Arrange
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _bulkQueue.AllCompleted += (_, _) => tcs.TrySetResult();

        var assetId = Guid.NewGuid();
        SeedAssetDirect(assetId);

        var categoryNode = new CategoryNode
        {
            Name = "DropCategory",
            CategoryId = Guid.NewGuid()
        };

        var payload = new DropPayload
        {
            TargetNode = categoryNode,
            AssetIdsCsv = assetId.ToString()
        };

        // Act
        _vm.AssignCategoryDropCommand.Execute(payload);
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(15));

        // Assert
        await using var db = _modeManager.CreateDbContext();
        var asset = await db.DigitalAssets
            .Include(a => a.Categories)
            .FirstAsync(a => a.Id == assetId);

        asset.Categories.Should().ContainSingle(c => c.Name == "DropCategory");
    }

    [Fact]
    public void AssignCategoryDropCommand_WithInvalidCategoryId_DoesNotEnqueue()
    {
        var categoryNode = new CategoryNode
        {
            Name = "InvalidCat",
            CategoryId = Guid.Empty
        };

        var payload = new DropPayload
        {
            TargetNode = categoryNode,
            AssetIdsCsv = Guid.NewGuid().ToString()
        };

        _vm.AssignCategoryDropCommand.Execute(payload);
    }

    [Fact]
    public void AssignCategoryDropCommand_WithNonCategoryTarget_DoesNotEnqueue()
    {
        // A KeywordNode dropped on the category command — wrong type
        var keywordNode = new KeywordNode
        {
            Name = "KeywordAsCategory",
            KeywordId = Guid.NewGuid()
        };

        var payload = new DropPayload
        {
            TargetNode = keywordNode,
            AssetIdsCsv = Guid.NewGuid().ToString()
        };

        _vm.AssignCategoryDropCommand.Execute(payload);
    }

    [Fact]
    public void AssignCategoryDropCommand_WithNullPayload_DoesNotThrow()
    {
        _vm.AssignCategoryDropCommand.Execute(null);
    }

    [Fact]
    public void AssignCategoryDropCommand_EmptyAssetIds_DoesNotEnqueue()
    {
        var categoryNode = new CategoryNode
        {
            Name = "EmptyDrop",
            CategoryId = Guid.NewGuid()
        };

        var payload = new DropPayload
        {
            TargetNode = categoryNode,
            AssetIdsCsv = ""
        };

        _vm.AssignCategoryDropCommand.Execute(payload);
    }

    // ──────────────────────────────────────────────
    //  DropPayload model
    // ──────────────────────────────────────────────

    [Fact]
    public void DropPayload_Properties_SetCorrectly()
    {
        var node = new KeywordNode { Name = "Test" };
        var payload = new DropPayload
        {
            TargetNode = node,
            AssetIdsCsv = "abc-123"
        };

        payload.TargetNode.Should().BeSameAs(node);
        payload.AssetIdsCsv.Should().Be("abc-123");
    }

    [Fact]
    public void DropPayload_DefaultValues_AreDefault()
    {
        var payload = new DropPayload();

        payload.TargetNode.Should().BeNull();
        payload.AssetIdsCsv.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────
    //  Helpers — reflection & seeding
    // ──────────────────────────────────────────────

    private void SetField(string fieldName, object? value)
    {
        var target = (object)_vm;
        var type = typeof(MainWindowViewModel);

        if (fieldName is "_selectedAsset" or "_tagsDirty" or "_descriptionDirty" or "_categoriesDirty" or "_selectedAssets")
        {
            target = _vm.PropertyInspector;
            type = typeof(PropertyInspectorViewModel);
        }
        else if (fieldName is "_isConnectedToService" or "_isServiceMode")
        {
            target = _vm.Connection;
            type = typeof(ConnectionViewModel);
        }
        else if (fieldName is "_isInitialLoading")
        {
            target = _vm.StatusBar;
            type = typeof(StatusBarViewModel);
        }

        var field = type.GetField(fieldName,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public)!;
        field.SetValue(target, value);
    }

    private static List<Guid> InvokeParseAssetIds(string csv)
    {
        var method = typeof(MainWindowViewModel)
            .GetMethod("ParseAssetIds",
                BindingFlags.Static | BindingFlags.NonPublic)!;
        return (List<Guid>)method.Invoke(null, [csv])!;
    }

    private void SeedAssetDirect(Guid assetId)
    {
        using var db = _modeManager.CreateDbContext();
        db.DigitalAssets.Add(new DigitalAsset
        {
            Id = assetId,
            FileName = "drop-test.jpg",
            FileExtension = ".jpg",
            MimeType = "image/jpeg",
            FileSize = 1024,
            ChecksumSha256 = new string('d', 64),
            StoragePath = "drop-test.jpg",
            Title = "Drop Test",
            Type = AssetType.Image,
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow,
        });
        db.SaveChanges();
    }
}
