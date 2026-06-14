# Phase 15: Quality & Platform

**Goal:** Upgrade to EF Core 10 stable, expand test coverage, fix accumulated polish items, and prepare the codebase for v3 feature work.

**Milestone:** v3.0 — Provenance & Trust (Phases 15-16)
**Depends on:** Phases 1-14 (complete)
**Estimated effort:** ~600-800 LOC total across all tasks

---

## T15.1 — EF Core 10 RTM Upgrade

**Problem:** All EF Core packages are pinned to `10.0.0-preview.3.25171.6` — unstable pre-release API surface. Risk of breaking changes between preview and RTM.

### Current State

| Package | Project | Current Version |
|---------|---------|----------------|
| `Microsoft.EntityFrameworkCore.Sqlite` | Adam.Shared, Adam.BrokerService | `10.0.0-preview.3.25171.6` |
| `Microsoft.EntityFrameworkCore.SqlServer` | Adam.Shared, Adam.BrokerService | `10.0.0-preview.3.25171.6` |
| `Microsoft.EntityFrameworkCore.Design` | Adam.BrokerService | `10.0.0-preview.3.25171.6` |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | Adam.Shared, Adam.BrokerService | `10.0.0-preview.3` |

**Key code affected:**
- `src/Adam.Shared/Data/AppDbContext.cs` — `OnModelCreating` with provider-specific quoting in `Col()`, concurrency tokens, query filters
- `src/Adam.BrokerService/Data/MigrationRunner.cs` — `db.Database.MigrateAsync()`
- `src/Adam.Shared/Services/DbProviderConfig.cs` — provider registration

### Scope

1. **Update package versions** in both `.csproj` files:
   - `Microsoft.EntityFrameworkCore.Sqlite` → stable `10.0.x`
   - `Microsoft.EntityFrameworkCore.SqlServer` → stable `10.0.x`
   - `Npgsql.EntityFrameworkCore.PostgreSQL` → stable `10.0.x`
   - `Microsoft.EntityFrameworkCore.Design` → stable `10.0.x` (if still needed)
2. **Check for breaking API changes** in EF Core 10 RTM vs preview.3:
   - `HasData()` seed data API (used for roles/permissions in `AppDbContext.SeedData`)
   - `IsConcurrencyToken()` behavior
   - `HasQueryFilter()` interaction with `IgnoreQueryFilters()`
   - `ToLowerInvariant()` in LINQ queries (provider translation)
   - `GroupBy()` translation for SQLite
3. **Update `DbProviderConfig.Configure()`** if the EF Core provider registration API changed
4. **Run full test suite** across all 3 providers:
   - SQLite (local, always run)
   - PostgreSQL (Docker, 2 tests currently skipped)
   - SQL Server (Docker, 2 tests currently skipped)
5. **Vendor-in any breaking changes** — modify `AppDbContext`, `MigrationRunner`, or provider config as needed

### Files Changed

| File | Change |
|------|--------|
| `src/Adam.Shared/Adam.Shared.csproj` | Update 3 EF Core package versions |
| `src/Adam.BrokerService/Adam.BrokerService.csproj` | Update 4 EF Core package versions |
| `src/Adam.Shared/Data/AppDbContext.cs` | Potential minor fixes for RTM API changes |
| `src/Adam.BrokerService/Data/MigrationRunner.cs` | Potential minor fixes |
| `src/Adam.Shared/Services/DbProviderConfig.cs` | Potential provider registration changes |

### Tests

- Run all 1,061 existing tests — expect 0 regressions
- Run `DbProviderMatrixTests` (2 Docker-dependent) — verify provider config still works
- Run BrokerService integration tests (2 Docker-dependent)

### Acceptance Criteria

- [ ] All 3 DB providers work with EF Core 10 stable
- [ ] All tests pass (0 new failures, 2 Docker skips unchanged)
- [ ] No new build warnings from EF Core API deprecations
- [ ] Migration path from preview to stable documented in commit message

---

## T15.2 — CSV Streaming for Large Imports

