# Roadmap: adam

**Project:** adam — Digital Asset Management System  
**Created:** 2026-05-23  
**Granularity:** Standard  
**Phases:** 7

## Overview

| # | Phase | Goal | Requirements | Success Criteria |
|---|-------|------|--------------|------------------|
| 1 | Standalone Mode | Deliver a fully functional self-contained catalog browser with SQLite, browse/search/view, and metadata extraction | CATA-01 to CATA-11, META-05, DB-01 | 5 |
| 2 | Multi-User Foundation | Connect catalog browser to broker service over TCP with auth, asset browsing, and folder watching | AUTH-01 to AUTH-03, AUTH-06, BROK-01, BROK-05 | 5 |
| 3 | Multi-User Concurrency | Ensure real-time change propagation, concurrent access stability, and conflict resolution | BROK-02 to BROK-04 | 3 |
| 4 | Asset Ingestion & Management | Complete asset lifecycle — upload (folder scan), metadata editing, adjustments, export, thumbnails | META-01 to META-04, EDIT-01 to EDIT-04 | 6 |
| 5 | Admin Panel & Mode Management | Mode toggle, native service deployment, database migration wizard, service monitoring | ADMIN-01 to ADMIN-04 | 4 |
| 6 | Database Provider Matrix | Full support and testing for SQLite, PostgreSQL, and SQL Server with swappable configuration | DB-02 to DB-04 | 3 |
| 7 | User & Role Administration | Full user lifecycle, RBAC enforcement, audit log viewer | AUTH-04, AUTH-05 | 3 |

## Phase Details

### Phase 1: Standalone Mode

**Goal:** A user launches the catalog browser with zero external dependencies, selects a folder of assets, and can browse, search, and view metadata.

**Requirements:** CATA-01, CATA-02, CATA-03, CATA-04, CATA-05, CATA-06, CATA-07, CATA-08, CATA-09, CATA-10, CATA-11, META-05, DB-01

**Success Criteria:**
1. Catalog browser launches and indexes a folder of 50 mixed files without any broker service running
2. Search returns results within 2 seconds for 1000 indexed assets
3. EXIF, IPTC, and XMP metadata is displayed in the metadata panel
4. Grid, loupe, and compare views render within 1 second
5. Duplicate detection via SHA256 works during initial scan

**UI Hint:** yes — Avalonia views for gallery, detail pane, collection tree, search bar

### Phase 2: Multi-User Foundation

**Goal:** The catalog browser connects to a running broker service over TCP, authenticates, and browses the shared catalog.

**Requirements:** AUTH-01, AUTH-02, AUTH-03, AUTH-06, BROK-01, BROK-05

**Success Criteria:**
1. Broker service starts and accepts TCP connections on configured port
2. Catalog browser in multi-user mode authenticates and receives JWT token
3. Asset list, search, and metadata display correctly via broker service
4. Folder watcher detects new files and auto-indexes within 30 seconds
5. Audit log records every authenticated request

**UI Hint:** yes — Login view, connection status indicator

### Phase 3: Multi-User Concurrency

**Goal:** Multiple users access the broker simultaneously with consistent state and real-time change propagation.

**Requirements:** BROK-02, BROK-03, BROK-04

**Success Criteria:**
1. 10 concurrent users browse and search with all responses under 3 seconds
2. Metadata change by User A appears for User B within 5 seconds
3. Simultaneous edits to same asset resolve with last-write-wins; both see final state

**UI Hint:** no — primarily transport and data layer work

### Phase 4: Asset Ingestion & Management

**Goal:** Users can trigger folder scans, edit metadata with round-trip to source files, apply basic adjustments, and export assets.

**Requirements:** META-01, META-02, META-03, META-04, EDIT-01, EDIT-02, EDIT-03, EDIT-04

**Success Criteria:**
1. Folder scan of 10,000 files completes in under 5 minutes with full metadata extraction
2. Metadata edits are written to source file XMP within 5 seconds
3. RAW files receive XMP sidecar on metadata edit
4. Export to JPEG/TIFF produces correct output with adjustments applied
5. Read-only file triggers user notification on save attempt

**UI Hint:** yes — Metadata editor view, ingestion progress dialog, export dialog

### Phase 5: Admin Panel & Mode Management

**Goal:** Administrators can toggle modes, deploy the broker as a native service, and migrate databases.

**Requirements:** ADMIN-01, ADMIN-02, ADMIN-03, ADMIN-04

**Success Criteria:**
1. Admin panel toggles between standalone and multi-user mode and reconnects correctly
2. Database migration wizard exports SQLite to PostgreSQL/SQL Server in under 5 minutes
3. Broker service registers as Windows Service / macOS launchd / Linux systemd
4. Service monitor shows accurate connection count and uptime

**UI Hint:** yes — Admin panel view, migration wizard, service status dashboard

### Phase 6: Database Provider Matrix

**Goal:** All three database providers (SQLite, PostgreSQL, SQL Server) are fully functional and tested.

**Requirements:** DB-02, DB-03, DB-04

**Success Criteria:**
1. Broker service starts and operates correctly with each provider
2. Provider swap requires only configuration change (no code changes)
3. Integration test suite passes against all three providers via Testcontainers
4. Previously stored data is accessible after restart with unchanged provider

**UI Hint:** no — configuration and integration test work

### Phase 7: User & Role Administration

**Goal:** Full user lifecycle management with role-based access control in multi-user mode.

**Requirements:** AUTH-04, AUTH-05

**Success Criteria:**
1. Administrator can create, edit, and deactivate users with role assignment
2. Viewer role can only read assets and collections
3. Editor role can modify metadata and collections but cannot manage users
4. Unauthorized access attempts are denied with appropriate error codes
5. Audit log viewer shows filterable operation history

**UI Hint:** yes — User management view, role picker, audit log viewer

## Milestones

### v1.0 — Core DAM (Phases 1-3)

**Goal:** A working digital asset catalog in both standalone and multi-user modes.
**Requirements:** 19 of 34 v1 requirements
**Completion Criteria:** All Phase 1-3 success criteria pass

### v1.1 — Management & Deployment (Phases 4-6)

**Goal:** Complete asset lifecycle management and flexible deployment options.
**Requirements:** 15 of 34 v1 requirements
**Completion Criteria:** All Phase 4-6 success criteria pass

### v1.2 — Administration (Phase 7)

**Goal:** Production-ready user management and access control.
**Requirements:** 2 of 34 v1 requirements
**Completion Criteria:** All Phase 7 success criteria pass

---
*Roadmap created: 2026-05-23*
*Last updated: 2026-05-23 after initialization*
