<!-- SPECKIT START -->
This feature is planned in `specs/001-digital-asset-management/plan.md`.
Data model: `specs/001-digital-asset-management/data-model.md`.
API contracts: `specs/001-digital-asset-management/contracts/api.md`.
Quickstart: `specs/001-digital-asset-management/quickstart.md`.
<!-- SPECKIT END -->

<!-- GSD START — This section is maintained by get-shit-done workflows -->

## Project Context

This repository uses **GSD (Get Shit Done)** for project planning and execution.

| Artifact | Location | Purpose |
|----------|----------|---------|
| Project | `.planning/PROJECT.md` | Living project context, core value, requirements |
| Config | `.planning/config.json` | Workflow preferences (mode, agents, granularity) |
| Roadmap | `.planning/ROADMAP.md` | Phases, requirements mapping, milestones |
| State | `.planning/STATE.md` | Current phase, progress, blockers |
| Requirements | `.planning/REQUIREMENTS.md` | v1/v2 requirements with traceability |
| Codebase Map | `.planning/codebase/` | Architecture, stack, conventions, concerns |

## Quick Commands

- `/gsd-progress` — Check current phase progress and next actions
- `/gsd-discuss-phase 7` — Gather context before planning Phase 7
- `/gsd-plan-phase 7` — Create detailed plan for Phase 7
- `/gsd-execute-phase 7` — Execute all plans in Phase 7
- `/gsd-verify-work` — Validate completed features against requirements
- `/gsd-code-review 7` — Review code changes in Phase 7

**Current Phase:** 20 — Client Experience Polish
**Milestone:** v3.x — Client Polish (Phases 14-20)
**Tests:** 1,232 passing (2 Docker-dependent skipped)

## Project-Specific Guidance

### Application Boundary: Client vs Server Panel

There is a strict separation between the **client** and the **server panel**:

- **CatalogBrowser** (`Adam.CatalogBrowser`) — The client. Only handles asset browsing, metadata editing, ingestion, and **connecting to** the service. All server administration (user management, service installation, server setup) has been removed. The connection bar in the title bar provides host/port, connect, disconnect, login, and logout functionality. The right panel provides metadata editing (tags, ratings, labels, flags, GPS, copyright), rotate/flip, and export.

- **ServiceManager** (`Adam.ServiceManager`) — The server panel. 3-tab layout: **Admin Panel** (service status, migration wizard), **Users** (add/edit/deactivate users, role assignment), **Audit** (filterable audit log viewer). Requires administrator/elevated privileges for service management operations.

- **BrokerService** (`Adam.BrokerService`) — The TCP broker that mediates multi-user access to the shared database.

**Rule of thumb**: If it manages users, installs services, or configures the server process — it belongs in `Adam.ServiceManager`. If it browses assets, edits metadata, or connects to an existing service — it belongs in `Adam.CatalogBrowser`.

### Dual-Mode Architecture
Always consider both operational modes when implementing features:
- **Standalone**: Direct SQLite access via `AppDbContext`, no external process
- **Multi-User**: TCP connection to `BrokerService`, JWT auth, shared PostgreSQL/SQL Server

Shared logic belongs in `Adam.Shared`. Mode-specific logic stays in the respective project.

### Database Provider Abstraction
`DbProviderConfig` in BrokerService configures the EF Core provider at runtime. Never hardcode provider-specific logic outside provider classes. Test changes against all three providers (see `DbProviderMatrixTests`).

### Transport Protocol
All multi-user communication uses raw TCP with length-prefixed protobuf framing (`TcpFrame`). No HTTP/REST/gRPC. Message types are in `Adam.Shared/Contracts/` with manual protobuf serialization.

### Critical Known Issues
- ~~Port mismatch~~ **RESOLVED**: `BrokerClient` now receives host/port from `AdamConfig` (configurable via connection bar UI / `settings.json`). No longer hardcoded.
- ~~Static JWT key~~ **RESOLVED**: `AuthHandler._signingKey` is now an instance field, no longer static mutable (T2.12)
- See `.planning/codebase/CONCERNS.md` for full list

### Service Infrastructure

#### Port Configuration
- **BrokerService** reads port from `appsettings.json` → `Broker:Port` (default 9100)
- **PortChecker** (`Adam.Shared.Services.PortChecker`): Static utility to check port availability
  - `IsPortFree(int port)` / `IsPortInUse(int port)` — checks via `TcpListener(IPAddress.Any, port)`
  - `FindFreePort(int preferredPort, int maxAttempts)` — scans sequentially, returns first free port
- **TcpListenerHostedService** reads `Broker:Port` from `IConfiguration` at startup
- **ServiceManager** provides a `NumericUpDown` (1-65535) bound to `ServicePort` for server-side configuration
- **AdamConfig** persists `ServiceHost`/`ServicePort` to `%LOCALAPPDATA%/Adam/CatalogBrowser/settings.json` (Windows) or `~/.local/share/Adam/CatalogBrowser/settings.json` (Linux)

