---
goal: Final stabilization, performance optimization, documentation, and distribution packaging for v1.0 release.
version: 1.0
date_created: 2026-06-09
last_updated: 2026-06-12
status: 'In Progress'
tags: [performance, documentation, packaging, release, ui-polish]
---

# Phase 8: v1.0 Polish & Ship

![Status: In Progress](https://img.shields.io/badge/status-In%20Progress-orange)

This phase finalizes the adam DAM system for a v1.0 release. It covers four active work streams: documentation (Admin Guide + User Guide), platform-specific distribution packaging (MSI for Windows, DMG for macOS, DEB for Linux), UI polish & interaction (context menus, delete wiring, keyboard model, and direct-manipulation affordances), and release stabilization.

**Deferred to v2:** Sidebar CRUD (T8.18), FTS5 full-text search (T8.3), thumbnail/virtualization performance optimization (T8.4-T8.6).

**Dependencies:** All prior phases (1-7, 9) — feature-complete codebase ready for stabilization.

---

## Scope Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Sidebar CRUD (T8.18) | **Defer to v2** | Largest UI item, touches broker/multi-user path. Sidebar is read-only in v1 — users can browse/filter but not create/edit/delete tree items. |
| FTS5 full-text search (T8.3) | **Defer to v2** | LIKE-based search is adequate for v1. Benchmark (T8.1) and indexes (T8.2) provide baseline. FTS5 can be added later without breaking changes. |
| Thumbnail/virtualization perf (T8.4-T8.6) | **Defer to v2** | Current implementation works for reasonable collection sizes. Optimization is not v1-blocking. |
| POLISH tasks (T8.23-T8.26) | **Execute in Phase 8** | Visual/consistency debt — color palette, type scale, tooltips, copywriting. Improves perceived quality. |

---

## 1. Requirements & Constraints

- **PERF-01**: Full-text search across all metadata fields returns results within 2 seconds at 100K assets
- **PERF-02**: Gallery loading and thumbnail display must be smooth at 100K assets with no UI freeze
- **DOC-01**: Admin Guide covers Service Manager, Broker deployment, user/role management, and database migration
- **DOC-02**: User Guide covers Catalog Browser, ingest, metadata editing, search/filter, and export
- **PKG-01**: Windows distribution as MSI installer via WiX Toolset or equivalent
- **PKG-02**: macOS distribution as DMG disk image
- **PKG-03**: Linux distribution as DEB package
- **UX-01**: Right-click context menus exist on gallery assets and sidebar tree nodes, exposing the relevant existing commands
- **UX-02**: Assets can be deleted, trashed, and restored from the UI
- **UX-03**: Standard DAM keyboard accelerators are available (Delete, F2, Ctrl+A/C/E/F, rating digits, flag keys)
- **UX-04**: Gallery tiles render their rating/label/flag/action affordances
- **CON-001**: No critical or high-severity bugs remaining before release
- **CON-002**: All 34 v1 requirements validated and traceable
- **PAT-001**: Use `dotnet publish` with self-contained deployment for all platforms
- **PAT-002**: Test suite must pass cleanly before any packaging step
- **PAT-003**: UI work reuses existing ViewModel commands where they already exist

---

## 2. Implementation Steps

### Work Stream 1: UI Polish & Interaction

**GOAL:** Close the interaction-completeness gaps found in the Phase 8 UI audit (`08-UI-REVIEW.md`) — right-click context menus, reachable delete, keyboard model, and direct-manipulation affordances.

| Task | Description | Priority | Status | Date |
|------|-------------|----------|--------|------|
| T8.15 | **Feedback components** — Reusable confirmation dialog + transient toast/notification surface. | BLOCK | ✅ | 2026-06-12 |
| T8.16 | **Gallery asset context menu + per-asset commands** — `ContextFlyout` via code-behind (`OnContextRequested` + `BuildContextMenu`). | BLOCK | ✅ | 2026-06-12 |
| T8.17 | **Wire up Delete** — `DeleteSelectedCommand` + confirmation dialog + Trash/Deleted view. | BLOCK | ✅ | 2026-06-12 |
| T8.19 | **New asset commands** — Reveal-in-folder, Copy path, Copy file, Add-to-collection. | HIGH | ✅ | 2026-06-12 |
| T8.20 | **Bind tile affordances** — Rating, ColorLabel, ColorBrush, IsFlagged, ToolbarActions bound. | BLOCK | ✅ | 2026-06-12 |
| T8.21 | **Keyboard model** — Delete, Ctrl+A/C/E, Ctrl+Shift+T, D0–D5 rating digits. | HIGH | ✅ | 2026-06-12 |
| T8.22 | **Bulk actions** — Rate/label/flag/key operate on ALL selected assets, single DB round-trip, in-memory tile updates. | HIGH | ✅ | 2026-06-12 |
| T8.23 | **Discoverability** — Permission-aware AI Tag tooltip with descriptive text. | POLISH | ✅ | 2026-06-12 |
| T8.24 | **Color palette resources** — Extract shared `ResourceDictionary` palette (60+ hardcoded hex literals today; blocks theming/dark mode). | POLISH | ✅ | 2026-06-12 |
| T8.25 | **Type scale + spacing tokens** — Collapse 8+ ad-hoc font sizes into ≤4 `TextBlock` style classes; introduce spacing tokens for 4/8/12/16 rhythm. | POLISH | ✅ | 2026-06-12 |
| T8.26 | **Copywriting cleanup** — Dedupe empty-state strings, replace bare "Clear"/"Refresh", standardize Save labels. | POLISH | ✅ | 2026-06-12 |

### Work Stream 2: Documentation

**GOAL:** Complete Admin Guide and User Guide covering all v1 features.

| Task | Description | Status | Date |
|------|-------------|--------|------|
| T8.7 | **Admin Guide** — Create `docs/admin-guide.md` covering Service Manager overview, multi-user mode setup, user & role lifecycle, database migration wizard, Broker service installation, audit log interpretation, firewall configuration, and troubleshooting. | ✅ | 2026-06-12 |
| T8.8 | **User Guide** — Create `docs/user-guide.md` covering Catalog Browser overview, standalone vs multi-user mode, folder selection and ingest, gallery navigation, metadata editing, search and filtering, XMP write-back, image adjustments, export, AI tagging, and collections. | ✅ | 2026-06-12 |
| T8.9 | **Requirements validation** — Audit all 34 v1 requirements against implemented features. Update `REQUIREMENTS.md` traceability. Create `docs/v1-release-notes.md`. | ✅ | 2026-06-12 |

### Work Stream 3: Distribution Packaging

**GOAL:** Create platform-specific installers for Windows, macOS, and Linux.

| Task | Description | Status | Date |
|------|-------------|--------|------|
| T8.10 | **Self-contained publish** — Create `scripts/publish.sh` that runs `dotnet publish -r <rid> --self-contained` for all three projects targeting `win-x64`, `osx-x64`, `linux-x64`. Configure trim mode, ready-to-run, single-file. | ✅ | 2026-06-12 |
| T8.11 | **Windows MSI package** — Set up WiX Toolset project. Configure Start Menu shortcut, file association, uninstaller. | ⬜ | |
| T8.12 | **macOS DMG package** — Create `create-dmg` script that bundles `.app` into compressed DMG. Configure code signing placeholder. | ⬜ | |
| T8.13 | **Linux DEB package** — Create `scripts/build-deb.sh` that structures publish output into DEB layout. Use `dpkg-deb` for packaging. | ⬜ | |
| T8.14 | **CI/CD integration** — Add `.github/workflows/release.yml` that runs on tag push (`v*`): builds all platforms, runs full test suite, creates MSI/DMG/DEB artifacts, uploads to GitHub Releases. | ✅ | 2026-06-12 |

### Work Stream 4: Release Stabilization

**GOAL:** Satisfy the release gate CON-001 — zero Critical/High-severity bugs remaining.

| Task | Description | Status | Date |
|------|-------------|--------|------|
| T8.27 | **Critical/High bug triage & bug-bash** — Dedicated stabilization pass. Triage open issues + carried tech debt, run manual bug-bash across golden paths (delete/trash, context menus, bulk actions). **Exit:** zero open Critical or High items before packaging. | ⬜ | |

---

## 3. Execution Waves

Sequencing commitment — later waves are blocked on earlier ones only where noted.

| Wave | Tasks | Depends On | Rationale |
|------|-------|------------|-----------|
| **Wave 1 — UI Blockers** | T8.15 → T8.17, T8.19 → T8.16 menu wiring; T8.20 (parallel) | — | T8.15 (confirmation/toast) precedes T8.17 (delete needs confirmation). Delete (T8.17) and reveal/copy/add-to-collection (T8.19) commands must exist before T8.16 context menu can wire entries. T8.20 (tile bindings) is independent. |
| **Wave 2 — UX HIGH** | T8.21, T8.22 | Wave 1 | Keyboard model and bulk actions. T8.22 reuses per-asset rate/label/flag commands from T8.16. |
| **Wave 3 — Docs** | T8.7, T8.8, T8.9 | — | Independent of UI streams; can run in parallel with Waves 1-2. |
| **Wave 4 — POLISH** | T8.23, T8.24, T8.25, T8.26 | Wave 1 | Visual/consistency debt. T8.24 (color palette) should precede T8.25 (type scale) since both touch shared resources. |
| **Wave 5 — Stabilization** | T8.27 | Waves 1-4 | CON-001 triage runs after all features land. Gates Wave 6. |
| **Wave 6 — Packaging** | T8.10, T8.11, T8.12, T8.13, T8.14 | Wave 5 | Runs only after T8.27 clears CON-001 AND PAT-002 (full suite green). |

---

## 4. Deferred to v2

| Task | Description | Deferral Reason | v2 Ticket |
|------|-------------|-----------------|-----------|
| T8.3 | FTS5 full-text search | LIKE-based search adequate for v1; FTS5 can be added without breaking changes | META-V2-01 |
| T8.4 | Thumbnail cache optimization | Current implementation works; optimization not v1-blocking | PERF-V2-01 |
| T8.5 | Gallery virtualized scrolling | Current implementation works for reasonable collection sizes | PERF-V2-02 |
| T8.6 | Startup time optimization | Not v1-blocking; can be addressed in v2 perf pass | PERF-V2-03 |
| T8.18 | Sidebar tree CRUD | Largest UI item, touches broker/multi-user path; sidebar read-only in v1 is acceptable | UI-V2-01 |

---

## 5. Alternatives

- **ALT-001**: Skip FTS5 and rely on EF Core `Contains()` / `LIKE` queries. **Accepted for v1** — benchmark baseline (T8.1) and indexes (T8.2) provide adequate performance.
- **ALT-002**: Use Squirrel.Windows or MSIX instead of WiX MSI. (WiX chosen for maturity; MSIX may be evaluated for Windows Store in v2.)
- **ALT-003**: Use .NET single-file publish instead of directory-based. (Single-file may cause issues with native dependencies; evaluate per-project.)
- **ALT-004**: Skip CI/CD automation and package manually. (Rejected — CI/CD essential for reproducible builds.)
- **ALT-005**: Split sidebar CRUD (T8.18) into its own phase. **Accepted** — deferred to v2.
- **ALT-006**: Defer all [POLISH] tasks (T8.23-T8.26) to v2. **Rejected** — executing in Phase 8 for perceived quality.

---

## 6. Dependencies

| Dependency | Version | Purpose |
|------------|---------|---------|
| `WiX Toolset` | 5.x | Windows MSI packaging |
| `create-dmg` | latest | macOS DMG creation |
| `dpkg-deb` | system | Linux DEB packaging |
| `dotnet publish` | .NET 10 | Self-contained framework-dependent publish |
| GitHub Actions | — | CI/CD pipeline for multi-platform builds |

---

## 7. Files

| File | Role |
|------|------|
| `src/Adam.CatalogBrowser/Views/AssetGalleryView.axaml` | Add `ContextFlyout`, bind tile affordances (T8.16, T8.20) |
| `src/Adam.CatalogBrowser/Views/MainWindow.axaml` | Key bindings, tooltips (T8.21, T8.23) |
| `src/Adam.CatalogBrowser/ViewModels/AssetGalleryViewModel.cs` | `DeleteSelectedCommand`, bulk actions, reveal/copy/add-to-collection (T8.17, T8.19, T8.22) |
| `src/Adam.CatalogBrowser/Services/DeleteService.cs` | Existing — wire to UI (T8.17) |
| `src/Adam.CatalogBrowser/Controls/Themes/AssetTileControlStyles.axaml` | Tile affordance styling (T8.20) |
| `docs/admin-guide.md` | New — Admin Guide (T8.7) |
| `docs/user-guide.md` | New — User Guide (T8.8) |
| `docs/v1-release-notes.md` | New — Release notes (T8.9) |
| `scripts/publish.ps1` | New — Cross-platform publish script (T8.10) |
| `scripts/build-deb.sh` | New — Linux DEB builder (T8.13) |
| `.github/workflows/release.yml` | New — CI/CD release pipeline (T8.14) |
| `*.wixproj` | New — WiX MSI project (T8.11) |

---

## 8. Testing

| Test | Type | Command |
|------|------|---------|
| Full test suite | Automated | `dotnet test` |
| UI smoke test | Manual | Launch app, verify context menus, delete, keyboard shortcuts, tile affordances |
| Package verification | Manual | Install MSI/DMG/DEB on each platform, verify launch and basic functionality |
| Bug bash | Manual | Exercise all golden paths: delete/trash, context menus, bulk actions, keyboard shortcuts |

**Quick command:** `dotnet test`

**Expected results:** All existing 383+ tests pass (plus 2 Docker-dependent skipped); zero Critical/High bugs; packages install and launch cleanly on all 3 platforms.

---

## 9. Risks & Assumptions

- **RISK-001**: WiX Toolset requires Windows SDK and may not be available in CI (Linux/macOS runners). Mitigation: use `dotnet msbuild` with built-in packaging on Windows runner only.
- **RISK-002**: macOS code signing requires an Apple Developer account. Mitigation: document signing as manual step; provide unsigned DMG for testing.
- **RISK-003**: Context menu + delete implementation may uncover ViewModel wiring issues not caught by unit tests. Mitigation: manual smoke test after each wave.
- **ASSUMPTION-001**: Users will install via platform-native package managers (Windows: MSI, macOS: DMG, Linux: apt/dpkg). Snap/Flatpak deferred to v2.
- **ASSUMPTION-002**: Sidebar read-only in v1 is acceptable — users can browse/filter but not create/edit/delete tree items.

---

## 10. Related Specifications

- `.planning/REQUIREMENTS.md` — All 34 v1 requirements
- `.planning/ROADMAP.md` — Phase 8 deliverables and success criteria
- `.planning/STATE.md` — Current project state
- `.planning/phases/08-v1-0-polish-ship/08-UI-REVIEW.md` — Phase 8 UI audit (6-pillar, 13/24)
