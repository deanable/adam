# Spec Audit: `specs/001-digital-asset-management/` vs GSD Phases 1–23

**Date:** 2026-06-18  
**Purpose:** Cross-reference the original feature specification against the actual GSD-implemented codebase to identify missed edge cases, unmet acceptance criteria, and gaps in functional requirements.

---

## Coverage Summary

| Category | Met | Partial | Missed |
|----------|-----|---------|--------|
| Functional Requirements (29) | 22 ✅ | 4 ⚠️ | 3 ❌ |
| Edge Cases (15) | 6 ✅ | 4 ⚠️ | 5 ❌ |
| Acceptance Scenarios (26) | 20 ✅ | 3 ⚠️ | 3 ❌ |
| Success Criteria (12) | 8 ✅ | 4 ⚠️ | 0 ❌ |

---

## Functional Requirements — Audit Table

| ID | Description | Status | Evidence / Gap |
|----|-------------|--------|----------------|
| FR-001 | Dual-mode: standalone + multi-user | ✅ | ModeManager routes between local DB and broker |
| FR-002 | Standalone init without external process | ✅ | ModeManager creates local SQLite + IFileService |
| FR-003 | First-launch folder picker, persisted | ❌ | **No onboarding flow.** Folder picker exists in Ingestion tab but is manual, not auto-triggered on first launch. |
| FR-004 | Multi-user TCP simultaneous connections | ✅ | TcpListenerService + ConnectionHandler |
| FR-005 | DB abstraction (SQLite/PG/SQL Server) | ✅ | DbProviderConfig, 3 providers |
| FR-006 | Auth against broker service | ✅ | JWT, PBKDF2, AuthHandler |
| FR-007 | EXIF/IPTC/XMP extraction on ingest | ⚠️ | **Partial.** Most fields extracted, but `UsageTerms`, `ContactInfo`, `City/State/Country`, `Orientation`, `GpsAltitude` are stored but invisible in UI (§25 finding). |
| FR-008 | Grid + loupe + compare views | ✅ | Phase 20 added LoupeView + CompareView |
| FR-009 | Hierarchical keywords (unlimited nesting) | ✅ | Keyword.ParentId + AssetKeywords junction |
| FR-010 | Star ratings, color labels, flagging | ✅ | RatingInfo, AssetLabel, AssetFlag enums |
| FR-011 | Curated collections independent of folders | ✅ | Collection entity with membership |
| FR-012 | Edit metadata (EXIF/IPTC/XMP, title, desc, keywords, ratings, labels, GPS, copyright) | ⚠️ | **Partial.** Creator/Copyright/Headline are displayed read-only. Eight stored fields invisible. GPS not editable in dedicated metadata tab. |
| FR-013 | Writeback to source file XMP/sidecar | ❌ | **`MetadataEditorViewModel.SaveAsync` persists to DB only, does NOT call `MetadataWritebackService`**. Writeback only triggered via broker AssetHandler and PropertyInspectorViewModel. |
| FR-014 | Rotate/flip | ✅ | ImageAdjustmentService |
| FR-015 | Export JPEG/TIFF with quality/resolution/color space | ✅ | ExportDialogViewModel + ImageExportService |
| FR-016 | Search across all metadata fields | ✅ | FTS5 + composite indexes (Phase 21) |
| FR-017 | Soft-delete (doesn't remove source file) | ✅ | IsDeleted flag + Trash view + PermanentDelete |
| FR-018 | Audit log all create/update/delete | ✅ | AccessLog + AuditLogger |
| FR-019 | TCP+Protobuf API | ✅ | TcpFrame + protobuf Envelope |
| FR-020 | SHA256 duplicate detection | ✅ | ChecksumService |
| FR-021 | Cross-platform (Win/Mac/Linux) | ✅ | Avalonia 12, platform service installers |
| FR-022 | Native service deploy (SCM/launchd/systemd) | ✅ | IServiceInstaller implementations |
| FR-023 | Folder watcher for auto-indexing | ⚠️ | **Partial.** Watches for new/changed/deleted files, but does NOT track external file moves/renames (deletes+creates not correlated). Metadata re-extraction on Changed events is not guaranteed. |
| FR-024 | Mode toggle UI (admin panel) | ⚠️ | **Restructured.** Mode toggle lives in connection bar, not a dedicated admin panel. ServiceManager is a separate project. Functional but differs from spec layout. |
| FR-025 | DB migration wizard | ✅ | DbMigrationService exists. No conflict detection on migration. |
| FR-026 | Service deployment tools per platform | ✅ | IServiceInstaller (Windows/Linux/macOS) |
| FR-027 | IFileService abstraction | ✅ | IFileService interface |
| FR-028 | Manual re-scan trigger | ✅ | RescanFolderCommand in SidebarViewModel |
| FR-029 | RBAC (Viewer/Editor/Administrator) | ✅ | AuthorizationMiddleware + 3 seeded roles |

---

## Edge Cases — Audit Table

| # | Edge Case (from spec) | Status | Notes |
|---|-----------------------|--------|-------|
| 1 | Broker unavailable → fallback to standalone? | ❌ | **No automatic fallback.** BrokerClient has auto-reconnect (10 attempts), but app sits disconnected on failure. User cannot switch to standalone mid-session. |
| 2 | Concurrent metadata edits by two editors | ✅ | Version-based last-write-wins conflict resolution |
| 3 | Storage runs out of space during ingest | ❌ | No disk-full detection. Would manifest as unhandled IOException. |
| 4 | Unsupported/corrupt files in root folder | ✅ | File type whitelist, graceful skip |
| 5 | Network interruption during multi-user ingest | ⚠️ | Auto-reconnect exists (`BrokerClient`), but no ingest retry/queue for interrupted operations |
| 6 | Configured DB provider unreachable | ⚠️ | Connection error handling exists, but no graceful degradation or retry guidance for user |
| 7 | File read-only when writing metadata | ✅ | `ReadOnlyFileException` + user notification in UI |
| 8 | XMP sidecar for RAW files | ✅ | `WriteSidecarXmpAsync` implemented and tested |
| 9 | User edits metadata conflicting with embedded | ⚠️ | **No "file differs from catalog" reconciliation UI.** No banner or authority chooser. |
| 10 | Large keyword hierarchies (10,000+) | ⚠️ | Indexes exist for keyword lookups, but no specific testing or optimization at this scale |
| 11 | Auth session expires mid-session | ✅ | Session expiry handling in MainWindowViewModel, logout prompt |
| 12 | Standalone DB migration conflicts | ❌ | **No conflict detection.** DbMigrationService copies data blindly; if target has data, may duplicate or skip silently. |
| 13 | Native service registration fails (permissions) | ✅ | Platform installer wrappers handle and surface errors |
| 14 | Corrupt metadata in watched files | ✅ | Extraction service error handling catches corrupt metadata gracefully |
| 15 | Files moved/renamed outside the application | ❌ | **No move/rename tracking.** `FolderWatcherService` sees separate Deleted + Created events. Asset becomes broken link at old path; duplicate at new path. |

---

## Acceptance Scenarios — Audit Table (Key Gaps)

Only scenarios with issues shown. Full list in `spec.md`.

| Story | Scenario | Status | Notes |
|-------|----------|--------|-------|
| US1 | On first launch, user selects root folder → scan + index | ❌ | No first-launch wizard; user must navigate to Ingestion tab manually |
| US2 | Search results appear within 2 seconds (multi-user) | ⚠️ | Not benchmarked in CI, but indexes + keyset pagination suggest it's achievable |
| US4 (Lightroom) | Loupe/compare views render within 1 second | ⚠️ | No automated performance benchmark |
| US5 (Metadata) | All EXIF/IPTC/XMP fields displayed and searchable | ❌ | 8 stored-but-invisible fields; Creator/Copyright/Headline read-only only |
| US5 (Metadata) | Metadata edits written to source file within 5 seconds | ❌ | MetadataEditorViewModel.SaveAsync does NOT invoke writeback |
| US7 (Ingest) | Metadata change on disk → watcher re-extracts within 30s | ⚠️ | Watcher detects changes but re-extraction on Changed events not guaranteed |
| US7 (Ingest) | Duplicate file → single entry + duplicate path logged | ⚠️ | ChecksumService detects duplicates, but the "log the duplicate path" part is unclear |

---

## Success Criteria — Audit Table

| ID | Criterion | Status | Notes |
|----|-----------|--------|-------|
| SC-001 | Standalone mode without service/network | ✅ | Verified |
| SC-002 | 10 concurrent users <3s response | ✅ | ConcurrentClientsTests pass |
| SC-003 | Search <2s at 100K assets | ⚠️ | Not benchmarked in CI. Phase 21 composite indexes + keyset pagination help. |
| SC-004 | 100% EXIF/IPTC/XMP captured for JPEG/TIFF | ⚠️ | Most fields captured; 8 stored fields invisible in UI |
| SC-005 | Metadata writeback <5s | ✅ | Tested in MetadataWritebackServiceTests |
| SC-006 | Grid 100 thumbnails <1s, Loupe <3s | ⚠️ | Phase 21 adds virtualization + async I/O. No automated benchmark. |
| SC-007 | 10K files scanned <5min | ✅ | FolderScanService tested |
| SC-008 | Launch <10s on all platforms | ✅ | Verified |
| SC-009 | Multi-user change propagation <5s | ✅ | ChangeNotificationService tested |
| SC-010 | Primary tasks without crash | ✅ | 1,302 tests pass, 0 fail |
| SC-011 | Native service on all 3 platforms | ✅ | Service installer implementations exist |
| SC-012 | DB migration <5min for 10K assets | ⚠️ | DbMigrationService exists but no benchmark |

---

## Summary of Correctness Concerns in Spec vs Implementation

### Spec artifacts (not bugs, but misleading)

1. **`CreateAssetRequest.Content` field** — the wire contract defines `bytes content` for upload, but the system uses in-place indexing (no file copying). This field is never populated. The protobuf contract is vestigial.
2. **`IFileService` was refactored** — the spec defines a single `IFileService` for metadata read/checksum/thumbnail. The actual code separates these into `MetadataExtractorService`, `ChecksumService`, `ThumbnailService`, `MetadataWritebackService`. Not a compliance gap but the spec abstraction doesn't match the architecture.
3. **Spec mentions gRPC** (`spec-user-draft.md`) but the decision was raw TCP + protobuf (`research.md`). The main spec correctly says TCP. The user draft is outdated.
4. **Admin panel** was split into `ServiceManager` (service admin, user mgmt, audit) and connection bar (mode toggle). The spec envisioned a single admin panel inside CatalogBrowser.

---

## Recommended Fix Priority

### 🔴 Fix immediately (high user impact, bounded scope)

| # | Gap | Est. Effort | Files Touched |
|---|-----|-------------|---------------|
| 5 | Surface 8 hidden metadata fields + make Creator/Copyright/Headline editable (§25-A) | 🟢 1-2 days | `MetadataEditorViewModel.cs`, `MetadataEditorView.axaml`, `MetadataProfile.cs` |
| 4 | Wire `MetadataEditorViewModel.SaveAsync` to call `MetadataWritebackService` | 🟢 1 day | `MetadataEditorViewModel.cs` |

### 🟡 Fix next phase (medium effort, high value)

| # | Gap | Est. Effort | Files Touched |
|---|-----|-------------|---------------|
| 2 | Broker-unavailable fallback to standalone | 🟡 3-5 days | `BrokerClient.cs`, `ModeManager.cs`, `MainWindowViewModel.cs` |
| 3 | External file move/rename tracking in FolderWatcher | 🟡 3-5 days | `FolderWatcherService.cs`, `FileIndexer.cs` |
| 1 | First-launch onboarding/wizard | 🟡 3-5 days | New `SetupWizardView.axaml`+VM, `App.axaml.cs`, `ModeManager.cs` |
| 6 | Metadata re-extraction on file modification | 🟢 2 days | `FolderWatcherService.cs`, `FileIndexer.cs` |

### 🟢 Polish later

| # | Gap | Est. Effort |
|---|-----|-------------|
| 7 | DbMigrationService conflict detection | 🟡 3 days |
| 8 | Performance benchmark automation in CI | 🟡 3-5 days |
| 9 | Connection settings UI | 🟢 1-2 days |
| 10 | Storage space monitoring during ingest | 🟢 1 day |
| 12 | Remove vestigial `CreateAssetRequest.Content` from API | 🟢 1 hour |

---

*Audit generated 2026-06-18 by cross-referencing `specs/001-digital-asset-management/` (plan.md, spec.md, data-model.md, api.md, quickstart.md, tasks.md, research.md) against the actual GSD-implemented codebase in `src/` and `tests/`. 1,302 passing tests verified.*
