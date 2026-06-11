# State: adam

**Project:** adam — Digital Asset Management System  
**Initialized:** 2026-05-23  
**Current Phase:** 8 — v1.0 Polish & Ship
**Current Milestone:** v1.2 — Client Polish

## Project Reference

See: `.planning/PROJECT.md` (updated 2026-05-23)

**Core value:** Users can browse, search, and manage digital assets with full metadata round-trip across platforms  
**Current focus:** Final stabilization, performance audit, and distribution packaging

## Phase Progress

| Phase | Status | Plans | Progress |
|-------|--------|-------|----------|
| 1 | ✅ | 1/1 | 100% | Archived |
| 2 | ✅ | 1/1 | 100% | Archived |
| 3 | ✅ | 1/1 | 100% | Archived |
| 4 | ✅ | 1/1 | 100% | Archived |
| 5 | ✅ | 1/1 | 100% | Archived |
| 6 | ✅ | 1/1 | 100% | Archived |
| 7 | ✅ | 1/1 | 100% | Archived |
| 8 | 🚧 | 0/1 | 0% | Current |
| 9 | ✅ | 1/1 | 100% | Archived |

## Accumulated Context

### Roadmap Evolution
- Phase 9 added (2026-06-07): AI Image Tagging — integrate in-repo `LiquidVision.Core` (LFM2-VL ONNX) for local image auto-tagging. Triggers: opt-in during ingest, per-asset Auto-tag button, bulk re-tag. Auto-apply/union/no-provenance; status-bar download progress; Q4F16/CPU defaults.
- Phase 8 expanded (2026-06-10): added **Work Stream 4 — UI Polish & Interaction** (T8.15–T8.26) following a UI audit (`.planning/phases/08-v1-0-polish-ship/08-UI-REVIEW.md`, 13/24, Experience Design 1/4). Audit found zero context menus app-wide, `DeleteService` built but unwired to any UI, and gallery tiles binding none of the affordances their control draws. Blockers: T8.16 (gallery context menu), T8.17 (wire delete), T8.20 (bind tile affordances).

## Active Decisions

