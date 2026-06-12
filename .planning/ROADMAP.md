# Roadmap: adam

**Project:** adam — Digital Asset Management System  
**Updated:** 2026-06-12 (Phase 8 complete — v1.0 ready)
**Granularity:** Standard  
**Phases:** 9

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
*Roadmap updated: 2026-06-12 — Phase 8 complete, v1.0 ready*
