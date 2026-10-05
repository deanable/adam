using Adam.CatalogBrowser.Services;
using Adam.CatalogBrowser.ViewModels;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Adam.Shared.Services;
using FluentAssertions;

namespace Adam.CatalogBrowser.Tests.ViewModels;

/// <summary>
/// Regression tests for the metadata round-trip: <see cref="MetadataEditorViewModel.SaveAsync"/>
/// must persist to the catalog <em>and</em> write metadata back to the source file via
/// <see cref="MetadataWritebackService"/> (the project's core value). The write-back
/// wiring previously had no test coverage, so it could regress silently.
/// </summary>
public sealed class MetadataEditorWritebackTests : IAsyncLifetime
{
    private readonly string _basePath;
    private readonly string _sourceDir;
    private ModeManager _modeManager = null!;

    public MetadataEditorWritebackTests()
    {
        _basePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _sourceDir = Path.Combine(_basePath, "assets");
    }

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_sourceDir);
        _modeManager = new ModeManager(_basePath);
        await _modeManager.InitializeAsync();
    }

    public Task DisposeAsync()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_basePath))
                Directory.Delete(_basePath, recursive: true);
        }
        catch (IOException) { }
        return Task.CompletedTask;
    }

    [Fact]
    public async Task SaveAsync_ForRawFile_WritesXmpSidecarBesideSource()
    {
        // A RAW file uses an XMP sidecar rather than embedded metadata.
        var rawPath = Path.Combine(_sourceDir, "shot.nef");
        await File.WriteAllBytesAsync(rawPath, [0x4D, 0x4D, 0x00, 0x2A]);
        var assetId = SeedAsset(rawPath, "shot.nef", ".nef");

        var vm = new MetadataEditorViewModel(_modeManager, new SyncUiDispatcher(), writeback: new MetadataWritebackService());
        await vm.LoadAssetAsync(assetId);

        vm.Title = "Round Trip Title";
        vm.Copyright = "ACME 2026";
        await vm.SaveAsync();

        var sidecarPath = Path.Combine(_sourceDir, "shot.xmp");
        File.Exists(sidecarPath).Should().BeTrue("SaveAsync must write an XMP sidecar for RAW files");

        var xmp = await File.ReadAllTextAsync(sidecarPath);
        xmp.Should().Contain("Round Trip Title");
        xmp.Should().Contain("ACME 2026");
    }

    [Fact]
    public async Task SaveAsync_PersistsTitleToCatalog()
    {
        var rawPath = Path.Combine(_sourceDir, "catalog.nef");
        await File.WriteAllBytesAsync(rawPath, [0x4D, 0x4D, 0x00, 0x2A]);
        var assetId = SeedAsset(rawPath, "catalog.nef", ".nef");

        var vm = new MetadataEditorViewModel(_modeManager, new SyncUiDispatcher(), writeback: new MetadataWritebackService());
        await vm.LoadAssetAsync(assetId);
        vm.Title = "Persisted In Catalog";
        await vm.SaveAsync();

        await using var db = await _modeManager.CreateDbContextAsync();
        var stored = await db.DigitalAssets.FindAsync(assetId);
        stored!.Title.Should().Be("Persisted In Catalog");
    }

    [Fact]
    public async Task SaveAsync_WithoutWritebackService_StillSavesCatalogWithoutThrowing()
    {
        var rawPath = Path.Combine(_sourceDir, "noservice.nef");
        await File.WriteAllBytesAsync(rawPath, [0x4D, 0x4D, 0x00, 0x2A]);
        var assetId = SeedAsset(rawPath, "noservice.nef", ".nef");

        // No write-back service supplied (e.g. standalone session that never registered one).
        var vm = new MetadataEditorViewModel(_modeManager, new SyncUiDispatcher());
        await vm.LoadAssetAsync(assetId);
        vm.Title = "Catalog Only";
        await vm.SaveAsync();

        await using var db = await _modeManager.CreateDbContextAsync();
        var stored = await db.DigitalAssets.FindAsync(assetId);
        stored!.Title.Should().Be("Catalog Only");
        File.Exists(Path.Combine(_sourceDir, "noservice.xmp")).Should().BeFalse();
    }

    private Guid SeedAsset(string storagePath, string fileName, string extension)
    {
        var id = Guid.NewGuid();
        using var db = _modeManager.CreateDbContext();
        db.DigitalAssets.Add(new DigitalAsset
        {
            Id = id,
            FileName = fileName,
            FileExtension = extension,
            MimeType = "image/x-nikon-nef",
            FileSize = 4,
            ChecksumSha256 = new string('e', 64),
            StoragePath = storagePath,
            Title = "Original",
            Type = AssetType.Image,
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow,
        });
        db.SaveChanges();
        return id;
    }

    /// <summary>
    /// Runs dispatcher work inline. The default <see cref="AvaloniaUiDispatcher"/>
    /// blocks forever in a headless test host because no dispatcher loop is pumping.
    /// </summary>
    private sealed class SyncUiDispatcher : IUiDispatcher
    {
        public Task InvokeAsync(Action action) { action(); return Task.CompletedTask; }
        public void Post(Action action) => action();
        public bool CheckAccess() => true;
    }
}
