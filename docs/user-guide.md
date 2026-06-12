# Adam User Guide

> **Version:** 1.0 — Covers the `Adam.CatalogBrowser` application.

This guide is for end-users browsing, searching, and managing digital assets with the Adam Catalog Browser. If you are a system administrator deploying the service, see the [Admin Guide](admin-guide.md).

---

## Table of Contents

1. [Getting Started](#1-getting-started)
2. [Gallery View](#2-gallery-view)
3. [Ingesting Assets](#3-ingesting-assets)
4. [Browsing & Filtering](#4-browsing--filtering)
5. [Metadata Editing](#5-metadata-editing)
6. [Right-Click Context Menu](#6-right-click-context-menu)
7. [Keyboard Shortcuts](#7-keyboard-shortcuts)
8. [Exporting Assets](#8-exporting-assets)
9. [AI Image Tagging](#9-ai-image-tagging)
10. [Trash & Recovery](#10-trash--recovery)
11. [Connecting to a Service](#11-connecting-to-a-service)
12. [Settings & Configuration](#12-settings--configuration)

---

## 1. Getting Started

Adam Catalog Browser is a cross-platform desktop application for browsing, searching, and managing digital assets. It runs in two modes:

| Mode | Description |
|------|-------------|
| **Local (Standalone)** | Direct access to a local SQLite database. No server required. All permissions granted. |
| **Service (Multi-User)** | Connect to a shared Broker Service for collaborative access with role-based permissions. |

On first launch, the app starts in **Local mode** and presents an empty gallery. Use the **Ingest** tab to add your first assets.

---

## 2. Gallery View

The gallery is the main view for browsing your asset collection.

### View Modes

- **Grid View** — Thumbnail tiles showing image previews, file name, type, and size. Each tile displays the asset's rating, color label, and flag status.
- **List View** — Compact rows with thumbnail, file name, type, size, dimensions, and date added.

Toggle between views using the **View** dropdown in the toolbar.

### Thumbnail Size

Use the **slider** in the toolbar to adjust thumbnail size from 80px to 300px.

### Sorting

Use the **Sort** dropdown to reorder assets:

| Sort Option | Description |
|-------------|-------------|
| File Name | Alphabetical by file name (default) |
| Date Added | Newest first |
| File Type | Grouped by MIME type |
| File Size | Smallest first |

### Multi-Select

Click and drag to rubber-band select, or hold **Ctrl+Click** to select multiple assets. Selected assets appear with a blue highlight. Operations like export, delete, rate, and AI tag apply to all selected assets.

### Tile Affordances

Each tile in Grid View shows:

- **Rating** — Star rating indicator (0–5 stars)
- **Color Label** — Colored swatch (Red, Green, Blue, Yellow, Purple)
- **Flag** — Flag icon when the asset is flagged (Pick or Reject)
- **Toolbar Actions** — Quick-action buttons (★ rate, ⚑ flag) that appear on hover

---

## 3. Ingesting Assets

The **Ingest** tab lets you add files to your catalog.

### Adding Files

Three ways to add files:

1. **Select Files** — Opens a file picker to choose individual files
2. **Select Folder** — Opens a folder picker to scan an entire directory
3. **Drag and Drop** — Drag files or folders directly onto the ingest area

### Supported File Types

Adam recognizes images (JPEG, PNG, TIFF, WebP, BMP, GIF, HEIC, RAW formats), videos, audio, and documents. Unsupported files are skipped during ingestion with a validation message.

### Ingestion Process

1. Added files appear in the **Pending Files** list
2. Click **Start Ingestion** to begin processing
3. A progress bar shows current file, count, and estimated time remaining
4. For each file, Adam:
   - Validates the file type and size
   - Computes a SHA-256 checksum (duplicates are automatically skipped)
   - Generates a thumbnail
   - Extracts EXIF/metadata (camera, lens, date taken, GPS, etc.)
   - Reads embedded keywords and categories from file metadata (XMP)
5. On completion, a summary shows ingested/skipped/error counts

### AI Tagging Opt-In

Check the **AI Tag** checkbox before ingesting to automatically tag images using local on-device AI inference. This runs as a sequential post-pass after the parallel ingest completes.

### Already Ingested Folders

The **Ingested Folders** tree shows all directories that contain cataloged assets. Click a folder to filter the gallery to assets from that directory.

---

## 4. Browsing & Filtering

The **left sidebar** provides multiple ways to filter the gallery. Click any sidebar item to filter; click again or click a different section to change the filter.

### Folders

A tree view of all ingested folder paths. Click a folder to show only assets from that directory (and its subdirectories).

### Collections

A tree view of asset collections. Collections are logical groupings — an asset can belong to multiple collections.

### Keywords

A searchable tree of all keywords in the catalog. Keywords are extracted from file metadata (XMP) and can be added via the metadata editor. Click a keyword to filter the gallery to assets tagged with that keyword.

**Drag and Drop:** Select assets in the gallery and drag them onto a keyword in the sidebar to assign that keyword to all selected assets.

### Media Format

Filter by asset type:

| Format | Description |
|--------|-------------|
| Images | JPEG, PNG, TIFF, WebP, etc. |
| Videos | MP4, MOV, AVI, etc. |
| Documents | PDF, DOCX, etc. |
| Audio | MP3, WAV, etc. |

### Categories

A searchable tree of all categories. Categories are broader groupings than keywords (e.g., "Nature", "Architecture"). Click a category to filter.

**Drag and Drop:** Select assets in the gallery and drag them onto a category to assign that category.

### Date Taken

A tree view grouped by year and month, based on the EXIF Date Taken field. Click a year or month to filter assets by when they were photographed.

---

## 5. Metadata Editing

Select a single asset in the gallery to view and edit its metadata in the **right panel**.

### Editable Fields

| Field | Description |
|-------|-------------|
| **Description** | Free-text description of the asset |
| **Date Taken** | Date picker for the capture date |
| **Categories** | Tag editor — add/remove categories with autocomplete |
| **Keywords** | Tag editor — add/remove keywords with autocomplete |
| **Rating** | 0–5 star rating (combo box) |
| **Color Label** | None, Red, Green, Blue, Yellow, Purple |
| **Flag** | Unflagged, Pick, Reject |
| **Copyright** | Free-text copyright notice |
| **GPS** | Latitude and longitude coordinates |

### Read-Only Metadata

The right panel also displays (read-only):

- Camera Make & Model
- Lens Model
- Focal Length, Aperture, Exposure Time, ISO
- Flash status
- Headline

### Saving Changes

Click **Save Changes** (or press **Ctrl+S**) to persist edits. Changes are written to the database and, for supported formats, written back to the file's XMP metadata.

### Image Adjustments

When a single image is selected, the right panel shows rotation and flip controls:

- **↻ 90°** — Rotate 90° clockwise
- **↺ 90°** — Rotate 90° counter-clockwise
- **⇄** — Flip horizontal
- **⇅** — Flip vertical

After rotation/flip, the thumbnail is regenerated and the change is saved to the database.

### Multi-Asset Editing

When multiple assets are selected, the tag editor shows **aggregated categories and keywords** across all selected assets. Tags shown in bold appear on all selected assets; tags shown in normal weight appear on some. Editing applies changes to all selected assets.

---

## 6. Right-Click Context Menu

Right-click any asset in the gallery to open the context menu with quick actions:

| Menu Item | Action |
|-----------|--------|
| **Rate (cycle)** | Cycles rating: 0 → 1 → 2 → 3 → 4 → 5 → 0. Applies to all selected assets. |
| **Set Color Label** | Cycles label: None → Red → Green → Blue → Yellow → Purple → None. Applies to all selected. |
| **Set Flag** | Cycles flag: Unflagged → Pick → Reject → Unflagged. Applies to all selected. |
| **AI Tag** | Runs local AI inference to add keywords and categories. (Images only.) |
| **Export…** | Opens the export dialog for the selected assets. |
| **Rotate 90° CW** | Rotates 90° clockwise. |
| **Rotate 90° CCW** | Rotates 90° counter-clockwise. |
| **Flip Horizontal** | Flips the image horizontally. |
| **Flip Vertical** | Flips the image vertically. |
| **Reveal in Folder** | Opens the file explorer to the asset's location. |
| **Copy File Path** | Copies the full file path to the clipboard. |
| **Copy File** | Copies the file path with filename confirmation. |
| **Delete…** | Soft-deletes the asset (moves to Trash). Confirmation required. |

---

## 7. Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| **Delete** | Delete selected assets (moves to Trash) |
| **Ctrl+A** | Select all assets in the gallery |
| **Ctrl+C** | Copy file path of first selected asset to clipboard |
| **Ctrl+E** | Open Export dialog |
| **Ctrl+S** | Save metadata changes |
| **Ctrl+Shift+T** | Open Trash view |
| **0** through **5** | Set rating on selected assets (0 = clear, 1–5 = star rating) |

---

## 8. Exporting Assets

Select one or more assets and click **Export…** (or press **Ctrl+E**) to open the export dialog.

### Export Options

| Option | Description |
|--------|-------------|
| **Destination Folder** | Where exported files will be saved |
| **Format** | JPEG or TIFF |
| **Quality** (JPEG) | Compression quality, 1–100 (default: 85) |
| **Compression** (TIFF) | None (uncompressed) or LZW (lossless) |
| **Max Dimension** | Optional — resize so the longest edge is no more than this many pixels |

Exported files are saved with `_export` appended to the filename (e.g., `photo_export.jpg`).

---

## 9. AI Image Tagging

Adam includes built-in AI image tagging that runs entirely on your local device — no cloud service required.

### How It Works

The AI model (LFM2-VL) analyzes images and generates:

- **Keywords** — Descriptive tags (e.g., "landscape", "sunset", "mountain")
- **Categories** — Broader classifications (e.g., "Nature", "Architecture")
- **Description** — A text description (only fills empty descriptions)

Keywords and categories are **unioned** with existing tags — your manually added tags are never removed.

### Trigger Modes

| Mode | When | How |
|------|------|-----|
| **Ingest opt-in** | During file ingestion | Check the "AI Tag" checkbox on the Ingest tab before starting |
| **Per-asset Auto-tag** | On a single selected asset | Click the **Auto Tag** button in the Metadata Editor (right panel) |
| **Bulk re-tag** | On multiple selected assets | Click the **🤖 AI Tag Selected** button in the gallery toolbar |

### Model Download

On first use, the AI model (~2 GB) downloads automatically. A progress indicator appears in the status bar. Once downloaded, the model is cached locally.

---

## 10. Trash & Recovery

Deleted assets are soft-deleted — they are removed from the gallery but retained in the database and can be restored.

### Accessing Trash

- Press **Ctrl+Shift+T**, or
- Use the context menu → Delete, or
- The Trash view shows all soft-deleted assets

### Restoring Assets

1. Open the Trash view
2. Select one or more deleted assets
3. Click **Restore** — assets return to the gallery with all metadata intact

### Permanent Deletion

1. Open the Trash view
2. Select assets to permanently delete
3. Click **Permanently Delete**
4. Confirm the action — this cannot be undone

---

## 11. Connecting to a Service

In multi-user mode, multiple users connect to a shared database through the Broker Service.

### Connecting

1. In the title bar, click **Service** to switch to service mode
2. Enter the server **Host** and **Port**
3. Click **Connect**
4. Enter your **Username** and **Password**
5. Click **Login**

### Connection Status

- 🟢 **Green dot** — Connected and authenticated
- 🔴 **Red dot** — Connected but not authenticated
- ⚪ **Gray dot** — Disconnected

The status bar shows your current session: username, role, or "Not logged in".

### Logging Out

Click **Logout** to end your session. Your local changes are preserved.

### Disconnecting

Click **Disconnect** to close the connection to the Broker Service and return to local mode.

### Permission-Aware UI

When connected to a service, the UI adapts to your role:

| Role | Permissions |
|------|-------------|
| **Viewer** | Browse and search assets. Cannot edit, ingest, or export. |
| **Editor** | Browse, search, ingest, edit metadata, and export. |
| **Administrator** | All Editor permissions plus user management and audit log access. |

Disabled controls show a tooltip explaining why they are locked (e.g., "Requires Editor or Administrator role").

---

## 12. Settings & Configuration

### Client Settings

Settings are stored in a JSON file:

| Platform | Location |
|----------|----------|
| Windows | `%LOCALAPPDATA%/Adam/CatalogBrowser/settings.json` |
| Linux | `~/.local/share/Adam/CatalogBrowser/settings.json` |
| macOS | `~/Library/Application Support/Adam/CatalogBrowser/settings.json` |

### Configurable Settings

| Setting | Description | Default |
|---------|-------------|---------|
| `Mode` | `Local` or `MultiUser` | `Local` |
| `ServiceHost` | Broker Service hostname | `localhost` |
| `ServicePort` | Broker Service port | `9100` |
| `LastUsername` | Remembered username for login | — |
| `RecentHosts` | List of recently connected servers | — |

### XMP Write-Back

When you edit metadata (title, description, keywords, rating, etc.), Adam writes the changes back to the file's XMP metadata:

- **JPEG, PNG, TIFF, WebP** — Embedded XMP in the file header
- **RAW files** (CR2, NEF, ARW, DNG) — Separate `.xmp` sidecar file next to the original

---

> **Next:** [Admin Guide](admin-guide.md) — How to deploy and manage the Broker Service.
