---
description: "Task list for Digital Asset Management System (adam) — Phase B onward"
---

# Tasks: Digital Asset Management System (adam)

**Input**: Design documents from `specs/001-digital-asset-management/`
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/api.md

**Tests**: xUnit + FluentAssertions per plan.md. TDD approach — tests precede implementation.

**Organization**: Tasks grouped by user story for independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: User story label (e.g., US2, US3)
- Include exact file paths

## Path Conventions

- **Solution root**: `src/`
- **Projects**: `src/Adam.Shared/`, `src/Adam.BrokerService/`, `src/Adam.CatalogBrowser/`
- **Test projects**: `tests/Adam.Shared.Tests/`, `tests/Adam.BrokerService.Tests/`, `tests/Adam.CatalogBrowser.Tests/`

---

## Phase 1: Setup — COMPLETE (Phase A / User Story 1)

**Purpose**: Solution scaffolding, shared domain models, EF Core SQLite, IFileService, Avalonia shell

**Status**: ✅ Completed in Phase A — Standalone Local Catalog (US1)

**Independent Test**: Launch catalog browser, select root folder with 50 mixed-type files containing EXIF/IPTC/XMP metadata, confirm scan extracts all metadata correctly, verify full browse/search/metadata-edit capabilities without any service process running.

- [x] T001 Create .NET 10 solution with Adam.Shared, Adam.BrokerService, Adam.CatalogBrowser, and test projects in src/
- [x] T002 Define Asset and Collection domain models in src/Adam.Shared/Models/
- [x] T003 Implement EF Core AppDbContext with SQLite provider in src/Adam.Shared/Data/
- [x] T004 [P] Build IFileService abstraction and file indexing engine in src/Adam.Shared/Data/
- [x] T005 Create Avalonia catalog browser shell with DI container and navigation frame in src/Adam.CatalogBrowser/
- [x] T006 [P] Build main asset gallery view (grid/list layout, collection tree navigation, sorting) in src/Adam.CatalogBrowser/Views/
- [x] T007 Implement full-text search across asset title, description, tags in src/Adam.CatalogBrowser/ViewModels/
- [x] T008 Add asset detail pane with thumbnail preview and metadata display in src/Adam.CatalogBrowser/Views/

---

## Phase 2: Multi-User Foundation — Browse & Search (Priority: P1) 🎯 CURRENT

**User Story**: US2 — Browse and Search Digital Assets (Multi-User)
**Goal**: Catalog browser working in multi-user mode — authenticates and browses/searches through broker service over TCP.

**Independent Test**: Launch catalog browser in multi-user mode, connect to running broker service with pre-loaded assets, verify assets appear in search results with correct metadata.

- [ ] T009 [P] [US2] Define Protobuf Envelope message (auth_token, correlation_id, message_type, payload, status_code, error_message) in src/Adam.Shared/Contracts/envelope.proto
- [ ] T010 [P] [US2] Define Protobuf Auth messages (LoginRequest, LoginResponse, UserProfile, ValidateTokenRequest, ValidateTokenResponse) in src/Adam.Shared/Contracts/auth.proto
- [ ] T011 [P] [US2] Define Protobuf Asset messages (ListAssetsRequest/Response, GetAssetRequest, AssetDetail, CreateAssetRequest/Response, UpdateAssetRequest/Response, DeleteAssetRequest/Response, GetChangesRequest/Response, ChangeEvent) in src/Adam.Shared/Contracts/asset.proto
- [ ] T012 [P] [US2] Define Protobuf Collection messages (ListCollectionsRequest/Response, CollectionNode, CreateCollectionRequest, UpdateCollectionRequest, DeleteCollectionRequest/Response) in src/Adam.Shared/Contracts/collection.proto
- [ ] T013 [US2] Implement length-prefixed TCP framing layer (4-byte big-endian int32 + protobuf Envelope) in src/Adam.Shared/Transport/TcpFrame.cs
- [ ] T014 [US2] Scaffold broker service with TCP listener, connection manager, and structured logging in src/Adam.BrokerService/
- [ ] T015 [US2] Implement auth handler with PBKDF2 password hashing, JWT issuance, and per-request token validation in src/Adam.BrokerService/Handlers/AuthHandler.cs
- [ ] T016 [P] [US2] Implement asset handlers (ListAssets with search/filter/paginate/sort, GetAsset with full detail) in src/Adam.BrokerService/Handlers/AssetHandler.cs
- [ ] T017 [P] [US2] Implement collection handlers (ListCollections with tree structure) in src/Adam.BrokerService/Handlers/CollectionHandler.cs
- [ ] T018 [US2] Implement client-side TCP transport with connection management and auto-reconnect in src/Adam.CatalogBrowser/Services/BrokerClient.cs
- [ ] T019 [US2] Implement client-side auth session with JWT storage and automatic token attachment in src/Adam.CatalogBrowser/Services/AuthSession.cs
- [ ] T020 [US2] Create ModeManager abstraction (IAssetService/ICollectionService routing to local DB vs broker) in src/Adam.CatalogBrowser/Services/ModeManager.cs
- [ ] T021 [US2] Update existing gallery view to operate against broker service when in multi-user mode in src/Adam.CatalogBrowser/ViewModels/AssetGalleryViewModel.cs

