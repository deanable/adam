# Milestone v2.x: Production Hardening & Feature Growth

**Status:** ✅ Complete
**Released:** 2026-06-14
**Phases:** 13-14 (of 14)

## Overview

v2.x delivers the final two phases of the current roadmap: **production hardening** (solution file, CI fixes, null-handler validation, protobuf docs, logging, FolderWatcher batching, EF Core prep) and **feature growth** (batch metadata editing, CSV import/export, metadata presets, Office XMP sidecars, bulk advanced filters, AI tag refinement, AI model selector, execution provider options, activity feed).

All 14 phases of the project roadmap are now complete.

---

## Phase 13 — Production Hardening

**Goal:** Fix accumulated technical debt before building new features.

### What Was Delivered

#### Wave 1 — Project Infrastructure
- **Solution file fix** — Reorganized to include all projects correctly
- **CI fix** — `test.sh` script and workflow updates for reliable CI runs

#### Wave 2 — Code Quality
- **Null-payload handler guards** — All broker handlers now validate for null/empty payloads before deserialization, returning appropriate error codes
- **Protobuf wire protocol docs** — Documented message framing, envelope structure, opcodes, and serialization format

#### Wave 3 — Infrastructure & Observability
- **Logging standardization** — Consistent log patterns across all services (structured logging, correlation IDs, severity levels)
- **FolderWatcher debounce/batch** — File system watcher now batches change events before triggering re-scan (reduces duplicate processing)

#### Wave 4 — EF Core Stabilization
- **Migration prep** — Prepared codebase for EF Core 10 RTM upgrade

#### Wave 5 — Handler Validation Tests + AI Layout Refactor
- **29 handler validation tests** — Comprehensive tests for null payloads, invalid data, and edge cases
- **AI model layout refactor** — Cleaned up model definition organization in LiquidVision.Core

---

## Phase 14 — Feature Growth

**Goal:** Deliver the v2 user-facing features.

### What Was Delivered

#### T14.1 — Batch Metadata Editing
- **Multi-select infrastructure** — `SetMultiSelection()` in `PropertyInspectorViewModel` activates batch mode when 2+ assets selected
- **Mixed-value detection** — `DetectBatchMixedValuesAsync()` queries DB, detects differing values across selected assets for Description, Rating, Label, Flag, Copyright, GPS, DateTaken; displays `"(mixed)"` in UI
- **Apply to all** — `ApplyBatchEditAsync()` writes only dirty fields to all selected assets with optimized DB round-trip (single query loads all, batch SaveChanges)
- **Metadata write-back** — After batch apply, writes XMP sidecars for RAW/Office files, embedded XMP for supported formats
- **Permission gating** — `ApplyBatchEditCommand.CanExecute` gated by `CanEdit`

#### T14.2 — CSV Metadata Import/Export
- **CsvMetadataService** — RFC 4180 compliant reader/writer with UTF-8 BOM for Excel compatibility
  - 13-column export: FileName, Title, Description, Keywords (pipe-separated), Categories, Rating, Label, Flag, Copyright, GpsLatitude, GpsLongitude, CameraMake, CameraModel
  - Field filter support (pick which groups to export via checkboxes)
  - `ReadCsvAsync()` — column-mapped parsing with validation (missing FileName throws)
  - `ImportFromCsvAsync()` — matches by FileName, supports 3 ConflictModes (Overwrite, SkipIfEmpty, AppendKeywords)
  - Batch save every 50 rows for large imports
  - `PreviewImportAsync()` — dry-run showing what would change per row
- **ExportDialog** — CSV format option alongside JPEG/TIFF, with field toggle checkboxes
- **ImportDialog** — File picker, preview pane, conflict mode selector, progress bar
- **13 tests** — all passing

#### T14.3 — Metadata Presets
- **MetadataPreset model** — 14 nullable fields (captures only a subset), `FieldSummary` computed property
- **PresetManager** — JSON file storage in `%LOCALAPPDATA%/Adam/CatalogBrowser/presets/`
  - CRUD: List, Load, Save, CaptureAndSave, Delete, Rename
  - Static `BaseDirectory` overridable for testing
  - File-name sanitization
- **PresetDialogViewModel** — Full CRUD dialog with Save/Apply/Delete commands, callback-based `OnApplyPreset`
- **14 tests** — all passing

