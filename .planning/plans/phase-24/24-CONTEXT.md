# Phase 24 — Metadata Panels & Preferences Persistence

**Phase:** 24
**Milestone:** v5.0 — AI-Native DAM (cont.)
**Status:** Discussed — ready for planning
**Date:** 2026-06-18

---

## Scope Summary

Combine **three workstreams** into a single phase:

1. **§25-B** — Collapsible schema panels in the Metadata Editor (A–H panels + Raw Metadata Viewer)
2. **§27** — User preferences persistence (two-tier: AdamConfig + catalog DB, with broker sync)
3. **Phase 23 Test Coverage** — 6 missing test files for Phase 23 facial recognition code

Plus a **bare-bones SettingsView/SettingsViewModel stub** to set up the navigation slot for §26.

---

## Locked Decisions

| # | Decision | Choice | Rationale |
|---|----------|--------|-----------|
| 1 | Phase scope | §25-B + §27 + Phase 23 test files | User selected this combination over narrower or broader alternatives |
| 2 | Schema panel count | **All 8 panels (A–H)** | Full §25 vision: Description & Content, Creator & Contact, Rights & Licensing, Location, Camera/EXIF, Dates, GPS/Map, Raw Metadata Viewer |
| 3 | Preferences persistence model | **Full two-tier: machine-local + catalog DB + broker sync** | Machine-local paths/endpoints stay in AdamConfig; portable UI state (theme, panel layout, gallery prefs) stored in catalog DB. Multi-user path through PreferenceHandler + protobuf contracts |
| 4 | Phase 23 test inclusion | **Yes — include all 6 missing test files** | Close the UAT gap from day one of Phase 24 |
| 5 | Settings tab | **Bare-bones stub only** | Create SettingsViewModel/SettingsView shell with category rail but no content. Audit keeps its nav position. Full Settings tab populated in later §26 phase |
| 6 | Wave strategy | **4 waves** | Wave 1: Tests + Prefs entity + migration. Wave 2: §25-B panels. Wave 3: PreferenceHandler + broker contracts. Wave 4: Wire prefs into app startup + integrate standalone/broker paths |

---

## Detailed Scope

### Wave 1 — Phase 23 Tests + UserPreference Entity

**Goal:** Close the Phase 23 UAT test-coverage gap and lay the DB foundation for preferences.

**Files to create:**
- `tests/Adam.Shared.Tests/Services/FaceAlignerTests.cs` — 5+ tests (112×112 output, landmark alignment, thumbnail extraction)
- `tests/Adam.Shared.Tests/Services/FaceMatcherServiceTests.cs` — 8+ tests (auto-assign, suggest, unknown, clustering, centroid, batch)
- `tests/Adam.CatalogBrowser.Tests/ViewModels/FaceTaggingViewModelTests.cs` — 6+ tests
- `tests/Adam.CatalogBrowser.Tests/ViewModels/PersonManagementViewModelTests.cs` — 5+ tests
- `tests/Adam.BrokerService.Tests/Handlers/FaceHandlerTests.cs` — 4+ tests
- `tests/Adam.BrokerService.Tests/Handlers/PersonHandlerTests.cs` — 4+ tests

**Files to create (preferences DB):**
- `src/Adam.Shared/Models/UserPreference.cs` — entity with Id, UserId (nullable), Key, ValueJson, UpdatedAt, Version

**Files to modify:**
- `src/Adam.Shared/Data/AppDbContext.cs` — add `DbSet<UserPreference> UserPreferences`, entity config, unique index on `(UserId, Key)`

> **Post-implementation note:** No EF Core migration was created. The `UserPreference` entity is handled by `EnsureCreated()` (consistent with the Phase 23 pattern). A formal migration should be added if/when the project adopts migration-based schema management.

### Wave 2 — §25-B: Collapsible Schema Panels

**Goal:** Restructure the MetadataEditor into 8 collapsible Expander panels with the Raw Metadata Viewer.

**Panel layout (A–H):**

```
▼ Description & Content        (editable — open by default)
   Title, Headline, Description, CaptionWriter, Keywords, 
   Instructions, Genre, Scene, Subject, Urgency
▼ Creator & Contact            (editable — open by default)
   Creator, JobTitle, Credit, Source, ContactInfo (address/city/region/postal/country/phone/email/website)
▼ Rights & Licensing           (editable — open by default)
   Copyright, CopyrightStatus, UsageTerms, WebStatement,
   ModelRelease Status/ID, PropertyRelease Status/ID, DigitalSourceType
▼ Location                     (editable)
   Sublocation, City, State, Country, CountryCode, 
   LocationCreated, LocationShown
▶ Dates                        (mixed — closed by default)
   DateTaken (R), DateCreated (E), DateModified (R)
▶ Camera / EXIF                (read-only — closed by default)
   Make, Model, Lens, Focal, Aperture, Exposure, ISO, Flash,
   Orientation, ExposureProgram, MeteringMode, WhiteBalance, 
   ExposureBias, MaxAperture, FocalLength35mm, ColorSpace, Software
▶ GPS / Map                    (read-only — closed by default)
   Latitude, Longitude, Altitude, ImageDirection
▶ All Metadata (raw dump)      (read-only — closed by default)
   Key→value pairs from MetadataExtractor
```

