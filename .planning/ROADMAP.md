# Roadmap: adam

**Project:** adam — Digital Asset Management System  
**Updated:** 2026-06-14
**Granularity:** Standard  
**Phases:** 18 planned (16 complete, 17-18 on roadmap)

## Overview

| # | Phase | Goal | Requirements | Status |
|---|-------|------|--------------|--------|
| 1 | Standalone Mode | Fully functional local catalog with SQLite | CATA-01 to CATA-11 | ✅ Complete |
| 2 | Multi-User Foundation | Client-Server TCP connectivity & Auth | AUTH-01 to AUTH-03 | ✅ Complete |
| 3 | Concurrency | Real-time change propagation & conflict resolution | BROK-01 to BROK-04 | ✅ Complete |
| 4 | Metadata Management | XMP Round-trip, adjustments, and export | META-01 to META-05, EDIT-01 to EDIT-04 | ✅ Complete |
| 5 | Server Management Tooling | Dedicated admin tool for Users, Roles, and Services | AUTH-04, AUTH-05, ADMIN-02, ADMIN-03 | ✅ Complete |
| 6 | Database Provider Matrix | Full SQL Server/PostgreSQL validation & Migration | DB-02 to DB-04 | ✅ Complete |
| 7 | Client RBAC & Hardening | Permission-aware UI and session stability | ADMIN-01, ADMIN-04 | ✅ Complete |
| 8 | v1.0 Polish & Ship | UI polish, docs, packaging, stabilization | All v1 Final | ✅ Complete |
| 9 | AI Image Tagging | Local LFM2-VL auto-tagging of images via LiquidVision.Core | AI-01 to AI-08 | ✅ Complete |
| 10 | Sidebar CRUD & Tree Interaction | XAML ContextFlyout context menus, cascade delete, inline rename, broker handlers, filter state | UI-V2-01 to UI-V2-08 | ✅ Complete |
| 11 | Full-Text Search (FTS5) | SearchService → IFtsService integration, dedicated search bar, highlighting, suggestions | PERF-01, META-V2-01 | ✅ Complete |
| 12 | Performance Optimization | Decode-to-size, VirtualizingStackPanel tuning, bitmap disposal, lazy init, startup profiling | PERF-02 to PERF-05 | ✅ Complete |
| 13 | Production Hardening | Solution file, CI fix, null-handler validation, protobuf docs, logging, watcher batching, EF Core prep | CONCERNS-01 to CONCERNS-07 | ✅ Complete |
| 14 | Feature Growth | Batch metadata editing, CSV import/export, activity feed, metadata presets, Office XMP sidecar, bulk advanced filters, AI tag refinement, AI model selector, execution provider | META-V2-02, META-V2-03, COLL-V2-01, COLL-V2-03 | ✅ Complete |
| 15 | Quality & Platform | EF Core 10 stable, CSV streaming, feed pruning, re-scan wiring, test coverage | CONCERNS (EF Core 10), T15.2-T15.5 | ✅ Complete |

## Phase Details

### Phase 5: Server Management Tooling ✅
**Goal:** Mature the `Adam.ServiceManager` application as the primary, separate interface for server-side administration.

**Deliverables:**
1. **User & Role Lifecycle**: Complete the management UI in `ServiceManager` for adding, editing, and deactivating users.
2. **Native Service Installation**: Fully implement service installers for Windows (SCM), Linux (systemd), and macOS (launchd).
3. **Admin Elevation Flow**: Ensure `ServiceManager` handles OS-level elevation (UAC/sudo) correctly for service management.
4. **Audit Log Access**: Provide a high-level overview of system audits within the management tool.

**Success Criteria:**
- ✅ Administrator can manage the full user lifecycle from the `ServiceManager` app.
- ✅ Broker service can be installed and started as a background worker on all three platforms.
- ✅ Administrator can view audit logs from the `ServiceManager` app.

---

### Phase 6: Database Provider Matrix ✅
**Goal:** Validate and optimize the multi-provider backend for production environments.