**Problem:** `CsvMetadataService.ReadCsvAsync()` loads the entire CSV into memory via `File.ReadAllLinesAsync()`. For 10K+ row imports, this causes high memory pressure and poor UX.

### Current State

```csharp
// ReadCsvAsync: loads ALL lines into memory before processing
var lines = await File.ReadAllLinesAsync(inputPath, Encoding.UTF8, ct);
// ... parse header, then loop lines[1..] into List<CsvMetadataRow>
```

```csharp
// ImportFromCsvAsync: takes pre-loaded List<CsvMetadataRow>, no streaming
// Batch save every 50 rows
for (var i = 0; i < rows.Count; i++) { ... }
```

### Scope

1. **Add streaming CSV reader** — `ReadCsvStreamAsync()` returning `IAsyncEnumerable<CsvMetadataRow>`:
   - Use `StreamReader.ReadLineAsync()` in a loop with `yield return`
   - Process header row first, then yield rows as they're parsed
   - Support cancellation via `CancellationToken`
   - Keep the existing `ReadCsvAsync()` for backward compat (small files, preview needs)
2. **Add streaming import** — `ImportFromCsvStreamAsync()` accepting `IAsyncEnumerable<CsvMetadataRow>`:
   - Process rows lazily as they arrive
   - Batch save every 50 rows (same as current behavior)
   - Progress reporting via `IProgress<(int processed, int total)>` — note that with streaming, `total` is unknown until EOF. Use estimated total from file size / avg row size, or report `(processed, null)` with a different progress display.
3. **Update `ImportViewModel`** to use streaming for large files (>1,000 rows):
   - Check row count in preview (which still loads header + sample rows from stream)
   - Switch to streaming import path when row count exceeds threshold
   - Show "Streaming import… (N rows processed)" progress text instead of percentage
4. **Add cancellation support** to the streaming path (already in `CancellationToken` parameter)

### Files Changed

| File | Change |
|------|--------|
| `src/Adam.CatalogBrowser/Services/CsvMetadataService.cs` | Add `ReadCsvStreamAsync()` returning `IAsyncEnumerable`, add `ImportFromCsvStreamAsync()` |
| `src/Adam.CatalogBrowser/ViewModels/ImportViewModel.cs` | Integrate streaming path for large imports |
| `tests/Adam.CatalogBrowser.Tests/Services/CsvMetadataServiceTests.cs` | Add streaming tests |

### Tests (5-8 new)

- `ReadCsvStreamAsync_YieldsRows_FromLargeFile` — stream 10K synthetic rows, verify all yielded
- `ReadCsvStreamAsync_RespectsCancellation` — cancel mid-stream, verify `OperationCanceledException`
- `ImportFromCsvStreamAsync_BatchSaves` — verify save-every-50 behavior with streaming
- `ImportFromCsvStreamAsync_ProgressReports` — verify `IProgress` receives updates
- `ReadCsvStreamAsync_ParsesCorrectly` — compare streaming parse vs in-memory parse for same data
- `CsvMetadataService_ImportFromCsvAsync_Performance_LargeFile` — verify memory < 50MB for 10K rows (sketch test, adjust threshold)

### Acceptance Criteria

- [ ] 10K row CSV imports with < 50 MB peak memory (vs current ~200 MB+)
- [ ] Progress reported during streaming import (not just after completion)
- [ ] Backward compatible — existing `ReadCsvAsync()` and `ImportFromCsvAsync()` unchanged
- [ ] Cancellation works mid-stream
- [ ] 5+ new tests passing

---

## T15.3 — Activity Feed Pruning

**Problem:** `AccessLog` entries accumulate indefinitely with no cleanup mechanism. The `ActivityFeedViewModel` already filters to last 30 days in its query, but old rows remain in the database, slowing queries over time.

### Current State

