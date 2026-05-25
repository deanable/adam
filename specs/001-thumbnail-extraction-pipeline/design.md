# Thumbnail Extraction Pipeline — Design Spec

**Date:** 2026-01-25
**Status:** Approved
**Scope:** Cross-platform thumbnail generation for all supported file formats

---

## 1. Problem Statement

The current `ThumbnailService` relies on the Windows Shell API (`IShellItemImageFactory`) for non-image files. This is:
- **Windows-only** — no support for Linux or macOS
- **Unpredictable** — we don't control which frame/page is extracted for video or documents
- **Monolithic** — all logic is crammed into a single 237-line class with COM interop, GDI bitmap handling, and path resolution mixed together

We need a clean, extensible, cross-platform pipeline that can extract meaningful thumbnails from images, video, audio (album art), documents, and PDFs.

---

## 2. Goals

1. **Cross-platform by default** — works on Windows, Linux, and macOS without requiring users to install external tools
2. **Pure .NET first** — maximize use of managed libraries to avoid native dependency hell
3. **Extensible** — adding a new format should mean adding one new class, not modifying existing code
4. **Graceful degradation** — if a file can't be thumbnailed, show a clean generic icon rather than a broken image
5. **Non-breaking** — existing `ThumbnailService.GenerateThumbnailAsync()` callers (e.g., `IngestionViewModel`) continue to work unchanged

---

## 3. Architecture

### 3.1 Pipeline Pattern

```
┌─────────────────┐     ┌──────────────────┐     ┌──────────────────────┐
│  ThumbnailService  │────▶│ ThumbnailPipeline  │────▶│ IThumbnailExtractor  │
│  (thin facade)     │     │ (priority-ordered) │     │ implementations      │
└─────────────────┘     └──────────────────┘     └──────────────────────┘
                                                           │
                                                  ┌──────────────┐
                                                  │ GenericIcon  │
                                                  │ (always last)│
                                                  └──────────────┘
```

### 3.2 Core Abstraction

```csharp
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
    /// Extract a thumbnail and save it to destPath (256x256 JPEG).
    /// Returns true on success. False allows pipeline to try next extractor.
    /// </summary>
    Task<bool> ExtractAsync(
        string sourcePath,
        string destPath,
        int maxSize,
        CancellationToken ct);
}
```

### 3.3 ThumbnailPipeline

- Maintains an ordered list of `IThumbnailExtractor` instances
- Iterates by `Priority`, calling `CanExtract` then `ExtractAsync`
- If all extractors return false, delegates to `GenericIconExtractor` (which never fails)
- Wraps each call in `try/catch` so one extractor crashing doesn't break the pipeline

### 3.4 ThumbnailService (Refactored)

Becomes a thin orchestrator:

```csharp
public class ThumbnailService
{
    private readonly ThumbnailPipeline _pipeline;
    private readonly ImageAdjustmentService _adjustment = new();

    public ThumbnailService()
    {
        _pipeline = new ThumbnailPipeline([
            new ImageThumbnailExtractor(_adjustment),      // Priority 100
            new AudioThumbnailExtractor(),                  // Priority 110
            new OfficeThumbnailExtractor(),                 // Priority 120
            new PdfPreviewExtractor(),                      // Priority 130
            new VideoThumbnailExtractor(),                  // Priority 200
            new GenericIconExtractor()                      // Priority 999
        ]);
    }

    public async Task<string> GenerateThumbnailAsync(
        string sourcePath, string thumbDir,
        ImageOrientation orientation = ImageOrientation.Normal,
        CancellationToken ct = default)
    {
        var destPath = GetThumbnailPath(sourcePath, thumbDir);
        if (File.Exists(destPath) && orientation == ImageOrientation.Normal)
            return destPath;

        Directory.CreateDirectory(thumbDir);
        var mimeType = ResolveMimeType(sourcePath);

        var success = await _pipeline.TryExtractAsync(
            sourcePath, destPath, mimeType, maxSize: 256, ct);

        return destPath;
    }

    // Existing GetThumbnailPath stays unchanged
}
```

---

## 4. Extractors

### 4.1 Tier 1 — Pure .NET (No External Dependencies)

#### ImageThumbnailExtractor
- **Formats:** JPG, JPEG, PNG, GIF, WEBP, BMP, TIFF, TIF, RAW (CR2, NEF, ARW, DNG), HEIC
- **Library:** `SixLabors.ImageSharp` (already in use)
- **Logic:** `Image.LoadAsync` → apply EXIF orientation via `ImageAdjustmentService` → resize to 256px on long edge → save as JPEG (quality 85)
- **Priority:** 100