**Deliverables:**
1. ✅ **Provider-Aware Schema**: `AppDbContext.OnModelCreating` generates provider-correct SQL for filtered queries (`NOT IsDeleted`, `"IsDeleted" = FALSE`, `[IsDeleted] = 0`)
2. ✅ **Provider-Aware Migrations**: `MigrationRunner.MigrateSchemaAsync` uses `Col()` local function for correct ALTER TABLE quoting per provider
3. ✅ **Docker Integration Tests**: `DockerAvailability` + `DockerFactAttribute` for conditional Testcontainers — 2 tests auto-run/skip based on Docker presence
4. ✅ **Configuration**: Default `DbProvider: "sqlite"` and `DbConnection` in `appsettings.json`
5. ✅ **Provider Config Tests**: `DbProviderConfig_Configure_builds_options` verifies correct EF Core provider extension registration

**Success Criteria:**
- ✅ Successful database migration verified via SHA256 checksum comparison.
- ✅ Integration tests pass against all three providers in CI.
- ✅ Provider selection is configuration-only.

---

### Phase 7: Client RBAC & Hardening ✅
**Goal:** Update the `CatalogBrowser` to respect the security model and permissions managed by the server.

**Deliverables:**
1. ✅ **Permission-Aware UI**: Disable or hide buttons/views (e.g., Ingest, Delete, Admin-only tools) based on the user's JWT claims.
2. ✅ **Connection Management**: Final polish on the mode toggle (Standalone vs. Multi-User) and connection settings.
3. ✅ **Session Stability**: Handle token expiration, forced logouts, and server-side user deactivation gracefully in the UI.

**Success Criteria:**
- ✅ A user with "Viewer" permissions cannot initiate a folder scan.
- ✅ The client UI updates dynamically when a user's role is changed on the server.

---

### Phase 8: v1.0 Polish & Ship ✅
**Goal:** Final stabilization, UI polish, documentation, and distribution packaging.

**Deliverables:**
1. ✅ **UI Polish & Interaction** (T8.15–T8.26): Confirmation dialog + toast, gallery context menu, delete/trash wiring, bulk actions, keyboard shortcuts (Delete, Ctrl+A/C/E, Ctrl+Shift+T, D0–D5, P/X, F2, Enter, Ctrl+F), tile affordance bindings, AI Tag discoverability, shared color palette (50 semantic brushes), type scale (4 TextBlock classes), copywriting cleanup.
2. ✅ **Documentation** (T8.7–T8.9): Admin Guide, User Guide, v1 release notes, all 34 v1 requirements validated.
3. ✅ **Distribution Packaging** (T8.10–T8.14): Cross-platform publish script, WiX MSI (CatalogBrowser + BrokerService), macOS DMG (x64 + arm64), Linux DEB (CatalogBrowser + BrokerService), GitHub Actions release pipeline.
4. ✅ **Release Stabilization** (T8.27): Zero Critical/High bugs, 502 tests passing, all golden paths triaged.

**Success Criteria:**
- ✅ All 34 v1 requirements validated and documented.
- ✅ Zero Critical/High bugs remaining (T8.27 triage).
- ✅ Platform packages build on CI (MSI, DMG, DEB).

## Milestones

### v1.1 — Server Maturity (Phases 5-6) ✅
Production-ready server administration and database flexibility.

### v1.2 — Client Polish (Phases 7-8) ✅
Permission-aware UI and v1.0 polish & ship — all complete.

### v2.0 — Advanced Features & Performance (Phases 10-12) ✅
Sidebar tree CRUD, FTS5 full-text search, and performance optimization for 100K+ asset collections.

**Status:** 🏁 Archived to `.planning/milestones/v2.0-release.md`

### Phase 9: AI Image Tagging ✅
**Goal:** Integrate the in-repo `LiquidVision.Core` (LFM2-VL ONNX vision model) into ADAM so users can auto-generate descriptions, keywords, and categories for image assets locally, with no Python or cloud dependency.

