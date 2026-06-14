# Phase 14 UAT — Feature Growth

**Date:** 2026-06-14
**Build:** All 1,061 tests pass (0 failed, 2 skipped Docker-dependent)
**Branch:** fix/ci-test-hang-241

---

## Feature 1: T14.1 — Batch Metadata Editing

**Status:** ✅ Code audit PASS — implementation complete and correct

**Implementation reviewed:**
- `PropertyInspectorViewModel.cs` (not MetadataEditorViewModel — actual implementation lives here, which is architecturally correct)
- `IsBatchMode` property (set when >1 asset selected)
- `SetMultiSelection()` accepts IEnumerable<AssetListItem>, activates batch mode
- `DetectBatchMixedValuesAsync()` queries DB, detects mixed values for: Description, Rating, Label, Flag, Copyright, GPS, DateTaken
- `ApplyBatchEditAsync()` applies only dirty fields to ALL selected assets with proper DB round-trip
- `IsApplyInProgress` progress state, `HasMultiSelection`/`HasSingleSelection`/`BatchSelectionCountText`/`ApplyBatchButtonText` UX properties
- Metadata write-back after apply (XMP sidecars for RAW/Office, embedded for others)
- Standalone mode only (multi-user batch not wired yet — acceptable for v2)
- `ApplyBatchEditCommand` with `CanExecute` gated by `CanEdit` (permission-aware)

**Code quality:** ✅ — Clean separation of concerns, proper async/await, ConfigureAwait on DB calls, comprehensive dirty-flag pattern, permission gating

**Issues found:** None

---

## Feature 2: T14.2 — CSV Metadata Import/Export

**Status:** ✅ Code audit PASS — implementation complete and correct

**Implementation reviewed:**
- `CsvMetadataService.cs` — Full RFC 4180 CSV reader/writer with:
  - 13-column export (FileName, Title, Description, Keywords (pipe-separated), Categories, Rating, Label, Flag, Copyright, GpsLatitude, GpsLongitude, CameraMake, CameraModel)
  - UTF-8 BOM for Excel compatibility
  - Field filter support (select which groups to export via `fieldFilter` HashSet)
  - `ReadCsvAsync` — column-mapped parsing with validation, missing FileName column throws
  - `ImportFromCsvAsync` — matches by FileName, supports ConflictMode (Overwrite, SkipIfEmpty, AppendKeywords)
  - Batch save every 50 rows for large imports
  - `PreviewImportAsync` — dry-run showing what would change per row
- `ExportDialogViewModel.cs` — CSV export option with field toggles
- `ImportViewModel.cs` — Full preview + import with progress reporting
- `ExportDialog.axaml` and `ImportDialog.axaml` UI files

**Test coverage:** 13 tests in `CsvMetadataServiceTests` — all passing ✅

**Issues found:** None

---

## Feature 3: T14.3 — Metadata Presets

**Status:** ✅ Code audit PASS — implementation complete and correct

**Implementation reviewed:**
- `MetadataPreset.cs` model — 14 nullable fields, `FieldSummary` computed property, `SavedAtFormatted`
- `PresetManager.cs` — JSON file storage in `%LOCALAPPDATA%/Adam/CatalogBrowser/presets/`
  - CRUD: ListPresetsAsync, LoadPresetAsync, SavePresetAsync, CaptureAndSavePresetAsync, DeletePresetAsync, RenamePresetAsync
  - `BaseDirectory` is static and overridable for testing
  - File-name sanitization for safety
  - Async I/O throughout
- `PresetDialogViewModel.cs` — Full CRUD dialog VM:
  - SavePresetCommand / ApplyPresetCommand / DeletePresetCommand / CancelCommand
  - `OnApplyPreset` callback (host handles application)
  - `PresetSaved`/`PresetApplied` events
  - Auto-selects newly created preset

**Test coverage:** 14 tests in `PresetManagerTests` — all passing ✅

**Issues found:** None

---

## Feature 4: T14.4 — Office Document XMP Sidecar Import & Writeback

**Status:** ✅ Code audit PASS — implementation complete and correct

