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

This phase finalizes the adam DAM system for a v1.0 release. It covers four work streams: performance audit of search indexes and thumbnail cache for large collections (100K+ assets), comprehensive documentation (Admin Guide + User Guide), platform-specific distribution packaging (MSI for Windows, DMG for macOS, DEB for Linux), and UI polish & interaction (context menus, delete wiring, keyboard model, and direct-manipulation affordances) derived from the Phase 8 UI audit (`08-UI-REVIEW.md`, overall 13/24, Experience Design 1/4).

**Dependencies:** All prior phases (1-7, 9) — feature-complete codebase ready for stabilization.

## 1. Requirements & Constraints

- **PERF-01**: Full-text search across all metadata fields returns results within 2 seconds at 100K assets
- **PERF-02**: Gallery loading and thumbnail display must be smooth at 100K assets with no UI freeze
- **DOC-01**: Admin Guide covers Service Manager, Broker deployment, user/role management, and database migration
- **DOC-02**: User Guide covers Catalog Browser, ingest, metadata editing, search/filter, and export
- **PKG-01**: Windows distribution as MSI installer via WiX Toolset or equivalent
- **PKG-02**: macOS distribution as DMG disk image
- **PKG-03**: Linux distribution as DEB package
- **UX-01**: Right-click context menus exist on gallery assets and sidebar tree nodes, exposing the relevant existing commands (rate, label, flag, tag, AI-tag, export, rotate/flip, delete, reveal, copy, add-to-collection)
- **UX-02**: Assets can be deleted, trashed, and restored from the UI (`DeleteService` wired to a command, confirmation, and a Trash view)
- **UX-03**: Standard DAM keyboard accelerators are available (Delete, F2, Ctrl+A/C/E/F, rating digits, flag keys)
- **UX-04**: Gallery tiles render their rating/label/flag/action affordances; destructive and async actions surface confirmation and toast feedback
- **CON-001**: No critical or high-severity bugs remaining before release
- **CON-002**: All 34 v1 requirements validated and traceable
- **PAT-001**: Use `dotnet publish` with self-contained deployment for all platforms
- **PAT-002**: Test suite must pass cleanly before any packaging step
- **PAT-003**: UI work reuses existing ViewModel commands where they already exist; new commands follow the established `CommunityToolkit.Mvvm` `[RelayCommand]` pattern

## 2. Implementation Steps

### Work Stream 1: Performance Audit

**GOAL:** Ensure the system performs acceptably at 100K assets — search <2s, gallery smooth, no UI freezes.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| T8.1 | **Benchmark baseline** — Create a `BenchmarkService` or script that ingests 100K synthetic assets with varied metadata (keywords, categories, EXIF), then measures search query times (full-text, filtered, sorted) and gallery load times. Consider `BenchmarkDotNet` for rigorous micro-benchmarks or a stopwatch-based console app for scenario-level measurements. Seed the 100K DB once and reuse across runs to avoid regenerating per execution. | ✅ | 2026-06-09 |
| T8.2 | **Search index audit** — Review EF Core query plans for all `DigitalAssets` queries (sidebar filters, keyword search, date range). Add missing composite indexes for common filter combinations (e.g., `Type + MimeType + FileName`, `CreatedAt + Type`). | ✅ | 2026-06-09 |
| T8.3 | **Full-text search optimization** — Investigate SQLite FTS5 virtual table for full-text search across Title, Description, FileName, and keyword names; for PostgreSQL leverage `tsvector`/`tsquery`. **Exit (binary):** either FTS5 implemented with triggers keeping it in sync AND benchmark shows full-text search <2s at 100K assets (PERF-01), OR a documented decision to use the `LIKE`-based fallback per ALT-001 with the measured numbers justifying it. | | |
| T8.4 | **Thumbnail cache optimization** — Profile `ThumbnailService.GenerateThumbnailAsync` for memory/CPU hot spots. **Exit (binary):** a recorded before/after profiling result naming the chosen optimization(s) from {lazy-load via virtualized list, batch pre-generation on ingest, DB-cached thumbnail metadata to drop repeated `File.Exists`} AND gallery scroll stays smooth (no UI freeze) at 100K per PERF-02. | | |
| T8.5 | **Gallery virtualized scrolling** — Verify `VirtualizingStackPanel` (ListView mode) handles 10K+ items without memory pressure; add on-demand thumbnail loading as items scroll into view. **Exit (binary):** measured working-set stays bounded while scrolling a 10K-item gallery (no linear growth), OR the page-based "Load More" fallback per RISK-002 is implemented; result recorded. | | |
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

### Work Stream 4: UI Polish & Interaction

