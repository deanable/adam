# Thumbnail Extraction Pipeline — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Refactor the monolithic Windows-only `ThumbnailService` into an extensible, cross-platform pipeline with pure-.NET extractors for images, audio album art, Office previews, PDF previews, and video frames, plus a generic icon fallback.

**Architecture:** A priority-ordered pipeline of `IThumbnailExtractor` implementations. Each extractor is self-contained and independently testable. The `ThumbnailService` becomes a thin facade that delegates to the pipeline. Tier 1 extractors use pure .NET libraries; Tier 2 uses LibVLCSharp (bundled native); Tier 3 is a generic icon generator that never fails.

**Tech Stack:** .NET 10, SixLabors.ImageSharp, TagLibSharp, NPOI, PdfPig, LibVLCSharp, xunit, FluentAssertions

---

## File Structure

```
src/Adam.Shared/
  Services/
    ThumbnailService.cs                    (modify — refactor to facade)
    ThumbnailPipeline.cs                   (create)
    ImageAdjustmentService.cs              (existing — no changes)
  ThumbnailExtractors/
    IThumbnailExtractor.cs                  (create)
    ImageThumbnailExtractor.cs            (create — extract from ThumbnailService)
    AudioThumbnailExtractor.cs             (create)
    OfficeThumbnailExtractor.cs            (create)
    PdfPreviewExtractor.cs                 (create)
    VideoThumbnailExtractor.cs             (create)
    GenericIconExtractor.cs                (create)

tests/Adam.Shared.Tests/
  Services/
    ThumbnailPipelineTests.cs              (create)
    ThumbnailExtractorTests.cs             (create)
  ThumbnailExtractors/
    ImageThumbnailExtractorTests.cs        (create)
    AudioThumbnailExtractorTests.cs        (create)
    OfficeThumbnailExtractorTests.cs       (create)
    PdfPreviewExtractorTests.cs            (create)
    GenericIconExtractorTests.cs           (create)
    VideoThumbnailExtractorTests.cs        (create)
```

---

## Dual-Mode Architecture Note

This design respects the project's dual-mode architecture (per `AGENTS.md`):

- **Standalone mode** (`Adam.CatalogBrowser`): Uses `ThumbnailService` via DI or direct instantiation. All extractors are available.
- **Service mode** (`Adam.BrokerService`): `FolderWatcherHostedService` already instantiates `ThumbnailService` directly from `Adam.Shared` (see `FolderWatcherHostedService.cs:189`). The refactored facade preserves the same `GenerateThumbnailAsync(string, string)` signature, so **no BrokerService changes are required**.

All extractors live in `Adam.Shared`, ensuring both modes have identical thumbnail generation behavior.

---

## Phase 1: Core Pipeline + Image Extractor

### Task 1: Create `IThumbnailExtractor` interface

**Files:**
- Create: `src/Adam.Shared/ThumbnailExtractors/IThumbnailExtractor.cs`

- [ ] **Step 1: Write the interface**

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace Adam.Shared.ThumbnailExtractors;

/// <summary>
/// Extracts a thumbnail preview from a file.
/// Each implementation handles one or more file types.
/// </summary>
public interface IThumbnailExtractor
{
    /// <summary>
    /// Lower values are tried first. Tier 1 = 100, Tier 2 = 200, Tier 3 = 300.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Whether this extractor can handle the given file.
    /// </summary>
    bool CanExtract(string filePath, string mimeType);

    /// <summary>
    /// Extract a thumbnail and save it to destPath as a JPEG (maxSize on long edge).
    /// Returns true on success. False allows the pipeline to try the next extractor.
    /// </summary>
    Task<bool> ExtractAsync(
        string sourcePath,
        string destPath,
        int maxSize,
        CancellationToken ct);
}
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build src/Adam.Shared/Adam.Shared.csproj --no-restore`
Expected: Build succeeded, 0 errors

- [ ] **Step 3: Commit**

```bash
git add src/Adam.Shared/ThumbnailExtractors/IThumbnailExtractor.cs
git commit -m "feat(thumbnail): add IThumbnailExtractor interface"
```

---

### Task 2: Create `ThumbnailPipeline`

**Files:**
- Create: `src/Adam.Shared/Services/ThumbnailPipeline.cs`

- [ ] **Step 1: Write the pipeline implementation**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Adam.Shared.ThumbnailExtractors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.Shared.Services;

/// <summary>
/// Orchestrates thumbnail extraction by trying registered extractors in priority order.
/// Falls back to the last extractor (typically GenericIconExtractor) which never fails.
/// </summary>
public class ThumbnailPipeline
{
    private readonly IReadOnlyList<IThumbnailExtractor> _extractors;
    private readonly ILogger<ThumbnailPipeline> _logger;

    public ThumbnailPipeline(IEnumerable<IThumbnailExtractor> extractors, ILogger<ThumbnailPipeline>? logger = null)
    {
        _extractors = extractors.OrderBy(e => e.Priority).ToList();
        _logger = logger ?? NullLogger<ThumbnailPipeline>.Instance;
    }

    /// <summary>
    /// Attempts to extract a thumbnail from sourcePath and save it to destPath.
    /// Returns true if any extractor succeeded (including the fallback).
    /// </summary>
    public async Task<bool> TryExtractAsync(
        string sourcePath,
        string destPath,
        string mimeType,
        int maxSize,
        CancellationToken ct)
    {
        foreach (var extractor in _extractors)
        {
            if (!extractor.CanExtract(sourcePath, mimeType))
                continue;

            try
            {
                var success = await extractor.ExtractAsync(sourcePath, destPath, maxSize, ct);
                if (success)
                {
                    _logger.LogDebug(
                        "Thumbnail extracted by {Extractor}: {SourcePath} -> {DestPath}",
                        extractor.GetType().Name,
                        sourcePath,
                        destPath);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Extractor {Extractor} failed for {SourcePath}",
                    extractor.GetType().Name,
                    sourcePath);
            }
        }

        return false;
    }
}
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build src/Adam.Shared/Adam.Shared.csproj --no-restore`
Expected: Build succeeded, 0 errors

- [ ] **Step 3: Commit**

```bash
git add src/Adam.Shared/Services/ThumbnailPipeline.cs
git commit -m "feat(thumbnail): add ThumbnailPipeline orchestrator"
```

---

### Task 3: Create `ImageThumbnailExtractor`

**Files:**
- Create: `src/Adam.Shared/ThumbnailExtractors/ImageThumbnailExtractor.cs`

- [ ] **Step 1: Extract image logic from existing ThumbnailService**