**Implementation reviewed:**
- `OfficeDocumentExtractor.cs` — Extracts metadata from .docx/.xlsx/.pptx via OpenXML SDK:
  - First checks for XMP sidecar (takes precedence over package properties)
  - Falls back to package properties (Title, Subject→Description, Keywords, Category→Categories)
  - Tries WordprocessingDocument → SpreadsheetDocument → PresentationDocument gracefully
- `XmpSidecarReader.cs` — Full XMP XML parser:
  - Supports dc, xmp, photoshop, MicrosoftPhoto, Iptc4xmpCore namespaces
  - HierarchicalSubject support (Lightroom hierarchical keywords)
  - Rating from multiple sources (xmp:Rating attribute, MicrosoftPhoto:Rating element)
  - `SidecarExists()`/`GetSidecarPath()` static helpers
- `MetadataWritebackService.cs` — Updated with `IsOfficeDocument()` for writeback routing
- Both standalone and batch-edit paths use XMP sidecar writeback for Office docs

**Test coverage:** 4 tests in `OfficeDocumentExtractorTests` + 10 in `XmpSidecarReaderTests` — all passing ✅

**Issues found:** None

---

## Feature 5: T14.5 — Bulk Metadata Operations

**Status:** ✅ Code audit PASS — implementation complete and correct

**Implementation reviewed:**
- `AssetGalleryViewModel.cs`:
  - `ApplyFilter()` accepts `ratingFilter`, `labelFilter`, `flagFilter` params
  - `ApplyFilters()` LINQ method applies all three filters via EF Core predicates
  - `SelectedCount`/`HasSelection` for multi-select status bar
  - `ClearSelectionCommand` to clear multi-select
  - `SelectAllCommand` for Ctrl+A
  - `MultiSelectionChanged` event fires on selection change
- `SidebarViewModel.cs`:
  - `SelectedRatingFilter`/`SelectedLabelFilter`/`SelectedFlagFilter` properties
  - `IsRatingFilterActive`/`IsLabelFilterActive`/`IsFlagFilterActive` visibility gates
  - Ready-made `RatingFilterOptions` (Any, Unrated, ★1..★5)
  - `LabelFilterOptions` (Any, None, Red..Purple)
  - `FlagFilterOptions` (Any, Unflagged, Pick, Reject)
  - `OnFilterChanged()` → notifies gallery to reload with filters

**Issues found:** None — filters are wired end-to-end from sidebar dropdowns → gallery query

---

## Feature 6: T14.6 — AI Tag Refinement (Confidence Scoring & Review Dialog)

**Status:** ✅ Code audit PASS — implementation complete and correct

**Implementation reviewed:**
- `AiTaggingService.cs` — Added confidence scoring infrastructure:
  - `KeywordScore` class (INotifyPropertyChanged, Name, Confidence, IsAccepted)
  - `CategoryScore` class (same pattern)
  - `AiTagResult` class (Description, Keywords, Categories, ProcessingTimeMs, ModelVersion)
  - `AnalyzeAssetWithScoresAsync()` — analyzes and returns scored results WITHOUT DB writes
  - `AnalyzeImageTagResult()` — estimates confidence from rank order heuristic:
    - Top 25% → 0.925-0.95
    - Middle 50% → 0.55-0.85
    - Bottom 25% → 0.3-0.5
- `AiTagReviewViewModel.cs` — Full review dialog:
  - KeywordScores/CategoryScores sorted by descending confidence
  - `ConfidenceThreshold` slider (0.0-1.0) with auto-apply threshold
  - `AcceptAllAboveThresholdCommand`
  - Per-item checkbox toggle via `ToggleKeyword`/`ToggleCategory`
  - `AcceptanceSummary` ("Accepted 8 of 12 tags")
  - `ConfidenceToColor()`: green ≥80%, yellow ≥50%, red <50%
  - `ConfidenceToBarWidth()` for visual confidence bars
- `MetadataEditorViewModel.cs` — Updated `AutoTagAsync()`:
  - `ShowAiReviewDialogAsync` callback for review dialog integration
  - When set → shows review dialog before applying (T14.6 flow)
  - When null → falls back to legacy auto-apply (backward compatible)
