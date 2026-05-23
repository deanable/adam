# Plan: Phase 4 — Asset Ingestion & Management

**Phase:** 4 of 7  
**Goal:** Complete asset lifecycle — metadata editing with source-file round-trip, basic image adjustments, and export  
**Requirements:** META-01 to META-04, EDIT-01 to EDIT-04  
**Estimated Effort:** ~6.5 days  
**Created:** 2026-05-23  

## Overview

Phase 4 makes the DAM system a true round-trip metadata editor. Users can edit ratings, labels, flags, GPS, and copyright in the client; changes are persisted to the database and written back to source files as embedded XMP (or XMP sidecar for RAW). Basic image adjustments (rotate, flip) and export to JPEG/TIFF complete the asset lifecycle.

## Tasks

### T4.1 — Expand metadata model: ratings, labels, flags, GPS, copyright

**Goal:** Support the full metadata fields specified in META-01.

**Details:**
- Add to `DigitalAsset` model: `Rating` (int 0-5), `Label` (enum), `Flag` (enum Pick/Reject/Unflagged), `GpsLatitude`/`GpsLongitude` (double?), `Copyright` (string)
- Add EF Core migration for SQLite, PostgreSQL, SQL Server
- Update protobuf contracts: `AssetDetail`, `UpdateAssetRequest`, `CreateAssetRequest`
- Update `AssetHandler` CRUD to read/write new fields

**Files:**
- `src/Adam.Shared/Models/DigitalAsset.cs`
- `src/Adam.Shared/Contracts/AssetMessages.cs`
- `src/Adam.BrokerService/Handlers/AssetHandler.cs`
- Migrations in `src/Adam.Shared/Data/Migrations/`

**Est:** 4h  
**Depends:** None

---

### T4.2 — Client-side metadata editor UI enhancements

**Goal:** Property inspector supports editing all new metadata fields.

**Details:**
- Star rating control (0-5, clickable)
- Color label picker (Red, Green, Blue, Yellow, Purple, None)
- Flag toggle (Pick / Reject / Unflagged)
- GPS coordinate inputs (lat/long with validation)
- Copyright text field
- All fields participate in dirty tracking and auto-save
- Handle `Conflict=true` response from update by showing merge/overwrite dialog

**Files:**
- `src/Adam.CatalogBrowser/ViewModels/MainWindowViewModel.cs` (expand dirty tracking)
- `src/Adam.CatalogBrowser/Views/MainWindow.axaml` (XAML additions)
- `src/Adam.CatalogBrowser/Controls/` (new rating/label controls if needed)

**Est:** 6h  
**Depends:** T4.1

---

### T4.3 — XMP write-back service

**Goal:** Metadata edits written back to source file's embedded XMP within 5 seconds (META-02).

**Details:**
- Create `XmpWriteService` in `Adam.Shared` that uses `MetadataExtractor` and `XmpCore` to write XMP packets
- Standalone mode: called directly after DB save in `AppDbContext.SaveChangesAsync` interceptor or `AdamRepository`
- Multi-user mode: BrokerService calls it after `AssetHandler.UpdateAssetAsync` succeeds
- Fields to write: title, description, keywords (as dc:subject), rating (xmp:Rating), label, copyright, GPS
- Handle files that already have XMP (update) vs. files without XMP (inject)
- Must be async, non-blocking to request handler

**Files:**
- `src/Adam.Shared/Services/XmpWriteService.cs`
- `src/Adam.BrokerService/Handlers/AssetHandler.cs` (wire call)
- `src/Adam.CatalogBrowser/Services/AdamRepository.cs` (wire call for standalone)

**Est:** 8h  
**Depends:** T4.1

---

### T4.4 — RAW sidecar support (XMP sidecar for CR2/NEF/ARW)

**Goal:** RAW files receive `.xmp` sidecar files instead of embedded write-back (META-03).

**Details:**
- Maintain list of RAW extensions: `.cr2`, `.nef`, `.arw`, `.dng`, `.raf`, `.orf`, `.pef`, `.rw2`
- In `XmpWriteService`: if file extension is RAW, write to `[filename].xmp` in same directory
- On read (ingest/index): if `.xmp` sidecar exists, read metadata from it and merge with embedded EXIF
- On metadata display: prefer sidecar values over embedded

**Files:**
- `src/Adam.Shared/Services/XmpWriteService.cs`
- `src/Adam.Shared/Services/MetadataExtractorService.cs` (sidecar read)
- `src/Adam.BrokerService/Handlers/AssetHandler.cs`

**Est:** 5h  
**Depends:** T4.3

---

### T4.5 — Read-only file guard

**Goal:** Read-only files trigger user notification when metadata save is attempted (META-04).

**Details:**
- Before XMP write, check `File.GetAttributes(path)` for `FileAttributes.ReadOnly`
- If read-only: return error to client (StatusCode=10 or dedicated error)
- Client shows modal/toast: "File is read-only. Cannot save metadata."
- Standalone mode: immediate feedback in property inspector
- Multi-user mode: broker returns error response; client displays it

**Files:**
- `src/Adam.Shared/Services/XmpWriteService.cs`
- `src/Adam.BrokerService/Handlers/AssetHandler.cs`
- `src/Adam.CatalogBrowser/ViewModels/MainWindowViewModel.cs` (error handling)

**Est:** 3h  
**Depends:** T4.3

---

### T4.6 — Image rotation (90/180/270 degrees)

**Goal:** User can rotate image clockwise/counter-clockwise (EDIT-01).