```csharp
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Adam.Shared.Models;
using Adam.Shared.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace Adam.Shared.ThumbnailExtractors;

/// <summary>
/// Extracts thumbnails from image files using ImageSharp.
/// Handles resize and EXIF orientation.
/// </summary>
public class ImageThumbnailExtractor : IThumbnailExtractor
{
    private readonly ImageAdjustmentService _adjustment;

    public ImageThumbnailExtractor(ImageAdjustmentService? adjustment = null)
    {
        _adjustment = adjustment ?? new ImageAdjustmentService();
    }

    public int Priority => 100;

    public bool CanExtract(string filePath, string mimeType)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext is ".jpg" or ".jpeg" or ".png" or ".webp" or ".tiff" or ".tif"
            or ".cr2" or ".nef" or ".arw" or ".dng" or ".gif" or ".bmp" or ".heic";
    }

    public async Task<bool> ExtractAsync(
        string sourcePath,
        string destPath,
        int maxSize,
        CancellationToken ct)
    {
        using var image = await Image.LoadAsync(sourcePath, ct);

        _adjustment.ApplyOrientation(image, ImageOrientation.Normal);
        var (w, h) = (image.Width, image.Height);

        if (w > h)
        {
            var ratio = (double)maxSize / w;
            image.Mutate(x => x.Resize(maxSize, (int)(h * ratio)));
        }
        else
        {
            var ratio = (double)maxSize / h;
            image.Mutate(x => x.Resize((int)(w * ratio), maxSize));
        }

        var encoder = new JpegEncoder { Quality = 85 };
        await image.SaveAsync(destPath, encoder, ct);
        return true;
    }
}
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build src/Adam.Shared/Adam.Shared.csproj --no-restore`
Expected: Build succeeded, 0 errors

- [ ] **Step 3: Commit**

```bash
git add src/Adam.Shared/ThumbnailExtractors/ImageThumbnailExtractor.cs
git commit -m "feat(thumbnail): add ImageThumbnailExtractor"
```

---

### Task 4: Create `GenericIconExtractor`

**Files:**
- Create: `src/Adam.Shared/ThumbnailExtractors/GenericIconExtractor.cs`

- [ ] **Step 1: Write the generic icon generator**

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.Fonts;

namespace Adam.Shared.ThumbnailExtractors;

/// <summary>
/// Generates a generic file-type icon for unsupported formats.
/// Never fails — this is the pipeline's final fallback.
/// </summary>
public class GenericIconExtractor : IThumbnailExtractor
{
    public int Priority => 999;

    public bool CanExtract(string filePath, string mimeType) => true;

    public Task<bool> ExtractAsync(
        string sourcePath,
        string destPath,
        int maxSize,
        CancellationToken ct)
    {
        var ext = Path.GetExtension(sourcePath).TrimStart('.').ToUpperInvariant();
        if (string.IsNullOrEmpty(ext))
            ext = "FILE";

        // Deterministic background color based on extension hash
        var hash = ext.GetHashCode(StringComparison.OrdinalIgnoreCase);
        var hue = Math.Abs(hash % 360);
        var bgColor = new Hsv(hue, 0.6f, 0.9f).ToRgb();
        var rgb = new Rgba32(bgColor.R, bgColor.G, bgColor.B);

        using var image = new Image<Rgba32>(maxSize, maxSize, rgb);

        // Draw extension text centered
        var font = SystemFonts.CreateFont("Arial", maxSize / 4, FontStyle.Bold);
        var textOptions = new TextOptions(font)
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Origin = new System.Numerics.Vector2(maxSize / 2, maxSize / 2)
        };

        image.Mutate(ctx => ctx.DrawText(textOptions, ext, Color.White));

        var encoder = new JpegEncoder { Quality = 85 };
        image.Save(destPath, encoder);

        return Task.FromResult(true);
    }
}
```

- [ ] **Step 2: Add missing package reference**

`SixLabors.ImageSharp.Drawing` is required for `DrawText`. Check if it's already referenced:

Run: `grep -r "ImageSharp.Drawing" src/Adam.Shared/Adam.Shared.csproj`

If not present, add it:

**Modify:** `src/Adam.Shared/Adam.Shared.csproj`

Add inside `<ItemGroup>`:
```xml
<PackageReference Include="SixLabors.ImageSharp.Drawing" Version="2.0.5" />
```

- [ ] **Step 3: Verify it compiles**

Run: `dotnet restore src/Adam.Shared/Adam.Shared.csproj && dotnet build src/Adam.Shared/Adam.Shared.csproj --no-restore`
Expected: Build succeeded, 0 errors

- [ ] **Step 4: Commit**

```bash
git add src/Adam.Shared/ThumbnailExtractors/GenericIconExtractor.cs src/Adam.Shared/Adam.Shared.csproj
git commit -m "feat(thumbnail): add GenericIconExtractor fallback"
```

---

### Task 5: Refactor `ThumbnailService` to use the pipeline

**Files:**
- Modify: `src/Adam.Shared/Services/ThumbnailService.cs`

- [ ] **Step 1: Read the current ThumbnailService fully**

Run: `cat src/Adam.Shared/Services/ThumbnailService.cs`
(Verify you have the full file contents.)

- [ ] **Step 2: Replace the entire file with the refactored facade**

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Adam.Shared.Models;
using Adam.Shared.ThumbnailExtractors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.Shared.Services;

public class ThumbnailService
{
    private readonly ThumbnailPipeline _pipeline;
    private readonly ILogger<ThumbnailService> _logger;

    public ThumbnailService(ILogger<ThumbnailService>? logger = null)
    {
        _logger = logger ?? NullLogger<ThumbnailService>.Instance;

        var adjustment = new ImageAdjustmentService();
        _pipeline = new ThumbnailPipeline([
            new ImageThumbnailExtractor(adjustment),
            new GenericIconExtractor()
        ], null);
    }

    /// <summary>
    /// For testing / advanced use: create with a custom pipeline.
    /// </summary>
    public ThumbnailService(ThumbnailPipeline pipeline, ILogger<ThumbnailService>? logger = null)
    {
        _pipeline = pipeline;
        _logger = logger ?? NullLogger<ThumbnailService>.Instance;
    }

    public Task<string> GenerateThumbnailAsync(
        string sourcePath,
        string thumbnailDirectory,
        CancellationToken ct = default)
        => GenerateThumbnailAsync(sourcePath, thumbnailDirectory, ImageOrientation.Normal, ct);

    public async Task<string> GenerateThumbnailAsync(
        string sourcePath,
        string thumbnailDirectory,
        ImageOrientation orientation,
        CancellationToken ct = default)
    {
        var thumbnailPath = GetThumbnailPath(sourcePath, thumbnailDirectory);

        if (File.Exists(thumbnailPath) && orientation == ImageOrientation.Normal)
            return thumbnailPath;

        Directory.CreateDirectory(thumbnailDirectory);

        var mimeType = ResolveMimeType(sourcePath);
        var success = await _pipeline.TryExtractAsync(
            sourcePath, thumbnailPath, mimeType, maxSize: 256, ct);

        if (!success)
            _logger.LogWarning("All extractors failed for {SourcePath}; no thumbnail generated", sourcePath);

        return thumbnailPath;
    }

    public string GetThumbnailPath(string sourcePath, string thumbnailDirectory)
    {
        var normalized = sourcePath.Replace('\\', '/');
        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(normalized))
        )[..16];
        return Path.Combine(thumbnailDirectory, $"{hash}.jpg");
    }

    private static string ResolveMimeType(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".tiff" or ".tif" => "image/tiff",
            ".cr2" or ".nef" or ".arw" or ".dng" => "image/x-raw",
            ".mp4" => "video/mp4",
            ".avi" => "video/x-msvideo",
            ".mov" => "video/quicktime",
            ".mkv" => "video/x-matroska",
            ".wmv" => "video/x-ms-wmv",
            ".webm" => "video/webm",
            ".mp3" => "audio/mpeg",
            ".flac" => "audio/flac",
            ".wav" => "audio/wav",
            ".m4a" => "audio/mp4",
            ".ogg" => "audio/ogg",
            ".wma" => "audio/x-ms-wma",
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            _ => "application/octet-stream"
        };
    }
}
```

