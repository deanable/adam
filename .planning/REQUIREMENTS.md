# Requirements: adam — Digital Asset Management System

**Defined:** 2026-05-23
**Core Value:** Users can browse, search, and manage digital assets with full metadata round-trip across platforms

## v1 Requirements

### Authentication & Access Control

- [ ] **AUTH-01**: User can create account with username and password in multi-user mode
- [ ] **AUTH-02**: User can log in and receive a JWT token valid for 24 hours
- [ ] **AUTH-03**: User session persists and validates across TCP requests
- [x] **AUTH-04**: Role-based access control enforces Viewer, Editor, and Administrator permissions
- [ ] **AUTH-05**: Administrator can create, edit, and deactivate user accounts
- [ ] **AUTH-06**: All create, update, and delete operations are logged with timestamp and user identity

### Asset Catalog & Browse

- [ ] **CATA-01**: Standalone mode launches without any external service or network dependency
- [ ] **CATA-02**: On first launch, user selects a root folder; system scans and indexes all recognized assets
- [ ] **CATA-03**: System extracts EXIF, IPTC, and XMP metadata from every recognized file during ingest
- [ ] **CATA-04**: Grid view displays assets with adjustable thumbnail sizes
- [ ] **CATA-05**: Loupe view provides full-resolution image inspection
- [ ] **CATA-06**: Compare view shows two assets side-by-side for evaluation
- [ ] **CATA-07**: Hierarchical keyword tagging with unlimited nesting (e.g., "Nature > Birds > Eagle")
- [ ] **CATA-08**: Star ratings (0-5), color labels (Red, Green, Blue, Yellow, Purple), and flagging (Pick/Reject)
- [ ] **CATA-09**: Curated collections group assets independently of disk folder structure
- [ ] **CATA-10**: Full-text search across all metadata fields returns results within 2 seconds at 100K assets
- [ ] **CATA-11**: Filtering by camera model, date range, keyword, rating, label, or flag

### Metadata Management

- [x] **META-01**: User can edit asset title, description, keywords, ratings, labels, GPS, and copyright in the client
- [x] **META-02**: Metadata edits are written back to source file's embedded XMP metadata within 5 seconds
- [x] **META-03**: RAW files (CR2, NEF, ARW) receive XMP sidecar files for metadata write-back
- [x] **META-04**: Read-only files trigger a user notification when metadata save is attempted
- [x] **META-05**: Duplicate files are detected via SHA256 checksum and recorded without duplicate catalog entries

### Multi-User & Broker

- [ ] **BROK-01**: Broker service accepts multiple simultaneous TCP connections (up to 50)
- [ ] **BROK-02**: 10 concurrent users can browse and search with all responses under 3 seconds
- [ ] **BROK-03**: Metadata changes by one user propagate to all connected users within 5 seconds
- [ ] **BROK-04**: Concurrent edits to the same asset use last-write-wins conflict resolution
- [ ] **BROK-05**: Broker service watches root folder and auto-indexes new or modified files

### Basic Adjustments & Export

- [x] **EDIT-01**: User can rotate image 90, 180, 270 degrees clockwise/counter-clockwise
- [x] **EDIT-02**: User can flip image horizontally or vertically
- [x] **EDIT-03**: User can export selected assets to JPEG with configurable quality and resolution
- [x] **EDIT-04**: User can export selected assets to TIFF with configurable color space

### Admin & Deployment

- [ ] **ADMIN-01**: Admin panel provides toggle between standalone and multi-user mode
- [ ] **ADMIN-02**: Database migration wizard exports standalone SQLite to PostgreSQL or SQL Server
- [ ] **ADMIN-03**: Broker service deployable as native background worker on Windows (SCM), macOS (launchd), and Linux (systemd)
- [ ] **ADMIN-04**: Service status monitor shows connected clients, uptime, and health

### Database Providers

- [ ] **DB-01**: System supports SQLite provider for standalone mode
- [ ] **DB-02**: System supports PostgreSQL provider for multi-user production
- [ ] **DB-03**: System supports SQL Server provider for multi-user enterprise
- [ ] **DB-04**: Provider selection is configuration-only (no code changes required)

## v2 Requirements

### Advanced Metadata

