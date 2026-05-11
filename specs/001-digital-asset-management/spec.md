# Feature Specification: Digital Asset Management System (adam)

**Feature Branch**: `001-digital-asset-management`  
**Created**: 2026-05-11  
**Status**: Draft  
**Input**: User description: "I want to build a digital asset management system called adam. Using dotnet 10 Avalonia for a platform agnostic approach. The software needs to be extensible. The user can choose their database platform e.g. SQL server, Postgres, SQLlite, etc. The application will consist of two separate modes: An catalog browser application and a service that brokers multi-user access."
**User Draft**: [spec-user-draft.md](spec-user-draft.md)

## Clarifications

### Session 2026-05-11

- Q: Folder watching vs manual upload? → A: No upload — ingest-only. User selects a root folder on setup. The system scans and indexes all assets within it. Folder watching handles new/changed files.
- Q: Standalone startup flow — CLI arg or interactive? → A: Interactive root folder picker on first launch. The user selects the folder containing their digital assets; the system creates/manages its SQLite database alongside it. Choice is persisted.
- Q: IFileService abstraction needed? → A: Yes — define `IFileService` contract (metadata read, thumbnail gen, checksum, file operations) shared across both modes

### Session 2026-05-11 (2)

- Q: Lightroom feature scope? → A: Library module (catalog, metadata, keywords, ratings, collections) plus basic adjustments (rotate, flip, export). Develop module out of scope.
- Q: Full metadata round-trip? → A: Yes — extract EXIF/IPTC/XMP on ingest, store in catalog, write metadata edits back to source files on disk.
- Q: RAW file support? → A: Yes — support common camera RAW formats (CR2, NEF, ARW, DNG, etc.) for preview and metadata, but only basic edits (export to JPEG/TIFF).

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Standalone Local Catalog (Priority: P1)

A user launches the catalog browser in standalone mode without needing a background service or network. On first launch, they select a root folder containing their digital assets via a folder picker dialog — the browser remembers this choice. The browser scans the folder, extracts embedded metadata (EXIF, IPTC, XMP) from every recognized file, indexes all assets into a local SQLite database (created alongside the root folder), and generates thumbnails. The user can then browse, search, filter, and manage metadata across the entire catalog on the local machine.

**Why this priority**: Standalone mode provides immediate value as a self-contained desktop application with zero infrastructure dependencies.

**Independent Test**: Launch the catalog browser, select a root folder with 50 mixed-type files containing EXIF/IPTC/XMP metadata, confirm the scan extracts all metadata correctly, and verify full browse/search/metadata-edit capabilities without any service process running.

**Acceptance Scenarios**:

1. **Given** no broker service is running, **When** a user launches the catalog browser and selects a root folder in standalone mode, **Then** the browser scans and indexes all recognized assets with full metadata extraction
2. **Given** a standalone session with 1000 indexed assets, **When** the user searches by any metadata field (e.g., camera model, keyword, date taken), **Then** results appear within 2 seconds
3. **Given** a standalone session with assets containing EXIF data, **When** the user views the metadata panel, **Then** all EXIF/IPTC/XMP fields are displayed and searchable

---

### User Story 2 — Browse and Search Digital Assets (Multi-User) (Priority: P1)

A user opens the catalog browser in multi-user mode, authenticates against the broker service over TCP, and browses or searches the shared digital asset catalog. They can view asset previews and metadata, navigate collections, filter by any metadata field, assign hierarchical keywords, rate assets, and compare them side by side.

**Why this priority**: Browsing and searching is the core value of a shared catalog — without it, collaborative asset management has no purpose.

**Independent Test**: Launch the catalog browser in multi-user mode, connect to a running broker service with pre-loaded assets, and verify that assets appear in search results with correct metadata.

**Acceptance Scenarios**:

1. **Given** the broker service is running with 1000 indexed assets, **When** a user opens the catalog browser in multi-user mode and enters a search query, **Then** matching assets appear within 2 seconds
2. **Given** a catalog of mixed asset types, **When** a user filters by camera model, date range, keyword, or rating, **Then** only matching assets are displayed
3. **Given** an empty search field, **When** a user browses the catalog in grid view, **Then** assets are displayed in a paginated grid with thumbnails, organized by collection

---

### User Story 3 — Multi-User Concurrent Access via Broker Service (Priority: P1)