#### Service Installers
`IServiceInstaller` interface (`Adam.Shared.Services`) provides platform-specific installation:
- **WindowsServiceInstaller** — Uses `sc.exe` for service management. Before install: checks port via `PortChecker`, alerts if in use. After install: adds Windows Firewall rule via `FirewallRuleManager`. Uninstall: removes the firewall rule.
- **LinuxServiceInstaller** — Uses `systemctl` for systemd service management (install/uninstall/start/stop/status). Accepts broker path and port.
- **MacOsServiceInstaller** — Uses `launchctl` for launchd service management (install/uninstall/start/stop/status). Accepts broker path and port.
- **NullServiceInstaller** — Fallback when no platform installer is available; throws `PlatformNotSupportedException`

#### Firewall Rules
`FirewallRuleManager` (`Adam.Shared.Services`) manages Windows Firewall inbound rules via `netsh advfirewall`:
- `AddRuleAsync(int port, CancellationToken)` — Creates rule named "Adam Broker Service (TCP)"
- `RemoveRuleAsync(CancellationToken)` — Removes the rule
- `RuleExistsAsync(CancellationToken)` — Checks if the rule exists
- Gracefully degrades on non-Windows (checks `OperatingSystem.IsWindows()`) and when netsh is unavailable (e.g., non-elevated context, Nano Server)
- Rule name: "Adam Broker Service (TCP)", description includes the specific port number

#### AI Image Tagging (Phase 9)

`LiquidVision.Core` (in-repo) provides local LFM2-VL ONNX model inference for auto-tagging images. No Python or cloud dependency.

**Configuration:**
- `AddLiquidVision()` in `CatalogBrowser/App.axaml.cs` with `Precision = Q4F16`, `ExecutionProvider = Cpu`
- `AiTaggingService` registered as singleton in DI

**Three triggers:**
1. **Ingest opt-in** (`EnableAiTagging` checkbox in `IngestionViewModel`) — runs as a sequential post-pass after parallel ingest completes (never inside `Parallel.ForEachAsync`)
2. **Per-asset Auto-tag** (`AutoTagCommand` in `MetadataEditorViewModel`) — unions keywords into editable tags, fills description when empty, applies categories directly to DB
3. **Bulk re-tag** (`AiTagSelectedCommand` in gallery) — filters to images, sequential, status bar progress

**Model download progress** is surfaced via `AiTaggingService.PropertyChanged` → `StatusBarViewModel.IsModelDownloading`/`ModelDownloadPercentage`

**Testing:**
```bash
dotnet test tests/Adam.Shared.Tests --filter "FullyQualifiedName~AiTagging"
```
7 unit tests cover image-only guard, keyword/category merge, description fill, cancellation, batch progress, and analyze-only path.

### Collection Sort Order (Phase 20)

**DigitalAsset.SortOrder** (`Adam.Shared.Models.DigitalAsset`) is an `int` field that controls custom ordering within a collection. Sorted ascending by default.

**Broker Messages:**
| Contract | Opcode | Direction |
|----------|--------|-----------|
| `ReorderCollectionAssetsRequest` | `MessageTypeCode.ReorderCollectionAssetsRequest = 37` | Client → Broker |
| `ReorderCollectionAssetsResponse` | `MessageTypeCode.ReorderCollectionAssetsResponse = 38` | Broker → Client |

**Handler:** `CollectionHandler.ReorderCollectionAssetsAsync()` — Reorders assets in a collection by updating `SortOrder` for each asset in the collection-scoped order. Assets not in the reorder list keep their existing sort order (they sort after reordered items).

**Gallery reorder trigger:** Double-tap/Enter on an asset opens the Loupe View. (Full drag-reorder UI is planned for a future phase.)

### Loupe View (Phase 20)

The Loupe View provides full-resolution image viewing with pan/zoom and filmstrip navigation for assets within the current gallery selection.

**Components:**
- **ZoomBorder** (`Adam.CatalogBrowser.Controls.ZoomBorder`) — Custom `ContentControl` with:
  - Mouse wheel zoom (configurable `MinZoom`/`MaxZoom`, defaults 0.1–20.0)
  - Click-drag pan
  - Double-click to fit to window
  - `ZoomChanged`/`PanChanged` events for synchronization
  - `BeginBatchUpdate()`/`EndBatchUpdate()` to suppress sync events during programmatic updates
- **LoupeViewModel** (`Adam.CatalogBrowser.ViewModels.LoupeViewModel`) — State management for:
  - Current asset (full-res image via `Bitmap`)
  - Filmstrip navigation through `AllAssets` (←/→/↑/↓ keys)
  - Info overlay: camera, lens, ISO, aperture, shutter speed, dimensions, file name
  - Multi-user mode: metadata comes from `AssetListItem` properties; standalone mode loads metadata from DB
