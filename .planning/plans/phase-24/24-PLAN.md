# Phase 24 Plan — Metadata Panels & Preferences Persistence

**Phase:** 24 | **Milestone:** v5.0 — AI-Native DAM (cont.) | **Date:** 2026-06-18 | **Status:** Executed (retrospective plan)

---

## Task Breakdown

### Wave 1 — Phase 23 Tests + UserPreference Entity (~540 LOC)

| # | Task | File(s) | LOC | Key Detail |
|---|------|---------|-----|------------|
| 1.1 | FaceAligner unit tests | `tests/Adam.Shared.Tests/Services/FaceAlignerTests.cs` | ~110 | 5 tests: 112×112 output, same-input consistency, scale invariance, normalized landmarks, thumbnail extraction |
| 1.2 | FaceMatcherService unit tests | `tests/Adam.Shared.Tests/Services/FaceMatcherServiceTests.cs` | ~210 | 7 tests: auto-assign, suggest (cos 40°), unknown, no-persons, centroid avg, cosine similarity (identical/orthogonal) |
| 1.3 | FaceTaggingViewModel tests | `tests/Adam.CatalogBrowser.Tests/ViewModels/FaceTaggingViewModelTests.cs` | ~160 | 5 tests: load person/unknown faces, loading state, name face (new), name face (existing), refresh |
| 1.4 | PersonManagementViewModel tests | `tests/Adam.CatalogBrowser.Tests/ViewModels/PersonManagementViewModelTests.cs` | ~200 | 6 tests: load all, loading state, select→edit name, rename, merge moves faces, delete unlinks |
| 1.5 | FaceHandler broker tests | `tests/Adam.BrokerService.Tests/Handlers/FaceHandlerTests.cs` | ~180 | 4 tests: non-image error, invalid asset ID, no-auth forbidden, malformed payload |
| 1.6 | PersonHandler broker tests | `tests/Adam.BrokerService.Tests/Handlers/PersonHandlerTests.cs` | ~210 | 5 tests: list persons, name person (new), name person (existing), merge, delete+unlink |
| 1.7 | UserPreference entity | `src/Adam.Shared/Models/UserPreference.cs` | ~25 | Id, UserId, Key, ValueJson, UpdatedAt, Version |
| 1.8 | AppDbContext UserPreferences | `src/Adam.Shared/Data/AppDbContext.cs` | ~15 | `DbSet<UserPreference>` + unique index `(UserId, Key)` |