Multiple users connect to the broker service simultaneously over TCP. Each user authenticates independently and receives a consistent view of the shared catalog. Changes made by one user (e.g., metadata updates, keyword assignments, ratings) are propagated to all connected users.

**Why this priority**: The core differentiator is multi-user access brokered by the service — without this, the system is just a local file browser.

**Independent Test**: Two separate catalog browser instances connect to the same broker service, make concurrent metadata changes, and verify both see consistent state.

**Acceptance Scenarios**:

1. **Given** a running broker service, **When** 10 simultaneous users connect and browse, **Then** all users receive responses within 3 seconds
2. **Given** User A updates an asset's metadata or rating, **When** User B refreshes their view, **Then** User B sees the updated metadata
3. **Given** two users attempt to modify the same asset metadata simultaneously, **Then** the last write wins and both users see the final state

---

### User Story 4 — Lightroom-Class Browsing & Organization (Priority: P1)

The catalog browser provides professional-grade browsing tools: a grid view with adjustable thumbnail sizes, a loupe view for full-resolution inspection, a compare view for side-by-side evaluation, hierarchical keyword tagging, star ratings, color labels, flagging (pick/reject), and curated collections that group assets independently of their on-disk folder structure.

**Why this priority**: A Lightroom-class browsing experience is the core value proposition of a professional DAM client.

**Independent Test**: Load a catalog of 500 assets, create hierarchical keywords and collections, rate and flag assets, and verify all views (grid, loupe, compare) render correctly with metadata overlays.

**Acceptance Scenarios**:

1. **Given** a catalog with 500 assets, **When** the user switches between grid, loupe, and compare views, **Then** each view renders within 1 second
2. **Given** an asset with a 5-star rating and green label, **When** the user filters by rating >= 4 stars, **Then** only that asset appears
3. **Given** hierarchical keywords ("Nature > Birds > Eagle"), **When** the user assigns keywords to multiple assets, **Then** filtering by any keyword level returns the correct subset
4. **Given** a curated collection, **When** the user drags assets into it, **Then** the collection preserves its membership independently of folder structure on disk

---

### User Story 5 — Metadata Extraction & Round-Trip (Priority: P1)

During ingest, the system extracts every embedded metadata field from industry-standard schemas (EXIF, IPTC, XMP) and stores them in the catalog database. When a user edits metadata in the client — including title, description, keywords, ratings, GPS coordinates, copyright, or any IPTC/XMP field — the changes synchronize back to the source file on disk, preserving the embedded metadata. This round-trip works for all supported file types that support metadata embedding.

**Why this priority**: Full metadata round-trip ensures the catalog never diverges from the source files — critical for professional asset management.

**Independent Test**: Ingest a JPEG with known EXIF/IPTC/XMP values, edit several fields in the client, save, then inspect the source file with a third-party metadata tool (e.g., ExifTool) to confirm the file reflects the edits.

**Acceptance Scenarios**:

1. **Given** a JPEG with EXIF, IPTC, and XMP metadata, **When** the ingest scan completes, **Then** all embedded fields are stored in the catalog database
2. **Given** an asset where the user edits the title, description, keywords, and copyright, **When** they save, **Then** the changes are written to the source file's XMP metadata within 5 seconds
3. **Given** a RAW file (CR2/NEF/ARW), **When** the user edits IPTC metadata, **Then** changes are written to an XMP sidecar file alongside the RAW
4. **Given** a file that is read-only on disk, **When** the user attempts to save metadata, **Then** the system notifies the user that the file could not be updated

---

### User Story 6 — Basic Adjustments & Export (Priority: P2)

The catalog browser provides basic image adjustments: rotate clockwise/counterclockwise, flip horizontal/vertical. From any view, the user can export selected assets to JPEG or TIFF format, optionally applying adjustments and specifying output resolution, quality, and color space.

**Why this priority**: Basic adjustments and export are common workflows that users expect in a DAM client.

**Independent Test**: Select three images, rotate one, flip another, leave the third untouched, export all three as JPEGs, and verify the output files reflect the adjustments.

**Acceptance Scenarios**:

1. **Given** an image in loupe view, **When** the user clicks rotate 90 degrees clockwise, **Then** the preview updates and the orientation flag is persisted in metadata
2. **Given** a selection of 5 assets, **When** the user exports as JPEG with 90% quality and 1920px long edge, **Then** 5 JPEG files are created with the specified parameters
3. **Given** a RAW file, **When** the user exports as TIFF, **Then** a rendered TIFF is created with default processing

