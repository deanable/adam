# Phase 1: Standalone Mode - Context

**Gathered:** 2026-05-23
**Status:** Ready for planning
**Mode:** Auto-generated (discuss skipped — extensive specs exist)

## Phase Boundary

Deliver a fully functional self-contained catalog browser with SQLite, browse/search/view, and metadata extraction. Zero external dependencies — user selects a root folder on first launch, system scans and indexes all recognized assets with EXIF/IPTC/XMP metadata extraction, and provides grid/loupe/compare views with full-text search.

## Implementation Decisions

### Locked (from specs)
- Standalone mode uses SQLite via EF Core (already implemented in Adam.Shared)
- Avalonia 12 for cross-platform UI (already in use)
- MetadataExtractor + ImageSharp for metadata and thumbnails
- No ASP.NET dependency — direct SQLite access only
- Folder-scan based ingestion (no manual upload UI)

### the agent's Discretion
- Specific Avalonia control layout and styling
- Search indexing strategy (EF Core queries vs full-text search)
- Thumbnail caching approach
- Error handling for corrupt/unreadable files

## Existing Code Insights

**Already implemented:**
- Domain models (DigitalAsset, Collection, Keyword, MetadataProfile, etc.)
- AppDbContext with SQLite provider, soft-delete, versioning
- EF Core migrations and seed data (roles)
- MetadataExtractorService, ThumbnailService, ChecksumService
- SearchService, FileIndexer, DuplicateDetector
- Avalonia app shell with DI container
- Views: MainWindow, AssetGallery, AssetDetail, AdminPanel, MetadataEditor, Ingestion, MigrationWizard, UserManagement, AuditLog
- ViewModels for all major screens
- BulkOperationQueue, DeleteService, ModeManager, BrokerClient, AuthSession

**Known issues to fix:**
- `BrokerClient` hardcodes `localhost:5000` but `TcpListenerService` uses `9100`
- `AuthHandler._signingKey` is static mutable (race condition)
- Several CatalogBrowser tests fail (BulkOperationQueue, SearchableTreeViewFilter)
- Test coverage gaps for core services

## Specific Ideas

No additional requirements beyond spec.

## Deferred Ideas

None.

## Canonical References

- `specs/001-digital-asset-management/spec.md` — Feature specification
- `specs/001-digital-asset-management/plan.md` — Implementation plan
- `.planning/REQUIREMENTS.md` — v1 requirements mapped to phases
- `.planning/codebase/ARCHITECTURE.md` — System architecture
- `.planning/codebase/CONCERNS.md` — Known issues