**Checkpoint**: Multi-user browsing and search functional — client authenticates to broker, lists/searches/views assets over TCP.

---

## Phase 3: Multi-User Concurrency (Priority: P1)

**User Story**: US3 — Multi-User Concurrent Access via Broker Service
**Goal**: Verified multi-user support with real-time change propagation across connected clients.

**Independent Test**: Two separate catalog browser instances connect to same broker service, make concurrent metadata changes, verify both see consistent state.

- [ ] T022 [US3] Implement polling-based change notification (GetChanges handler returning ChangeEvent list since timestamp) in src/Adam.BrokerService/Handlers/ChangeHandler.cs
- [ ] T023 [US3] Add EF Core SaveChangesAsync override to auto-increment Version and set ModifiedAt on entity updates in src/Adam.Shared/Data/AppDbContext.cs
- [ ] T024 [US3] Implement last-write-wins conflict resolution (compare expected_version, apply update, return conflict flag) in src/Adam.BrokerService/Handlers/AssetHandler.cs
- [ ] T025 [US3] Add connection pooling with max concurrent connections (50) and per-connection request limit (5) in src/Adam.BrokerService/Transport/ConnectionManager.cs
- [ ] T026 [US3] Implement client-side change poller (5s interval, graceful stop on disconnect) in src/Adam.CatalogBrowser/Services/ChangePoller.cs
- [ ] T027 [US3] Write integration test: 10 concurrent clients browsing and searching simultaneously in tests/Adam.BrokerService.Tests/Integration/ConcurrentClientsTests.cs

**Checkpoint**: Multi-user concurrency verified — changes propagate, conflicts resolve, 10 concurrent clients supported.

---

## Phase 4: Asset Ingestion & Metadata Management (Priority: P2)

**User Story**: US4 — Asset Ingestion and Metadata Management
**Goal**: Complete asset lifecycle — ingest with metadata extraction, browse, edit metadata, delete in both modes.

**Independent Test**: Ingest JPEG with known EXIF/IPTC/XMP values, edit fields in client, save, then inspect source file with ExifTool to confirm edits are embedded.