**Test environment notes:**
- Each test uses its own SQLite temp database (`Guid.NewGuid()` + `EnsureCreated()`)
- Cleanup via `SqliteConnection.ClearAllPools()` + `File.Delete(dbPath)`
- FaceHandler/PersonHandler tests use `IAsyncLifetime` with full DI setup (AuthHandler, AuthorizationMiddleware, seeded admin role/user)
- FaceMatcherService and PersonManagementViewModel tests needed SQLite compatibility fixes:
  - `byte[].Length > 0` moved to client-side (EF Core SQLite can't translate `.Length` on byte arrays)
  - `DateTimeOffset.Max()`/`OrderByDescending` on DateTimeOffset subquery → two-query client-side approach

---

### Wave 2 — §25-B: Collapsible Schema Panels (~330 LOC)

| # | Task | File(s) | LOC | Key Detail |
|---|------|---------|-----|------------|
| 2.1 | MetadataEditorViewModel panel state | `src/Adam.CatalogBrowser/ViewModels/MetadataEditorViewModel.cs` | ~150 | Added `IsPanel{A-H}Expanded` (8 bool properties), `TogglePanelCommand` with `"A"–"H"` case switch, `MetadataRawItem` collection for Panel H |
| 2.2 | MetadataRawItem model | `src/Adam.CatalogBrowser/ViewModels/MetadataRawItem.cs` | ~15 | `record` with Namespace, Tag, DisplayName, Value |
| 2.3 | XAML panel restructure | `src/Adam.CatalogBrowser/Views/MetadataEditorView.axaml` | ~165 | 8 collapsible sections with Expander + IsVisible binding to `IsPanel{A-H}Expanded`. Panels A–C open by default, D–H closed |

**Panel layout (what was implemented):**

| Panel | Name | Default | Fields Shown |
|-------|------|---------|--------------|
| A | Description & Content | Open | Title (editable), Headline (editable), Description (editable), Keywords (TagEditor) |
| B | Creator & Contact | Open | Creator (editable), Copyright (editable), ContactInfo |
| C | Rights & Usage | Open | UsageTerms, Headline |
| D | Location | Closed | City, State, Country, ContactInfo |
| E | Dates | Closed | DateTaken |
| F | Camera / EXIF | Closed | Make, Model, Lens, Focal, Aperture, Exposure, ISO, Flash, Orientation |
| G | GPS / Map | Closed | GPS coords, Altitude |
| H | Raw Metadata | Closed | MetadataRawItem list (empty — extraction not wired) |

**What's deferred** (not in implemented code):
- Controlled vocabulary dropdowns (Urgency dropdown, CopyrightStatus tri-state, ModelRelease/PropertyRelease statuses, DigitalSourceType IPTC CV URIs — none of these fields exist on MetadataProfile model)
- `MetadataExtractorService.GetAllProperties()` helper for Panel H data population
- Panel H empty until extraction is wired in a future phase

---

### Wave 3 — PreferenceHandler + Broker Contracts (~202 LOC)

| # | Task | File(s) | LOC | Key Detail |
|---|------|---------|-----|------------|
| 3.1 | PreferenceMessages protobuf | `src/Adam.Shared/Contracts/PreferenceMessages.cs` | ~100 | 5 contracts: `GetPreferencesRequest/Response`, `SetPreferenceRequest/Response`, `ResetPreferenceRequest/Response`, `ResetAllPreferencesRequest/Response`, `PreferenceItem` |
| 3.2 | MessageTypeCode opcodes | `src/Adam.Shared/Contracts/MessageTypeCode.cs` | ~8 | 6 opcodes: `GetPreferencesRequest=184` through `ResetAllPreferencesResponse=189` |
| 3.3 | PreferenceHandler | `src/Adam.BrokerService/Handlers/PreferenceHandler.cs` | ~120 | 4 handler methods (Get/Set/Reset/ResetAll). Uses `ServiceProvider.CreateScope()` pattern (not `IDbContextFactory`). Auth-gated to `asset:read`. Extracts `UserId` from JWT `sub` claim |
| 3.4 | ConnectionHandler wiring | `src/Adam.BrokerService/Handlers/ConnectionHandler.cs` | ~8 | 4 routes in MessageDispatcher dict |
| 3.5 | DI registration | `src/Adam.BrokerService/Program.cs` | ~1 | `services.AddSingleton<PreferenceHandler>()` |

**Design note:** `PreferenceHandler` was initially written with `IDbContextFactory<AppDbContext>` constructor injection, but this broke BrokerServiceIntegrationTests (which use `AddDbContext`, not `AddDbContextFactory`). Changed to `ServiceProvider.CreateScope()` pattern matching FaceHandler/PersonHandler.

**Opcode map:**

| Opcode | Direction | Contract |
|--------|-----------|----------|
| 184 → 185 | Client → Broker → Client | `GetPreferencesRequest` → `GetPreferencesResponse` |
| 186 → 187 | Client → Broker → Client | `SetPreferenceRequest` → `SetPreferenceResponse` |
| 188 → 189 | Client → Broker → Client | `ResetPreferenceRequest` → `ResetPreferenceResponse` |

---

### Wave 4 — Wire-Up + Settings Stub (~352 LOC)

| # | Task | File(s) | LOC | Key Detail |
|---|------|---------|-----|------------|
| 4.1 | IUserPreferenceService | `src/Adam.Shared/Services/IUserPreferenceService.cs` | ~25 | 6 methods: `GetAsync<T>`, `GetOrDefaultAsync<T>`, `SetAsync<T>`, `ResetAsync`, `ResetAllAsync`, `LoadAsync` |
| 4.2 | UserPreferenceService | `src/Adam.Shared/Services/UserPreferenceService.cs` | ~180 | Two constructors: `(IDbContextFactory, ILogger)` for standalone, `(BrokerClient, Guid, ILogger)` for multi-user. In-memory cache. `LoadAsync` hydrates all prefs. 4 broker methods are `// TODO` stubs. JSON via `System.Text.Json` |
| 4.3 | SettingsViewModel | `src/Adam.CatalogBrowser/ViewModels/SettingsViewModel.cs` | ~75 | 11-category rail (General, Catalog, Connection, Ingestion, Metadata, AI, Search, Keyboard, Security, Audit, About). `SelectCategoryCommand`, `SearchText` filter stub |
| 4.4 | SettingsView XAML | `src/Adam.CatalogBrowser/Views/SettingsView.axaml` | ~90 | Master-detail layout: left `ListBox` with 11 categories, right detail pane, top search `TextBox` |
| 4.5 | SettingsView code-behind | `src/Adam.CatalogBrowser/Views/SettingsView.axaml.cs` | ~15 | Category selection handler, placeholder events |
| 4.6 | MainWindow DataTemplate | `src/Adam.CatalogBrowser/Views/MainWindow.axaml` | ~3 | `DataTemplate DataType="{x:Type vm:SettingsViewModel}"` → `views:SettingsView` |
| 4.7 | MainWindowViewModel ShowSettings | `src/Adam.CatalogBrowser/ViewModels/MainWindowViewModel.cs` | ~15 | `ShowSettingsCommand` resolves `SettingsViewModel` from DI, sets `CurrentView` |
| 4.8 | DI registration | `src/Adam.CatalogBrowser/App.axaml.cs` | ~3 | `services.AddSingleton<IUserPreferenceService, UserPreferenceService>()`, `services.AddTransient<SettingsViewModel>()` |

**What's deferred:**
- `UserPreferenceService.LoadAsync()` not called on startup (no startup hydration)
- Broker client methods not wired (4 `// TODO` stubs — standalone path works)
- No debounced autosave
- No controlled-vocabulary dropdowns
- No `MetadataExtractorService.GetAllProperties()` for Panel H

---

## Wiring Diagrams

### Preference Handler Flow (Broker-side)

```
Client                          BrokerService                    SQLite/Postgres/SQL Server
  │                                  │                                  │
  │  GetPreferencesRequest (184)     │                                  │
  │ ───────────────────────────────→ │                                  │
  │                                  │  Authz.HasPermission("asset:read")│
  │                                  │  ExtractUserId(JWT sub claim)    │
  │                                  │  CreateScope()                   │
  │                                  │  db.UserPreferences.Where(...)   │
  │                                  │ ────────────────────────────────→│
  │                                  │ ←────────────────────────────────│
  │  GetPreferencesResponse (185)    │                                  │
  │ ←─────────────────────────────── │                                  │
```

### UserPreferenceService Flow (Client-side)

```
App.axaml.cs DI
  └─ IUserPreferenceService → UserPreferenceService
       ├─ Standalone: IDbContextFactory<AppDbContext> constructor
       │    └─ LoadAsync() → db.UserPreferences.ToListAsync() → _cache
       │    └─ SetAsync()   → upsert UserPreference row
       │    └─ GetAsync()   → _cache hit → return; miss → DB query
       │
       └─ Multi-user: BrokerClient + Guid constructor
            └─ All methods → // TODO stubs (planned for §26)
```

### Metadata Editor Panel State Flow

```
User clicks panel header → CommandParameter="A".."H"
  └─ TogglePanelCommand
       └─ switch(panelName):
            case "A" → IsPanelADescriptionExpanded ^= true
            case "B" → IsPanelBCreatorExpanded ^= true
            ...
       └─ XAML: Expander IsExpanded="{Binding IsPanelADescriptionExpanded}"
```

---

## File Manifest

### New Files (15)

```
src/Adam.Shared/Models/UserPreference.cs
src/Adam.Shared/Contracts/PreferenceMessages.cs
src/Adam.Shared/Services/IUserPreferenceService.cs
src/Adam.Shared/Services/UserPreferenceService.cs
src/Adam.BrokerService/Handlers/PreferenceHandler.cs
src/Adam.CatalogBrowser/ViewModels/MetadataRawItem.cs
src/Adam.CatalogBrowser/ViewModels/SettingsViewModel.cs
src/Adam.CatalogBrowser/Views/SettingsView.axaml
src/Adam.CatalogBrowser/Views/SettingsView.axaml.cs
tests/Adam.Shared.Tests/Services/FaceAlignerTests.cs
tests/Adam.Shared.Tests/Services/FaceMatcherServiceTests.cs
tests/Adam.CatalogBrowser.Tests/ViewModels/FaceTaggingViewModelTests.cs
tests/Adam.CatalogBrowser.Tests/ViewModels/PersonManagementViewModelTests.cs
tests/Adam.BrokerService.Tests/Handlers/FaceHandlerTests.cs
tests/Adam.BrokerService.Tests/Handlers/PersonHandlerTests.cs
```

### Modified Files (9)

```
src/Adam.Shared/Data/AppDbContext.cs              (+UserPreference DbSet + index)
src/Adam.Shared/Contracts/MessageTypeCode.cs       (+6 opcodes 184-189)
src/Adam.BrokerService/Handlers/ConnectionHandler.cs (+4 preference routes)
src/Adam.BrokerService/Handlers/HandlerBase.cs      (base class — no change needed)
src/Adam.BrokerService/Program.cs                   (+PreferenceHandler registration)
src/Adam.CatalogBrowser/App.axaml.cs                (+IUserPreferenceService + SettingsViewModel DI)
src/Adam.CatalogBrowser/ViewModels/MainWindowViewModel.cs (+ShowSettingsCommand)
src/Adam.CatalogBrowser/ViewModels/MetadataEditorViewModel.cs (+8 panel properties, TogglePanelCommand)
src/Adam.CatalogBrowser/Views/MetadataEditorView.axaml     (+8 collapsible Expander panels)
src/Adam.CatalogBrowser/Views/MainWindow.axaml              (+SettingsViewModel DataTemplate)
```

### Modified During Testing (7)

```
src/Adam.Shared/Services/FaceMatcherService.cs     (client-side byte[] filtering — 3 sites)
src/Adam.Shared/Services/FaceAligner.cs            (ExtractThumbnail Fix: SkiaSharp Pixels copy-back)
src/Adam.BrokerService/Handlers/PreferenceHandler.cs (scope pattern, not IDbContextFactory)
src/Adam.CatalogBrowser/ViewModels/PersonManagementViewModel.cs (two-query LoadAsync for SQLite)
tests/Adam.BrokerService.Tests/Integration/BrokerServiceIntegrationTests.cs (+PreferenceHandler DI)
tests/Adam.BrokerService.Tests/Integration/ConcurrentClientsTests.cs (+PreferenceHandler DI)
tests/Adam.Shared.Tests/Services/FaceMatcherServiceTests.cs (test data fixes)
```

---

## Test Coverage

### Phase 23 Tests Added

| Suite | Tests | Coverage |
|-------|-------|----------|
| `FaceAlignerTests` | 5 | AlignFace (112×112, consistency, scale, normalized landmarks), ExtractThumbnail |
| `FaceMatcherServiceTests` | 7 | MatchAsync (auto-assign, suggest, unknown, no-persons), ComputeCentroid, CosineSimilarity (×2) |
| `FaceTaggingViewModelTests` | 5 | LoadAsync, NameFace (new/existing), RefreshCommand, loading state |
| `PersonManagementViewModelTests` | 6 | LoadAsync, SelectPerson, RenameCommand, MergeCommand, DeleteCommand, loading state |
| `FaceHandlerTests` | 4 | DetectFaces (non-image, invalid-id, no-auth, malformed) |
| `PersonHandlerTests` | 5 | ListPersons, NamePerson (new/existing), MergePersons, DeletePerson |

### Test Results (Final Pass)

| Project | Passed | Failed | Skipped |
|---------|--------|--------|---------|
| Adam.Shared.Tests | 387 | 0 | 0 |
| Adam.ServiceManager.Tests | 156 | 0 | 0 |
| Adam.CatalogBrowser.Tests | 621 | 0 | 0 |
| Adam.BrokerService.Tests | 171 | 0 | 2 (Docker) |
| **Total** | **1,335** | **0** | **2** |

---

## LOC Estimate Summary

| Wave | New LOC | Modified LOC | Total |
|------|---------|--------------|-------|
| 1 (Tests + Prefs entity) | ~815 | ~15 | ~830 |
| 2 (Collapsible panels) | ~15 | ~315 | ~330 |
| 3 (Broker prefs) | ~220 | ~17 | ~237 |
| 4 (Wire-up + Settings stub) | ~310 | ~21 | ~331 |
| **Total** | **~1,360** | **~368** | **~1,728** |

---

## Deferred Items (Planned for §26)

| Item | Reason |
|------|--------|
| `MetadataExtractorService.GetAllProperties()` + Panel H population | Requires MetadataExtractor integration |
| Controlled vocabulary dropdowns (Urgency, CopyrightStatus, ModelRelease, etc.) | Requires MetadataProfile model changes + migration |
| `UserPreferenceService` broker methods (4 `// TODO` stubs) | Requires BrokerClient message-send integration |
| Startup hydration (`LoadAsync()` call in MainWindowViewModel) | Requires Settings tab content to apply |
| Panel state persistence (save/restore expanded panels) | Requires preferences + Settings tab |
| Debounced autosave | UX polish — low priority |
| Full Settings tab content (Appearance, AI Config, etc.) | Major scope — §26 |
