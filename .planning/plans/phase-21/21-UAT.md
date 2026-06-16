# Phase 21 — Virtualized Gallery, Lazy Loading, Thumbnail Caching, DB Optimization

## UAT Verification Checklist

### T21.1 — Viewport-Based Gallery Virtualization
- [x] `LoadVisibleThumbnails()` calculates visible range from `_viewportTop`, `_viewportHeight`, `_viewportWidth`
- [x] Only loads thumbnails for items within viewport + 50% overscan buffer
- [x] Cancels pending loads for off-screen items via `CancelPendingLoad()`
- [x] Scroll-viewer wired via `OnScrollChanged` → `UpdateViewport()` in code-behind
- [x] Uses actual viewport width (not hardcoded 1280px) for items-per-row estimate

### T21.2 — Prioritized Thumbnail Loading Pipeline
- [x] `BatchLoadRemainingThumbnailsAsync()` loads deferred thumbnails in batches of 8
- [x] 50ms delay between batches to avoid CPU/IO bursts
- [x] Cancellation token cancels previous batch on new page load / filter change
- [x] Visible items load immediately via `LoadVisibleThumbnails()`
- [x] Off-screen items deferred to batch loader

### T21.3 — Async Thumbnail Disk I/O
- [x] `AssetListItem.LoadThumbnailAsync()` uses `FileStream(useAsync: true)` for non-blocking I/O
- [x] `CancellationToken` passed to all async operations
- [x] Memory cache check first (LRU, 256 MB default)
- [x] Thread-safe: runs inside `Task.Run` with `CancellationToken`

### T21.4a — Composite DB Indexes
- [x] `(Type, CreatedAt, Id)` — filter by type, sort by date
- [x] `(Rating, CreatedAt)` and `(Rating, CreatedAt, Id)` — filter by rating, sort by date
- [x] `(MimeType, FileName)` — filter by type, sort by name
- [x] `(MimeType, CreatedAt)` — filter by type, sort by date
- [x] `(FileName, CreatedAt, Id)` — sort by filename with date tiebreaker
- [x] `(CreatedAt, Id)` — sort by date
- [x] `(FileSize, Id)` — sort by file size
- [x] All indexes added to `AppDbContext.OnModelCreating` for `DigitalAsset` entity
- [x] Compatible with SQLite, PostgreSQL, and SQL Server

### T21.4b — Keyset Pagination
- [x] `ApplyKeysetPagination()` replaces `Skip/TAKE` for "load more" calls (page > 0)
- [x] Seek-based pagination uses `WHERE (sortCol > @lastSeen)` instead of `OFFSET`
- [x] Supports all 4 sort modes: FileName, Date Added, FileType, FileSize
- [x] Each sort mode has appropriate tiebreaker on `Id`
- [x] `Skip/TAKE` retained for page 0 (initial load) — seek values not available yet
- [x] Last item's sort values stored in `_lastSeek*` fields after each page load

### Build & Test Verification
- [x] `dotnet build` — 0 errors
- [x] `dotnet test` — 1,232 tests pass (2 Docker-dependent skipped)
- [x] Code review completed — no blocking issues found

---

**Status: ✅ ALL CHECKS PASSED**
**Committed as:** `0ce32ef`