**Editable fields gated by `CanEdit`** (existing permission system). Read-only panels shown regardless.

**Controlled vocabularies (dropdowns):**
- Urgency: 1–8 integer
- Copyright Status: tri-state (Marked/Unmarked/Unknown)
- Model Release Status: MR-NON, MR-NAP, MR-UMR, MR-LMR
- Property Release Status: PR-NON, PR-NAP, PR-UPR, PR-LPR
- Digital Source Type: 8 IPTC CV URIs (including AI-disclosure)

**Raw Metadata Viewer (Panel H):** Placeholder model (`MetadataRawItem`) exists and is wired in the ViewModel's `TogglePanelCommand`. The extraction logic to populate raw key→value pairs from `MetadataExtractorService` is **not yet implemented** — Panel H will remain empty until a future phase wires the extraction.

> **Deferred:** `MetadataExtractorService.GetAllProperties()` — add a helper that returns all extracted EXIF/IPTC/XMP key→value pairs, then wire into `LoadAssetAsync` to populate `RawMetadataItems`.

**Controlled vocabulary dropdowns deferred:** The CONTEXT originally specified dropdowns for Urgency (1–8), CopyrightStatus (tri-state), ModelRelease/PropertyRelease statuses, and DigitalSourceType (IPTC CV URIs). These fields are not in the `MetadataProfile` model and would require a DB migration. They remain as `TextBox` bindings for now.

**Files to modify:**
- `src/Adam.CatalogBrowser/Views/MetadataEditorView.axaml` — major XAML restructure
- `src/Adam.CatalogBrowser/ViewModels/MetadataEditorViewModel.cs` — add new panel properties (no controlled-vocab backing fields yet)

### Wave 3 — PreferenceHandler + Broker Contracts

**Goal:** Enable multi-user preference sync through the broker.

**Files to create:**
- `src/Adam.Shared/Contracts/PreferenceMessages.cs` — protobuf contracts:
  - `GetPreferencesRequest` / `GetPreferencesResponse`
  - `SetPreferenceRequest` / `SetPreferenceResponse`
  - `ResetPreferencesRequest` / `ResetPreferencesResponse`
- `src/Adam.BrokerService/Handlers/PreferenceHandler.cs` — CRUD handler for UserPreference rows, scoped to authenticated UserId

**Files to modify:**
- `src/Adam.Shared/Contracts/MessageTypeCode.cs` — add 6 new opcodes (184–189)
- `src/Adam.BrokerService/Handlers/ConnectionHandler.cs` — map preference opcodes
- `src/Adam.BrokerService/Program.cs` — register PreferenceHandler singleton

### Wave 4 — Wire Preferences into App Startup + IUserPreferenceService

**Goal:** Create the service layer and wire preferences into the app lifecycle.

**Files to create:**
- `src/Adam.Shared/Services/IUserPreferenceService.cs` — interface:
  - `Task<T?> GetAsync<T>(string key, T? defaultValue = default, CancellationToken ct = default)` where T : class
  - `Task<T> GetOrDefaultAsync<T>(string key, T defaultValue, CancellationToken ct = default)`
  - `Task SetAsync<T>(string key, T value, CancellationToken ct = default)`
  - `Task ResetAsync(string key, CancellationToken ct = default)`
  - `Task ResetAllAsync(CancellationToken ct = default)`
  - `Task LoadAsync(CancellationToken ct = default)`
- `src/Adam.Shared/Services/UserPreferenceService.cs` — implementation:
  - Standalone: reads/writes UserPreference row directly via AppDbContext
  - Multi-user: all 4 broker methods are fully wired through `BrokerClient.SendAsync()` with `GetPreferencesRequest`/`SetPreferenceRequest`/`ResetPreferenceRequest`/`ResetAllPreferencesRequest` — preferences sync through the `PreferenceHandler` on the broker side
  - JSON serialization via `System.Text.Json`
  - In-memory `_cache` dictionary for fast reads
  - `LoadAsync()` loads all preferences into cache on first call
  - **No debounced autosave** — writes are synchronous immediate. Add debounced save in §26 if needed.
  - `schemaVersion` field understood but not enforced (pass-through JSON)

**Settings stub:**
- `src/Adam.CatalogBrowser/ViewModels/SettingsViewModel.cs` — bare-bones VM with category rail (General, Catalog, Connection, Ingestion, Metadata, AI, Search, Keyboard, Security, Audit, About)
- `src/Adam.CatalogBrowser/Views/SettingsView.axaml` — master-detail shell with left category list, right detail pane (empty), search box at top
- `src/Adam.CatalogBrowser/Views/SettingsView.axaml.cs` — navigation by category selection
- Modify `MainWindow.axaml` — add DataTemplate mapping `SettingsViewModel → SettingsView`
- Modify `MainWindowViewModel.cs` — add `ShowSettingsCommand`

