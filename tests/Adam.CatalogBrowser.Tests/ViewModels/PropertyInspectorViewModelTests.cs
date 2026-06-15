using Adam.CatalogBrowser.Models;
using Adam.CatalogBrowser.ViewModels;
using Adam.Shared.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.CatalogBrowser.Tests.ViewModels;

/// <summary>
/// Tests for PropertyInspectorViewModel batch-mode guards and mixed-value detection (T15.5).
/// Follows the same patterns as MainWindowViewModelTests.
/// </summary>
public sealed class PropertyInspectorViewModelTests : IDisposable
{
    private readonly string _basePath;
    private readonly ModeManager _modeManager;
    private readonly PropertyInspectorViewModel _vm;

    public PropertyInspectorViewModelTests()
    {
        _basePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _modeManager = new ModeManager(_basePath);
        _modeManager.InitializeAsync().GetAwaiter().GetResult();

        _vm = new PropertyInspectorViewModel(
            new NullLogger<PropertyInspectorViewModel>(),
            _modeManager,
            new MetadataWritebackService(),
            new SyncUiDispatcher());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_basePath, recursive: true); } catch { }
    }

    // ──────────────────────────────────────────────
    //  Initial state
    // ──────────────────────────────────────────────

    [Fact]
    public void Constructor_InitialState_AllDirtyFlagsFalse()
    {
        _vm.IsBatchMode.Should().BeFalse();
        _vm.IsApplyInProgress.Should().BeFalse();
        _vm.HasSelectedAsset.Should().BeFalse();
        _vm.HasMultiSelection.Should().BeFalse();
        _vm.HasSingleSelection.Should().BeFalse();
    }

    [Fact]
    public void Constructor_NotDirty_CommandCanExecuteFalse()
    {
        _vm.SaveTagsCommand.CanExecute(null).Should().BeFalse();
        _vm.ApplyBatchEditCommand.CanExecute(null).Should().BeFalse();
    }

    // ──────────────────────────────────────────────
    //  Single-selection state
    // ──────────────────────────────────────────────

    [Fact]
    public void SelectedAsset_Set_UpdatesHasSelectedAsset()
    {
        // Arrange
        var asset = new AssetListItem { Id = Guid.NewGuid(), FileName = "test.jpg", Title = "Test" };

        // Act
        _vm.SelectedAsset = asset;

        // Assert
        _vm.HasSelectedAsset.Should().BeTrue();
        _vm.HasSingleSelection.Should().BeTrue();
        _vm.HasMultiSelection.Should().BeFalse();
        _vm.IsBatchMode.Should().BeFalse();
    }

    // ──────────────────────────────────────────────
    //  Multi-selection (batch mode) state
    // ──────────────────────────────────────────────

    [Fact]
    public void SetMultiSelection_TwoAssets_SetsBatchMode()
    {
        // Arrange
        var assets = new List<AssetListItem>
        {
            new() { Id = Guid.NewGuid(), FileName = "a.jpg", Title = "A" },
            new() { Id = Guid.NewGuid(), FileName = "b.jpg", Title = "B" }
        };

        // Act
        _vm.SetMultiSelection(assets);

        // Assert
        _vm.HasMultiSelection.Should().BeTrue();
        _vm.HasSingleSelection.Should().BeFalse();
        _vm.IsBatchMode.Should().BeTrue();
    }

    [Fact]
    public void ApplyBatchEditCommand_NoDirtyFlags_CannotExecute()
    {
        // Arrange
        var assets = new List<AssetListItem>
        {
            new() { Id = Guid.NewGuid(), FileName = "a.jpg", Title = "A" },
            new() { Id = Guid.NewGuid(), FileName = "b.jpg", Title = "B" }
        };
        _vm.SetMultiSelection(assets);

        // No dirty flags set — CanExecute should be false (the command
        // first checks AnyDirty, then checks _modeManager.IsStandalone).
        // In standalone mode, the guard is the dirty flag check.
        _vm.ApplyBatchEditCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void ApplyBatchEditCommand_TagsDirty_CanExecute()
    {
        // Arrange
        var assets = new List<AssetListItem>
        {
            new() { Id = Guid.NewGuid(), FileName = "a.jpg", Title = "A" },
            new() { Id = Guid.NewGuid(), FileName = "b.jpg", Title = "B" }
        };
        _vm.SetMultiSelection(assets);

        // Act — simulate setting tags dirty
        _vm.SelectedAssetTags.Add("NewTag");

        // Assert — dirty + standalone = can execute
        _vm.ApplyBatchEditCommand.CanExecute(null).Should().BeTrue();
    }

    // ──────────────────────────────────────────────
    //  Mixed-value indicators
    // ──────────────────────────────────────────────

    [Fact]
    public void IsBatchMode_Set_RaisesPropertyChanged()
    {
        var changed = new List<string?>();
        _vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _vm.IsBatchMode = true;

        changed.Should().Contain(nameof(PropertyInspectorViewModel.IsBatchMode));
        changed.Should().Contain(nameof(PropertyInspectorViewModel.IsSingleMode));
    }

    [Fact]
    public void SetMultiSelection_SingleAsset_NotBatchMode()
    {
        // Arrange
        var assets = new List<AssetListItem>
        {
            new() { Id = Guid.NewGuid(), FileName = "only.jpg", Title = "Only" }
        };

        // Act
        _vm.SetMultiSelection(assets);

        // Assert
        _vm.HasMultiSelection.Should().BeFalse();
        _vm.IsBatchMode.Should().BeFalse();
    }

    [Fact]
    public void IsApplyInProgress_SetAndGet_RoundTrips()
    {
        _vm.IsApplyInProgress.Should().BeFalse();
        _vm.IsApplyInProgress = true;
        _vm.IsApplyInProgress.Should().BeTrue();
        _vm.IsApplyInProgress = false;
        _vm.IsApplyInProgress.Should().BeFalse();
    }
}