- `AccessLog` model has: `Id`, `UserId`, `Action`, `EntityType`, `EntityId`, `Details`, `IpAddress`, `Timestamp`
- `ActivityFeedViewModel.LoadRecentActivityAsync()` filters: `.Where(l => l.Timestamp > DateTimeOffset.UtcNow.AddDays(-30))`
- No background cleanup service exists
- `AccessLogs` DbSet in `AppDbContext` has no query filter for soft-delete

### Scope

1. **Create `AccessLogCleanupService`** in `Adam.Shared.Services`:
   - `Task PruneAsync(int retentionDays, CancellationToken ct)` — deletes `AccessLog` entries older than `retentionDays`
   - Configuration: read from `appsettings.json` key `AccessLog:RetentionDays` (default 30)
   - Logs number of deleted rows: `"Pruned {Count} access log entries older than {RetentionDays} days"`
   - Graceful handling: no-op if retention is 0 (disabled)
2. **Wire in BrokerService** (`Program.cs`):
   - Register as `IHostedService` singleton
   - Run on startup (before accepting connections)
   - Run on a 24-hour timer (`PeriodicTimer`)
   - Use `IServiceScopeFactory` to create scoped `AppDbContext`
3. **Wire in CatalogBrowser** (standalone mode):
   - Run on startup (before gallery loads)
   - No timer needed for standalone (app lifetime = session)
4. **Update appsettings**:
   - Add `"AccessLog": { "RetentionDays": 30 }` to BrokerService `appsettings.json`

### Files Changed

| File | Change |
|------|--------|
| `src/Adam.Shared/Services/AccessLogCleanupService.cs` | New — pruning logic |
| `src/Adam.BrokerService/Program.cs` | Register `AccessLogCleanupService` as hosted service |
| `src/Adam.CatalogBrowser/App.axaml.cs` | Run cleanup on standalone startup |
| `src/Adam.BrokerService/appsettings.json` | Add `AccessLog:RetentionDays` config |
| `tests/Adam.Shared.Tests/Services/AccessLogCleanupServiceTests.cs` | New — pruning tests |

### Tests (3-5 new)

- `PruneAsync_DeletesOldEntries` — insert entries 0-60 days old, prune at 30, verify only entries >30d remain
- `PruneAsync_RespectsRetentionZero` — retention=0, verify no entries deleted
- `PruneAsync_LogsDeletedCount` — verify logger receives correct count
- `PruneAsync_HandlesEmptyTable` — no entries, verify no error

### Acceptance Criteria

- [ ] Entries older than `RetentionDays` are deleted on broker startup
- [ ] Standalone mode prunes on app startup
- [ ] BrokerService prunes daily via `PeriodicTimer`
- [ ] Retention period configurable via `appsettings.json` (default 30 days)
- [ ] Retention=0 disables pruning
- [ ] 3+ new tests passing

---

## T15.4 — Gallery "Re-scan Folder" Wiring

**Problem:** `SidebarViewModel.RescanFolderAsync()` has a TODO comment (line 1557) and currently just refreshes sidebar data without triggering an actual folder re-scan. When users right-click a folder in the sidebar and select "Re-scan", they expect the folder to be re-scanned for new/changed files.

### Current State

```csharp
// SidebarViewModel.cs ~line 1552
private async Task RescanFolderAsync()
{
    if (SelectedFolder == null || string.IsNullOrEmpty(SelectedFolder.Path)) return;
    try
    {
        // TODO (Phase 10 Wave 2): Wire to ingestion service to re-scan the folder path.
        // For now, refresh the sidebar data which re-queries asset counts.
        await LoadAsync();
        _logger.LogInformation("Re-scan requested for folder: {Path}", SelectedFolder.Path);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to rescan folder: {Path}", SelectedFolder.Path);
    }
}
```

**Research findings:**
- `IngestionViewModel.cs` handles ingestion but expects explicit file paths (from folder browser dialog), not folders
- There's no `IngestionService.cs` — ingestion logic is embedded in `IngestionViewModel.StartIngestionAsync()`
- `FolderWatcherService` in BrokerService watches root folders but doesn't expose a per-folder scan API

### Scope