**Depends on:** Phase 1 (Standalone catalog + ingestion pipeline)

**Deliverables:**
1. ✅ **Build wiring**: `LiquidVision.Core` referenced from `Adam.Shared`, ONNX Runtime + SkiaSharp distribution dependency accepted
2. ✅ **`AiTaggingService`** (`Adam.Shared/Services`): Wraps `ILiquidVisionAnalyzer` — lazy `InitializeAsync` (model download on first use), image-only guard (`AssetType.Image`), keyword/category/description merge, auto-apply/union/no-provenance
3. ✅ **DI registration**: `AddLiquidVision(...)` in `CatalogBrowser/App.axaml.cs` with `Precision = Q4F16`, `ExecutionProvider = Cpu`
4. ✅ **Trigger A — opt-in during ingest**: `EnableAiTagging` checkbox, sequential post-pass after parallel ingest
5. ✅ **Trigger B — per-asset Auto-tag**: `AutoTagCommand` in `MetadataEditorViewModel`
6. ✅ **Trigger C — bulk re-tag selection**: `AiTagSelectedCommand` in gallery toolbar, filtered to images
7. ✅ **Model download progress**: `IsModelDownloading`/`ModelDownloadPercentage` + `IsAiTaggingActive`/`AiTaggingPercentage` on `StatusBarViewModel`
8. ✅ **Tests**: 7 unit tests covering image-only guard, merge, description fill, cancellation, batch progress, analyze-only path

**Success Criteria:**
- ✅ A user can auto-tag an image (during ingest, per-asset, or in bulk) and the generated keywords/categories/description persist to the catalog.
- ✅ First-use model download shows progress in the status bar and does not block parallel ingestion.
- ✅ Non-image assets are skipped; AI tagging is fully opt-in.

---
### Phase 10: Sidebar CRUD & Tree Interaction ✅
**Goal:** Complete sidebar tree interaction model — **XAML ContextFlyout** context menus, **cascade delete** (parent + children), inline rename (F2/double-click), broker-side CRUD handlers with ChangeNotification broadcast, visual filter state indicators.

**Deliverables:**
1. ✅ **XAML ContextFlyout context menus** — dynamic code-behind population for all node types (Keywords, Categories, Collections, Folders) with Create/Rename/Delete/Filter entries
2. ✅ **Inline rename** — F2 or double-click, Enter commits, Escape cancels
3. ✅ **Cascade delete** — recursive descendant deletion with confirmation dialog showing child count
4. ✅ **Filter commands** — "Filter by this" / "Clear filter" on every node type
5. ✅ **Visual filter state** — `IsActiveFilter` property with bold blue indicator on active filter nodes
6. ✅ **Broker handlers** — cascade delete + ChangeNotification broadcast added to all CRUD handlers
7. ✅ **Keyboard shortcuts** — F2 rename, P/X flags, Enter reveal, Ctrl+F search focus
8. ✅ **41 SidebarCrud tests** — all passing

