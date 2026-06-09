---
goal: Final stabilization, performance optimization, documentation, and distribution packaging for v1.0 release.
version: 1.0
date_created: 2026-06-09
last_updated: 2026-06-09
status: 'Planned'
tags: [performance, documentation, packaging, release]
---

# Phase 8: v1.0 Polish & Ship

![Status: Planned](https://img.shields.io/badge/status-Planned-blue)

This phase finalizes the adam DAM system for a v1.0 release. It covers three work streams: performance audit of search indexes and thumbnail cache for large collections (100K+ assets), comprehensive documentation (Admin Guide + User Guide), and platform-specific distribution packaging (MSI for Windows, DMG for macOS, DEB for Linux).

**Dependencies:** All prior phases (1-7, 9) — feature-complete codebase ready for stabilization.

## 1. Requirements & Constraints

- **PERF-01**: Full-text search across all metadata fields returns results within 2 seconds at 100K assets
- **PERF-02**: Gallery loading and thumbnail display must be smooth at 100K assets with no UI freeze
- **DOC-01**: Admin Guide covers Service Manager, Broker deployment, user/role management, and database migration
- **DOC-02**: User Guide covers Catalog Browser, ingest, metadata editing, search/filter, and export
- **PKG-01**: Windows distribution as MSI installer via WiX Toolset or equivalent
- **PKG-02**: macOS distribution as DMG disk image
- **PKG-03**: Linux distribution as DEB package
- **CON-001**: No critical or high-severity bugs remaining before release
- **CON-002**: All 34 v1 requirements validated and traceable
- **PAT-001**: Use `dotnet publish` with self-contained deployment for all platforms
- **PAT-002**: Test suite must pass cleanly before any packaging step

## 2. Implementation Steps

### Work Stream 1: Performance Audit

**GOAL:** Ensure the system performs acceptably at 100K assets — search <2s, gallery smooth, no UI freezes.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| T8.1 | **Benchmark baseline** — Create a `BenchmarkService` or script that ingests 100K synthetic assets with varied metadata (keywords, categories, EXIF), then measures search query times (full-text, filtered, sorted) and gallery load times. Consider `BenchmarkDotNet` for rigorous micro-benchmarks or a stopwatch-based console app for scenario-level measurements. Seed the 100K DB once and reuse across runs to avoid regenerating per execution. | | |
| T8.2 | **Search index audit** — Review EF Core query plans for all `DigitalAssets` queries (sidebar filters, keyword search, date range). Add missing composite indexes for common filter combinations (e.g., `Type + MimeType + FileName`, `CreatedAt + Type`). | | |
| T8.3 | **Full-text search optimization** — Investigate SQLite FTS5 virtual table for full-text search across Title, Description, FileName, and keyword names. If feasible, implement FTS5 with triggers keeping it in sync. For PostgreSQL, leverage `tsvector`/`tsquery`. | | |
| T8.4 | **Thumbnail cache optimization** — Profile `ThumbnailService.GenerateThumbnailAsync` for memory/CPU hot spots. Consider: lazy-loading thumbnails with virtualized `ListBox`, pre-generating thumbnails on ingest in batches, caching thumbnail metadata in DB to avoid repeated `File.Exists` checks. | | |
| T8.5 | **Gallery virtualized scrolling** — Verify `VirtualizingStackPanel` in ListView mode handles 10K+ items without memory pressure. Add `AsyncImageLoader` pattern to load thumbnails on-demand as items scroll into view. | | |
| T8.6 | **Startup time optimization** — Profile app startup (DI container resolution, `ModeManager.InitializeAsync`, sidebar loading). Implement lazy initialization for non-critical services. Target <3s cold start. | | |

### Work Stream 2: Final Documentation

**GOAL:** Complete Admin Guide and User Guide covering all v1 features.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| T8.7 | **Admin Guide** — Create `docs/admin-guide.md` covering: Service Manager overview, multi-user mode setup (host/port, TLS), user & role lifecycle (add/edit/deactivate), database migration wizard (SQLite → PostgreSQL/SQL Server), Broker service installation (Windows SCM, macOS launchd, Linux systemd), audit log interpretation, firewall configuration, and troubleshooting. | | |
| T8.8 | **User Guide** — Create `docs/user-guide.md` covering: Catalog Browser overview, standalone vs multi-user mode, folder selection and ingest, gallery navigation (grid/list), metadata editing (tags, ratings, labels, flags, GPS, copyright), search and filtering, XMP write-back and sidecars, image adjustments (rotate/flip), export (JPEG/TIFF), AI tagging, and collections. | | |
| T8.9 | **Requirements validation** — Audit all 34 v1 requirements against implemented features. Update `REQUIREMENTS.md` traceability table to mark all checked boxes. Create `docs/v1-release-notes.md` with feature summary and known limitations. | | |

### Work Stream 3: Distribution Packaging

**GOAL:** Create platform-specific installers for Windows, macOS, and Linux.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| T8.10 | **Self-contained publish** — Create `scripts/publish.ps1` (or cross-platform script) that runs `dotnet publish -r <rid> --self-contained` for all three projects (`CatalogBrowser`, `ServiceManager`, `BrokerService`) targeting `win-x64`, `osx-x64`, `linux-x64`. Configure trim mode, ready-to-run, and single-file where appropriate. | | |
| T8.11 | **Windows MSI package** — Set up WiX Toolset project or use `dotnet msbuild` with built-in `PublishSingleFile` + `PublishReadyToRun`. Configure Start Menu shortcut, file association for `.adam` files (if applicable), and uninstaller. | | |
| T8.12 | **macOS DMG package** — Create `create-dmg` script that bundles the `.app` bundle (via `dotnet publish -o Adam.app`) into a compressed DMG. Configure code signing placeholder (`hardened-runtime` entitlements). | | |
| T8.13 | **Linux DEB package** — Create `scripts/build-deb.sh` that structures the publish output into DEB layout (`usr/lib/adam/`, `usr/share/applications/adam.desktop`, `usr/bin/adam` symlink). Use `dpkg-deb` for packaging. | | |
| T8.14 | **CI/CD integration** — Add GitHub Actions workflow (`.github/workflows/release.yml`) that runs on tag push (`v*`): builds all three platforms, runs full test suite, creates MSI/DMG/DEB artifacts, and uploads to GitHub Releases. | | |

## 3. Alternatives

- **ALT-001**: Skip FTS5 and rely on EF Core `Contains()` / `LIKE` queries for full-text search. (Rejected if benchmark shows >2s at 100K — FTS5 is the standard SQLite approach for full-text search.)
- **ALT-002**: Use Squirrel.Windows or MSIX instead of WiX MSI. (WiX chosen for maturity and fine-grained control over install layout; MSIX may be evaluated for Windows Store distribution in v2.)
- **ALT-003**: Use .NET单文件发布 instead of directory-based publish. (Single-file may cause issues with native dependency loading for SkiaSharp/ImageSharp; evaluate per-project.)
- **ALT-004**: Skip CI/CD automation and package manually. (Rejected — CI/CD is essential for reproducible builds and release management.)

## 4. Dependencies

| Dependency | Version | Purpose |
|------------|---------|---------|
| `WiX Toolset` | 5.x | Windows MSI packaging |
| `create-dmg` | latest | macOS DMG creation |
| `dpkg-deb` | system | Linux DEB packaging |
| `dotnet publish` | .NET 10 | Self-contained framework-dependent publish |
| GitHub Actions | — | CI/CD pipeline for multi-platform builds |

## 5. Files

| File | Role |
|------|------|
| `src/Adam.Shared/Data/AppDbContext.cs` | Index definitions to audit/optimize (lines 57-73) |
| `src/Adam.Shared/Services/ThumbnailService.cs` | Thumbnail cache hot path to profile |
| `src/Adam.Shared/Services/ModeManager.cs` | Startup path to profile (lines 160-165: manual index creation) |
| `docs/admin-guide.md` | New — Admin Guide |
| `docs/user-guide.md` | New — User Guide |
| `scripts/publish.ps1` | New — Cross-platform publish script |
| `scripts/build-deb.sh` | New — Linux DEB builder |
| `.github/workflows/release.yml` | New — CI/CD release pipeline |
| `*.wixproj` | New — WiX MSI project |

## 6. Testing

| Test | Type | Command |
|------|------|---------|
| Search benchmark | Manual/script | `dotnet run --project scripts/benchmark/Benchmark.csproj` (new) |
| Full test suite | Automated | `dotnet test` |
| Package verification | Manual | Install MSI/DMG/DEB on each platform, verify launch and basic functionality |
| Startup time | Manual | Measure cold-start with `dotnet run -c Release` |

**Quick command:** `dotnet test`

**Expected results:** All existing 383+ tests pass (plus 2 Docker-dependent skipped); benchmark shows <2s search at 100K; packages install and launch cleanly on all 3 platforms.

## 7. Risks & Assumptions

- **RISK-001**: SQLite FTS5 may not be available on all platforms (requires ICU collation). Mitigation: add `SQLitePCLRaw.bundle_e_sqlite3` NuGet dependency or use `LIKE`-based fallback.
- **RISK-002**: Avalonia's `VirtualizingStackPanel` may not perform well with the current `AssetListItem` data template. Mitigation: fall back to page-based loading (load 50 at a time, "Load More" button) if virtualization underperforms.
- **RISK-003**: WiX Toolset requires Windows SDK and may not be available in CI (Linux/macOS runners). Mitigation: use `dotnet msbuild` with built-in packaging on Windows runner only; cross-platform artifacts can be built on separate runners.
- **RISK-004**: macOS code signing requires an Apple Developer account. Mitigation: document signing as a manual step; provide unsigned DMG for testing, signed DMG for production distribution.
- **ASSUMPTION-001**: The 100K asset benchmark is achievable on consumer hardware (16GB RAM, SSD). Performance targets are for this class of hardware.
- **ASSUMPTION-002**: Users will install via platform-native package managers (Windows: MSI, macOS: DMG, Linux: apt/dpkg). Snap/Flatpak packaging is deferred to v2.

## 8. Related Specifications / Further Reading

- `.planning/REQUIREMENTS.md` — All 34 v1 requirements
- `.planning/ROADMAP.md` — Phase 8 deliverables and success criteria
- `.planning/STATE.md` — Current project state
- `.planning/milestones/v1.1-audit-report.md` — Previous milestone audit
- `src/Adam.Shared/Data/AppDbContext.cs` — Index definitions for performance audit
- `src/Adam.Shared/Services/ThumbnailService.cs` — Thumbnail cache service
