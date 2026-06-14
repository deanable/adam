using Adam.CatalogBrowser.Services;
using Adam.Shared.Models;

namespace Adam.CatalogBrowser.Tests.Services;

public sealed class PresetManagerTests : IDisposable
{
    private readonly PresetManager _sut;
    private readonly string _presetsDir;
    private readonly string _originalBaseDir;

    public PresetManagerTests()
    {
        // Save the original base directory and redirect to an isolated temp directory
        _originalBaseDir = PresetManager.BaseDirectory;
        _presetsDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "presets");
        PresetManager.BaseDirectory = _presetsDir;
        Directory.CreateDirectory(_presetsDir);
        _sut = new PresetManager();
    }

    public void Dispose()
    {
        // Restore original base directory
        PresetManager.BaseDirectory = _originalBaseDir;

        // Clean up test presets
        try
        {
            if (Directory.Exists(_presetsDir))
                Directory.Delete(_presetsDir, recursive: true);
        }
        catch { }
    }

    // ──────────────────────────────────────────────
    //  Save / Load
    // ──────────────────────────────────────────────

    [Fact]
    public async Task SaveThenLoad_RoundTrip_PreservesValues()
    {
        // Arrange
        var preset = new MetadataPreset
        {
            Name = "Test Preset",
            Description = "A test preset",
            Title = "Test Title",
            Keywords = "keyword1|keyword2",
            Categories = "cat1|cat2",
            Rating = 4,
            Label = "Red",
            Flag = "Pick",
            Copyright = "© 2024",
            GpsLatitude = 48.8566,
            GpsLongitude = 2.3522,
            CameraMake = "Canon",
            CameraModel = "EOS R5",
            DateTaken = new DateTime(2024, 6, 15, 10, 0, 0, DateTimeKind.Utc)
        };

        // Act
        await _sut.SavePresetAsync(preset);
        var loaded = await _sut.LoadPresetAsync("Test Preset");

        // Assert
        Assert.NotNull(loaded);
        Assert.Equal("Test Preset", loaded.Name);
        Assert.Equal("A test preset", loaded.Description);
        Assert.Equal("Test Title", loaded.Title);
        Assert.Equal("keyword1|keyword2", loaded.Keywords);
        Assert.Equal("cat1|cat2", loaded.Categories);
        Assert.Equal(4, loaded.Rating);
        Assert.Equal("Red", loaded.Label);
        Assert.Equal("Pick", loaded.Flag);
        Assert.Equal("© 2024", loaded.Copyright);
        Assert.Equal(48.8566, loaded.GpsLatitude);
        Assert.Equal(2.3522, loaded.GpsLongitude);
        Assert.Equal("Canon", loaded.CameraMake);
        Assert.Equal("EOS R5", loaded.CameraModel);
        Assert.Equal(new DateTime(2024, 6, 15, 10, 0, 0, DateTimeKind.Utc), loaded.DateTaken);
    }

    [Fact]
    public async Task SaveThenLoad_MinimalPreset_HandlesNullFields()
    {
        // Arrange
        var preset = new MetadataPreset
        {
            Name = "Minimal",
            Title = "Just a title"
        };

        // Act
        await _sut.SavePresetAsync(preset);
        var loaded = await _sut.LoadPresetAsync("Minimal");

        // Assert
        Assert.NotNull(loaded);
        Assert.Equal("Minimal", loaded.Name);
        Assert.Equal("Just a title", loaded.Title);
        Assert.Null(loaded.Description);
        Assert.Null(loaded.Keywords);
        Assert.Null(loaded.Categories);
        Assert.Null(loaded.Rating);
    }

    [Fact]
    public async Task LoadPreset_NonExistent_ReturnsNull()
    {
        // Act
        var result = await _sut.LoadPresetAsync("NonExistent");

        // Assert
        Assert.Null(result);
    }

    // ──────────────────────────────────────────────
    //  List
    // ──────────────────────────────────────────────

    [Fact]
    public async Task ListPresets_ReturnsAllSavedPresets()
    {
        // Arrange
        await _sut.SavePresetAsync(new MetadataPreset { Name = "Alpha" });
        await _sut.SavePresetAsync(new MetadataPreset { Name = "Beta" });
        await _sut.SavePresetAsync(new MetadataPreset { Name = "Gamma" });

        // Act
        var names = await _sut.ListPresetsAsync();

        // Assert
        Assert.Contains("Alpha", names);
        Assert.Contains("Beta", names);
        Assert.Contains("Gamma", names);
        Assert.Equal(3, names.Count);
    }

    [Fact]
    public async Task ListPresets_WhenEmpty_ReturnsEmpty()
    {
        // Act
        var names = await _sut.ListPresetsAsync();

        // Assert
        Assert.Empty(names);
    }

    // ──────────────────────────────────────────────
    //  Delete
    // ──────────────────────────────────────────────

    [Fact]
    public async Task DeletePreset_RemovesFromList()
    {
        // Arrange
        await _sut.SavePresetAsync(new MetadataPreset { Name = "ToDelete" });
        var before = await _sut.ListPresetsAsync();
        Assert.Single(before);

        // Act
        var deleted = await _sut.DeletePresetAsync("ToDelete");

        // Assert
        Assert.True(deleted);
        var after = await _sut.ListPresetsAsync();
        Assert.Empty(after);
    }

    [Fact]
    public async Task DeletePreset_NonExistent_ReturnsFalse()
    {
        // Act
        var result = await _sut.DeletePresetAsync("NonExistent");

        // Assert
        Assert.False(result);
    }

    // ──────────────────────────────────────────────
    //  Rename
    // ──────────────────────────────────────────────

    [Fact]
    public async Task RenamePreset_UpdatesNameAndPreservesValues()
    {
        // Arrange
        var preset = new MetadataPreset { Name = "OldName", Title = "Test Title", Rating = 3 };
        await _sut.SavePresetAsync(preset);

        // Act
        var renamed = await _sut.RenamePresetAsync("OldName", "NewName");

        // Assert
        Assert.True(renamed);
        var oldLoaded = await _sut.LoadPresetAsync("OldName");
        Assert.Null(oldLoaded);
        var newLoaded = await _sut.LoadPresetAsync("NewName");
        Assert.NotNull(newLoaded);
        Assert.Equal("NewName", newLoaded.Name);
        Assert.Equal("Test Title", newLoaded.Title);
        Assert.Equal(3, newLoaded.Rating);
    }

    [Fact]
    public async Task RenamePreset_NonExistentOldName_ReturnsFalse()
    {
        // Act
        var result = await _sut.RenamePresetAsync("NonExistent", "NewName");

        // Assert
        Assert.False(result);
    }

    // ──────────────────────────────────────────────
    //  Capture and save from DigitalAsset
    // ──────────────────────────────────────────────

    [Fact]
    public async Task CaptureAndSavePresetAsync_CapturesAllFields()
    {
        // Arrange
        var asset = new DigitalAsset
        {
            FileName = "photo.jpg",
            Title = "Sunset",
            Rating = 4,
            Label = AssetLabel.Red,
            Flag = AssetFlag.Pick,
            Copyright = "© 2024",
            GpsLatitude = 48.8566,
            GpsLongitude = 2.3522,
            Keywords = [new Keyword { Name = "sunset", NormalizedName = "sunset" }, new Keyword { Name = "landscape", NormalizedName = "landscape" }],
            Categories = [new Category { Name = "Nature", NormalizedName = "nature" }],
            MetadataProfile = new MetadataProfile
            {
                CameraMake = "Canon",
                CameraModel = "EOS R5",
                DateTaken = new DateTime(2024, 6, 15, 10, 0, 0, DateTimeKind.Utc)
            }
        };

        // Act
        await _sut.CaptureAndSavePresetAsync("My Preset", asset);
        var loaded = await _sut.LoadPresetAsync("My Preset");

        // Assert
        Assert.NotNull(loaded);
        Assert.Equal("My Preset", loaded.Name);
        Assert.Null(loaded.Description); // preset description captures asset.Description, which is null
        Assert.Equal("Sunset", loaded.Title);
        Assert.Equal("sunset|landscape", loaded.Keywords);
        Assert.Equal("Nature", loaded.Categories);
        Assert.Equal(4, loaded.Rating);
        Assert.Equal("Red", loaded.Label);
        Assert.Equal("Pick", loaded.Flag);
        Assert.Equal("© 2024", loaded.Copyright);
        Assert.Equal(48.8566, loaded.GpsLatitude);
        Assert.Equal(2.3522, loaded.GpsLongitude);
        Assert.Equal("Canon", loaded.CameraMake);
        Assert.Equal("EOS R5", loaded.CameraModel);
        Assert.Equal(new DateTime(2024, 6, 15, 10, 0, 0, DateTimeKind.Utc), loaded.DateTaken);
    }

    [Fact]
    public async Task CaptureAndSavePresetAsync_EmptyFields_AreNull()
    {
        // Arrange
        var asset = new DigitalAsset
        {
            FileName = "empty.jpg",
            Title = string.Empty,
            Rating = 0,
            Label = AssetLabel.None,
            Flag = AssetFlag.Unflagged,
            Keywords = [],
            Categories = []
        };

        // Act
        await _sut.CaptureAndSavePresetAsync("Empty", asset);
        var loaded = await _sut.LoadPresetAsync("Empty");

        // Assert
        Assert.NotNull(loaded);
        Assert.Null(loaded.Title); // empty string → null
        Assert.Null(loaded.Rating); // 0 → null
        Assert.Null(loaded.Label); // None → null
        Assert.Null(loaded.Flag); // Unflagged → null
        Assert.Null(loaded.Keywords); // empty → null
        Assert.Null(loaded.Categories); // empty → null
    }

    // ──────────────────────────────────────────────
    //  File name sanitization
    // ──────────────────────────────────────────────

    [Fact]
    public async Task SavePreset_SanitizesInvalidFileChars()
    {
        // Arrange — these chars are invalid in file names on Windows
        var preset = new MetadataPreset { Name = @"Test/<Preset>:*?|""<>" };

        // Act
        await _sut.SavePresetAsync(preset);
        var loaded = await _sut.LoadPresetAsync(preset.Name);

        // Assert
        Assert.NotNull(loaded);
        Assert.Equal(preset.Name, loaded.Name);
    }

    [Fact]
    public void FieldSummary_WithMultipleFields_ReturnsCommaSeparated()
    {
        // Arrange
        var preset = new MetadataPreset
        {
            Name = "Test",
            Title = "My Title",
            Description = "My Desc",
            Keywords = "kw1|kw2",
            Rating = 3
        };

        // Act
        var summary = preset.FieldSummary;

        // Assert
        Assert.Contains("Title", summary);
        Assert.Contains("Description", summary);
        Assert.Contains("Keywords", summary);
        Assert.Contains("Rating", summary);
        Assert.DoesNotContain("Label", summary);
        Assert.DoesNotContain("GPS", summary);
    }

    [Fact]
    public void FieldSummary_EmptyPreset_ReturnsEmptyPlaceholder()
    {
        // Arrange
        var preset = new MetadataPreset { Name = "Empty" };

        // Act
        var summary = preset.FieldSummary;

        // Assert
        Assert.Equal("(empty)", summary);
    }
}