1. **Extract folder scanning logic** into a reusable `FolderScanService` in `Adam.Shared.Services`:
   - `Task<int> ScanFolderAsync(string folderPath, bool recursive, CancellationToken ct)`
   - Discovers files, validates, checks duplicates, ingests new assets
   - Reuses `AssetValidator`, `ChecksumService`, `MetadataExtractorService`, `ThumbnailService`, etc.
   - Returns count of newly ingested assets
   - Logs results per folder
2. **Wire `FolderScanService`** through dependency injection in both:
   - `CatalogBrowser` (standalone) — `App.axaml.cs` or as a singleton
   - `BrokerService` — for remote scan requests (optional, for multi-user mode)
3. **Update `SidebarViewModel.RescanFolderAsync()`**:
   - Replace TODO with `await _folderScanService.ScanFolderAsync(SelectedFolder.Path, recursive: true)`
   - Show progress/status (could add a status bar message)
   - After scan completes, call `LoadAsync()` to refresh sidebar counts
   - Handle errors gracefully (log + toast)
4. **Add progress notification**:
   - Add a `ScanProgress` event or callback that updates the status bar
   - "Scanning {folder}… found {N} new assets"

### Files Changed

| File | Change |
|------|--------|
| `src/Adam.Shared/Services/FolderScanService.cs` | New — reusable folder scan logic |
| `src/Adam.CatalogBrowser/ViewModels/SidebarViewModel.cs` | Wire to FolderScanService, remove TODO |
| `src/Adam.CatalogBrowser/App.axaml.cs` | Register FolderScanService in DI |
| `src/Adam.BrokerService/Program.cs` | Register FolderScanService in DI (optional) |
| `tests/Adam.Shared.Tests/Services/FolderScanServiceTests.cs` | New — scan tests |

### Tests (3-5 new)

- `ScanFolderAsync_DiscoversNewFiles` — create temp dir with 5 files, scan, verify 5 ingested
- `ScanFolderAsync_SkipsExistingDuplicates` — ingest a file, re-scan folder, verify 0 new
- `ScanFolderAsync_RespectsCancellation` — cancel mid-scan, verify stops gracefully
- `ScanFolderAsync_HandlesEmptyFolder` — empty folder, verify 0 ingested, no error

### Acceptance Criteria

- [ ] Right-click folder → "Re-scan" triggers an actual file scan
- [ ] New files in the folder are ingested (duplicates skipped)
- [ ] Removed/deleted files are NOT removed from catalog (re-scan is additive)
- [ ] Progress/status visible during scan
- [ ] Sidebar counts refresh after scan completes
- [ ] 3+ new tests passing

---

## T15.5 — Test Coverage Expansion

**Problem:** CONCERNS.md identifies critical paths with minimal or no test coverage. Specifically: metadata extraction, thumbnail generation, search fallback (LIKE-based when FTS unavailable), and the batch editing multi-user guard.

### Current State

| Area | Files | Test Status |
|------|-------|-------------|
| Metadata extraction | `MetadataExtractorService.cs` | ❌ No dedicated tests |
| Thumbnail generation | `ImageThumbnailExtractor.cs` | ❌ No dedicated tests |
| Search (LIKE fallback) | `SearchService.cs` | ❌ No dedicated tests |
| Batch multi-user guard | `PropertyInspectorViewModel.cs` | ⚠️ Has batch mode tests but no multi-user guard test |

### Scope

#### T15.5a — MetadataExtractorService Tests
- Test `ExtractTextMetadata()` for JPEG with EXIF title/description/keywords
- Test `ExtractTextMetadata()` for image with no embedded metadata (returns nulls)
- Test `Extract()` returns full `MetadataProfile` with camera make/model, GPS, date taken, etc.
- Test `Extract()` for unsupported file type (returns null or empty profile)
- Mock `MetadataExtractor` library or use real test images (small embedded ones)

**Target:** 8-10 new tests