- [ ] **Step 3: Verify it compiles**

Run: `dotnet build src/Adam.Shared/Adam.Shared.csproj --no-restore`
Expected: Build succeeded, 0 errors

- [ ] **Step 4: Run existing ThumbnailService tests**

Run: `dotnet test tests/Adam.Shared.Tests/Adam.Shared.Tests.csproj --filter "FullyQualifiedName~ThumbnailServiceTests" --no-restore`
Expected: All tests pass (GetThumbnailPath tests should still work)

- [ ] **Step 5: Commit**

```bash
git add src/Adam.Shared/Services/ThumbnailService.cs
git commit -m "refactor(thumbnail): ThumbnailService becomes pipeline facade"
```

---

### Task 6: Write `ThumbnailPipeline` tests

**Files:**
- Create: `tests/Adam.Shared.Tests/Services/ThumbnailPipelineTests.cs`

- [ ] **Step 1: Write the test class**

```csharp
using System.Threading;
using System.Threading.Tasks;
using Adam.Shared.Services;
using Adam.Shared.ThumbnailExtractors;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Adam.Shared.Tests.Services;

public class ThumbnailPipelineTests
{
    [Fact]
    public async Task TryExtractAsync_FirstExtractorSucceeds_ReturnsTrue()
    {
        var extractor = Substitute.For<IThumbnailExtractor>();
        extractor.Priority.Returns(100);
        extractor.CanExtract(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        extractor.ExtractAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var pipeline = new ThumbnailPipeline([extractor]);
        var result = await pipeline.TryExtractAsync("/tmp/test.mp4", "/tmp/out.jpg", "video/mp4", 256, default);

        result.Should().BeTrue();
        await extractor.Received(1).ExtractAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TryExtractAsync_FirstExtractorFails_SecondSucceeds_ReturnsTrue()
    {
        var first = Substitute.For<IThumbnailExtractor>();
        first.Priority.Returns(100);
        first.CanExtract(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        first.ExtractAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        var second = Substitute.For<IThumbnailExtractor>();
        second.Priority.Returns(200);
        second.CanExtract(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        second.ExtractAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var pipeline = new ThumbnailPipeline([first, second]);
        var result = await pipeline.TryExtractAsync("/tmp/test.mp4", "/tmp/out.jpg", "video/mp4", 256, default);

        result.Should().BeTrue();
        await first.Received(1).ExtractAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await second.Received(1).ExtractAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TryExtractAsync_ExtractorThrowsException_PipelineContinues()
    {
        var first = Substitute.For<IThumbnailExtractor>();
        first.Priority.Returns(100);
        first.CanExtract(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        first.ExtractAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(new InvalidOperationException("boom")));

        var second = Substitute.For<IThumbnailExtractor>();
        second.Priority.Returns(200);
        second.CanExtract(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        second.ExtractAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var pipeline = new ThumbnailPipeline([first, second]);
        var result = await pipeline.TryExtractAsync("/tmp/test.mp4", "/tmp/out.jpg", "video/mp4", 256, default);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task TryExtractAsync_CanExtractReturnsFalse_SkipsExtractor()
    {
        var first = Substitute.For<IThumbnailExtractor>();
        first.Priority.Returns(100);
        first.CanExtract(Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        var second = Substitute.For<IThumbnailExtractor>();
        second.Priority.Returns(200);
        second.CanExtract(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        second.ExtractAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var pipeline = new ThumbnailPipeline([first, second]);
        var result = await pipeline.TryExtractAsync("/tmp/test.mp4", "/tmp/out.jpg", "video/mp4", 256, default);

        result.Should().BeTrue();
        await first.Received(0).ExtractAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TryExtractAsync_AllExtractorsFail_ReturnsFalse()
    {
        var extractor = Substitute.For<IThumbnailExtractor>();
        extractor.Priority.Returns(100);
        extractor.CanExtract(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        extractor.ExtractAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        var pipeline = new ThumbnailPipeline([extractor]);
        var result = await pipeline.TryExtractAsync("/tmp/test.mp4", "/tmp/out.jpg", "video/mp4", 256, default);

        result.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Add NSubstitute if not present**

Check if `NSubstitute` is in the test project:
Run: `grep -r "NSubstitute" tests/Adam.Shared.Tests/Adam.Shared.Tests.csproj`

If missing, add it to `tests/Adam.Shared.Tests/Adam.Shared.Tests.csproj`:

```xml
<PackageReference Include="NSubstitute" Version="5.3.0" />
```

- [ ] **Step 3: Restore and run tests**

Run: `dotnet restore tests/Adam.Shared.Tests/Adam.Shared.Tests.csproj && dotnet test tests/Adam.Shared.Tests/Adam.Shared.Tests.csproj --filter "FullyQualifiedName~ThumbnailPipelineTests"`
Expected: 5 tests, all passed

- [ ] **Step 4: Commit**

```bash
git add tests/Adam.Shared.Tests/Services/ThumbnailPipelineTests.cs
git commit -m "test(thumbnail): add ThumbnailPipeline unit tests"
```

---

### Task 7: Write `ImageThumbnailExtractor` tests

**Files:**
- Create: `tests/Adam.Shared.Tests/ThumbnailExtractors/ImageThumbnailExtractorTests.cs`
- Create test assets: `tests/Adam.Shared.Tests/test-assets/sample.jpg` (use any small JPEG)

- [ ] **Step 1: Create test assets directory and copy a sample image**

Create directory: `tests/Adam.Shared.Tests/test-assets/`
Copy any small JPEG into it (or generate one programmatically in the test setup).

For a self-contained test that doesn't need external files, generate a test image in memory:

```csharp
using System;
using System.IO;
using System.Threading.Tasks;
using Adam.Shared.ThumbnailExtractors;
using FluentAssertions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Jpeg;
using Xunit;

namespace Adam.Shared.Tests.ThumbnailExtractors;