- [ ] T028 [P] [US4] Implement SHA256 checksum-based duplicate detection in src/Adam.Shared/Services/DuplicateDetector.cs
- [ ] T029 [P] [US4] Add file storage abstraction with local filesystem provider in src/Adam.Shared/Services/Storage/LocalFileSystemProvider.cs
- [ ] T030 [US4] Implement metadata extraction from EXIF/IPTC/XMP using MetadataExtractor library in src/Adam.Shared/Services/MetadataExtractionService.cs
- [ ] T031 [US4] Implement metadata write-back to source files (XMP embedding for JPEG, TIFF, PNG, WebP) in src/Adam.Shared/Services/MetadataWritebackService.cs
- [ ] T032 [US4] Add XMP sidecar support for RAW files (CR2, NEF, ARW, DNG) in src/Adam.Shared/Services/MetadataWritebackService.cs
- [ ] T033 [P] [US4] Add thumbnail generation (256px long edge, JPEG quality 85, cached to disk) in src/Adam.Shared/Services/ThumbnailService.cs
- [ ] T034 [P] [US4] Build asset upload/ingestion view with drag-and-drop and progress indicator in src/Adam.CatalogBrowser/Views/IngestionView.axaml
- [ ] T035 [US4] Build metadata editor view (title, description, tags with autocomplete, EXIF read-only, IPTC/XMP editable, ratings/labels/flags) in src/Adam.CatalogBrowser/Views/MetadataEditorView.axaml
- [ ] T036 [US4] Implement asset soft-delete with confirmation dialog in src/Adam.CatalogBrowser/Services/DeleteService.cs
- [ ] T037 [US4] Implement folder watcher for auto-indexing new/changed files in multi-user mode in src/Adam.BrokerService/Services/FolderWatcherService.cs
- [ ] T038 [US4] Add validation rules (file type whitelist, 2GB size limit, title max 200 chars, max 20 tags) in src/Adam.Shared/Validation/AssetValidator.cs

**Checkpoint**: Full asset lifecycle works in both modes — ingest with metadata extraction, browse, edit, delete, auto-indexing.

---

## Phase 5: Admin Panel & Mode Management (Priority: P2)

**User Story**: US5 — Admin Panel & Mode Management
**Goal**: Admin panel with mode management, native service deployment, and DB migration.

**Independent Test**: Start in standalone mode, use admin panel to register broker service as native background worker, switch to multi-user mode, verify catalog connects through service.

- [ ] T039 [P] [US5] Build admin panel shell with mode toggle (Standalone / Multi-User radio buttons) in src/Adam.CatalogBrowser/Views/AdminPanelView.axaml
- [ ] T040 [P] [US5] Implement Windows Service registration (SC create/delete via ServiceController) in src/Adam.BrokerService/Hosting/WindowsServiceInstaller.cs
- [ ] T041 [P] [US5] Implement macOS launchd plist generation and load/unload in src/Adam.BrokerService/Hosting/MacOsServiceInstaller.cs
- [ ] T042 [P] [US5] Implement Linux systemd unit generation and enable/disable in src/Adam.BrokerService/Hosting/LinuxServiceInstaller.cs
- [ ] T043 [US5] Build database migration wizard UI (select source SQLite, target provider + connection string, confirm, execute) in src/Adam.CatalogBrowser/Views/MigrationWizardView.axaml
- [ ] T044 [US5] Implement migration engine (read all data from SQLite, write to target provider via EF Core) in src/Adam.BrokerService/Services/DbMigrationService.cs
- [ ] T045 [US5] Add service status monitor (connected clients, uptime, health check handler) in src/Adam.CatalogBrowser/Views/AdminPanelView.axaml

**Checkpoint**: Admin panel operational — mode switching, native service deployment on all platforms, DB migration functional.

---

## Phase 6: Database Provider Configuration (Priority: P2)

**User Story**: US6 — Database Provider Configuration
**Goal**: All 3 providers (SQLite, PostgreSQL, SQL Server) functional in multi-user mode, swappable via config.

**Independent Test**: Deploy broker service with each supported DB provider, populate test data, verify catalog browser functions identically across all providers.

- [ ] T046 [P] [US6] Implement PostgreSQL EF Core provider via Npgsql with code-first migrations in src/Adam.BrokerService/Data/
- [ ] T047 [P] [US6] Implement SQL Server EF Core provider with code-first migrations in src/Adam.BrokerService/Data/
- [ ] T048 [US6] Create DbProviderConfig for provider selection from configuration in src/Adam.BrokerService/Configuration/DbProviderConfig.cs
- [ ] T049 [US6] Implement auto-migration runner on broker startup (idempotent, per-provider) in src/Adam.BrokerService/Data/MigrationRunner.cs
- [ ] T050 [US6] Write integration test matrix (SQLite in-memory + PostgreSQL Testcontainers + SQL Server Testcontainers) in tests/Adam.BrokerService.Tests/Integration/DbProviderMatrixTests.cs