**Key decisions:**
- XAML ContextFlyout with code-behind population (polymorphic node types can't use compiled bindings)
- Cascade delete always (no re-parent option)
- Permission gating via existing Phase 7 CanEditMetadata/CanCreateMetadata

### Phase 11: Full-Text Search (FTS5) ✅
**Goal:** Complete FTS integration — wire IFtsService implementations into SearchService, add search bar with autocomplete, search highlighting, multi-provider validation.

**Deliverables:**
1. ✅ **IFtsService interface** — `SearchAsync`, `GetSuggestionsAsync`, `IsAvailableAsync`, `EnsureReadyAsync`, `RebuildIndexAsync`
2. ✅ **SqliteFtsService** — FTS5 virtual table with rowid mapping, bm25() ranking, sync triggers, prefix matching, phrase queries
3. ✅ **PostgresFtsService** — tsvector/tsquery with GIN index
4. ✅ **SqlServerFtsService** — full-text catalog with CONTAINS
5. ✅ **SearchService dual-path** — IFtsService when available, LIKE fallback
6. ✅ **Search bar UI** — dedicated TextBox above gallery, 300ms debounce, Ctrl+F shortcut
7. ✅ **Search suggestions** — Popup dropdown with autocomplete
8. ✅ **Result highlighting** — bold match in title, matched-fields badge in tile
9. ✅ **29 FTS tests** — all passing

### Phase 12: Performance Optimization ✅
**Goal:** Optimize thumbnails, virtualization, and startup for 100K+ asset collections.

**Deliverables:**
1. ✅ **Decode-to-size** — `DecoderOptions.TargetSize` in ImageThumbnailExtractor constrains decode to maxSize, avoids full-decode of large sources
2. ✅ **Thumbnail cache timestamps** — LastWriteTimeUtc comparison skips regeneration for unchanged sources
3. ✅ **Bitmap lifecycle** — AssetListItem implements IDisposable with CancelPendingLoad(), old bitmap disposal in Thumbnail setter, cache removal before dispose
4. ✅ **Collection cleanup** — AssetGalleryViewModel disposes items before Assets.Clear() in search and load paths
5. ✅ **Startup profiling** — Stopwatch telemetry with [PERF] log messages for DB init, sidebar load, gallery load, total startup
6. ⏭️ **Skipped:** BufferFactor (not in Avalonia 12 API), SkiaOptions (API uncertain), compiled bindings (polymorphic templates), lazy AI tagging (already lazy by default)

**Key decisions:**
- Decode-to-size via ImageSharp DecoderOptions.TargetSize (not SKBitmap.DecodeToWidth)
- `_previousActiveFilterNode` tracking for IsActiveFilter (tree-walk misses standalone nodes)
- Startup profiling via ConnectionDebugLogger (not external telemetry)

---
## Milestones

### v2.x — Production Hardening & Feature Growth (Phases 13-14)

**Phase 13 — Production Hardening** 📋
**Goal:** Fix accumulated technical debt before building new features. Solution file, CI fixes, null-handler validation, protobuf contract documentation, logging standardization, FolderWatcher batching, EF Core 10 stable migration prep.

**Depends on:** Phases 1-12

**Waves:**
1. **Project Infrastructure** — solution file, CI fixes for CatalogBrowser tests
2. **Code Quality** — null-payload guards on all broker handlers, protobuf contract documentation
3. **Infrastructure & Observability** — logging standardization, FolderWatcher debounce/batch
4. **EF Core Stabilization** — migration prep for EF Core 10 RTM

**Phase 14 — Feature Growth** 📋
**Goal:** Deliver the v2 user-facing features. Batch metadata editing, CSV/XMP import/export, activity feed, full-resolution loupe/compare views, AI tag refinement with confidence scores.

**Depends on:** Phase 13

**Waves:**
1. **Batch Operations** — batch metadata editing, CSV/XMP import/export
2. **Collaboration** — activity feed / notification panel
3. **Views & AI** — compare/loupe view completion, AI tag refinement

### v3.0 — Provenance & Trust (Phases 15-16) 🏁

**Status:** 🏁 Archived to `.planning/milestones/v3.0-release.md`
**Goal:** EF Core 10 stable, CSV streaming, feed pruning, re-scan wiring, test coverage expansion, AiGenerated provenance flag, wire protocol, UI badge

### v3.1 — Collaboration (Phase 17)

**Status:** 🏁 Archived to `.planning/milestones/v3.1-release.md`
**Goal:** Fulfill COLL-V2-02 with threaded comment threads on assets.

### v3.2 — Integration (Phase 18)

**Status:** 🏁 Archived to `.planning/milestones/v3.2-release.md`
**Goal:** Fulfill INTG-V2-01 with plugin system for third-party metadata extractors.

---
*Roadmap updated: 2026-06-17 — All 18 phases complete. All milestones (v1.0–v3.2) archived.*