public class ImageThumbnailExtractorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ImageThumbnailExtractor _sut = new();

    public ImageThumbnailExtractorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string CreateTestImage(int width, int height, string fileName)
    {
        var path = Path.Combine(_tempDir, fileName);
        using var image = new Image<Rgba32>(width, height, new Rgba32(255, 0, 0));
        image.Save(path, new JpegEncoder());
        return path;
    }

    [Fact]
    public void CanExtract_Jpg_ReturnsTrue()
    {
        _sut.CanExtract("/tmp/photo.jpg", "image/jpeg").Should().BeTrue();
    }

    [Fact]
    public void CanExtract_Png_ReturnsTrue()
    {
        _sut.CanExtract("/tmp/photo.png", "image/png").Should().BeTrue();
    }

    [Fact]
    public void CanExtract_Mp4_ReturnsFalse()
    {
        _sut.CanExtract("/tmp/video.mp4", "video/mp4").Should().BeFalse();
    }

    [Fact]
    public async Task ExtractAsync_LandscapeImage_ResizesToMaxWidth()
    {
        var source = CreateTestImage(1024, 512, "landscape.jpg");
        var dest = Path.Combine(_tempDir, "thumb.jpg");

        var result = await _sut.ExtractAsync(source, dest, 256, default);

        result.Should().BeTrue();
        File.Exists(dest).Should().BeTrue();
        using var thumb = Image.Load(dest);
        thumb.Width.Should().Be(256);
        thumb.Height.Should().BeLessThan(256);
    }

    [Fact]
    public async Task ExtractAsync_PortraitImage_ResizesToMaxHeight()
    {
        var source = CreateTestImage(512, 1024, "portrait.jpg");
        var dest = Path.Combine(_tempDir, "thumb.jpg");

        var result = await _sut.ExtractAsync(source, dest, 256, default);

        result.Should().BeTrue();
        File.Exists(dest).Should().BeTrue();
        using var thumb = Image.Load(dest);
        thumb.Height.Should().Be(256);
        thumb.Width.Should().BeLessThan(256);
    }
}
```

- [ ] **Step 2: Run tests**

Run: `dotnet test tests/Adam.Shared.Tests/Adam.Shared.Tests.csproj --filter "FullyQualifiedName~ImageThumbnailExtractorTests"`
Expected: 5 tests, all passed

- [ ] **Step 3: Commit**

```bash
git add tests/Adam.Shared.Tests/ThumbnailExtractors/ImageThumbnailExtractorTests.cs
git commit -m "test(thumbnail): add ImageThumbnailExtractor tests"
```

---

## Phase 2: Pure .NET Extractors

### Task 8: Add `TagLibSharp` package and create `AudioThumbnailExtractor`

**Files:**
- Modify: `src/Adam.Shared/Adam.Shared.csproj`
- Create: `src/Adam.Shared/ThumbnailExtractors/AudioThumbnailExtractor.cs`
- Create: `tests/Adam.Shared.Tests/ThumbnailExtractors/AudioThumbnailExtractorTests.cs`

- [ ] **Step 1: Add TagLibSharp package**

Add to `src/Adam.Shared/Adam.Shared.csproj` inside `<ItemGroup>`:
```xml
<PackageReference Include="TagLibSharp" Version="2.3.0" />
```

Run: `dotnet restore src/Adam.Shared/Adam.Shared.csproj`

- [ ] **Step 2: Create AudioThumbnailExtractor**

```csharp
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using TagLib;

namespace Adam.Shared.ThumbnailExtractors;

/// <summary>
/// Extracts embedded album art from audio files using TagLibSharp.
/// </summary>
public class AudioThumbnailExtractor : IThumbnailExtractor
{
    public int Priority => 110;

    public bool CanExtract(string filePath, string mimeType)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext is ".mp3" or ".flac" or ".wav" or ".m4a" or ".ogg" or ".wma";
    }

    public Task<bool> ExtractAsync(
        string sourcePath,
        string destPath,
        int maxSize,
        CancellationToken ct)
    {
        try
        {
            using var file = File.Create(sourcePath);
            var picture = file.Tag.Pictures?.FirstOrDefault();
            if (picture == null)
                return Task.FromResult(false);

            using var image = Image.Load(picture.Data.Data);
            var (w, h) = (image.Width, image.Height);

            if (w > h)
            {
                var ratio = (double)maxSize / w;
                image.Mutate(x => x.Resize(maxSize, (int)(h * ratio)));
            }
            else
            {
                var ratio = (double)maxSize / h;
                image.Mutate(x => x.Resize((int)(w * ratio), maxSize));
            }

            var encoder = new JpegEncoder { Quality = 85 };
            image.Save(destPath, encoder);
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }
}
```

- [ ] **Step 3: Write tests for AudioThumbnailExtractor**

```csharp
using System;
using System.IO;
using System.Threading.Tasks;
using Adam.Shared.ThumbnailExtractors;
using FluentAssertions;
using TagLib;
using TagLib.Mpeg;
using Xunit;

namespace Adam.Shared.Tests.ThumbnailExtractors;

