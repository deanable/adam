using System.ComponentModel;
using System.Windows.Input;
using Adam.CatalogBrowser.ViewModels;
using Adam.Shared.Services;
using FluentAssertions;

namespace Adam.CatalogBrowser.Tests.ViewModels;

/// <summary>
/// Tests for <see cref="MetadataEditorViewModel"/> CanEdit gating, permission tooltip,
/// and SaveCommand defense-in-depth (Phase 7 T7.2).
/// </summary>
public sealed class MetadataEditorViewModelTests : IDisposable
{
    private readonly string _basePath;
    private readonly ModeManager _modeManager;
    private readonly MetadataEditorViewModel _vm;

    public MetadataEditorViewModelTests()
    {
        _basePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _modeManager = new ModeManager(_basePath);
        _vm = new MetadataEditorViewModel(_modeManager);
    }

    // ─────────────────────────────────────────────────────────────────
    //  Constructor: default state
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_CanEdit_DefaultsToTrue()
    {
        _vm.CanEdit.Should().BeTrue();
    }

    [Fact]
    public void Constructor_EditPermissionTooltip_DefaultsToEmpty()
    {
        _vm.EditPermissionTooltip.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_SaveCommand_Exists()
    {
        _vm.SaveCommand.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_SaveCommand_CanExecute_IsFalse_WhenNoAsset()
    {
        // No asset loaded, not dirty → CanExecute false
        _vm.SaveCommand.CanExecute(null).Should().BeFalse();
    }

    // ─────────────────────────────────────────────────────────────────
    //  CanEdit property
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void CanEdit_SetToFalse_RaisesPropertyChanged()
    {
        var changedProperties = new List<string?>();
        _vm.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);

        _vm.CanEdit = false;

        changedProperties.Should().Contain(nameof(MetadataEditorViewModel.CanEdit));
    }

    [Fact]
    public void CanEdit_SetToFalse_RaisesEditPermissionTooltipPropertyChanged()
    {
        var changedProperties = new List<string?>();
        _vm.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);

        _vm.CanEdit = false;

        changedProperties.Should().Contain(nameof(MetadataEditorViewModel.EditPermissionTooltip));
    }

    [Fact]
    public void CanEdit_WhenFalse_EditPermissionTooltip_ReturnsEmpty()
    {
        // EditPermissionTooltip returns empty string when CanEdit is true
        // When CanEdit is false, it returns the stored _editDisabledReason
        // which defaults to empty unless explicitly set
        _vm.CanEdit = false;

        _vm.EditPermissionTooltip.Should().BeEmpty("tooltip text was not set");
    }

    [Fact]
    public void CanEdit_WhenTrue_EditPermissionTooltip_ReturnsEmpty()
    {
        _vm.CanEdit = true;

        _vm.EditPermissionTooltip.Should().BeEmpty();
    }

    [Fact]
    public void CanEdit_SetToTrue_RaisesPropertyChanged()
    {
        _vm.CanEdit = false; // Start with false

        var changedProperties = new List<string?>();
        _vm.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);

        _vm.CanEdit = true;