**App startup integration:**
- **`UserPreferenceService.LoadAsync()` is NOT called during app startup.** The startup hydration and theme/accent application remain deferred to §26 when the Settings tab content is populated.
- The service instance is registered in DI and available, but preferences are loaded lazily on first access.
- **Broker path is LIVE** — when the service is constructed with the broker constructor (passing `BrokerClient`, `userId`, and `authToken`), all 4 CRUD operations route through `BrokerClient.SendAsync()` using the `PreferenceHandler` on the server side.

**Files to modify:**
- `src/Adam.CatalogBrowser/App.axaml.cs` — register `IUserPreferenceService`, `SettingsViewModel`
- `src/Adam.CatalogBrowser/ViewModels/MainWindowViewModel.cs` — add `ShowSettingsCommand`
- `src/Adam.CatalogBrowser/Views/MainWindow.axaml` — add DataTemplate for `SettingsViewModel → SettingsView`

---

## Key Design Decisions

### Schema versioning for preference blobs
```json
{ "schemaVersion": 1, "appearance": { ... }, "gallery": { ... }, ... }
```
- Unknown keys preserved on round-trip (forward compat)
- Future versions can migrate old blobs via `schemaVersion`
- Window geometry excluded (stays in machine-local AdamConfig)

### Machine-local vs portable split
| Tier | Storage | Examples |
|------|---------|----------|
| **Machine-local** | `AdamConfig` (settings.json) | catalog/cache paths, broker endpoints + TLS, window geometry, "last opened catalog" |
| **Portable** | `UserPreference` in catalog DB | theme/accent/density, default view + thumbnail size, gallery sort, sidebar width, panel order/expansion, recent searches |

### Panel expand/collapse state
- Default states: A–C open, D (Location) closed, E–H closed
- Panel state persistence (via §27 preferences as `metadata.expandedPanels` array) is **not yet wired** — panels always open at defaults. Persistence deferred to §26.

---

## What's NOT in Phase 24 (deferred)

| Feature | Reason | Target Phase |
|---------|--------|-------------|
| Full IPTC Core fields (caption writer, credit, source, sublocation, etc.) | Requires model changes + migration | Phase 25 (§25-C) |
| IPTC Extension + XMP Rights + PLUS (releases, licensor, structured location) | Requires owned types + complex writeback | Phase 26 (§25-D) |
| Full Settings tab content (Appearance, AI Config, etc.) | Scope kept to stub only | Phase 26 (§26) |
| Metadata Workspace rework (preview pane, filmstrip, batch editing) | Too large for this phase | Phase 25+ (§28) |
| Dublin Core export + MWG reconciliation tests | Deferred to later | Phase 27 (§25-E) |

---

## Files Tally

| Wave | New Files | Modified Files |
|------|-----------|----------------|
| 1 (Tests + Prefs entity) | 8 new (6 test + UserPreference + PreferenceMessages) | 1 (AppDbContext) |
| 2 (Collapsible panels) | 1 new (MetadataRawItem) | 2 (MetadataEditorVM, View) |
| 3 (Broker prefs) | 2 new (PreferenceHandler + PreferenceHandler) | 3 (MessageTypeCode, ConnectionHandler, Program.cs) |
| 4 (Wire-up + Settings stub) | 4 new (interface + service + Settings VM/View) | 3 (App.axaml.cs, MainWindowVM, MainWindow.axaml) |
| **Total** | **15 new** | **9 modified** |

> **Note:** No changes to `MetadataExtractorService.cs` — `GetAllProperties()` was deferred. File tally updated to match actual: 15 new + 9 modified.

---

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| §25-B panel restructure breaks existing dirty/save flow | Low | High | Keep all existing properties + commands; only restructure XAML layout — ✅ verified, no breakage |
| Preference blob JSON schema evolves and old clients clobber new fields | Medium | Medium | `schemaVersion` pass-through not enforced yet — **deferred to §26** |
| Broker PreferenceHandler adds surface area for unauthorized access | Low | High | Scope all preference ops to authenticated UserId; validate ownership server-side — ✅ implemented |
| 6 new test files inflate Phase 24 effort | Medium | Low | Created and passing — ✅ closed |
| Settings stub creates expectation of full Settings tab | Medium | Low | Stub shows "(coming soon)" placeholders — ✅ documented |
| Broker client stubs previously not wired | Resolved | Resolved | All 4 broker methods in `UserPreferenceService` now fully implemented — ✅ verified (build clean, 1,335 tests pass) |

---

## Dependencies

| Workstream | Depends On |
|------------|-----------|
| Wave 2 (Panels) | Nothing — pure UI restructure |
| Wave 3 (Broker prefs) | Wave 1 (UserPreference entity) |
| Wave 4 (Wire-up) | Waves 1 + 3 (entity + broker handler) |
| Phase 23 tests | Nothing — standalone test files |

Waves 1 and 2 can be executed in parallel. Waves 3 and 4 are sequential after Wave 1.