#### T14.4 — Office Document XMP Sidecar Import & Writeback
- **OfficeDocumentExtractor** — Extracts metadata from .docx/.xlsx/.pptx via OpenXML SDK
  - First checks for XMP sidecar (takes precedence)
  - Falls back to package properties: Title, Subject->Description, Keywords, Category->Categories
  - Graceful fallback chain: Wordprocessing -> Spreadsheet -> Presentation document
- **XmpSidecarReader** — Full XMP XML parser supporting dc, xmp, photoshop, MicrosoftPhoto, Iptc4xmpCore namespaces
  - HierarchicalSubject support (Lightroom hierarchical keywords)
  - Rating from multiple sources (xmp:Rating, MicrosoftPhoto:Rating)
  - Static helpers: `SidecarExists()`, `GetSidecarPath()`
- **MetadataWritebackService** — `IsOfficeDocument()` method added for writeback routing (creates XMP sidecars for Office docs)
- **14 tests** — all passing

#### T14.5 — Bulk Metadata Operations (Advanced Filters)
- **Sidebar advanced filters** — Rating/Label/Flag dropdowns in sidebar
  - RatingFilterOptions: Any, Unrated, Star 1-5
  - LabelFilterOptions: Any, None, Red, Green, Blue, Yellow, Purple
  - FlagFilterOptions: Any, Unflagged, Pick, Reject
  - Active filter indicators: `IsRatingFilterActive`, `IsLabelFilterActive`, `IsFlagFilterActive`
- **Gallery filter integration** — `ApplyFilter()` accepts rating/label/flag params; `ApplyFilters()` LINQ applies EF Core predicates
- **Multi-select** — `SelectedCount`/`HasSelection`/`ClearSelectionCommand` for selection bar

#### T14.6 — AI Tag Refinement (Confidence Scoring & Review Dialog)
- **KeywordScore/CategoryScore** — INotifyPropertyChanged classes with Name, Confidence, IsAccepted
- **AiTagResult** — Structured result with Description, Keywords, Categories, ProcessingTimeMs, ModelVersion
- **Rank-based confidence heuristic** — Top 25% -> 0.925-0.95, middle 50% -> 0.55-0.85, bottom 25% -> 0.3-0.5
- **AiTagReviewViewModel** — Review dialog with:
  - Keywords sorted by descending confidence with color-coded bars (green >=80%, yellow >=50%, red <50%)
  - Confidence threshold slider with auto-apply
  - Per-item checkbox toggles
  - "Accept all above threshold" button
  - Acceptance summary counter
- **MetadataEditorViewModel** — `ShowAiReviewDialogAsync` callback integrates review dialog into AutoTag flow
- **51 tests** — all passing

#### Activity Feed / Notification Panel 
- **ActivityEntry model** — EntityType, ChangeType, EntityId, AssetName, UserName, Timestamp, IsRead + computed RelativeTime, ChangeIcon (Created/Updated/Deleted), Summary
- **ActivityFeedViewModel** — Standalone mode: loads from AccessLog with batch asset name resolution. Multi-user mode: subscribes to BrokerClient.NotificationReceived for live updates + loads history via ListAuditLogsRequest
- **ActivityFeedView** — Styled UserControl with header, filter bar, per-entry icons, unread dot, empty state
- **Wired** into MainWindowViewModel, MainWindow.axaml (nav button + DataTemplate), App.axaml.cs (DI)

#### AI Model Selector
- **AiModelDefinition** — 7 model variants with ModelId, Name, Precision, DownloadSize, Architecture detection (Lfm2Vl/Lfm25Vl)
- **AiModelSelectorViewModel** — AvailableModels dropdown, SelectedModel selection with restart detection, DownloadOrApplyCommand with progress tracking, config persistence
- **81 tests** — all passing

#### Execution Provider Options
- **AdamConfig** — Adds `AiExecutionProvider` (default "Cpu") and `AiGpuDeviceId` (default 0)
- **ProviderOptions** — CPU, CUDA (NVIDIA), DirectML (Windows) in dropdown
- **IsGpuProvider** — Gates GPU Device ID NumericUpDown visibility; PropertyChanged only fires when GPU/CPU status changes
- **Persistence** — Provider selection saved to config, restored at startup