**Checkpoint**: All DB providers functional — config-driven provider selection with auto-migrations.

---

## Phase 7: User & Role Administration (Priority: P3)

**User Story**: US7 — User & Role Administration
**Goal**: Full user lifecycle management with RBAC enforcement in multi-user mode.

**Independent Test**: Create three users with Viewer, Editor, Administrator roles; verify each can only perform permitted actions.

- [ ] T051 [P] [US7] Define User TCP handlers (CreateUser, UpdateUser, DeleteUser, ListUsers with PBKDF2 hashing) in src/Adam.BrokerService/Handlers/UserHandler.cs
- [ ] T052 [P] [US7] Define Role model with seeded roles (Viewer/Editor/Administrator) and permission sets in src/Adam.BrokerService/Data/RoleSeeder.cs
- [ ] T053 [US7] Implement authorization middleware enforcing RBAC on all TCP handlers in src/Adam.BrokerService/Handlers/AuthorizationMiddleware.cs
- [ ] T054 [US7] Build admin user management UI (list, create/edit form, role picker, soft-delete) in src/Adam.CatalogBrowser/Views/UserManagementView.axaml
- [ ] T055 [US7] Build audit log viewer (filterable by user, action, entity type, date range; CSV export) in src/Adam.CatalogBrowser/Views/AuditLogView.axaml
- [ ] T056 [US7] Implement audit logging for all create/update/delete operations in src/Adam.BrokerService/Data/AuditLogger.cs

**Checkpoint**: User lifecycle and RBAC fully operational in multi-user mode.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Lightroom-class features, remaining spec items, performance, documentation

- [ ] T057 [P] Add loupe view for full-resolution image inspection in src/Adam.CatalogBrowser/Views/LoupeView.axaml
- [ ] T058 [P] Add compare view for side-by-side asset evaluation in src/Adam.CatalogBrowser/Views/CompareView.axaml
- [ ] T059 [P] Add hierarchical keyword tagging with unlimited nesting in src/Adam.CatalogBrowser/Views/KeywordEditorView.axaml
- [ ] T060 [P] Add star ratings (0-5), color labels, and flagging (pick/reject) in src/Adam.CatalogBrowser/Controls/RatingControl.axaml
- [ ] T061 [P] Add curated collections independent of folder structure in src/Adam.CatalogBrowser/ViewModels/CollectionViewModel.cs
- [ ] T062 [P] Add basic image adjustments (rotate 90/180/270, flip horizontal/vertical) in src/Adam.CatalogBrowser/Services/ImageAdjustmentService.cs
- [ ] T063 [P] Add export to JPEG/TIFF with configurable quality, resolution, and color space in src/Adam.CatalogBrowser/Services/ExportService.cs
- [ ] T064 [P] Write remaining unit tests for all services in tests/
- [ ] T065 [P] Write integration tests for multi-user broker handlers in tests/Adam.BrokerService.Tests/Integration/
- [ ] T066 [P] Performance optimization: search <2s at 100K assets, gallery renders 100 thumbnails <1s
- [ ] T067 [P] Security hardening across all broker service handlers
- [ ] T068 [P] Code cleanup and refactoring across all projects
- [ ] T069 [P] Documentation updates in specs/001-digital-asset-management/
- [ ] T070 [P] Run quickstart.md validation — confirm all run commands work end-to-end

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup/US1)**: ✅ Already complete. Foundation for all subsequent phases.
- **Phase 2 (US2 Multi-User)**: Depends on Phase 1. **Current phase to execute.**
- **Phase 3 (US3 Concurrency)**: Depends on Phase 2 (needs broker service running).
- **Phase 4 (US4 Ingest & Metadata)**: Depends on Phase 1. Can begin after Phase 2 or in parallel with Phase 3.
- **Phase 5 (US5 Admin Panel)**: Depends on Phase 2 (needs multi-user mode working).
- **Phase 6 (US6 DB Providers)**: Depends on Phase 2 (needs broker service). Independent from Phase 3/4/5.
- **Phase 7 (US7 User Admin)**: Depends on Phase 2 and Phase 6 (needs broker + DB abstraction).
- **Phase 8 (Polish)**: Depends on all other phases being complete.