---

### User Story 7 — Asset Ingestion via Folder Scan (Priority: P2)

On initial setup or manual re-scan, the system scans the configured root folder, identifies recognized file types, and extracts full EXIF/IPTC/XMP metadata from each file. In multi-user mode, the broker service continuously watches the root folder for changes and auto-indexes new or modified files. The scan reports the number of files indexed, skipped (unsupported), and any metadata extraction warnings.

**Why this priority**: Without the ability to ingest assets from disk with full metadata, the catalog has no data to work with.

**Independent Test**: Point the system at a folder with 100 mixed-type files (JPEG, RAW, TIFF, PDF, unsupported), trigger a scan, and verify all recognized files appear in the catalog with full EXIF/IPTC/XMP metadata extracted.

**Acceptance Scenarios**:

1. **Given** a configured root folder with files containing EXIF, IPTC, and XMP metadata, **When** an initial scan runs, **Then** all three metadata schemas are extracted and stored as catalog metadata
2. **Given** a new file is added to the root folder, **When** the watcher detects the change, **Then** the file is automatically indexed with full metadata extraction within 30 seconds
3. **Given** a file's metadata changes on disk (e.g., via another application), **When** the watcher detects the modification, **Then** the system re-extracts metadata and updates the catalog
4. **Given** a duplicate file (matching checksum), **When** the scan encounters it, **Then** the system records a single asset entry and logs the duplicate path

---

### User Story 8 — Admin Panel & Mode Management (Priority: P2)

An administrator uses the admin panel within the catalog browser to toggle between standalone and multi-user modes, deploy the broker service as a native OS background worker (Windows Service, launchd, systemd), and migrate a standalone SQLite database to a shared PostgreSQL/SQL Server instance.

**Why this priority**: Mode management bridges standalone and multi-user operation, enabling teams to start locally and scale to shared deployments.

**Independent Test**: Start in standalone mode, use the admin panel to register the broker service as a native background worker, switch to multi-user mode, and verify the catalog connects through the service.

**Acceptance Scenarios**:

1. **Given** an administrator in standalone mode, **When** they toggle to multi-user mode and provide service connection details, **Then** the catalog browser reconnects through the broker service
2. **Given** a running standalone SQLite database, **When** the administrator uses the migration wizard, **Then** the data is migrated to a PostgreSQL/SQL Server instance
3. **Given** an administrator on Windows, **When** they deploy the broker service, **Then** it registers as a Windows Service; on macOS as a launchd daemon; on Linux as a systemd unit

---

### User Story 9 — Database Provider Configuration (Priority: P2)

An administrator configures the system to use a specific database provider (SQLite, PostgreSQL, or SQL Server) at deployment time. In multi-user mode, the broker service uses the selected provider. In standalone mode, SQLite is used automatically.

**Why this priority**: Database extensibility is explicitly required and foundational to system architecture.

**Independent Test**: Deploy the broker service with each supported database provider, populate with test data, and verify the catalog browser functions identically across all providers.

**Acceptance Scenarios**:

1. **Given** a fresh deployment, **When** an administrator configures SQLite as the provider, **Then** the system creates and uses a local SQLite database
2. **Given** a fresh deployment, **When** an administrator configures PostgreSQL as the provider, **Then** the system connects to the specified PostgreSQL instance
3. **Given** a fresh deployment, **When** an administrator configures SQL Server as the provider, **Then** the system connects to the specified SQL Server instance
4. **Given** a running system with data, **When** the provider configuration is unchanged, **Then** all previously stored data is accessible on restart

---

### User Story 10 — User and Role Management (Priority: P3)

An administrator creates user accounts and assigns roles. Roles determine what actions a user can perform (view, edit metadata, create collections, manage keywords, delete from catalog, manage users). This is only available in multi-user mode.

**Why this priority**: Basic operations can work with a single admin account; role management is needed for production deployment but not for initial validation.

**Independent Test**: Create three users with different roles and verify each can only perform permitted actions.

**Acceptance Scenarios**:

1. **Given** an admin user, **When** they create a new user with Viewer role, **Then** that user can browse, search, and view metadata but cannot edit or delete
2. **Given** an admin user, **When** they create a new user with Editor role, **Then** that user can edit metadata, assign keywords and ratings, and organize collections but cannot delete assets or manage users
3. **Given** a non-admin user, **When** they attempt to access user management, **Then** the system denies access

