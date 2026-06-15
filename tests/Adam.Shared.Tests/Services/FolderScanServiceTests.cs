using Adam.Shared.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.Shared.Tests.Services;

/// <summary>
/// Tests for FolderScanService focusing on boundary conditions and
/// validation logic that doesn't require the full scan pipeline.
/// The full end-to-end scan relies on many services (thumbnail generation,
/// metadata extraction, checksumming) that are tested individually.
/// </summary>
public sealed class FolderScanServiceTests : IDisposable
{
    private readonly string _basePath;
    private readonly string _scanDir;
    private readonly ModeManager _modeManager;
    private readonly FolderScanService _sut;

    public FolderScanServiceTests()
    {
        _basePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_basePath);

        _scanDir = Path.Combine(_basePath, "scan");
        Directory.CreateDirectory(_scanDir);

        _modeManager = new ModeManager(_basePath);
        _modeManager.InitializeAsync().GetAwaiter().GetResult();

        _sut = new FolderScanService(
            _modeManager,
            NullLogger<FolderScanService>.Instance);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_basePath, recursive: true); } catch { }
    }

    /// <summary>Creates a test file with text content.</summary>
    private string WriteTestFile(string fileName, string content = "test content")
    {
        var path = Path.Combine(_scanDir, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task ScanFolderAsync_NonExistentFolder_ReturnsZero()
    {
        var badPath = Path.Combine(_basePath, "does_not_exist");
        var count = await _sut.ScanFolderAsync(badPath);
        count.Should().Be(0);
    }

    [Fact]
    public async Task ScanFolderAsync_EmptyFolder_ReturnsZero()
    {
        var count = await _sut.ScanFolderAsync(_scanDir);
        count.Should().Be(0);
    }

    [Fact]
    public async Task ScanFolderAsync_OnlyUnsupportedFiles_ReturnsZero()
    {
        WriteTestFile("script.exe");
        WriteTestFile("data.bin");

        var count = await _sut.ScanFolderAsync(_scanDir);
        count.Should().Be(0);
    }
}