- **LoupeView** (`Adam.CatalogBrowser.Views.LoupeView`) — XAML view with:
  - ZoomBorder for pan/zoom
  - Filmstrip strip at bottom with selection highlight
  - Info overlay panel (auto-hides when no data)
  - Keyboard shortcuts: ← → ↑ ↓ (navigate), Esc (close)

**Navigation:** Set `LoupeViewModel.CurrentAsset = asset` via `OpenAssetRequested` event raised from `AssetGalleryViewModel.RequestOpenAsset()`. The ViewModel computes `_currentIndex` from `AllAssets.IndexOf` by `Id`.

**Wiring:** Two new `DataTemplate`s in `MainWindow.axaml` map `LoupeViewModel` → `LoupeView` and `CompareViewModel` → `CompareView`. Gallery double-tap/Enter fires `OpenAssetRequested` on the ViewModel.

### Compare View (Phase 20)

The Compare View places two assets side-by-side (or overlaid) with synchronized zoom/pan and a metadata difference table.

**Components:**
- **CompareViewModel** (`Adam.CatalogBrowser.ViewModels.CompareViewModel`) — Manages:
  - Left/right `AssetListItem` and `Bitmap`
  - `CompareViewMode` — `SideBySide` (default) or `Overlay` (right image composited over left with opacity slider)
  - `ZoomSyncState` shared between left/right `ZoomBorder` instances
  - `IsSyncEnabled` toggle for independent vs linked zoom/pan
  - `MetadataDiffItem` collection — compares filename, title, dimensions, file size, file type, rating
  - `SwapAssetsCommand`, `ToggleSyncCommand`, `ToggleViewModeCommand`, `CloseCommand`
  - Async image loading with `IsLoadingLeft`/`IsLoadingRight` states
- **CompareView** (`Adam.CatalogBrowser.Views.CompareView`) — XAML with:
  - Toolbar: Swap, Sync toggle, View mode toggle (Side-by-side/Overlay)
  - Two `ZoomBorder` panels with loading overlays
  - Overlay mode: composited images with `Slider` for opacity
  - Metadata diff bar at bottom (scrollable `ItemsControl` with field/value diff icons)

**Converters** (`Adam.CatalogBrowser.Converters.SharedConverters`):
- `NullToBoolConverter` — Returns `true` when value is not null
- `BoolToFilmstripBorderConverter` — Highlights selected filmstrip item
- `BoolToVisibilityConverter` — Converts `bool` to visibility
- `CompareViewModeToButtonTextConverter` — Toggle button label: overlay → "Side by Side", side-by-side → "Overlay"
- `DiffStatusToIconConverter` — Returns `"✓"` or `"✗"` based on `IsDifferent`
- `DiffStatusToColorConverter` — Returns `"#4CAF50"` (green) or `"#FF5252"` (red)

### Design Templates (Phase 20)

`.design_templates/` — 5 markdown files providing UI design guidance for new features:
- `README.md` — Overview and usage guide
- `design_token_reference.md` — Color palette, typography, spacing, elevation tokens
- `component_style_guide.md` — Reusable component patterns (buttons, inputs, cards, badges, etc.)
- `layout_patterns.md` — Common layout grids and panel patterns
- `accent_palettes.md` — Alternative accent color palettes (teal, amber, rose, violet, emerald)

Use these templates as a reference when building new UI components to maintain visual consistency across the application.

### File Streaming
- **GetFileRequest** / **GetFileResponse** — Protobuf contracts for retrieving file bytes from BrokerService
- **AssetHandler.GetFileAsync** — Looks up asset in DB, verifies `StoragePath` exists on disk, reads all bytes, returns metadata (filename, extension, MIME type, size, checksum) + content as `ByteString`
- Error codes: 5 (asset not found), 6 (file missing on disk), 7 (forbidden), 13 (read failure)

#### Transport Protocol
All multi-user communication uses raw TCP with length-prefixed protobuf framing (`TcpFrame`). No HTTP/REST/gRPC. Message types are in `Adam.Shared/Contracts/` with manual protobuf serialization.
- **Envelope** — Wraps all messages with `MessageType` (opcode), `Payload` (serialized contract), `StatusCode`, `CorrelationId`, `AuthToken`, `ConnectionId`
- **Max payload size**: 256 MB (enforced by `TcpFrame.ReceiveAsync`)
- **Timeouts**: 5-minute receive timeout, 30-second send timeout

### Testing
Run all tests before committing:
```bash
dotnet test
```