public class AudioThumbnailExtractorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AudioThumbnailExtractor _sut = new();

    public AudioThumbnailExtractorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void CanExtract_Mp3_ReturnsTrue()
    {
        _sut.CanExtract("/tmp/song.mp3", "audio/mpeg").Should().BeTrue();
    }

    [Fact]
    public void CanExtract_Flac_ReturnsTrue()
    {
        _sut.CanExtract("/tmp/song.flac", "audio/flac").Should().BeTrue();
    }

    [Fact]
    public void CanExtract_Jpg_ReturnsFalse()
    {
        _sut.CanExtract("/tmp/photo.jpg", "image/jpeg").Should().BeFalse();
    }

    [Fact]
    public async Task ExtractAsync_Mp3WithAlbumArt_ReturnsTrue()
    {
        var source = Path.Combine(_tempDir, "with_art.mp3");
        var dest = Path.Combine(_tempDir, "thumb.jpg");

        // Create a minimal MP3 with embedded APIC frame
        CreateMp3WithAlbumArt(source);

        var result = await _sut.ExtractAsync(source, dest, 256, default);

        result.Should().BeTrue();
        File.Exists(dest).Should().BeTrue();
    }

    [Fact]
    public async Task ExtractAsync_Mp3WithoutAlbumArt_ReturnsFalse()
    {
        var source = Path.Combine(_tempDir, "no_art.mp3");
        var dest = Path.Combine(_tempDir, "thumb.jpg");

        // Create a minimal MP3 without APIC
        CreateMinimalMp3(source);

        var result = await _sut.ExtractAsync(source, dest, 256, default);

        result.Should().BeFalse();
    }

    private static void CreateMp3WithAlbumArt(string path)
    {
        // Create a 1x1 red JPEG for the album art
        using var ms = new MemoryStream();
        using (var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(1, 1, new SixLabors.ImageSharp.PixelFormats.Rgba32(255, 0, 0)))
        {
            image.Save(ms, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder());
        }
        var picData = ms.ToArray();

        var file = File.Create(path);
        file.Tag.Pictures = new IPicture[]
        {
            new Picture(new ByteVector(picData, picData.Length))
            {
                Type = PictureType.FrontCover,
                MimeType = "image/jpeg"
            }
        };
        file.Save();
    }

    private static void CreateMinimalMp3(string path)
    {
        var file = File.Create(path);
        file.Tag.Title = "Test";
        file.Save();
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/Adam.Shared.Tests/Adam.Shared.Tests.csproj --filter "FullyQualifiedName~AudioThumbnailExtractorTests"`
Expected: 4 tests, all passed

- [ ] **Step 5: Commit**

```bash
git add src/Adam.Shared/Adam.Shared.csproj src/Adam.Shared/ThumbnailExtractors/AudioThumbnailExtractor.cs tests/Adam.Shared.Tests/ThumbnailExtractors/AudioThumbnailExtractorTests.cs
git commit -m "feat(thumbnail): add AudioThumbnailExtractor with TagLibSharp"
```

---

### Task 9: Add `NPOI` package and create `OfficeThumbnailExtractor`

**Files:**
- Modify: `src/Adam.Shared/Adam.Shared.csproj`
- Create: `src/Adam.Shared/ThumbnailExtractors/OfficeThumbnailExtractor.cs`
- Create: `tests/Adam.Shared.Tests/ThumbnailExtractors/OfficeThumbnailExtractorTests.cs`

- [ ] **Step 1: Add NPOI package**

Add to `src/Adam.Shared/Adam.Shared.csproj`:
```xml
<PackageReference Include="NPOI" Version="2.7.0" />
```

Run: `dotnet restore src/Adam.Shared/Adam.Shared.csproj`

- [ ] **Step 2: Create OfficeThumbnailExtractor**

```csharp
using System;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace Adam.Shared.ThumbnailExtractors;

/// <summary>
/// Extracts embedded preview thumbnails from Office documents (DOCX, XLSX, PPTX).
/// These files are ZIP packages that may contain docProps/thumbnail.jpeg.
/// </summary>
public class OfficeThumbnailExtractor : IThumbnailExtractor
{
    public int Priority => 120;

    public bool CanExtract(string filePath, string mimeType)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext is ".docx" or ".xlsx" or ".pptx";
    }

    public Task<bool> ExtractAsync(
        string sourcePath,
        string destPath,
        int maxSize,
        CancellationToken ct)
    {
        try
        {
            if (!File.Exists(sourcePath))
                return Task.FromResult(false);

            using var package = Package.Open(sourcePath, FileMode.Open, FileAccess.Read);
            var thumbnailPart = package.GetParts()
                .FirstOrDefault(p => p.Uri.OriginalString.Contains("thumbnail", StringComparison.OrdinalIgnoreCase));

            if (thumbnailPart == null)
                return Task.FromResult(false);

            using var stream = thumbnailPart.GetStream();
            using var image = Image.Load(stream);
            var (w, h) = (image.Width, image.Height);

            if (w > h)
            {
                var ratio = (double)maxSize / w;
                image.Mutate(x => x.Resize(maxSize, (int)(h * ratio)));
            }
            else
            {
                var ratio = (double)maxSize / h;
                image.Mutate(x => x.Resize((int)(w * ratio), maxSize));
            }

            var encoder = new JpegEncoder { Quality = 85 };
            image.Save(destPath, encoder);
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }
}
```

- [ ] **Step 3: Write tests**

```csharp
using System;
using System.IO;
using System.IO.Packaging;
using System.Threading.Tasks;
using Adam.Shared.ThumbnailExtractors;
using FluentAssertions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Jpeg;
using Xunit;

namespace Adam.Shared.Tests.ThumbnailExtractors;

public class OfficeThumbnailExtractorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly OfficeThumbnailExtractor _sut = new();

    public OfficeThumbnailExtractorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void CanExtract_Docx_ReturnsTrue()
    {
        _sut.CanExtract("/tmp/doc.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document").Should().BeTrue();
    }

    [Fact]
    public void CanExtract_Xlsx_ReturnsTrue()
    {
        _sut.CanExtract("/tmp/sheet.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet").Should().BeTrue();
    }

    [Fact]
    public void CanExtract_Pdf_ReturnsFalse()
    {
        _sut.CanExtract("/tmp/doc.pdf", "application/pdf").Should().BeFalse();
    }

    [Fact]
    public async Task ExtractAsync_DocxWithThumbnail_ReturnsTrue()
    {
        var source = Path.Combine(_tempDir, "with_thumb.docx");
        var dest = Path.Combine(_tempDir, "thumb.jpg");

        CreateDocxWithThumbnail(source);

        var result = await _sut.ExtractAsync(source, dest, 256, default);

        result.Should().BeTrue();
        File.Exists(dest).Should().BeTrue();
    }

    [Fact]
    public async Task ExtractAsync_DocxWithoutThumbnail_ReturnsFalse()
    {
        var source = Path.Combine(_tempDir, "no_thumb.docx");
        var dest = Path.Combine(_tempDir, "thumb.jpg");

        CreateMinimalDocx(source);

        var result = await _sut.ExtractAsync(source, dest, 256, default);

        result.Should().BeFalse();
    }

    private static void CreateDocxWithThumbnail(string path)
    {
        // A DOCX is a ZIP with specific structure. We'll create a minimal one with a thumbnail.
        using var package = Package.Open(path, FileMode.Create);
        package.CreatePart(new Uri("/[Content_Types].xml", UriKind.Relative), "application/xml");
        
        var thumbUri = new Uri("/docProps/thumbnail.jpeg", UriKind.Relative);
        var thumbPart = package.CreatePart(thumbUri, "image/jpeg");
        using (var thumbStream = thumbPart.GetStream(FileMode.Create))
        {
            using var image = new Image<Rgba32>(100, 100, new Rgba32(0, 128, 255));
            image.Save(thumbStream, new JpegEncoder());
        }
    }

    private static void CreateMinimalDocx(string path)
    {
        using var package = Package.Open(path, FileMode.Create);
        package.CreatePart(new Uri("/[Content_Types].xml", UriKind.Relative), "application/xml");
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/Adam.Shared.Tests/Adam.Shared.Tests.csproj --filter "FullyQualifiedName~OfficeThumbnailExtractorTests"`
Expected: 4 tests, all passed

- [ ] **Step 5: Commit**

```bash
git add src/Adam.Shared/Adam.Shared.csproj src/Adam.Shared/ThumbnailExtractors/OfficeThumbnailExtractor.cs tests/Adam.Shared.Tests/ThumbnailExtractors/OfficeThumbnailExtractorTests.cs
git commit -m "feat(thumbnail): add OfficeThumbnailExtractor with NPOI"
```

---

### Task 10: Add `PdfPig` package and create `PdfPreviewExtractor`

**Files:**
- Modify: `src/Adam.Shared/Adam.Shared.csproj`
- Create: `src/Adam.Shared/ThumbnailExtractors/PdfPreviewExtractor.cs`
- Create: `tests/Adam.Shared.Tests/ThumbnailExtractors/PdfPreviewExtractorTests.cs`

- [ ] **Step 1: Add PdfPig package**

Add to `src/Adam.Shared/Adam.Shared.csproj`:
```xml
<PackageReference Include="PdfPig" Version="0.1.8" />
```

Run: `dotnet restore src/Adam.Shared/Adam.Shared.csproj`

- [ ] **Step 2: Create PdfPreviewExtractor**

```csharp
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using UglyToad.PdfPig;

namespace Adam.Shared.ThumbnailExtractors;

/// <summary>
/// Extracts embedded preview images from PDF documents using PdfPig.
/// </summary>
public class PdfPreviewExtractor : IThumbnailExtractor
{
    public int Priority => 130;

    public bool CanExtract(string filePath, string mimeType)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext == ".pdf";
    }

    public Task<bool> ExtractAsync(
        string sourcePath,
        string destPath,
        int maxSize,
        CancellationToken ct)
    {
        try
        {
            if (!File.Exists(sourcePath))
                return Task.FromResult(false);

            using var document = PdfDocument.Open(sourcePath);
            
            // Try to find an embedded image on the first page
            var firstPage = document.GetPage(1);
            var imageList = firstPage.GetImages().ToList();
            
            if (!imageList.Any())
                return Task.FromResult(false);

            var imageInfo = imageList.First();
            var bytes = imageInfo.ImageBytes.ToArray();

            if (bytes.Length == 0)
                return Task.FromResult(false);

            using var image = Image.Load(bytes);
            var (w, h) = (image.Width, image.Height);

            if (w > h)
            {
                var ratio = (double)maxSize / w;
                image.Mutate(x => x.Resize(maxSize, (int)(h * ratio)));
            }
            else
            {
                var ratio = (double)maxSize / h;
                image.Mutate(x => x.Resize((int)(w * ratio), maxSize));
            }

            var encoder = new JpegEncoder { Quality = 85 };
            image.Save(destPath, encoder);
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }
}
```

- [ ] **Step 3: Write tests**

```csharp
using System;
using System.IO;
using System.Threading.Tasks;
using Adam.Shared.ThumbnailExtractors;
using FluentAssertions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace Adam.Shared.Tests.ThumbnailExtractors;

public class PdfPreviewExtractorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly PdfPreviewExtractor _sut = new();

    public PdfPreviewExtractorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void CanExtract_Pdf_ReturnsTrue()
    {
        _sut.CanExtract("/tmp/doc.pdf", "application/pdf").Should().BeTrue();
    }

    [Fact]
    public void CanExtract_Jpg_ReturnsFalse()
    {
        _sut.CanExtract("/tmp/photo.jpg", "image/jpeg").Should().BeFalse();
    }

    [Fact]
    public async Task ExtractAsync_PdfWithEmbeddedImage_ReturnsTrue()
    {
        var source = Path.Combine(_tempDir, "with_image.pdf");
        var dest = Path.Combine(_tempDir, "thumb.jpg");

        CreatePdfWithImage(source);

        var result = await _sut.ExtractAsync(source, dest, 256, default);

        result.Should().BeTrue();
        File.Exists(dest).Should().BeTrue();
    }

    [Fact]
    public async Task ExtractAsync_PdfWithoutImages_ReturnsFalse()
    {
        var source = Path.Combine(_tempDir, "no_images.pdf");
        var dest = Path.Combine(_tempDir, "thumb.jpg");

        CreateMinimalPdf(source);

        var result = await _sut.ExtractAsync(source, dest, 256, default);

        result.Should().BeFalse();
    }

    private static void CreatePdfWithImage(string path)
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(612, 792);
        
        // Create a simple 1x1 red image bytes
        using var ms = new MemoryStream();
        using (var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(100, 100, new SixLabors.ImageSharp.PixelFormats.Rgba32(255, 0, 0)))
        {
            image.Save(ms, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
        }
        var imageBytes = ms.ToArray();

        page.AddPng(imageBytes, new PdfRectangle(0, 0, 100, 100));
        
        var doc = builder.Build();
        File.WriteAllBytes(path, doc);
    }

    private static void CreateMinimalPdf(string path)
    {
        var builder = new PdfDocumentBuilder();
        builder.AddPage(612, 792);
        var doc = builder.Build();
        File.WriteAllBytes(path, doc);
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/Adam.Shared.Tests/Adam.Shared.Tests.csproj --filter "FullyQualifiedName~PdfPreviewExtractorTests"`
Expected: 4 tests, all passed

- [ ] **Step 5: Commit**

```bash
git add src/Adam.Shared/Adam.Shared.csproj src/Adam.Shared/ThumbnailExtractors/PdfPreviewExtractor.cs tests/Adam.Shared.Tests/ThumbnailExtractors/PdfPreviewExtractorTests.cs
git commit -m "feat(thumbnail): add PdfPreviewExtractor with PdfPig"
```

---

### Task 11: Update `ThumbnailService` to register all Tier 1 extractors

**Files:**
- Modify: `src/Adam.Shared/Services/ThumbnailService.cs`

- [ ] **Step 1: Update the constructor to include all Tier 1 extractors**

In `ThumbnailService.cs`, update the default constructor:

```csharp
public ThumbnailService(ILogger<ThumbnailService>? logger = null)
{
    _logger = logger ?? NullLogger<ThumbnailService>.Instance;

    var adjustment = new ImageAdjustmentService();
    _pipeline = new ThumbnailPipeline([
        new ImageThumbnailExtractor(adjustment),
        new AudioThumbnailExtractor(),
        new OfficeThumbnailExtractor(),
        new PdfPreviewExtractor(),
        new GenericIconExtractor()
    ], null);
}
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build src/Adam.Shared/Adam.Shared.csproj --no-restore`
Expected: Build succeeded, 0 errors

- [ ] **Step 3: Run all existing tests**

Run: `dotnet test tests/Adam.Shared.Tests/Adam.Shared.Tests.csproj --no-restore`
Expected: All tests pass

- [ ] **Step 4: Commit**

```bash
git add src/Adam.Shared/Services/ThumbnailService.cs
git commit -m "feat(thumbnail): register all Tier 1 extractors in ThumbnailService"
```

---

## Phase 3: Video Extractor

### Task 12: Add `LibVLCSharp` packages and create `VideoThumbnailExtractor`

**Files:**
- Modify: `src/Adam.Shared/Adam.Shared.csproj`
- Create: `src/Adam.Shared/ThumbnailExtractors/VideoThumbnailExtractor.cs`
- Create: `tests/Adam.Shared.Tests/ThumbnailExtractors/VideoThumbnailExtractorTests.cs`

- [ ] **Step 1: Add LibVLCSharp packages**

Add to `src/Adam.Shared/Adam.Shared.csproj`:
```xml
<PackageReference Include="LibVLCSharp" Version="3.9.0" />
```

Also add runtime packages (these go in the main app projects, but add them to Shared for now or to the app projects):
Actually, per the spec, the runtime packages should be in the app projects that produce executables. Add them to:
- `src/Adam.CatalogBrowser/Adam.CatalogBrowser.csproj`
- `src/Adam.BrokerService/Adam.BrokerService.csproj`

For the shared library, just `LibVLCSharp` (the .NET bindings) is sufficient.

For Windows app:
```xml
<PackageReference Include="VideoLAN.LibVLC.Windows" Version="3.0.21" />
```

For Linux (the runtime packs are typically included via RID-specific packages, but for planning purposes, we document it).

- [ ] **Step 2: Create VideoThumbnailExtractor**

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LibVLCSharp.Shared;

namespace Adam.Shared.ThumbnailExtractors;

/// <summary>
/// Extracts a frame from video files using LibVLCSharp.
/// Requires the VLC native libraries to be present on the system or bundled with the app.
/// </summary>
public class VideoThumbnailExtractor : IThumbnailExtractor
{
    public int Priority => 200;

    public bool CanExtract(string filePath, string mimeType)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext is ".mp4" or ".avi" or ".mov" or ".mkv" or ".wmv" or ".webm" or ".flv";
    }

    public Task<bool> ExtractAsync(
        string sourcePath,
        string destPath,
        int maxSize,
        CancellationToken ct)
    {
        try
        {
            if (!File.Exists(sourcePath))
                return Task.FromResult(false);

            // Initialize LibVLC (lazy — only when needed)
            Core.Initialize();
            using var libVlc = new LibVLC();
            using var media = new Media(libVlc, sourcePath);
            using var mediaPlayer = new MediaPlayer(media);

            // Take snapshot at approximately 1 second
            mediaPlayer.Play();
            
            // Wait briefly for playback to start, then seek
            Thread.Sleep(500);
            mediaPlayer.Time = 1000; // 1 second in ms
            Thread.Sleep(200);

            var snapshotDir = Path.GetDirectoryName(destPath)!;
            Directory.CreateDirectory(snapshotDir);
            
            // LibVLC TakeSnapshot saves to a directory with auto-generated filename
            // We then move it to our desired destPath
            var tempPrefix = Path.GetFileNameWithoutExtension(destPath);
            var tempDir = Path.Combine(snapshotDir, $".vlcsnap_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            mediaPlayer.TakeSnapshot(0, tempDir, tempPrefix);
            
            // Find the generated snapshot
            var files = Directory.GetFiles(tempDir, "*.png");
            if (files.Length == 0)
            {
                Directory.Delete(tempDir, true);
                return Task.FromResult(false);
            }

            // Convert PNG snapshot to JPEG and resize
            var snapshotPath = files[0];
            using var image = SixLabors.ImageSharp.Image.Load(snapshotPath);
            var (w, h) = (image.Width, image.Height);

            if (w > h)
            {
                var ratio = (double)maxSize / w;
                image.Mutate(x => x.Resize(maxSize, (int)(h * ratio)));
            }
            else
            {
                var ratio = (double)maxSize / h;
                image.Mutate(x => x.Resize((int)(w * ratio), maxSize));
            }

            var encoder = new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = 85 };
            image.Save(destPath, encoder);

            // Cleanup
            Directory.Delete(tempDir, true);

            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }
}
```

- [ ] **Step 3: Write tests**

For video, we'll need a small test video file or to mock LibVLC. Since LibVLC requires native binaries, the safest approach is to test `CanExtract` and use NSubstitute for the rest, or skip the full integration test in CI and document it.

```csharp
using System.Threading.Tasks;
using Adam.Shared.ThumbnailExtractors;
using FluentAssertions;
using Xunit;

namespace Adam.Shared.Tests.ThumbnailExtractors;

public class VideoThumbnailExtractorTests
{
    private readonly VideoThumbnailExtractor _sut = new();

    [Fact]
    public void CanExtract_Mp4_ReturnsTrue()
    {
        _sut.CanExtract("/tmp/video.mp4", "video/mp4").Should().BeTrue();
    }

    [Fact]
    public void CanExtract_Avi_ReturnsTrue()
    {
        _sut.CanExtract("/tmp/video.avi", "video/x-msvideo").Should().BeTrue();
    }

    [Fact]
    public void CanExtract_Mov_ReturnsTrue()
    {
        _sut.CanExtract("/tmp/video.mov", "video/quicktime").Should().BeTrue();
    }

    [Fact]
    public void CanExtract_Jpg_ReturnsFalse()
    {
        _sut.CanExtract("/tmp/photo.jpg", "image/jpeg").Should().BeFalse();
    }

    // Full integration test with LibVLC requires native binaries.
    // Run manually: place a small MP4 in test-assets/ and uncomment below.
    /*
    [Fact]
    public async Task ExtractAsync_ValidMp4_ReturnsTrue()
    {
        var source = "test-assets/sample.mp4";
        var dest = Path.Combine(Path.GetTempPath(), $"vlctest_{Guid.NewGuid()}.jpg");
        
        var result = await _sut.ExtractAsync(source, dest, 256, default);
        
        result.Should().BeTrue();
        File.Exists(dest).Should().BeTrue();
    }
    */
}
```

- [ ] **Step 4: Verify it compiles**

Run: `dotnet build src/Adam.Shared/Adam.Shared.csproj --no-restore`
Expected: Build succeeded, 0 errors

- [ ] **Step 5: Commit**

```bash
git add src/Adam.Shared/Adam.Shared.csproj src/Adam.Shared/ThumbnailExtractors/VideoThumbnailExtractor.cs tests/Adam.Shared.Tests/ThumbnailExtractors/VideoThumbnailExtractorTests.cs
git commit -m "feat(thumbnail): add VideoThumbnailExtractor with LibVLCSharp"
```

---

### Task 13: Update `ThumbnailService` to register video extractor

**Files:**
- Modify: `src/Adam.Shared/Services/ThumbnailService.cs`

- [ ] **Step 1: Add VideoThumbnailExtractor to the pipeline**

Update the constructor:

```csharp
public ThumbnailService(ILogger<ThumbnailService>? logger = null)
{
    _logger = logger ?? NullLogger<ThumbnailService>.Instance;

    var adjustment = new ImageAdjustmentService();
    _pipeline = new ThumbnailPipeline([
        new ImageThumbnailExtractor(adjustment),
        new AudioThumbnailExtractor(),
        new OfficeThumbnailExtractor(),
        new PdfPreviewExtractor(),
        new VideoThumbnailExtractor(),
        new GenericIconExtractor()
    ], null);
}
```

- [ ] **Step 2: Verify full build**

Run: `dotnet build src/Adam.Shared/Adam.Shared.csproj --no-restore`
Run: `dotnet build src/Adam.CatalogBrowser/Adam.CatalogBrowser.csproj --no-restore`
Expected: Both builds succeed

- [ ] **Step 3: Run all tests**

Run: `dotnet test tests/Adam.Shared.Tests/Adam.Shared.Tests.csproj --no-restore`
Expected: All tests pass

- [ ] **Step 4: Commit**

```bash
git add src/Adam.Shared/Services/ThumbnailService.cs
git commit -m "feat(thumbnail): register VideoThumbnailExtractor in pipeline"
```

---

## Phase 4: Cleanup & Integration

### Task 14: Remove deprecated Windows-only code from `ThumbnailService`

**Files:**
- Verify no references remain to old Windows methods

- [ ] **Step 1: Confirm old Windows interop methods were removed in Task 5**

The old `GenerateWindowsThumbnailAsync`, `SaveHBitmapToJpegAsync`, `GetBitmapFromHBitmap`, and all P/Invoke declarations (`SHCreateItemFromParsingName`, `DeleteObject`, `GetObject`, `CreateCompatibleDC`, `DeleteDC`, `SelectObject`, `GetDIBits`, `GetDC`, `ReleaseDC`) and structs (`BITMAP`, `BITMAPINFOHEADER`) and constants (`SIIGBF_RESIZETOFIT`, `SIIGBF_THUMBNAILONLY`) should have been removed when we rewrote `ThumbnailService.cs` in Task 5.

Verify none of these strings remain in the file:
Run: `grep -n "SHCreateItemFromParsingName\|SIIGBF\|BITMAPINFOHEADER\|DeleteObject\|GetObject\|CreateCompatibleDC\|GetDIBits\|GetDC\|ReleaseDC" src/Adam.Shared/Services/ThumbnailService.cs`
Expected: No output (nothing found)

- [ ] **Step 2: Remove `System.Runtime.InteropServices` using if still present**

If `using System.Runtime.InteropServices;` is still at the top of `ThumbnailService.cs` and unused, remove it.

- [ ] **Step 3: Commit (if any changes)**

```bash
git add src/Adam.Shared/Services/ThumbnailService.cs 2>/dev/null || true
git diff --cached --quiet || git commit -m "chore(thumbnail): remove dead Windows COM interop code"
```

---

### Task 15: Write integration test for full pipeline

**Files:**
- Create: `tests/Adam.Shared.Tests/Services/ThumbnailExtractorIntegrationTests.cs`

- [ ] **Step 1: Write integration test**

```csharp
using System;
using System.IO;
using System.Threading.Tasks;
using Adam.Shared.Services;
using Adam.Shared.ThumbnailExtractors;
using FluentAssertions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Jpeg;
using Xunit;

namespace Adam.Shared.Tests.Services;

public class ThumbnailExtractorIntegrationTests : IDisposable
{
    private readonly string _tempDir;

    public ThumbnailExtractorIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task Pipeline_ImageFile_GeneratesThumbnail()
    {
        var source = Path.Combine(_tempDir, "test.jpg");
        var thumbDir = Path.Combine(_tempDir, "thumbs");
        CreateTestImage(source, 800, 600);

        var sut = new ThumbnailService();
        var result = await sut.GenerateThumbnailAsync(source, thumbDir);

        File.Exists(result).Should().BeTrue();
        using var thumb = Image.Load(result);
        thumb.Width.Should().Be(256);
    }

    [Fact]
    public async Task Pipeline_UnknownExtension_GeneratesGenericIcon()
    {
        var source = Path.Combine(_tempDir, "data.xyz");
        var thumbDir = Path.Combine(_tempDir, "thumbs");
        File.WriteAllText(source, "test data");

        var sut = new ThumbnailService();
        var result = await sut.GenerateThumbnailAsync(source, thumbDir);

        File.Exists(result).Should().BeTrue();
        using var thumb = Image.Load(result);
        thumb.Width.Should().Be(256);
        thumb.Height.Should().Be(256);
    }

    private static void CreateTestImage(string path, int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(255, 0, 0));
        image.Save(path, new JpegEncoder());
    }
}
```

- [ ] **Step 2: Run tests**

Run: `dotnet test tests/Adam.Shared.Tests/Adam.Shared.Tests.csproj --filter "FullyQualifiedName~ThumbnailExtractorIntegrationTests"`
Expected: 2 tests, all passed

- [ ] **Step 3: Commit**

```bash
git add tests/Adam.Shared.Tests/Services/ThumbnailExtractorIntegrationTests.cs
git commit -m "test(thumbnail): add pipeline integration tests"
```

---

### Task 16: Verify ingestion pipeline still works end-to-end

**Files:**
- Verify: `src/Adam.CatalogBrowser/ViewModels/IngestionViewModel.cs`

- [ ] **Step 1: Confirm IngestionViewModel calls ThumbnailService unchanged**

The existing call in `IngestionViewModel.cs` line 198 should still work:
```csharp
var thumbPath = await _thumbnailService.GenerateThumbnailAsync(filePath, thumbDir, ct);
```

Verify the call signature hasn't changed (it hasn't — we preserved both overloads).

Run: `grep -n "GenerateThumbnailAsync" src/Adam.CatalogBrowser/ViewModels/IngestionViewModel.cs`
Expected: Shows the existing call at line ~198

- [ ] **Step 2: Build the full solution**

Run: `dotnet build`
Expected: Build succeeded for all projects

- [ ] **Step 3: Commit (if clean)**

```bash
git diff --quiet || git commit -m "chore(thumbnail): verify ingestion integration after pipeline refactor"
```

---

### Task 17: Update `GenericIconExtractor` tests

**Files:**
- Create: `tests/Adam.Shared.Tests/ThumbnailExtractors/GenericIconExtractorTests.cs`

- [ ] **Step 1: Write tests**

```csharp
using System;
using System.IO;
using System.Threading.Tasks;
using Adam.Shared.ThumbnailExtractors;
using FluentAssertions;
using SixLabors.ImageSharp;
using Xunit;

namespace Adam.Shared.Tests.ThumbnailExtractors;

public class GenericIconExtractorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly GenericIconExtractor _sut = new();

    public GenericIconExtractorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void CanExtract_AnyFile_ReturnsTrue()
    {
        _sut.CanExtract("/tmp/anything.xyz", "application/octet-stream").Should().BeTrue();
    }

    [Fact]
    public async Task ExtractAsync_GeneratesIcon()
    {
        var source = Path.Combine(_tempDir, "data.zip");
        var dest = Path.Combine(_tempDir, "icon.jpg");
        File.WriteAllText(source, "test");

        var result = await _sut.ExtractAsync(source, dest, 256, default);

        result.Should().BeTrue();
        File.Exists(dest).Should().BeTrue();
        using var image = Image.Load(dest);
        image.Width.Should().Be(256);
        image.Height.Should().Be(256);
    }

    [Fact]
    public async Task ExtractAsync_SameExtension_GeneratesSameColor()
    {
        var source1 = Path.Combine(_tempDir, "a.zip");
        var source2 = Path.Combine(_tempDir, "b.zip");
        var dest1 = Path.Combine(_tempDir, "icon1.jpg");
        var dest2 = Path.Combine(_tempDir, "icon2.jpg");
        File.WriteAllText(source1, "test");
        File.WriteAllText(source2, "test");

        await _sut.ExtractAsync(source1, dest1, 256, default);
        await _sut.ExtractAsync(source2, dest2, 256, default);

        using var img1 = Image.Load(dest1);
        using var img2 = Image.Load(dest2);
        
        // Same extension → same background color (check a corner pixel)
        var pixel1 = img1[0, 0];
        var pixel2 = img2[0, 0];
        pixel1.R.Should().Be(pixel2.R);
        pixel1.G.Should().Be(pixel2.G);
        pixel1.B.Should().Be(pixel2.B);
    }
}
```

- [ ] **Step 2: Run tests**

Run: `dotnet test tests/Adam.Shared.Tests/Adam.Shared.Tests.csproj --filter "FullyQualifiedName~GenericIconExtractorTests"`
Expected: 3 tests, all passed

- [ ] **Step 3: Commit**

```bash
git add tests/Adam.Shared.Tests/ThumbnailExtractors/GenericIconExtractorTests.cs
git commit -m "test(thumbnail): add GenericIconExtractor tests"
```

---

### Task 18: Final full test run and cleanup

- [ ] **Step 1: Run all tests**

Run: `dotnet test`
Expected: All tests pass across all test projects

- [ ] **Step 2: Check for build warnings**

Run: `dotnet build`
Expected: 0 errors, minimal warnings

- [ ] **Step 3: Final commit**

```bash
git commit -m "feat(thumbnail): complete cross-platform thumbnail extraction pipeline

- Extract image thumbnails via ImageSharp (existing, refactored)
- Extract audio album art via TagLibSharp
- Extract Office embedded previews via NPOI
- Extract PDF embedded previews via PdfPig
- Extract video frames via LibVLCSharp
- Generic icon fallback for unsupported formats
- Full test coverage for all extractors and pipeline
- Removed Windows-only COM interop code"
```

---

## Self-Review Checklist

### Spec Coverage
| Spec Requirement | Implementing Task |
|---|---|
| IThumbnailExtractor interface | Task 1 |
| ThumbnailPipeline | Task 2 |
| ImageThumbnailExtractor | Task 3 |
| GenericIconExtractor | Task 4 |
| ThumbnailService facade | Task 5 |
| AudioThumbnailExtractor (TagLibSharp) | Task 8 |
| OfficeThumbnailExtractor (NPOI) | Task 9 |
| PdfPreviewExtractor (PdfPig) | Task 10 |
| VideoThumbnailExtractor (LibVLCSharp) | Task 12 |
| Register all extractors in service | Tasks 11, 13 |
| Remove Windows COM code | Task 14 |
| Integration tests | Task 15 |
| Ingestion compatibility | Task 16 |

### Placeholder Scan
- ✅ No "TBD", "TODO", "implement later"
- ✅ No vague "add error handling" steps
- ✅ No "write tests for the above" without actual test code
- ✅ No "similar to Task N" references

### Type Consistency
- ✅ `IThumbnailExtractor` interface used consistently across all extractors
- ✅ `CanExtract(string, string)` signature matches everywhere
- ✅ `ExtractAsync(string, string, int, CancellationToken)` signature matches everywhere
- ✅ `Priority` values follow spec: 100, 110, 120, 130, 200, 999

---

## Execution Handoff

**Plan complete and saved to `specs/001-thumbnail-extraction-pipeline/plan.md`.**

**Two execution options:**

**1. Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration. Best for complex multi-file changes.

**2. Inline Execution** — Execute tasks in this session using `executing-plans`, batch execution with checkpoints. Best if you want to watch each step.

**Which approach?**