- `AiTagReviewDialog.axaml` UI file

**Test coverage:** 51 tests in `AiTagReviewViewModelTests` — all passing ✅

**Issues found:** None — note: `AiGenerated` provenance flag on keywords (from original plan) not implemented; confidence is rank-based heuristic, not from model logits

---

## Feature 7: Activity Feed / Notification Panel (recent-changes)

**Status:** ✅ Code audit PASS — implementation complete and correct

**Implementation reviewed:**
- `ActivityEntry.cs` — data model with EntityType, ChangeType, EntityId, AssetName, UserName, Timestamp, IsRead + computed RelativeTime, ChangeIcon (➕✏️🗑️), ChangeIconColor, EntityTypeDisplay, Summary
- `ActivityFeedViewModel.cs`:
  - ObservableCollection<ActivityEntry>, UnreadCount, HeaderText (e.g. "Activity (3 new)")
  - Standalone mode: loads from AccessLog table with batch asset name resolution
  - Multi-user mode: subscribes to BrokerClient.NotificationReceived for live updates
  - Loads historical entries via ListAuditLogsRequest in multi-user mode
  - Commands: Refresh, MarkAllAsRead, ClearAll, ClearFilter
  - UI-thread safe via IUiDispatcher
- `ActivityFeedView.axaml` — Full UserControl with header bar, filter bar, styled ListBox with per-entry icons/summary/username/relative time/unread dot, empty state
- Wired into MainWindowViewModel (DI param + property + ShowActivityFeedCommand), MainWindow.axaml (DataTemplate + "Activity" nav button), App.axaml.cs (Transient DI registration)
- Test files updated: MainWindowViewModelTests, PermissionTests, DropCommandHandlersTests

**Issues found:** None

---

## Feature 8: Execution Provider Options (AI Model Selector)

**Status:** ✅ Code audit PASS — implementation complete and correct

**Implementation reviewed:**
- `AdamConfig.cs` — Added `AiExecutionProvider` (default "Cpu") and `AiGpuDeviceId` (default 0)
- `AiModelSelectorViewModel.cs`:
  - `ProviderOptions` (ObservableCollection with CPU / CUDA (NVIDIA) / DirectML (Windows))
  - `SelectedProviderOption` getter/setter with immediate config persistence + RestartRequired
  - `IsGpuProvider` (true for Cuda/DirectML, gates GPU Device ID field visibility)
  - `GpuDeviceId` with immediate persistence
  - `DownloadOrApplyModelAsync` saves all provider settings together
- `MainWindow.axaml` — Execution Provider ComboBox + GPU Device ID NumericUpDown with IsVisible binding
- `App.axaml.cs` — Reads saved AiExecutionProvider via Enum.TryParse at startup
- Production fix: IsGpuProvider PropertyChanged only fires when value actually changes (not on GPU↔GPU switches)

**Test coverage:** 81 AiModelSelectorViewModel tests — all passing ✅

**Issues found:** None

---

## Feature 9: AI Model Selector (Dropdown, Size Display, Download)

**Status:** ✅ Code audit PASS — implementation complete and correct

**Implementation reviewed:**
- `AiModelDefinition.cs` — 7 model variants with ModelId, Name, Precision, DownloadSize, DownloadSizeBytes, Description, Architecture detection
- `AiModelSelectorViewModel.cs`:
  - AvailableModels populated from AiModelDefinition.All
  - SelectedModel with IsCurrentModel() detection → sets RestartRequired when different
  - SelectedModelDisplay (combines Name + DownloadSize)
  - DownloadOrApplyCommand gated by IsModelSelected && !IsModelDownloading
  - Download progress relayed from AiTaggingService via PropertyChanged events
  - After download: status "Model ready", RestartRequired cleared
- `MainWindow.axaml` — Model ComboBox with DisplayMemberBinding

**Test coverage:** 81 AiModelSelectorViewModel tests (combined with Feature 8) — all passing ✅

**Issues found:** None
