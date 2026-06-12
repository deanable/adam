# Adam v1.0 Release Notes

**Release date:** June 2026
**Version:** 1.0.0

## Overview

Adam is a cross-platform digital asset management (DAM) system built with .NET and Avalonia UI. It provides a desktop catalog browser for organizing, searching, and managing image and media assets with full metadata round-trip, alongside a TCP broker service for multi-user collaboration.

## What's New in v1.0

### Standalone Mode
- **Zero-dependency launch** — run the Catalog Browser with no external service or network
- **Folder ingestion** — scan and index folders of images, videos, and other media files
- **EXIF/IPTC/XMP extraction** — automatic metadata extraction from all recognized file types
- **Thumbnail generation** — on-the-fly thumbnails for fast gallery browsing

### Asset Catalog & Browsing
- **Grid and list gallery views** with adjustable thumbnail sizes and sorting (name, date, rating, size, type)
- **Sidebar filters** — browse by folder, collection, keyword, category, media format, and date taken
- **Full-text search** across title, description, file name, keywords, and camera model
- **Star ratings** (0–5), **color labels** (Red, Green, Blue, Yellow, Purple), and **pick/reject flags**
- **Curated collections** — group assets independently of disk folder structure
- **Hierarchical keywords and categories** with unlimited nesting

### Metadata Editing
- Edit title, description, keywords, ratings, labels, flags, copyright, and GPS coordinates
- **XMP write-back** — metadata edits written back to source file's embedded XMP within seconds
- **RAW file sidecar support** — CR2, NEF, ARW files receive `.xmp` sidecar files
- **Read-only file notifications** — user is alerted when metadata save is attempted on protected files
- **Duplicate detection** — SHA256 checksum prevents duplicate catalog entries

### Image Adjustments
- **Rotate** 90° clockwise/counter-clockwise
- **Flip** horizontally or vertically

### Export
- Export selected assets to **JPEG** (configurable quality) or **TIFF** (configurable compression and color space)
- Configurable max dimension and resolution

### AI Image Tagging
- **Local on-device inference** using LiquidVision (ONNX-based LFM2-VL model)
- Three trigger modes: ingest opt-in, per-asset auto-tag, and bulk re-tag
- Automatic keyword and category suggestions from image content
- No cloud dependency — all processing runs locally

### Trash & Recovery
- **Soft-delete** with confirmation dialog
- **Trash view** to browse deleted assets
- **Restore** or **permanently delete** assets from trash

### Context Menu & Keyboard Shortcuts
- Right-click context menu on gallery assets with rate, label, flag, AI tag, export, rotate/flip, reveal, copy, and delete
- **Bulk actions** — rate, label, flag, and delete work across multi-selection
- Keyboard accelerators: Delete, Ctrl+A/C/E/S, Ctrl+Shift+T (trash), rating digits 0–5

### Multi-User Mode
- **TCP broker service** for simultaneous multi-user access (tested with 10+ concurrent clients)
- **JWT authentication** with 24-hour token expiry and session persistence
- **Role-based access control** — Viewer, Editor, and Administrator roles with granular permissions
- **Audit logging** — all create, update, and delete operations logged with timestamp and user identity
- **Real-time change propagation** — metadata changes propagate to connected users within seconds
- **Last-write-wins** conflict resolution for concurrent edits

### Admin Panel (Service Manager)
- **Service status monitor** — connected clients, uptime, and health indicators
- **User management** — add, edit, deactivate user accounts; assign roles
- **Database migration wizard** — migrate standalone SQLite to PostgreSQL or SQL Server
- **Service installation** — native background worker on Windows (SCM), macOS (launchd), and Linux (systemd)
- **Firewall configuration** — automatic Windows Firewall rule management

### Database Providers
- **SQLite** for standalone mode (zero configuration)
- **PostgreSQL** for multi-user production
- **SQL Server** for multi-user enterprise
- Provider selection is configuration-only — no code changes required

### Platform Distribution
- **Windows** — MSI installer via WiX Toolset
- **macOS** — DMG disk image
- **Linux** — DEB package
- Self-contained deployment — no .NET runtime installation required

## Architecture

| Component | Technology |
|-----------|-----------|
| Desktop UI | Avalonia UI (cross-platform XAML) |
| Backend | .NET 10, Entity Framework Core |
| Transport | Raw TCP with length-prefixed protobuf framing |
| Database | SQLite / PostgreSQL / SQL Server (configurable) |
| AI Tagging | LiquidVision ONNX (LFM2-VL model) |
| Packaging | WiX MSI, DMG, DEB |

## Test Coverage

| Test Suite | Tests |
|------------|-------|
| Adam.Shared.Tests | 239 |
| Adam.BrokerService.Tests | 107 (2 skipped — Docker-dependent) |
| Adam.ServiceManager.Tests | 156 |
| Adam.CatalogBrowser.Tests | 23 |
| **Total** | **525** |

## Known Limitations

- **Loupe view (CATA-05):** The asset detail panel displays metadata and a preview placeholder but does not yet provide full-resolution image inspection with pan/zoom. This is planned for v2.
- **Compare view (CATA-06):** Side-by-side asset comparison is not yet implemented. This is planned for v2.
- **Sidebar CRUD (T8.18):** The sidebar tree is read-only in v1 — users can browse and filter but cannot create, rename, or delete tree items (collections, keywords, categories) directly from the sidebar. CRUD is available via context menus and dialog prompts.
- **Full-text search:** Uses EF Core `LIKE`-based queries rather than FTS5. Adequate for collections up to 100K assets; FTS5 optimization is planned for v2.
- **Thumbnail performance:** Current implementation works for reasonable collection sizes. Virtualization optimization is planned for v2.
- **Docker-dependent tests:** 2 BrokerService integration tests (PostgreSQL, SQL Server providers) are skipped when Docker is not available.

## System Requirements

- **OS:** Windows 10+, macOS 12+, Ubuntu 20.04+ (or equivalent Linux)
- **RAM:** 4 GB minimum, 8 GB recommended
- **Disk:** 500 MB for application + storage for assets and thumbnails
- **Display:** 1280×720 minimum, 1920×1080 recommended

## Upgrade Notes

This is the initial v1.0 release. No migration from previous versions is required.

## Acknowledgments

Built with Avalonia UI, Entity Framework Core, CommunityToolkit.Mvvm, Google.Protobuf, and SixLabors.ImageSharp.
