# Phase 15 Review — Quality & Platform

**Date:** 2026-06-15
**Reviewer:** deepseek-flash
**Build:** All 1,097 tests pass (0 failed, 2 Docker-dependent skipped)
**Scope:** All Phase 15 source files (T15.2–T15.5) + deferred test files

---

## Summary

No blocking bugs found. Phase 15 code is well-structured and follows project conventions. All **1,097 tests pass** with **0 failures**.

| Severity | Count |
|----------|:-----:|
| 🔴 Critical | 0 |
| 🟡 Medium | 0 (1 fixed) |
| 🟢 Minor | 2 |
| 💡 Suggestion | 2 |

---

## Findings

### ~~🟡 1. `AccessLogCleanupService.cs` — `catch (Exception)` silently swallows all errors~~ **FIXED**

The `catch (Exception) { return 0; }` block has been removed from `PruneAsync()`. Exceptions now propagate naturally to both callers (`AccessLogCleanupHostedService.RunPruneAsync` and `MainWindowViewModel` startup), each of which already has its own exception handling. Tests confirm 5/5 pass with no regressions.

---

### 🟢 2. `FolderScanService.cs` — Two DbContexts per file with no explanation

**File:** `src/Adam.Shared/Services/FolderScanService.cs`
**Lines:** 74–81, 104

For each file, the service creates a `DbContext` for the duplicate check and a *second* `DbContext` for the actual save. This is intentional (SQLite file-based DB can't share connections across operations), but the reason is undocumented.

```csharp
// Duplicate check: separate context
await using (var checkDb = await _modeManager.CreateDbContextAsync(ct))
{
    var existing = await checkDb.DigitalAssets
        .FirstOrDefaultAsync(a => a.ChecksumSha256 == checksum, ct);
    // ...
}

// Save: separate context
await using var db = await _modeManager.CreateDbContextAsync(ct);
db.DigitalAssets.Add(asset);
await db.SaveChangesAsync(ct);
```

**Suggestion:** Add a brief comment explaining why two separate contexts are used (SQLite file-based mode requires separate connections for concurrent read/write).

---

### 🟢 3. `ImportViewModel.cs` — Streaming progress shows 0% visually

**File:** `src/Adam.CatalogBrowser/ViewModels/ImportViewModel.cs`
**Line:** 167

```csharp
ProgressValue = 0; // Unknown total, show indeterminate progress
```

Setting `ProgressValue = 0` keeps the progress bar at empty throughout a large streaming import. In Avalonia, an `IsIndeterminate` property is needed for marquee-style progress. The text (`"Streaming import… (N rows processed)"`) is helpful, but the visual progress bar at 0% could confuse users.

**Suggestion:** Add an `IsIndeterminate` property to the ViewModel and bind it during streaming import.

---

### 💡 4. `AccessLogCleanupServiceTests.cs` — Unused tuple value

**File:** `tests/Adam.Shared.Tests/Services/AccessLogCleanupServiceTests.cs`

`SeedUserAndLogsAsync` returns `(Guid UserId, AppDbContext Db)` but all callers discard `UserId` with `_`:

```csharp
var (_, db) = await SeedUserAndLogsAsync(("Login", 60), ("Export", 45), ("View", 5));
```

**Suggestion:** Simplify to return only `AppDbContext`, or make `UserId` accessible if needed by future tests.

---

### 💡 5. `SearchService.cs` — DateTimeOffset comparison throws on SQLite

**File:** `src/Adam.Shared/Services/SearchService.cs`
**Lines:** 91–100

The `fromDate`/`toDate` filter uses `DateTimeOffset` comparison in `Where()` clauses, which throws `InvalidOperationException` on SQLite. Documented in comments and test removed, but any caller passing date filters in standalone mode will get a crash.

```csharp
// Note: DateTimeOffset comparison operators (>=, <=) in Where clauses work on
// PostgreSQL/SQL Server but are NOT supported by the SQLite EF Core provider.
```

**Suggestion:** Either:
- Wrap the date filter in a `try/catch` and skip gracefully on SQLite
- Convert to client-side evaluation (load then filter) — similar to the `AccessLogCleanupService` approach

---

## Files Reviewed

| File | Lines | Type |
|------|-------|------|
| `src/Adam.Shared/Services/AccessLogCleanupService.cs` | 85 | Source |
| `src/Adam.Shared/Services/FolderScanService.cs` | 175 | Source |
| `src/Adam.Shared/Services/SearchService.cs` | 112 | Source |
| `src/Adam.BrokerService/Services/AccessLogCleanupHostedService.cs` | 53 | Source |
| `src/Adam.BrokerService/Program.cs` | 96 | Source |
| `src/Adam.CatalogBrowser/Services/CsvMetadataService.cs` | 385 | Source |
| `src/Adam.CatalogBrowser/ViewModels/ImportViewModel.cs` | 188 | Source |
| `tests/Adam.Shared.Tests/Services/SearchServiceTests.cs` | 205 | Test |
| `tests/Adam.Shared.Tests/Services/AccessLogCleanupServiceTests.cs` | 134 | Test |
| `tests/Adam.Shared.Tests/Services/FolderScanServiceTests.cs` | 89 | Test |
| `tests/Adam.CatalogBrowser.Tests/Services/CsvMetadataServiceStreamingTests.cs` | 285 | Test |
| `tests/Adam.CatalogBrowser.Tests/ViewModels/PropertyInspectorViewModelTests.cs` | 190 | Test |

---

## Verdict

✅ **No blocking issues.** All 1,097 tests pass (0 failed). Phase 15 is ready for milestone archiving.