#### AudioThumbnailExtractor
- **Formats:** MP3, FLAC, WAV, M4A, OGG, WMA
- **Library:** `TagLibSharp` (NuGet: `TagLibSharp`)
- **Logic:** Open file via `TagLib.File.Create` → read `Tag.Pictures` array → if `APIC`/`PICT` frame exists, save embedded image → resize to 256px via ImageSharp
- **Fallback:** If no album art, returns `false` so pipeline continues to generic icon
- **Priority:** 110

#### OfficeThumbnailExtractor
- **Formats:** DOCX, XLSX, PPTX
- **Library:** `NPOI` (NuGet: `NPOI`)
- **Logic:** Office files are ZIP packages. NPOI can read `docProps/thumbnail.jpeg` from the package. Extract → resize to 256px.
- **Fallback:** If no embedded thumbnail (e.g., file created by non-Office tool), returns `false`
- **Priority:** 120

#### PdfPreviewExtractor
- **Formats:** PDF
- **Library:** `PdfPig` (NuGet: `PdfPig`)
- **Logic:** Many PDFs embed a preview image in the document structure. Use PdfPig to inspect `/XObject` resources and extract an embedded image stream. Save → resize.
- **Fallback:** If no embedded preview found, returns `false`
- **Priority:** 130

### 4.2 Tier 2 — .NET Wrapper with Bundled Native

#### VideoThumbnailExtractor
- **Formats:** MP4, AVI, MOV, MKV, WMV, WEBM, FLV
- **Library:** `LibVLCSharp` + `VideoLAN.LibVLC.Windows/Linux/macOS`
- **Logic:**
  1. Initialize `libvlc` instance (bundled with app)
  2. Create media from file path
  3. Seek to ~1 second into video
  4. Use `TakeSnapshot` or VideoCallbacks to capture a frame
  5. Save as JPEG
- **Bundling:** LibVLC native binaries (~60-80MB per platform) are distributed via NuGet runtime packages. The app references the appropriate runtime package per target.
- **Priority:** 200
- **Note:** LibVLC is LGPL. Dynamic linking (how LibVLCSharp works) satisfies LGPL requirements without affecting our app's license.

### 4.3 Tier 3 — Always-Succeed Fallback

#### GenericIconExtractor
- **Formats:** All unsupported files (TXT, ZIP, RAR, XML, JSON, unknown, etc.)
- **Library:** `SixLabors.ImageSharp`
- **Logic:**
  1. Generate a 256x256 colored background (color derived from file extension hash for consistency)
  2. Draw the uppercase file extension (e.g., "PDF", "ZIP") in white text centered
  3. Save as JPEG
- **Caching:** Generated icons are cached in `thumbnails/.icons/{extension}.jpg` to avoid regenerating the same icon thousands of times
- **Priority:** 999 (always last, never returns false)

---

## 5. File Type Detection

Use a combination of strategies, in priority order:

1. **Extension-based:** Fast lookup table for known extensions → MIME type
2. **Magic bytes:** For ambiguous cases (e.g., `.bin` files that might actually be PDF), read first few bytes
3. **Fallback:** `application/octet-stream`

The `ResolveMimeType` helper lives in `ThumbnailService` and is used by the pipeline to route files to the right extractor.

---

## 6. Error Handling & Resilience

| Scenario | Behavior |
|----------|----------|
| Extractor throws exception | Pipeline catches, logs warning, moves to next extractor |
| Extractor returns false | Pipeline moves to next extractor |
| Video file but LibVLC not available | `VideoThumbnailExtractor.CanExtract` returns false, falls to generic icon |
| Audio file with no album art | `AudioThumbnailExtractor` returns false, falls to generic icon |
| Corrupted PDF | `PdfPreviewExtractor` catches and returns false, falls to generic icon |
| All extractors exhausted | `GenericIconExtractor` always succeeds |

**Timeouts:** Not needed for Tier 1 (pure .NET, fast). For Tier 2 video, LibVLC's snapshot API is synchronous and typically completes within seconds for the first frame.

---

## 7. Caching Strategy

- **Thumbnail files:** Already cached by hash (`{sha16}.jpg`). Unchanged.
- **Generic icons:** New cache at `thumbnails/.icons/{extension}.jpg`. Generated once per extension, reused forever.
- **No in-memory cache needed** — the filesystem is the cache. `AssetListItem.LoadThumbnailAsync` already loads from disk.

---

## 8. Testing Strategy