**GOAL:** Close the interaction-completeness gaps found in the Phase 8 UI audit (`08-UI-REVIEW.md`) — right-click context menus, reachable delete, keyboard model, and direct-manipulation affordances. Priority tags: **[BLOCK]** = v1.0-blocking, **[HIGH]** = expected DAM affordance, **[POLISH]** = visual/consistency debt (deferrable to v2 if scope tightens).

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| T8.15 | **[HIGH] Feedback components** — Reusable confirmation dialog + transient toast/notification surface. None exist today (audit Pillar 6 / table F); prerequisite for safe delete and async feedback (AI-tag, export, ingest, delete). | | |
| T8.16 | **[BLOCK] Gallery asset context menu + per-asset commands** — Add `ContextFlyout` to gallery items (`AssetGalleryView.axaml:113-189`). AI-Tag (`AiTagSelectedCommand`), Export (`ExportCommand`), Rotate/Flip already exist and only need an affordance. **Rate / Set color label / Set flag exist ONLY as right-panel combos, not as commands (audit table A)** — this task must create per-asset `RateCommand`/`SetLabelCommand`/`SetFlagCommand` on the gallery VM (multi-select variants land in T8.22). Menu wiring of Delete/Reveal/Copy/Add-to-collection entries depends on T8.17 and T8.19 (see Execution Waves). | | |
| T8.17 | **[BLOCK] Wire up Delete** — `DeleteService` (`Services/DeleteService.cs`, DI-registered `App.axaml.cs:77`, tested) is referenced by no UI. Add `DeleteSelectedCommand` + confirmation (T8.15) + `Delete` key + context-menu entry, and a Trash/Deleted view surfacing `GetDeletedAssetsAsync`/`RestoreAsync`/`PermanentlyDeleteAsync`. | | |
| T8.18 | **[HIGH] Sidebar tree context menus + CRUD** — New/Rename/Delete + inline rename for Collections, Keywords, Categories (`MainWindow.axaml:217-292`, `SearchableTreeView.axaml`). These commands do NOT exist in `SidebarViewModel` today — add the commands (and broker-side handlers for multi-user mode). Also (audit table B): a **Folders** context menu (Reveal in Explorer, Re-scan folder) and explicit **"Filter by this" / "Clear filter"** entries on tree nodes (filter is implicit-on-selection today). Largest item; candidate to split into its own slice if scope tightens (ALT-005). | | |
| T8.19 | **[HIGH] New asset commands** — Reveal-in-folder / Open (`Process.Start` on `StoragePath`), Copy path / Copy file (no clipboard usage exists today), Add-to-collection (collections are currently read-only in the UI). Surfaced via T8.16 context menu (table A). | | |
| T8.20 | **[BLOCK] Bind tile affordances** — `AssetTileControl` exposes Rating/ColorLabel/ColorBrush/IsFlagged/ToolbarActions and the template draws them (`AssetTileControlStyles.axaml:43-122`), but the gallery binds only thumbnail + 3 text fields (`AssetGalleryView.axaml:115-124`), so every tile shows empty slots. Bind these + add hover-reveal action overlay (table D). | | |
| T8.21 | **[HIGH] Keyboard model** — Extend `Window.KeyBindings` (only `Ctrl+S`×2 today) with Delete, F2 rename, Ctrl+A/C/E/F, Enter open, rating digits 0–5, P/X flag (table C). | | |
| T8.22 | **[HIGH] Bulk actions** — `SelectionMode="Multiple"` + `SelectedAssets` exist but only AI-tag/Export consume them. Add bulk rate / label / flag / delete / keyword across multi-select (table E). | | |
| T8.23 | **[POLISH] Discoverability** — Tooltips on icon-only buttons (rotate/flip glyphs `MainWindow.axaml:481-484`, AI-tag emoji); drop-target highlight for keyword/category drag-drop assignment (table D). | | |
| T8.24 | **[POLISH] Color palette resources** — Extract a shared `ResourceDictionary` palette (60+ hardcoded hex literals today; blocks theming/dark mode) and rein in competing accents (audit Pillar 3). | | |
| T8.25 | **[POLISH] Type scale + spacing tokens** — Collapse 8+ ad-hoc font sizes into ≤4 `TextBlock` style classes; introduce spacing tokens for the 4/8/12/16 rhythm (audit Pillars 4 & 5). | | |
| T8.26 | **[POLISH] Copywriting cleanup** — Dedupe empty-state strings, replace bare "Clear"/"Refresh", standardize Save labels (audit Pillar 1). | | |

### Work Stream 5: Release Stabilization