**Details:**
- Add `RotateCommand` to `MainWindowViewModel` with 90° CW and 90° CCW (repeated for 180/270)
- Use `SixLabors.ImageSharp` for in-memory rotation
- Generate rotated thumbnail and overwrite cached thumbnail
- Store rotation in metadata (XMP `tiff:Orientation` or custom field)
- Rotation is a metadata adjustment, not a file write — but optionally export rotated version
- Client UI: rotate buttons in property inspector or context menu

**Files:**
- `src/Adam.CatalogBrowser/ViewModels/MainWindowViewModel.cs`
- `src/Adam.CatalogBrowser/Views/MainWindow.axaml`
- `src/Adam.Shared/Services/ThumbnailService.cs` (regenerate after rotation)

**Est:** 5h  
**Depends:** None (can be parallel with T4.1)

---

### T4.7 — Image flip (horizontal/vertical)

**Goal:** User can flip image horizontally or vertically (EDIT-02).

**Details:**
- Add `FlipHorizontalCommand` and `FlipVerticalCommand`
- Use ImageSharp `Flip(FlipMode.Horizontal)` / `Flip(FlipMode.Vertical)`
- Regenerate thumbnail
- Track flip state in metadata (similar to rotation)
- UI: flip buttons alongside rotate controls

**Files:**
- Same as T4.6

**Est:** 3h  
**Depends:** T4.6

---

### T4.8 — Export to JPEG with configurable quality and resolution

**Goal:** User can export selected assets to JPEG (EDIT-03).

**Details:**
- Create `ExportDialog.axaml` with: destination folder, quality slider (1-100), max dimension (optional)
- Use ImageSharp to load source, apply any rotation/flip adjustments, resize if needed, save as JPEG
- Preserve metadata (XMP) in exported file
- Progress reporting for batch export
- Standalone: direct file access. Multi-user: broker streams bytes? Or client fetches asset path and does local export?
  → **Decision:** For v1, export is a client-side operation. Client already has asset path (or receives it from broker). Broker need not be involved in export.
- Handle export of multiple selected assets

**Files:**
- `src/Adam.CatalogBrowser/Views/ExportDialog.axaml`
- `src/Adam.CatalogBrowser/ViewModels/ExportDialogViewModel.cs`
- `src/Adam.Shared/Services/ImageExportService.cs`

**Est:** 8h  
**Depends:** T4.6, T4.7

---

### T4.9 — Export to TIFF with configurable color space

**Goal:** User can export selected assets to TIFF (EDIT-04).

**Details:**
- Extend `ExportDialog` with format selector (JPEG / TIFF)
- TIFF options: color space (sRGB, Adobe RGB, ProPhoto RGB), compression (LZW, ZIP, None)
- Use ImageSharp TIFF encoder (check if supported; if not, use alternative or document limitation)
- Preserve metadata in exported TIFF

**Files:**
- Same as T4.8
- `src/Adam.Shared/Services/ImageExportService.cs`

**Est:** 5h  
**Depends:** T4.8

---

### T4.10 — Integration tests for metadata round-trip

**Goal:** Automated tests verify metadata write and read-back correctness.

**Details:**
- Unit test: `XmpWriteService` writes title/keywords to a test JPEG, `MetadataExtractorService` reads it back
- Unit test: `XmpWriteService` creates `.xmp` sidecar for `.nef` file
- Unit test: Read-only file throws/returns expected error
- Integration test: Update asset via broker → verify file on disk has updated XMP
- Integration test: Export produces valid JPEG/TIFF with correct dimensions

**Files:**
- `tests/Adam.BrokerService.Tests/Integration/MetadataRoundTripTests.cs`
- `tests/Adam.Shared.Tests/Services/XmpWriteServiceTests.cs`

**Est:** 6h  
**Depends:** T4.3, T4.4, T4.5

---

## Dependency Graph

```
T4.1 ──────┬──→ T4.2
           │
           ├──→ T4.3 ──→ T4.4 ──→ T4.5
           │                    │
           │                    └──→ T4.10
           │
T4.6 ──→ T4.7 ──→ T4.8 ──→ T4.9
```

**Parallel workstreams:**
- Stream A (metadata): T4.1 → T4.2, T4.3 → T4.4 → T4.5 → T4.10
- Stream B (adjustments/export): T4.6 → T4.7 → T4.8 → T4.9

## Risk Register

| Risk | Impact | Mitigation |
|------|--------|------------|
| ImageSharp TIFF encoder may not support all color spaces | MEDIUM | Research before T4.9; fall back to JPEG-only export if needed |
| XMP write-back corrupts existing metadata | HIGH | Test with variety of file types; make backup before write; use XmpCore library |
| MetadataExtractor/XmpCore APIs are complex | MEDIUM | Start with simple fields (title, description, keywords); add GPS/copyright later |
| RAW sidecar read on ingest affects performance | LOW | Only check for sidecar if extension is in RAW list; cache result |

## Success Criteria Checklist

- [ ] Folder scan of 10,000 files completes in under 5 minutes (already true from Phase 1; verify still holds)
- [ ] Metadata edits are written to source file XMP within 5 seconds
- [ ] RAW files receive XMP sidecar on metadata edit
- [ ] Read-only file triggers user notification on save attempt
- [ ] Rotate/flip produces correct thumbnail update
- [ ] Export to JPEG produces correct output with quality and resolution settings
- [ ] Export to TIFF produces correct output with color space setting
- [ ] All integration tests pass

## Completion Definition

Phase 4 is complete when:
1. All tasks T4.1–T4.10 are implemented and committed
2. All new features are covered by passing tests (T4.10)
3. Client UI supports all new metadata fields
4. Export dialog works for both JPEG and TIFF
5. No regression in existing Phase 1–3 functionality
6. `ROADMAP.md` and `STATE.md` are updated

---
*Plan created: 2026-05-23*
