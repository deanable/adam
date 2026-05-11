# Implementation Plan: Digital Asset Management System (adam)

**Branch**: `001-digital-asset-management` | **Date**: 2026-05-11 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/001-digital-asset-management/spec.md`

## Summary

Build a cross-platform digital asset management system with two operational modes: standalone (self-contained desktop app with direct SQLite access, no external service) and multi-user (catalog browser connecting over TCP to a broker service backed by PostgreSQL/SQL Server). Delivered incrementally: standalone mode first, then multi-user core, asset management, admin panel, and provider support.

## Technical Context

**Language/Version**: C# 13 / .NET 10  
**Primary Dependencies**: Avalonia 11 (UI), Google.Protobuf (serialization), Entity Framework Core 10 (data access), Microsoft.Extensions.Hosting (service lifecycle)  
**Storage**: SQLite (standalone/local); PostgreSQL (multi-user production); SQL Server (multi-user enterprise)  
**Testing**: xUnit + FluentAssertions, Avalonia.Headless (UI tests), Testcontainers (DB integration)  
**Target Platform**: Windows 10+, macOS 13+, Linux (Ubuntu 22.04+/Fedora)  
**Project Type**: Desktop application (catalog browser) + backend service (broker)  
**Performance Goals**: Search <2s at 100K assets; 10 concurrent users <3s response; metadata propagation <5s  
**Constraints**: Cross-platform UI identical behavior; DB provider swap via config only; TCP between browser and service (multi-user mode); standalone mode requires zero external dependencies; zero ASP.NET dependency  
**Scale/Scope**: Single-machine standalone up to 100K assets; multi-user up to 50 concurrent users

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment | Notes |
|-----------|------------|-------|
| I. Clarity-First Specifications | PASS | Spec has 7 prioritized stories with Gherkin acceptance scenarios and edge cases |
| II. Incremental Delivery | PASS | Standalone mode is independently deliverable P1; multi-user builds on same codebase |
| III. Test Verification (NON-NEGOTIABLE) | PASS | xUnit + FluentAssertions selected; tests precede implementation per Red-Green-Refactor |
| IV. Self-Documenting Development | PASS | Plan produces research.md, data-model.md, quickstart.md, contracts/ |
| V. Simplicity & YAGNI | PASS | Dual-mode architecture justified by explicit user spec; standalone avoids unnecessary service dependency |

**Gate Decision**: PASS — proceed to Phase 0.

## Development Phases

### Phase A — Standalone Mode (P1)

*Implements User Story 1: Standalone Local Catalog*

| Task | Effort | Description |
|------|--------|-------------|
| A1 | M | Create .NET solution with projects: Adam.Shared, Adam.BrokerService, Adam.CatalogBrowser, test projects |
| A2 | S | Define shared domain models (Asset, Collection) in Adam.Shared |
| A3 | M | Implement EF Core AppDbContext with SQLite provider |
| A4 | M | Build IFileService and file indexing engine for standalone mode |
| A5 | S | Create Avalonia catalog browser: window shell, DI container, navigation frame |
| A6 | L | Build main asset gallery view: grid/list layout, collection tree navigation, sorting |
| A7 | L | Implement full-text search across asset title, description, tags |
| A8 | S | Add asset detail pane: thumbnail preview, metadata display |

**Deliverables**: Fully functional standalone catalog browser that opens a local SQLite DB and provides browse/search/view.

### Phase B — Multi-User Foundation (P1)

*Implements User Story 2: Browse and Search (Multi-User)*

| Task | Effort | Description |
|------|--------|-------------|
| B1 | M | Define TCP+Protobuf message contracts: request/response types for Auth, Asset, Collection services |
| B2 | M | Implement length-prefixed framing layer: `[4-byte payload length][protobuf bytes]` over TCP |
| B3 | M | Scaffold broker service: TCP listener, connection manager, logging, configuration |
| B4 | M | Implement auth handler: login, JWT token issuance, token validation per request |
| B5 | M | Implement asset handlers: List, Search, Get — backed by shared database |
| B6 | M | Implement client-side transport in catalog browser: TCP connector, auth session |
| B7 | S | Update browser's existing gallery view to work against broker service instead of local DB |

**Deliverables**: Catalog browser working in multi-user mode — authenticates and browses/searches through broker service over TCP.

### Phase C — Multi-User Concurrency (P1)

*Completes User Story 3: Multi-User Concurrent Access*

| Task | Effort | Description |
|------|--------|-------------|
| C1 | M | Implement polling-based change notification over TCP (client sends since-timestamp, server returns changes) |
| C2 | M | Add change tracking: asset modification timestamps, version increments |
| C3 | M | Implement last-write-wins conflict resolution on asset updates |
| C4 | S | Connection pooling and concurrent request handling in broker service |
| C5 | S | Integration test: 10 concurrent clients browsing and searching simultaneously |

**Deliverables**: Verified multi-user support with real-time change propagation across connected clients.

### Phase D — Asset Ingestion & Management (P2)

*Implements User Story 5: Asset Ingestion and Metadata Management*

| Task | Effort | Description |
|------|--------|-------------|
| D1 | L | Build asset upload with drag-and-drop, progress indicator, chunked large-file support |
| D2 | M | Implement SHA256 checksum-based duplicate detection on upload |
| D3 | M | Add file storage abstraction: local filesystem provider; storage path configuration |
| D4 | M | Build metadata editor view (title, description, tags, collection assignment) |
| D5 | S | Implement asset delete with confirmation dialog and soft-delete |
| D6 | S | Add thumbnail generation for images on upload |
| D7 | M | Validation: file type whitelist, file size limits, required metadata fields |

**Deliverables**: Complete asset lifecycle — upload, browse, edit, delete in both modes.

### Phase E — Admin Panel (P2) ✅

*Implements User Story 6: Admin Panel & Mode Management*

| Task | Effort | Description |
|------|--------|-------------|
| E1 | M | Build mode toggle UI: switch between standalone and multi-user configuration |
| E2 | M | Implement native service deployment: Windows Service registration API |
| E3 | M | Implement native service deployment: macOS launchd plist generation |
| E4 | M | Implement native service deployment: Linux systemd unit generation |
| E5 | L | Build database migration wizard: export standalone SQLite → import to PostgreSQL/SQL Server |
| E6 | S | Add service status monitor: connected clients, uptime, health check |

**Deliverables**: Admin panel with mode management, service deployment, and DB migration.

### Phase F — Additional Database Providers (P2) ✅

*Implements User Story 4: Database Provider Configuration*

| Task | Effort | Description |
|------|--------|-------------|
| F1 | M | Implement PostgreSQL EF Core provider via Npgsql; configuration for provider selection |
| F2 | M | Implement SQL Server EF Core provider; configuration |
| F3 | S | Create migration strategy: code-first migrations per provider, auto-applied on startup |
| F4 | S | Integration test suite using Testcontainers: run full matrix against all 3 providers |

**Deliverables**: All 3 providers functional in multi-user mode, swappable via config change.

### Phase G — User & Role Administration (P3) ✅

*Implements User Story 7: User and Role Management*

| Task | Effort | Description |
|------|--------|-------------|
| G1 | M | Build user management TCP handlers: CRUD, password hashing, role assignment |
| G2 | M | Enforce RBAC in broker service: authorize TCP requests by role claim |
| G3 | M | Build admin user management UI in catalog browser: user list, create/edit, role picker |
| G4 | S | Build audit log viewer (filterable by user, action, date range) |

**Deliverables**: Full user lifecycle management with role-based access enforcement.

## Project Structure

### Documentation

```
specs/001-digital-asset-management/
├── spec.md               # Feature specification
├── spec-user-draft.md    # User's original specification draft
├── plan.md               # This file — implementation plan
├── research.md           # Phase 0 — technology decisions & rationale
├── data-model.md         # Phase 1 — entity definitions & validation rules
├── quickstart.md         # Phase 1 — setup & run guide
├── contracts/
│   └── api.md            # TCP+Protobuf message contracts and protocol specification
└── tasks.md              # Created by /speckit.tasks
```

### Source Code

```
src/
├── Adam.Shared/
│   ├── Models/             # Domain entities (Asset, Collection, User, Role, AccessLog)
│   ├── Contracts/          # Protobuf message types and service contracts
│   └── Data/               # IDbProvider, IAssetRepository, IFileService abstractions
├── Adam.BrokerService/
│   ├── Handlers/           # TCP message handlers (AuthHandler, AssetHandler, CollectionHandler)
│   ├── Transport/          # TCP listener, connection manager, framing layer
│   ├── Data/               # AppDbContext, Migrations, Repository implementations
│   ├── Hosting/            # Native service registration (SCM, launchd, systemd)
│   └── Configuration/      # DbProviderConfig, AuthConfig, StorageConfig
└── Adam.CatalogBrowser/
    ├── Views/              # Avalonia views (LoginView, AssetGalleryView, AdminPanelView)
    ├── ViewModels/         # MVVM view models
    ├── Services/           # TcpClient, AuthSession, FileUploadService, ModeManager
    └── Models/             # UI state models

tests/
├── Adam.Shared.Tests/
├── Adam.BrokerService.Tests/
│   ├── Unit/
│   └── Integration/        # Testcontainers per DB provider
└── Adam.CatalogBrowser.Tests/
    └── Unit/
```

## Complexity Tracking

No constitution violations detected. Dual-mode architecture is explicitly required by the user specification.