**GOAL:** Satisfy the release gate CON-001 — zero Critical/High-severity bugs remaining.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| T8.27 | **[BLOCK] Critical/High bug triage & bug-bash** — Dedicated stabilization pass that owns CON-001 (passing the test suite proves regression safety, not absence of High-severity defects). Triage open issues + carried tech debt (`X509Certificate2` SYSLIB0057 obsolete-ctor warnings; CatalogBrowser CI test timeouts), run a manual bug-bash across the golden paths exercised by the new UI work (delete/trash, context menus, bulk actions), and record results. **Exit:** a triage log shows zero open Critical or High items before any packaging task (T8.10–T8.14) ships. | | |

### Execution Waves

Sequencing commitment (resolves the menu-before-commands inversion and de-risks the v1.0 date). Later waves are blocked on earlier ones only where noted.

| Wave | Tasks | Rationale |
|------|-------|-----------|
| **Wave 1 — UI blockers** | T8.15 → (T8.16 per-asset commands, T8.17, T8.19) → T8.16 menu wiring; T8.20 | T8.15 (confirmation/toast) precedes T8.17 (delete needs confirmation). Delete (T8.17) and reveal/copy/add-to-collection (T8.19) commands must exist before the T8.16 `ContextFlyout` can wire their entries. T8.20 (tile bindings) is independent. |
| **Wave 2 — UX (HIGH)** | T8.18, T8.21, T8.22 | Sidebar CRUD, keyboard model, bulk actions. T8.22 reuses the per-asset rate/label/flag commands from T8.16. T8.18 may split out per ALT-005. |
| **Wave 3 — Performance + Docs** | T8.3–T8.6, T8.7–T8.9 | Independent of the UI streams; can run in parallel with Waves 1–2. |
| **Wave 4 — Stabilization gate + POLISH** | T8.27; POLISH T8.23–T8.26 (defer-eligible per ALT-006) | CON-001 triage runs after all features land (Waves 1–3). Gates Wave 5. |
| **Wave 5 — Packaging & release** | T8.10–T8.14 | Runs only after T8.27 clears CON-001 (zero Critical/High) AND PAT-002 (full suite green). This keeps the W4 release gate and the wave invariant consistent — packaging is strictly downstream of stabilization. |

## 3. Alternatives

- **ALT-001**: Skip FTS5 and rely on EF Core `Contains()` / `LIKE` queries for full-text search. (Rejected if benchmark shows >2s at 100K — FTS5 is the standard SQLite approach for full-text search.)
- **ALT-002**: Use Squirrel.Windows or MSIX instead of WiX MSI. (WiX chosen for maturity and fine-grained control over install layout; MSIX may be evaluated for Windows Store distribution in v2.)
- **ALT-003**: Use .NET单文件发布 instead of directory-based publish. (Single-file may cause issues with native dependency loading for SkiaSharp/ImageSharp; evaluate per-project.)
- **ALT-004**: Skip CI/CD automation and package manually. (Rejected — CI/CD is essential for reproducible builds and release management.)
- **ALT-005**: Split sidebar CRUD (T8.18) into its own dedicated phase/slice rather than Phase 8. (Open decision — it is the largest UI item and touches the broker/multi-user path; keep in Phase 8 unless it threatens the v1.0 ship date.)
- **ALT-006**: Defer all [POLISH] tasks (T8.23–T8.26) to v2. (Acceptable fallback if scope tightens — they are visual/consistency debt, not interaction blockers.)

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
| `src/Adam.CatalogBrowser/Views/AssetGalleryView.axaml` | Add `ContextFlyout`, bind tile affordances (T8.16, T8.20) |
| `src/Adam.CatalogBrowser/Views/MainWindow.axaml` | Sidebar context menus, key bindings, tooltips (T8.18, T8.21, T8.23) |
| `src/Adam.CatalogBrowser/Controls/SearchableTreeView.axaml` | Tree-node context menus + inline rename (T8.18) |
| `src/Adam.CatalogBrowser/ViewModels/SidebarViewModel.cs` | New CRUD commands for collections/keywords/categories (T8.18) |
| `src/Adam.CatalogBrowser/ViewModels/AssetGalleryViewModel.cs` | `DeleteSelectedCommand`, bulk actions, reveal/copy/add-to-collection (T8.17, T8.19, T8.22) |
| `src/Adam.CatalogBrowser/Services/DeleteService.cs` | Existing — wire to UI (T8.17) |
| `src/Adam.CatalogBrowser/Views/` (new) | Confirmation dialog + toast/notification components, Trash view (T8.15, T8.17) |

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
- `.planning/phases/08-v1-0-polish-ship/08-UI-REVIEW.md` — Phase 8 UI audit (6-pillar, 13/24) backing Work Stream 4
- `src/Adam.Shared/Data/AppDbContext.cs` — Index definitions for performance audit
- `src/Adam.Shared/Services/ThumbnailService.cs` — Thumbnail cache service