---

## Test Suite Results

| Test Project | Passed | Failed | Skipped |
|-------------|-------:|-------:|--------:|
| **Adam.CatalogBrowser.Tests** | **487** | 0 | 0 |
| **Adam.Shared.Tests** | **282** | 0 | 0 |
| **Adam.ServiceManager.Tests** | **156** | 0 | 0 |
| **Adam.BrokerService.Tests** | **136** | 0 | 2 |
| **Total** | **1,061** | **0** | **2** |

**Skipped:** 2 Docker-dependent integration tests (PostgreSQL and SQL Server) — require Docker runtime.

**Phase 14 test additions:**
- 13 CsvMetadataService tests
- 14 PresetManager tests
- 4 OfficeDocumentExtractor tests
- 10 XmpSidecarReader tests
- 51 AiTagReviewViewModel tests
- 81 AiModelSelectorViewModel tests
- Plus PropertyInspector batch tests, MainWindowViewModel wiring tests

---

## File Changes Summary

| Phase | Key Files | Impact |
|-------|-----------|--------|
| **Phase 13** | Solution file, CI workflow, 9 broker handlers, protobuf docs, logging config, FolderWatcher, EF Core prep | Hardening & tech debt |
| **Phase 14** | PropertyInspectorVM, CsvMetadataService, ExportDialogVM, ImportVM, PresetManager, PresetDialogVM, OfficeDocumentExtractor, XmpSidecarReader, AiTagReviewVM, AiTaggingService, ActivityFeedVM, ActivityEntry, AiModelSelectorVM, AdamConfig, SidebarVM, AssetGalleryVM | Batch editing, CSV, presets, Office XMP, bulk filters, AI review, activity feed, model selection |

---

## Key Decisions Made

| Decision | Rationale |
|----------|-----------|
| Batch editing in PropertyInspectorVM (not MetadataEditorVM) | PropertyInspectorVM already manages multi-selection and per-asset metadata; MetadataEditorVM is single-asset focused |
| Rank-based confidence heuristic (not model logits) | LiquidVision model doesn't expose per-token logits; rank order is a reliable confidence proxy (model outputs most relevant tags first) |
| AccessLog reuse for Activity Feed (not new table) | AccessLog already records entity type, change type, user, and timestamp; avoids schema migration for a dedicated ActivityLog table |
| XMP sidecar for Office docs (not embedded) | OpenXML package properties are limited; XMP sidecar provides richer metadata round-trip |
| IsGpuProvider guarded PropertyChanged | Prevents unnecessary UI updates when switching between GPU providers (CUDA <-> DirectML) |

---

## What This Means

With v2.x complete, the **entire 14-phase roadmap is done**. The application now has:

- **Full metadata round-trip** (XMP read/write, RAW sidecar, Office document extraction)
- **Multi-user collaboration** (TCP broker, JWT auth, RBAC, real-time change notifications, activity feed)
- **Server management** (cross-platform service installers, user lifecycle, audit log)
- **AI image tagging** (local ONNX model, confidence scoring, review dialog, no cloud dependency)
- **Full-text search** across all three database providers (FTS5/tsvector/CONTAINS)
- **Complete sidebar interaction** (context menus, inline rename, cascade delete, filter state)
- **Batch operations** (metadata editing, CSV import/export, advanced filters, metadata presets)
- **AI model management** (7 model variants, execution provider selection: CPU/CUDA/DirectML)
- **Performance optimizations** (decode-to-size, cached thumbnails, bounded memory, startup profiling)
- **Cross-platform packaging** (Windows MSI, Linux DEB, macOS DMG)
- **1,061 tests passing** across all projects

---

## What's Left for Future Milestones

1. **AiGenerated provenance flag** — Track AI-suggested vs user-added keywords (requires DB schema change)
2. **Multi-user batch editing** — Current batch editing works in standalone mode only
3. **Compare/Loupe views** — Full-resolution pan/zoom and side-by-side comparison (deferred from original plan)
4. **Activity feed pruning** — Auto-prune entries older than 30 days
5. **CSV import for 10K+ rows** — Large file handling with streaming
6. **User feedback-driven features** — v3.0 planning based on real-world usage

---

*Archive generated: 2026-06-14*