---

### Edge Cases

- What happens when the broker service is unavailable — does the browser fall back to standalone mode?
- How does the system handle concurrent metadata edits of the same asset by two editors?
- What happens when storage runs out of space during an asset ingest?
- How does the system handle unsupported or corrupt file types in the root folder?
- What happens when a network interruption occurs during an ingest in multi-user mode?
- How does the system behave when the configured database provider is unreachable?
- How are metadata write-backs handled when a file is read-only or locked?
- How does the system handle XMP sidecar separation for RAW files?
- What happens when a user edits metadata that conflicts with existing embedded metadata?
- How does the system handle large keyword hierarchies (10,000+ keywords)?
- What happens when a user's authentication session expires mid-session?
- How does a standalone database migration handle conflicts if new metadata was added locally?
- What happens if the native service registration fails (e.g., insufficient permissions)?
- What happens when a watched folder contains files with corrupt embedded metadata?
- How does the system handle files moved or renamed outside the application?

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The catalog browser MUST support two operational modes: local standalone (SQLite, no external service) and multi-user (broker service over TCP)
- **FR-002**: In standalone mode, the catalog browser MUST initialize its own indexing engine and `IFileService` internally without requiring any external process
- **FR-003**: On first launch, the catalog browser MUST prompt the user to select a root folder containing their digital assets, persist this choice, and scan the folder to build the catalog
- **FR-004**: The broker service MUST accept multiple simultaneous client connections over TCP and route requests appropriately
- **FR-005**: The broker service MUST provide a database abstraction layer supporting SQLite, PostgreSQL, and SQL Server providers
- **FR-006**: The catalog browser MUST authenticate against the broker service before allowing multi-user catalog access
- **FR-007**: The catalog browser MUST extract EXIF, IPTC, and XMP metadata from every recognized file during ingest and store it in the catalog database
- **FR-008**: The catalog browser MUST provide grid view (adjustable thumbnail sizes), loupe view (full-resolution inspection), and compare view (side-by-side)
- **FR-009**: The system MUST support hierarchical keywords with unlimited nesting (e.g., "Nature > Birds > Eagle")
- **FR-010**: The system MUST support star ratings (0-5), color labels, and flagging (pick/reject) on assets
- **FR-011**: The system MUST support curated collections that hold asset references independently of disk folder structure
- **FR-012**: Authorized users MUST be able to edit metadata (EXIF/IPTC/XMP fields, title, description, keywords, ratings, labels, GPS, copyright) in the client
- **FR-013**: When metadata is edited in the client, the system MUST write changes back to the source file's embedded XMP metadata (or XMP sidecar for RAW files)
- **FR-014**: The system MUST support basic image adjustments: rotate (90, 180, 270), flip (horizontal, vertical)
- **FR-015**: The system MUST support export of selected assets to JPEG and TIFF with configurable quality, resolution, and color space
- **FR-016**: The system MUST support search across all metadata fields including EXIF, IPTC, XMP, keywords, ratings, labels, and free text
- **FR-017**: Authorized users MUST be able to delete assets from the catalog (soft-delete, does not remove source file)
- **FR-018**: The system MUST log all create, update, and delete operations with timestamp and user identity
- **FR-019**: The broker service MUST expose a TCP+Protobuf API consumed by the catalog browser for all multi-user operations
- **FR-020**: The system MUST detect duplicate files via content checksum during scan and avoid duplicate catalog entries
- **FR-021**: The catalog browser MUST be fully functional on Windows, macOS, and Linux
- **FR-022**: The broker service MUST be deployable as a native background worker: Windows Service (SCM), macOS Launch Daemon (launchd), and Linux systemd unit
- **FR-023**: The broker service MUST watch the configured root folder for file additions, modifications, and deletions, and auto-index changes with full metadata extraction
- **FR-024**: The admin panel MUST provide a mode toggle between standalone and multi-user configurations
- **FR-025**: The admin panel MUST provide a database migration wizard to migrate standalone SQLite data to PostgreSQL or SQL Server
- **FR-026**: The admin panel MUST provide service deployment tools for registering/unregistering the native background worker per platform
- **FR-027**: The system MUST define an `IFileService` abstraction for file operations (metadata reading, checksum computation, thumbnail generation) shared across standalone and multi-user modes
- **FR-028**: The system MUST support triggering a manual re-scan of the root folder from the admin panel
- **FR-029**: The system MUST support role-based access control with Viewer, Editor, and Administrator roles in multi-user mode