Run specific test suites:
```bash
dotnet test tests/Adam.Shared.Tests         # 137 tests (core services, AI tagging, auth)
dotnet test tests/Adam.BrokerService.Tests  # 92 tests (90 pass, 2 skipped w/o Docker)
dotnet test tests/Adam.ServiceManager.Tests # 156 tests (admin panel, user mgmt, audit log)
dotnet test tests/Adam.CatalogBrowser.Tests # headless Avalonia tests
```

Run BrokerService tests by category (filter by test name):
```bash
dotnet test tests/Adam.BrokerService.Tests --filter "FullyQualifiedName~ConfigurablePort"
dotnet test tests/Adam.BrokerService.Tests --filter "FullyQualifiedName~FileStreaming"
dotnet test tests/Adam.BrokerService.Tests --filter "FullyQualifiedName~Collections"
dotnet test tests/Adam.BrokerService.Tests --filter "FullyQualifiedName~DbProvider"
```

Run AI Tagging tests:
```bash
dotnet test tests/Adam.Shared.Tests --filter "FullyQualifiedName~AiTagging"
```

**Total: 1,232 tests pass** (2 Docker-dependent skipped for PostgreSQL/SQL Server integration)

### Testing ServiceManager ViewModels

The `UserManagementViewModel` (and other ViewModels that interact with `Dispatcher.UIThread`) requires special test infrastructure to avoid hangs in test context.

#### IUiDispatcher Pattern

ViewModels that dispatch work to the UI thread should accept an optional `IUiDispatcher` parameter (defaulting to `AvaloniaUiDispatcher`). Tests provide a `SyncUiDispatcher` that runs actions synchronously:

```csharp
internal sealed class SyncUiDispatcher : IUiDispatcher
{
    public Task InvokeAsync(Action action)
    {
        action();
        return Task.CompletedTask;
    }
    public void Post(Action action) => action();
    public bool CheckAccess() => true;
}
```

The ViewModel constructor pattern:
```csharp
public MyViewModel(..., IUiDispatcher? dispatcher = null)
{
    _dispatcher = dispatcher ?? new AvaloniaUiDispatcher();
}
```

#### Seeded Roles

`AppDbContext.SeedData()` seeds 3 default roles on `EnsureCreatedAsync()`. All SQLite databases include these roles from the start:

| Id | Name | Permissions |
|-----|------|-------------|
| `...0001` | Viewer | `asset:read`, `collection:read` |
| `...0002` | Editor | `asset:read`, `asset:create`, `asset:update`, `collection:read`, `collection:update` |
| `...0003` | Administrator | `asset:*`, `collection:*`, `user:*`, `role:*`, `audit:read` |

**Test implications**:
- `LoadUsersAsync()` on a fresh database always returns 3 roles and 0 users.
- Use unique role names (e.g., `"Operator"`, `"Analyst"`) when calling `SeedRoleAsync()` in tests to avoid `UNIQUE constraint` violations.
- Existing role names like `"Admin"` are safe (≠ `"Administrator"`), but `"Viewer"` and `"Editor"` conflict with seeded data.

#### Isolated Database Per Test

Each test gets its own SQLite database at a unique temp path:
```csharp
_basePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
_modeManager = new ModeManager(_basePath);
await _modeManager.InitializeAsync(); // → creates DB at {basePath}/.adam/catalog.db
```

Cleanup via `IDisposable`:
```csharp
public void Dispose()
{
    SqliteConnection.ClearAllPools();
    Directory.Delete(_basePath, recursive: true); // catches IOException
}
```

#### Testing Private Async Methods

Use reflection helpers when methods are `private` (not `public`):
```csharp
private async Task InvokeSaveUserAsync()
{
    var method = typeof(UserManagementViewModel)
        .GetMethod("SaveUserAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
    await (Task)method.Invoke(_vm, null)!;
}
```

Internal VM state (fields like `_editUserId`) is set via reflection:
```csharp
private void SetField(string fieldName, object? value)
{
    var field = typeof(UserManagementViewModel)
        .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!;
    field.SetValue(_vm, value);
}
```

#### Test Coverage Expectations

New ViewModel tests should cover:
- Constructor state (commands, initial values, log entries)
- Property round-trips and `PropertyChanged` notifications
- Command `CanExecute` logic
- `BeginAddUser` / `BeginEditUser` / `CancelEdit` editing state transitions
- Async operations via `SyncUiDispatcher` (load, save, delete)
- Validation rules (empty fields, invalid formats, minimum lengths)
- Database round-trips (verify data persisted via direct `AppDbContext` queries)
- Log message generation during operations

## Workflow Preferences

- **Mode**: YOLO (auto-approve plans, execute directly)
- **Granularity**: Standard (5-8 phases)
- **Agents**: Research, Plan Check, and Verifier enabled
- **Commit docs**: Yes (planning docs tracked in git)

<!-- GSD END -->