### User Story Dependencies

- **US2 (P1)**: Must complete before US3, US5. Foundation for all multi-user features.
- **US3 (P1)**: Depends on US2 broker service. Independent from US4, US5, US6.
- **US4 (P2)**: Depends on Phase 1. Standalone metadata features independent of US2.
- **US5 (P2)**: Depends on US2 broker service.
- **US6 (P2)**: Depends on US2 broker service. Independent from US3, US4, US5.
- **US7 (P3)**: Depends on US2 and US6 complete.

### Within Each User Story

- Contracts/protocol definitions before handler implementation
- Handler implementation before client-side integration
- Core logic before UI binding
- Story complete and independently testable before moving to next

---

## Parallel Execution Examples

### Phase 2 — US2 (Multi-User Foundation)

```bash
# Contract definitions in parallel (different .proto files):
Task T009: "Define Protobuf Envelope message"
Task T010: "Define Protobuf Auth messages"
Task T011: "Define Protobuf Asset messages"
Task T012: "Define Protobuf Collection messages"

# Broker service handlers in parallel (different handler files):
Task T015: "Implement auth handler"
Task T016: "Implement asset handlers"
Task T017: "Implement collection handlers"

# Client-side services in parallel (different service files):
Task T018: "Implement client TCP transport"
Task T019: "Implement auth session"
```

### Phase 3 — US3 (Multi-User Concurrency)

```bash
# Change notification and tracking (different concerns):
Task T022: "Polling change notification"
Task T023: "Change tracking in DbContext"

# After above complete — in parallel:
Task T024: "Conflict resolution"
Task T025: "Connection pooling"
```

### Phase 4 — US4 (Asset Ingestion & Metadata)

```bash
# Core services (parallel — different service files):
Task T028: "Checksum duplicate detection"
Task T029: "File storage abstraction"
Task T030: "Metadata extraction"
Task T033: "Thumbnail generation"

# UI views (parallel — different view files):
Task T034: "Upload view"
Task T035: "Metadata editor view"
```

### Phase 5 — US5 (Admin Panel)

```bash
# Platform-specific service installers (parallel):
Task T040: "Windows Service registration"
Task T041: "macOS launchd generation"
Task T042: "Linux systemd generation"

# After installers — UI in parallel:
Task T039: "Admin panel shell"
Task T043: "Migration wizard UI"
```

### Phase 6 — US6 (DB Providers)

```bash
# Provider implementations (parallel — different providers):
Task T046: "PostgreSQL provider"
Task T047: "SQL Server provider"
```

---

## Implementation Strategy

### Recommended Execution Order

1. **Complete Phase 2 (US2)** — Multi-user foundation. Highest priority P1 item, blocks most other phases.
2. **After Phase 2** — parallel tracks possible:
   - Track A: Phase 3 (US3 Concurrency) — service-side concurrent access
   - Track B: Phase 4 (US4 Metadata) — standalone metadata features
   - Track C: Phase 6 (US6 DB Providers) — database extensibility
3. **Phase 5 (US5 Admin Panel)** — requires US2 multi-user working
4. **Phase 7 (US7 User Admin)** — requires US2 + US6 (DB abstraction)
5. **Phase 8 (Polish)** — remaining Lightroom features, performance, security

### MVP Scope

- **MVP 1** (Phase 2): Multi-user broker service with browse/search/auth — independently demoable
- **MVP 2** (Phase 2+3): Multi-user with change propagation and concurrency
- **MVP 3** (Phase 2+3+4): Full metadata extraction round-trip with asset management
- **Full** (All phases): Complete DAM system with admin panel, RBAC, all DB providers

### Incremental Delivery

1. Complete Phase 2 → Test independently (multi-user browse/search MVP)
2. Complete Phase 3 → Test independently (concurrency working)
3. Complete Phase 4 → Test independently (metadata round-trip)
4. Complete Phase 5 → Test independently (admin panel)
5. Complete Phase 6 → Test independently (DB providers)
6. Complete Phase 7 → Test independently (RBAC)
7. Complete Phase 8 → Final validation
8. Each phase adds value without breaking previous phases