| Decision | Status | Notes |
|----------|--------|-------|
| Dual-mode architecture | ✓ Confirmed | Standalone first, then multi-user |
| Avalonia 12 (not 11 as spec'd) | ✓ Confirmed | Project already uses 12.0.3 |
| EF Core 10 preview | ⚠️ Risk | Monitor for stable release |
| Manual protobuf | ✓ Confirmed | No protoc generator in use |
| TLS required for multi-user | ✓ Decided | Architecture review made this mandatory |

## Blockers

None.

## Architecture Review Complete

**Location:** `.planning/ARCHITECTURE-REVIEW.md`

5 domains analyzed, 47 findings identified, 30 recommendations made.

### Critical Issues Found (Must Fix in Phase 2)
- **✅ CRITICAL-4:** No TLS — JWT and passwords in plaintext over TCP → **FIXED** (T2.1)
- **✅ CRITICAL-5:** Hardcoded JWT secret committed to source control → **FIXED** (T2.2 — env var required, documented)
- **✅ CRITICAL-6:** No authorization on Asset/Collection/Change handlers → **FIXED** (T2.3)
- **✅ CRITICAL-8:** StatusMessages.cs compares raw protobuf tag values (bug) → **FIXED** (T2.4)
- **✅ CRITICAL-9:** String-based MessageType routing tied to C# `nameof` → **FIXED** (T2.5 — stable opcode enum)
- **✅ CRITICAL-10:** BrokerClient has zero reconnection logic → **FIXED** (T2.6)
- **✅ HIGH-13:** Multi-user sidebar is non-functional (empty trees) → **FIXED** (T2.7)

### Overall Grade: C (Adequate for prototype, not production)

## Context Notes

- Brownfield codebase exists with domain models, EF Core config, TCP transport, and Avalonia shell
- See `.planning/codebase/` for architecture, conventions, and concerns
- **FIXED:** Client/server port mismatch (5000 vs 9100) — BrokerClient now uses 9100
- **FIXED:** All failing tests — BulkOperationQueue disposal, progress tracking, checksum uniqueness, filter expectations
- **FIXED:** DatePicker binding — SelectedAssetDateTaken changed from DateTime? to DateTimeOffset? with model conversion
- **FIXED:** Static mutable JWT key in AuthHandler (T2.12) — instance field, no longer static
- **FIXED:** TLS transport (T2.1), brute-force protection (T2.13), JWT claims (T2.14), security logging (T2.15)
- **FIXED:** Auto-reconnect (T2.6), timeout/retry (T2.20), ChangePoller retry (T2.21), token expiry (T2.23), connection status UI (T2.22)
- **FIXED:** FolderWatcher service (T2.24) with debounced auto-indexing
- **FIXED:** Watched folder DB persistence + admin panel UI (T2.25)
- All 104 tests pass (2 Docker-dependent skipped)
- **25 of 25 Phase 2 tasks complete**
- **Phase 2 archived to:** `.planning/milestones/v1.0-in-progress.md`
- **9 of 9 Phase 3 tasks complete**
- **Phase 3 plan:** `.planning/plans/phase-3/PLAN.md`
- **Phase 5 archived to:** `.planning/milestones/v1.0-in-progress.md`
- All 106 tests pass (2 Docker-dependent skipped)

## Phase 2 Plan

**Location:** `.planning/plans/phase-2/PLAN.md`
**Tasks:** 25 organized into 6 work streams
**Estimated Effort:** ~15.5 days

### Work Streams
1. **Security Hardening** — TLS, JWT secret removal, authorization, protocol fixes
2. **Broker Reliability** — Task observation, graceful shutdown, write timeout, idle detection
3. **Auth Layer Fixes** — Signing key race fix, brute-force protection, structured security logging
4. **Database & Data Layer** — Concurrency tokens, query optimization, connection resiliency
5. **Client Resilience & UX** — Auto-reconnect, retry logic, token expiration, connection status UI
6. **Folder Watcher** — FileSystemWatcher-based auto-indexing

## Phase 3 Plan

**Location:** `.planning/plans/phase-3/PLAN.md`
**Tasks:** 9
**Estimated Effort:** ~5.5 days
**Status:** Complete

### Work Streams
1. **Connection Registry** — Per-connection tracking, thread-safe broadcast
2. **Change Notifications** — Push-based real-time updates, fire-and-forget broadcast
3. **Conflict Resolution** — ExpectedVersion optimistic concurrency
4. **Client Refresh** — Gallery auto-refresh on remote change notification

## Phase 4 Plan

**Location:** `.planning/plans/phase-4/PLAN.md`
**Tasks:** 10
**Estimated Effort:** ~6.5 days
**Status:** Complete — all tasks T4.1–T4.10 implemented and committed

### Work Streams
1. **Metadata Round-Trip** — Ratings, labels, flags, GPS, copyright + XMP write-back
2. **RAW Sidecar** — XMP sidecar for CR2/NEF/ARW/DNG
3. **Image Adjustments** — Rotate 90/180/270, flip horizontal/vertical
4. **Export** — JPEG with quality/resolution, TIFF with color space

## Phase 4 Completion Summary

- **T4.1:** Added Rating, Label, Flag, GpsLatitude, GpsLongitude, Copyright, Orientation to DigitalAsset
- **T4.2:** Client UI with ComboBoxes for Rating/Label/Flag, TextBoxes for Copyright/GPS, dirty tracking + auto-save
- **T4.3-T4.4:** XMP write-back service with embedded (JPEG/PNG/TIFF/WebP) and RAW sidecar (.xmp) support
- **T4.5:** Read-only file guard with ReadOnlyFileException and client toast notification
- **T4.6-T4.7:** Image rotation (90/180/270) and flip (horizontal/vertical) with thumbnail regeneration via ImageSharp
- **T4.8-T4.9:** Export dialog for JPEG (quality) and TIFF (LZW/None compression) with optional max dimension resize
- **T4.10:** 5 integration tests passing for metadata round-trip, sidecar creation, read-only guard, JPEG/TIFF export
- All 117 tests pass (2 Docker-dependent skipped)

## Phase 5 Completion Summary

- **T5.1:** Created `AdminPanelViewModel` — consolidated dashboard with mode toggle (Standalone/Multi-User), service connection (host/port, connect/disconnect), service status (connected clients, uptime, traffic-light health), and embedded migration wizard
- **T5.2:** Created `AdminPanelView.axaml` — dashboard layout with mode selection radio buttons, connection panel, service status cards, and collapsible migration wizard section
- **T5.3-T5.4:** Linux systemd & macOS launchd service installers were already fully implemented (not stubs as originally thought)
- **T5.5:** In-app service status display with traffic-light indicators, connected client count, uptime formatting, and auto-refresh every 10 seconds
- **T5.6:** Admin navigation button in title bar, `ShowAdminPanelCommand` in `MainWindowViewModel`, DataTemplate registration, DI registration for `AdminPanelViewModel`
- **T5.7:** Migration wizard polish — `BrowseSourceAsync` using `StorageProvider` file picker, `CancelMigration` with `CancellationTokenSource`, guard checks for empty source/missing service
- **T5.8:** 47 new tests across 2 test files — `AdminPanelViewModelTests.cs` (33 tests: constructor, mode properties, service status, UI state, FormatUptime, PropertyChanged) and `MigrationWizardViewModelTests.cs` (14 tests: constructor, provider selection, command guards, PropertyChanged, cancel)
- **T5.9:** Shared Data Foundation — `DbProviderConfig` moved to `Adam.Shared`, provider packages added, `ModeManager` and `BrokerService/Program.cs` updated to use shared config
- **T5.10:** User Lifecycle — ServiceManagerConfig persisted `DbProvider`/`DbConnection`, `App.axaml.cs` initializes `ModeManager` with configured provider, full CRUD in `UserManagementViewModel` with soft-delete, `UserManagementView.axaml` layout finalized, 40+ tests
- **T5.11:** Windows Service Hardening — UAC dismissal handled gracefully (Win32Exception → OperationCanceledException), `FirewallRuleManager` checks rule existence before adding, "Open Log" button in ServiceManagerView, background status poll timer
- **T5.12:** Audit Log View — `AuditLogViewModel` with standalone DB querying, client-side DateTimeOffset filtering (SQLite-compatible), Action/EntityType/Date range filters, 19 new tests. `AuditLogView.axaml` wired as third tab in ServiceManager.
- All **156 ServiceManager tests pass** (0 failed)
- Grid.Row layout fix in MainWindow.axaml — 3-column layout moved from Row 1 to Row 2 to coexist with connection panel

## Phase 6 Completion Summary

- **T6.1:** Fixed `HasFilter("NOT IsDeleted")` in `AppDbContext.OnModelCreating` — now provider-aware via `Database.ProviderName`: SQLite → `NOT IsDeleted`, PostgreSQL → `"IsDeleted" = FALSE`, SQL Server → `[IsDeleted] = 0`
- **T6.2:** Fixed raw SQL in `MigrationRunner.MigrateSchemaAsync` — introduced provider-aware `Col()` local function that generates correct ALTER TABLE syntax with proper quoting for SQLite (unquoted), PostgreSQL (double-quoted), and SQL Server (bracketed)
- **T6.3:** No change needed — `ModeManager.ApplyMigrationsAsync` is standalone-only (always SQLite)
- **T6.4:** Created `DockerAvailability` helper + `DockerFactAttribute` for conditional Testcontainers test skipping — PostgreSQL/SQL Server integration tests now run automatically when Docker is available and skip with "Requires Docker" when absent (vs. previously hard-skipped)
- **T6.5:** Added `DbProvider: "sqlite"` and `DbConnection: "Data Source=catalog.db"` defaults to `appsettings.json`
- **T6.6:** Added `DbProviderConfig_Configure_builds_options` theory test — verifies the correct EF Core provider extension (Sqlite/Npgsql/SqlServer) is registered for each provider string
- All **510 tests pass** (3 new from T6.6, 2 Docker-dependent skipped)

## Phase 9 Completion Summary

- **T9.1:** Build wiring — `LiquidVision.Core` referenced from `Adam.Shared`, ONNX Runtime + SkiaSharp distribution dependency accepted
- **T9.2:** `AiTaggingService` (`Adam.Shared/Services`) — wraps `ILiquidVisionAnalyzer`, lazy `InitializeAsync` with model download on first use, image-only guard, keyword/category/description merge via existing `AssociateKeywordsAsync`/`AssociateCategoriesAsync`. Auto-apply, union with existing tags, no provenance tracking. Fills `Description` when empty.
- **T9.3:** DI registration — `AddLiquidVision(...)` in `CatalogBrowser/App.axaml.cs` with `Precision = Q4F16`, `ExecutionProvider = Cpu` (singleton analyzer)
- **T9.4:** Trigger A (Ingest opt-in) — `EnableAiTagging` checkbox in `IngestionViewModel`, sequential post-pass after parallel ingest completes
- **T9.5:** Trigger B (Per-asset Auto-tag) — `AutoTagCommand` in `MetadataEditorViewModel`, unions into Tags/Categories, persisted via `SaveAsync`
- **T9.6:** Trigger C (Bulk re-tag) — `AiTagSelectedCommand` in gallery toolbar, filters to images, sequential with status bar progress
- **T9.7:** Status bar progress — `IsModelDownloading`/`ModelDownloadPercentage` + `IsAiTaggingActive`/`AiTaggingPercentage`/`AiTaggingProgressText` on `StatusBarViewModel`
- **T9.8:** Tests — 7 unit tests covering image-only guard, keyword/category merge, description fill-only-when-empty, cancellation, batch progress, analyze-only path
- All **7 AI Tagging tests pass**; all projects build cleanly (0 warnings)

## Phase 7 Completion Summary

- **T7.1:** Permission-aware properties (`CanIngest`, `CanEditMetadata`, `CanAudit`, `CanAdminister`, `CanAiTag`, `SessionStatusText`) with `EvaluatePermission()` helper and `RefreshPermissionsAsync()` — fires PropertyChanged and raises CanExecuteChanged for all permission-gated commands
- **T7.2:** UI gating with `IsEnabled` on right panel Tags StackPanel — gates 14+ editing controls (description, date picker, categories/keywords, rating, label, flag, copyright, GPS, rotate/flip, export, save) by `CanEditMetadata`. MetadataEditorView left-column editing controls gated with `IsEnabled="{Binding CanEdit}"` and defense-in-depth `SaveCommand` CanExecute checking `CanEdit`. Permission tooltips (`EditPermissionTooltip`) show contextual explanation on hover when controls are disabled: "Sign in to edit metadata", "Session expired — re-login required to edit metadata", "Requires Editor or Administrator role"
- **T7.3:** 60-second `_sessionCheckTimer` — calls `ValidateTokenAsync()` which now queries the DB on the broker side for the user's current role and active status
- **T7.4:** `ForceLogout` event handling — `ConnectionViewModel.ValidateSessionAsync` fires `ForceLogout` on deactivation → `MainWindowViewModel` clears session state, updates status bar, refreshes permissions
- **T7.5:** Dynamic role change detection — `AuthHandler.ValidateToken` now queries DB for current role via `AppDbContext`, returning updated claims (not stale JWT). `SessionInvalidated` opcode (115) pushed to affected connections via `ConnectionRegistry.GetConnectionIdsByUserId` when role changes or account is deactivated in `UserHandler`. `BrokerClient.SessionInvalidated` event triggers `ConnectionViewModel.ValidateSessionAsync`. Three-tier detection: instant push (SessionInvalidated), periodic (60s timer), and defense (token expiry)
- **T7.6:** `SessionStatusText` visible in status bar — shows "Local mode — full access", "Username — Role", "Not logged in", or "Session expired — Role (relogin required)"
- **Nav button tooltips:** Ingest, Metadata, Audit, and Server Admin buttons changed from `IsVisible` (hidden) to `IsEnabled` with `Classes="NavButton"` — users see all features even when locked. `GetNavTooltipText(actionDescription, requiredRole)` shared helper returns session-state-aware tooltips (not logged in / token expired / no permission). `IngestPermissionTooltip`, `MetadataPermissionTooltip`, `AuditPermissionTooltip`, `AdminPermissionTooltip` properties with PropertyChanged notifications. Disabled state styled with `Opacity="0.35"`.
- **Service infrastructure:** `ConnectionRegistry.GetConnectionIdsByUserId` for targeted push notifications. `UserHandler` injects `ConnectionRegistry` and calls `NotifySessionInvalidatedAsync` after role change or deactivation
- **IngestionView defense-in-depth:** Select Files, Select Folder, and drag-drop area gated by `IsEnabled="{Binding DataContext.CanIngest}"` with `ToolTip.Tip` for permission tooltip, ensuring a Viewer user cannot initiate a folder scan even if they navigate to the page. Gallery toolbar AI Tag Selected button changed from `IsVisible` to `IsEnabled` with `ToolTip.Tip` binding.
- **Tests:** 23 permission tests (nav tooltips, session status, token expiry, dynamic role changes), 20 MetadataEditorViewModel CanEdit tests — all passing

## Next Actions

1. **Execute Phase 8**: v1.0 Polish & Ship — four work streams: Performance (T8.1–T8.6), Documentation (T8.7–T8.9), Packaging (T8.10–T8.14), and **UI Polish & Interaction (T8.15–T8.26)**. Plan at `.planning/phases/08-v1-0-polish-ship/08-PLAN.md`.
2. **UI blockers first**: T8.16 gallery context menu, T8.17 wire delete + Trash view, T8.20 bind tile affordances — these close most of the perceived UI incompleteness.
3. **Open scope decision**: T8.18 (sidebar CRUD) is the largest UI item and touches the broker/multi-user path — keep in Phase 8 or split per ALT-005. [POLISH] tasks T8.23–T8.26 deferrable to v2 per ALT-006.
4. **Continue milestone v1.2**: Client Polish — Phase 8 (v1.0 Polish & Ship).

---
*State updated: 2026-06-10 — Phase 8 UI audit complete, Work Stream 4 added to plan*