- **META-V2-01**: Automatic keyword suggestions based on image content (AI/ML)
- **META-V2-02**: Batch metadata editing across multiple assets
- **META-V2-03**: Metadata import/export to CSV/XMP sidecar bundles

### Collaboration

- **COLL-V2-01**: In-app notifications for asset changes by other users
- **COLL-V2-02**: Comment threads on individual assets
- **COLL-V2-03**: Activity feed showing recent catalog changes

### Integration

- **INTG-V2-01**: Plugin system for third-party metadata extractors
- **INTG-V2-02**: WebDAV or SMB mount support for remote asset folders
- **INTG-V2-03**: Cloud storage provider sync (S3, Azure Blob, etc.)

## Out of Scope

| Feature | Reason |
|---------|--------|
| Lightroom Develop module (pixel-level RAW processing) | Complex image processing pipeline; basic adjustments (rotate, flip, export) sufficient for v1 |
| Real-time chat / messaging | Not core to DAM value; collaboration features deferred to v2 |
| Video editing / manipulation | Asset metadata support only; no video manipulation in v1 |
| OAuth / social login | Email/password auth sufficient for v1; can add later |
| Mobile app (iOS/Android) | Desktop-first strategy; mobile is a separate product surface |
| Manual file upload UI | Explicit design decision — all ingestion is folder-scan based |
| Cloud-scale multi-region deployment | Targets LAN and single-machine; scale requirements are modest |
| ASP.NET / web UI stack | Explicit architecture constraint — desktop + TCP service only |
| AI-powered image recognition | Deferred to v2; keyword suggestion is a differentiator, not table stakes |

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| AUTH-01 | Phase 2 | ✅ Complete |
| AUTH-02 | Phase 2 | ✅ Complete |
| AUTH-03 | Phase 2 | ✅ Complete |
| AUTH-04 | Phase 5 (foundation) + Phase 7 (UI) | ✅ Complete |
| AUTH-05 | Phase 5 | ✅ Complete |
| AUTH-06 | Phase 2 | ✅ Complete |
| CATA-01 | Phase 1 | ✅ Complete |
| CATA-02 | Phase 1 | ✅ Complete |
| CATA-03 | Phase 1 | ✅ Complete |
| CATA-04 | Phase 1 | ✅ Complete |
| CATA-05 | Phase 1 | ✅ Complete |
| CATA-06 | Phase 1 | ✅ Complete |
| CATA-07 | Phase 1 | ✅ Complete |
| CATA-08 | Phase 1 | ✅ Complete |
| CATA-09 | Phase 1 | ✅ Complete |
| CATA-10 | Phase 1 | ✅ Complete |
| CATA-11 | Phase 1 | ✅ Complete |
| META-01 | Phase 4 | ✅ Complete |
| META-02 | Phase 4 | ✅ Complete |
| META-03 | Phase 4 | ✅ Complete |
| META-04 | Phase 4 | ✅ Complete |
| META-05 | Phase 1 | ✅ Complete |
| BROK-01 | Phase 2 | ✅ Complete |
| BROK-02 | Phase 3 | ✅ Complete |
| BROK-03 | Phase 3 | ✅ Complete |
| BROK-04 | Phase 3 | ✅ Complete |
| BROK-05 | Phase 2 | ✅ Complete |
| EDIT-01 | Phase 4 | ✅ Complete |
| EDIT-02 | Phase 4 | ✅ Complete |
| EDIT-03 | Phase 4 | ✅ Complete |
| EDIT-04 | Phase 4 | ✅ Complete |
| ADMIN-01 | Phase 5 | ✅ Complete |
| ADMIN-02 | Phase 5 | ✅ Complete |
| ADMIN-03 | Phase 5 | ✅ Complete |
| ADMIN-04 | Phase 5 | ✅ Complete |
| DB-01 | Phase 1 | ✅ Complete |
| DB-02 | Phase 6 | ✅ Complete |
| DB-03 | Phase 6 | ✅ Complete |
| DB-04 | Phase 6 | ✅ Complete |

**Coverage:**
- v1 requirements: 34 total
- Mapped to phases: 34
- Unmapped: 0 ✓

---
*Requirements defined: 2026-05-23*
*Last updated: 2026-06-03 after Phase 4 completion*