        changedProperties.Should().Contain(nameof(MetadataEditorViewModel.CanEdit));
    }

    // ─────────────────────────────────────────────────────────────────
    //  EditPermissionTooltip
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void EditPermissionTooltip_Setter_StoresValue_AndReturnsWhenCanEditIsFalse()
    {
        // The getter returns the stored value only when CanEdit is false
        _vm.EditPermissionTooltip = "Requires Editor role";
        _vm.CanEdit = false;

        _vm.EditPermissionTooltip.Should().Be("Requires Editor role");
    }

    [Fact]
    public void EditPermissionTooltip_WhenCanEditFalse_ReturnsStoredText()
    {
        _vm.EditPermissionTooltip = "Sign in to edit metadata";
        _vm.CanEdit = false;

        _vm.EditPermissionTooltip.Should().Be("Sign in to edit metadata");
    }

    [Fact]
    public void EditPermissionTooltip_WhenCanEditTrue_ReturnsEmpty_EvenIfSet()
    {
        _vm.EditPermissionTooltip = "Requires Editor role";
        _vm.CanEdit = true;

        _vm.EditPermissionTooltip.Should().BeEmpty("CanEdit is true, so getter returns empty");
    }

    [Fact]
    public void EditPermissionTooltip_CanEditToggled_UpdatesGetTextCorrectly()
    {
        _vm.EditPermissionTooltip = "Requires Editor role";

        // First set CanEdit = false (tooltip should show)
        _vm.CanEdit = false;
        _vm.EditPermissionTooltip.Should().Be("Requires Editor role");

        // Then set CanEdit = true (tooltip should be hidden)
        _vm.CanEdit = true;
        _vm.EditPermissionTooltip.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────
    //  SaveCommand defense-in-depth
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void SaveCommand_CanExecute_WhenCanEditFalse_ReturnsFalse_EvenIfDirtyAndHasAsset()
    {
        // Simulate having an asset with dirty state
        _vm.HasAsset = true;
        _vm.IsDirty = true;

        // Disable editing
        _vm.CanEdit = false;

        // SaveCommand should be disabled despite having a dirty asset
        _vm.SaveCommand.CanExecute(null).Should().BeFalse("CanEdit is false");
    }

    [Fact]
    public void SaveCommand_CanExecute_WhenCanEditTrue_AndHasAssetAndDirty_ReturnsTrue()
    {
        _vm.HasAsset = true;
        _vm.IsDirty = true;
        _vm.CanEdit = true;

        _vm.SaveCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void SaveCommand_CanExecute_WhenCanEditTrue_ButNotDirty_ReturnsFalse()
    {
        _vm.HasAsset = true;
        _vm.IsDirty = false;
        _vm.CanEdit = true;

        _vm.SaveCommand.CanExecute(null).Should().BeFalse("IsDirty is false");
    }

    [Fact]
    public void SaveCommand_CanExecute_WhenCanEditTrue_ButNoAsset_ReturnsFalse()
    {
        _vm.HasAsset = false;
        _vm.IsDirty = true;
        _vm.CanEdit = true;

        _vm.SaveCommand.CanExecute(null).Should().BeFalse("HasAsset is false");
    }

    [Fact]
    public void SaveCommand_CanExecute_WhenCanEditFalse_RaisesCanExecuteChanged()
    {
        // Subscribing to CanExecuteChanged requires a delegate
        var canExecuteChangedFired = false;
        _vm.SaveCommand.CanExecuteChanged += (_, _) => canExecuteChangedFired = true;

        // Set up conditions that would make CanExecute true
        _vm.HasAsset = true;
        _vm.IsDirty = true;

        // Reset flag
        canExecuteChangedFired = false;

        // Toggle CanEdit to false — should raise CanExecuteChanged
        _vm.CanEdit = false;

        canExecuteChangedFired.Should().BeTrue("SaveCommand.CanExecuteChanged should fire when CanEdit changes");
    }

    [Fact]
    public void SaveCommand_CanExecute_RethrowsOnToggle()
    {
        _vm.HasAsset = true;
        _vm.IsDirty = true;
        _vm.CanEdit = true;

        // Initially CanExecute is true
        _vm.SaveCommand.CanExecute(null).Should().BeTrue();

        // Toggle CanEdit = false
        _vm.CanEdit = false;
        _vm.SaveCommand.CanExecute(null).Should().BeFalse();

        // Toggle CanEdit = true
        _vm.CanEdit = true;
        _vm.SaveCommand.CanExecute(null).Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────────────
    //  PropertyChanged: EditPermissionTooltip sync
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void CanEdit_PropertyChanged_EditPermissionTooltip_GetReturnsCorrectValue()
    {
        // Simulate MainWindowViewModel's pattern: set tooltip text THEN CanEdit
        _vm.EditPermissionTooltip = "Requires Editor or Administrator role";
        _vm.CanEdit = false;

        // Verify the getter returns the correct tooltip text
        _vm.EditPermissionTooltip.Should().Be("Requires Editor or Administrator role");

        // Now toggle back — tooltip should be hidden
        _vm.CanEdit = true;
        _vm.EditPermissionTooltip.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────
    //  Cleanup
    // ─────────────────────────────────────────────────────────────────

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_basePath))
                Directory.Delete(_basePath, recursive: true);
        }
        catch (IOException) { }
    }
}