### Unit Tests (per extractor)
- `ImageThumbnailExtractorTests` — valid image, corrupted image, orientation handling
- `AudioThumbnailExtractorTests` — file with album art, file without album art
- `OfficeThumbnailExtractorTests` — Office file with preview, Office file without preview, non-Office DOCX
- `PdfPreviewExtractorTests` — PDF with embedded preview, PDF without preview, corrupted PDF
- `VideoThumbnailExtractorTests` — valid video, seek to 1s, frame captured
- `GenericIconExtractorTests` — generates icon, caches icon, text is centered

### Integration Tests
- `ThumbnailPipelineTests` — pipeline routes file to correct extractor, handles cascading failures
- `ThumbnailServiceTests` — end-to-end: input file → thumbnail file exists and is valid JPEG

### Test Data
- A `test-assets/` directory with sample files for each format
- Include edge cases: 0-byte file, corrupted JPEG, MP3 with no tags

---

## 9. Migration Plan

1. **Phase 1 — Extractor interface + refactor**
   - Create `IThumbnailExtractor` and `ThumbnailPipeline`
   - Refactor existing image logic into `ImageThumbnailExtractor`
   - Keep `ThumbnailService` as facade
   - Remove Windows COM interop code from `ThumbnailService`

2. **Phase 2 — Add pure .NET extractors**
   - `AudioThumbnailExtractor` (TagLibSharp)
   - `OfficeThumbnailExtractor` (NPOI)
   - `PdfPreviewExtractor` (PdfPig)
   - `GenericIconExtractor` (ImageSharp)

3. **Phase 3 — Add video extractor**
   - `VideoThumbnailExtractor` (LibVLCSharp)
   - Add LibVLC runtime packages to project
   - Test on Windows, Linux, macOS

4. **Phase 4 — Cleanup**
   - Remove deprecated Windows-only `GenerateWindowsThumbnailAsync` and GDI helpers
   - Update tests
   - Verify ingestion pipeline still works end-to-end

---

## 10. Open Questions / Decisions

| Decision | Status | Rationale |
|----------|--------|-----------|
| PDF: embedded preview only | **Decided** | No mature pure .NET PDF renderer exists. Embedded previews cover most real-world PDFs. |
| Office: embedded thumbnail only | **Decided** | NPOI can read thumbnails but cannot render pages. Full rendering requires a layout engine. |
| Video: LibVLCSharp | **Decided** | Bundled, cross-platform, LGPL-compliant via dynamic linking. |
| Generic icon style | **Decided** | Colored background + white extension text, cached per extension. |

---

## 11. File Structure

```
src/Adam.Shared/
  Services/
    ThumbnailService.cs              (refactored — thin facade)
    ThumbnailPipeline.cs             (new — orchestrator)
    ImageAdjustmentService.cs      (existing — unchanged)
  ThumbnailExtractors/
    IThumbnailExtractor.cs           (new — interface)
    ImageThumbnailExtractor.cs       (new — extracted from ThumbnailService)
    AudioThumbnailExtractor.cs       (new)
    OfficeThumbnailExtractor.cs      (new)
    PdfPreviewExtractor.cs           (new)
    VideoThumbnailExtractor.cs       (new)
    GenericIconExtractor.cs          (new)
```

---

## 12. NuGet Dependencies

| Package | Version | Used By |
|---------|---------|---------|
| `SixLabors.ImageSharp` | existing | ImageThumbnailExtractor, GenericIconExtractor |
| `TagLibSharp` | 2.3.0 | AudioThumbnailExtractor |
| `NPOI` | 2.7.0 | OfficeThumbnailExtractor |
| `PdfPig` | 0.1.8 | PdfPreviewExtractor |
| `LibVLCSharp` | 3.x | VideoThumbnailExtractor |
| `VideoLAN.LibVLC.Windows` | 3.x | Runtime (Windows) |
| `VideoLAN.LibVLC.Linux` | 3.x | Runtime (Linux) |
| `VideoLAN.LibVLC.macOS` | 3.x | Runtime (macOS) |

---

## 13. Risks & Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|-----------|
| LibVLC binaries bloat app size | High | Medium | Only include runtime for target platform; publish trimmed |
| TagLibSharp fails on exotic audio | Low | Low | Falls to generic icon |
| NPOI can't read all Office variants | Medium | Low | Falls to generic icon |
| PdfPig doesn't find preview in some PDFs | Medium | Low | Falls to generic icon |
| Video snapshot is black frame | Low | Medium | Seek to 1s instead of 0s; try 2s if first frame is black |

---

*Spec written and ready for review.*