### Key Entities *(include if feature involves data)*

- **Digital Asset**: A file (image, RAW, video, document, audio) discovered in the root folder, with embedded metadata including EXIF (camera, lens, exposure, GPS, date taken), IPTC (creator, description, keywords, copyright), and XMP (extended metadata, ratings, labels)
- **Collection**: A curated, user-defined grouping of assets independent of disk folder structure — membership is explicit, not derived
- **Keyword**: A hierarchical tag (e.g., "Wildlife > Birds > Eagle") assignable to assets, with parent-child relationships for inheritance in search
- **Rating**: A star rating (0-5), color label (Red, Green, Blue, Yellow, Purple), or flag (Pick, Reject) attached to an asset
- **User**: An authenticated account with a username, password hash, email, assigned role, and account status
- **Role**: A named permission set (Viewer, Editor, Administrator) defining allowable operations
- **Metadata Profile**: Extracted metadata payload from a file organized by schema (EXIF, IPTC, XMP) — stored as structured fields in the database for queryability
- **Access Log**: An immutable record of operations performed (who, what, when, result)
- **Mode Configuration**: Settings defining the current operational mode (standalone vs multi-user) and associated connection details
- **IFileService**: Abstraction for file operations including metadata extraction, checksum computation, thumbnail generation, and metadata write-back

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Standalone mode launches and provides full catalog functionality without any service or network dependency
- **SC-002**: Multiple users (at least 10) can browse and search the catalog concurrently with all responses under 3 seconds
- **SC-003**: Search results return within 2 seconds for catalogs of up to 100,000 assets, including full-text search across metadata fields
- **SC-004**: EXIF, IPTC, and XMP metadata extraction completes during ingest with 100% of embedded fields captured for JPEG and TIFF files
- **SC-005**: Metadata edits in the client are written back to the source file within 5 seconds of save
- **SC-006**: Grid view renders 100 thumbnails in under 1 second; loupe view loads full-resolution preview in under 3 seconds
- **SC-007**: A root folder with 10,000 files is fully scanned, metadata-extracted, and indexed within 5 minutes on first setup
- **SC-008**: The catalog browser launches and is usable within 10 seconds on all supported platforms
- **SC-009**: Asset metadata changes are reflected for all connected users within 5 seconds of save
- **SC-010**: Users can perform all primary tasks (browse, search, edit metadata, export) without the application crashing or requiring restart
- **SC-011**: The broker service can be deployed as a native background worker on all three target platforms
- **SC-012**: A standalone SQLite database can be migrated to PostgreSQL or SQL Server in under 5 minutes for databases up to 10,000 assets

## Assumptions

- The catalog browser and broker service communicate over TCP with Google.Protobuf serialization in multi-user mode
- In standalone mode, the catalog browser operates entirely self-contained with direct SQLite access
- Asset files remain in their original location on disk (the root folder); the database stores metadata and relative paths only
- Metadata extraction uses the MetadataExtractor .NET library (cross-platform, no ASP.NET dependency)
- Metadata write-back uses XMP embedding for supported formats (JPEG, TIFF, PNG, WebP) and XMP sidecar files for RAW formats (CR2, NEF, ARW, DNG)
- Authentication is handled by the broker service using local user accounts with password-based login (multi-user mode)
- Standalone mode uses a single local admin account without authentication
- Supported asset types include images (JPEG, PNG, WebP, TIFF), camera RAW (CR2, NEF, ARW, DNG), videos (MP4, MOV), documents (PDF, DOCX, TXT), and audio (MP3, WAV)
- The system targets Windows, macOS, and Linux as first-class platforms
- Initial deployments will be on local area networks or single machines, not requiring cloud-scale distribution
- The database abstraction will be configured at deployment time through configuration settings, not code changes
- Native service registration requires appropriate OS-level permissions (admin/sudo)
- On first launch, standalone mode prompts the user to select a root folder; the SQLite database is created alongside the root folder
- In multi-user mode, the broker service runs on the same machine as the asset files and watches the root folder for changes
- The system does not provide a manual file upload mechanism — all asset ingestion is folder-scan based
- Deleting an asset from the catalog does not delete the source file on disk
- Adjustments (rotate, flip) are applied as metadata flags (orientation tag) rather than pixel-level destructive edits, except during export