#### T15.5b — ImageThumbnailExtractor Tests
- Test thumbnail generation for JPEG (verifies file exists at output path)
- Test thumbnail generation for unsupported format (throws or returns null gracefully)
- Test cache behavior — same source file returns cached thumbnail
- Test cancellation during generation
- Use small synthetic test images or mocking

**Target:** 5-7 new tests

#### T15.5c — SearchService Tests
- Test basic text search returns matching assets
- Test search with no query returns all assets
- Test search with special characters (safe query handling)
- Test combined search + type filter
- Test search + date range filter
- Test sorting by different fields (FileName, DateAdded, FileSize)
- Test pagination (page/pageSize parameters)

**Target:** 8-10 new tests

#### T15.5d — Batch Multi-User Guard Test
- Test `ApplyBatchEditAsync()` returns early when `_modeManager.IsStandalone` returns `false`
- Test `DetectBatchMixedValuesAsync()` returns early in multi-user mode
- Test `ComputeAggregatedTagsAsync()` returns null in multi-user mode
- Verify the `!_modeManager.IsStandalone` guard is present in all 4 guarded methods

**Target:** 3-4 new tests

### Total: ~25-30 new tests

### Files Changed

| File | Change |
|------|--------|
| `tests/Adam.Shared.Tests/Services/MetadataExtractorServiceTests.cs` | New |
| `tests/Adam.CatalogBrowser.Tests/Services/ImageThumbnailExtractorTests.cs` | New |
| `tests/Adam.Shared.Tests/Services/SearchServiceTests.cs` | New |
| `tests/Adam.CatalogBrowser.Tests/ViewModels/PropertyInspectorViewModelTests.cs` | Add multi-user guard tests |

### Acceptance Criteria

- [ ] 25+ new tests across 4 critical areas
- [ ] Total test suite grows from 1,061 → 1,086+
- [ ] All new tests pass
- [ ] No regressions in existing tests

---

## Task Summary

| Task | Effort | Files | Tests Added | Priority |
|------|--------|-------|-------------|----------|
| **T15.1** EF Core 10 RTM Upgrade | 1 file, ~15 LOC changed | 2 `.csproj` + potential 2 `.cs` | 0 (run existing) | 🔴 High — blocker for v3.0 stable |
| **T15.2** CSV Streaming | 1 service, ~100 LOC | 2 files changed, 1 test file | 5-8 | 🟡 Medium — only matters for power users |
| **T15.3** Activity Feed Pruning | 1 new service, ~50 LOC | 3 files changed + 1 new | 3-5 | 🟢 Low — non-functional until DB grows |
| **T15.4** Re-scan Folder Wiring | 1 new service, ~80 LOC | 2 files changed + 1 new | 3-5 | 🟡 Medium — user-facing feature gap |
| **T15.5** Test Coverage Expansion | 3 new test files, ~300 LOC | 4 test files | 25-30 | 🟡 Medium — quality investment |

**Total:** ~600-800 LOC, 6-8 files changed, 5 new files, 35-50 new tests

---

## Execution Plan

```
Wave 1: T15.1 (EF Core upgrade) → run tests → fix regressions
Wave 2: T15.3 (feed pruning) + T15.4 (re-scan folder) in parallel
Wave 3: T15.2 (CSV streaming) + T15.5 (test coverage) in parallel
```

**Ordering rationale:**
- EF Core upgrade first because it changes package references that could affect all other work
- Feed pruning and re-scan are independent of each other
- CSV streaming and test coverage are independent and can run in parallel
- Each wave runs its tests before moving on

---

## Success Criteria

- ✅ All 3 DB providers pass with EF Core 10 stable
- ✅ CSV imports 10K+ rows with < 50 MB peak memory
- ✅ Activity feed auto-prunes entries older than retention period
- ✅ Folder "Re-scan" works from sidebar context menu
- ✅ 25+ new tests added (total grows from 1,061 → 1,086+)
- ✅ Zero new warnings from EF Core upgrade
- ✅ All existing tests still pass

---

*Plan generated: 2026-06-14*
*See `.planning/plans/v3.0-milestone.md` for milestone context*
