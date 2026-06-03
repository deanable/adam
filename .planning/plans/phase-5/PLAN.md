# Plan: Phase 5 — Admin Panel & Mode Management

**Phase:** 5 of 7  
**Goal:** Administrators can toggle modes, deploy the broker as a native service, and migrate databases  
**Requirements:** ADMIN-01, ADMIN-02, ADMIN-03, ADMIN-04  
**Estimated Effort:** ~8 days  
**Created:** 2026-06-03  

## Overview

Phase 5 consolidates scattered admin features into a single Admin Panel dashboard, implements full cross-platform service installers, and adds in-app service status monitoring. The admin panel provides mode toggling, database migration, service status, and a launch point for the Service Manager application.

## Prerequisites & Existing Assets

| Asset | Location | Status |
|-------|----------|--------|
| Mode toggle (Local/Service) | `MainWindow.axaml` title bar | ✅ Exists — **will move into Admin Panel** |
| Login dialog | `LoginDialogView` + `LoginDialogViewModel` | ✅ Complete |
| Migration wizard UI | `MigrationWizardView.axaml` + `MigrationWizardViewModel` | ✅ Complete — **reuse as sub-view** |
| Migration backend | `DbMigrationService` in BrokerService | ✅ Complete |
| Service Manager app | `Adam.ServiceManager` project | ✅ Complete — **launch from Admin Panel** |
| Windows service installer | `WindowsServiceInstaller` | ✅ Complete |
| Linux/macOS installers | `LinuxServiceInstaller` / `MacOsServiceInstaller` | ❌ Stubs — **to implement** |
| Service status handler | `StatusHandler` in BrokerService | ✅ Complete |
| Service status protobuf | `GetServiceStatusResponse` | ✅ Complete |
| Health indicator | `ServiceManagerViewModel` traffic-light | ✅ Complete |
| Elevation/relaunch | `ServiceManagerViewModel.RelaunchAsAdminAsync` | ✅ Complete |

## Tasks

### T5.1 — Admin Panel ViewModel

**Goal:** Create an `AdminPanelViewModel` that consolidates admin features into a single dashboard.

**Details:**
- Create `AdminPanelViewModel` in `src/Adam.CatalogBrowser/ViewModels/`
- Properties:
  - `Mode` (Local/Service) with toggle command
  - `ServiceHost` / `ServicePort` (editable)
  - `ConnectionStatus` (connected/disconnected/reconnecting)
  - `ServiceStatus` — fetched from broker: `ConnectedClients`, `Uptime`, `ServiceState`
  - `IsMigrationWizardVisible` — toggle to show/hide the migration sub-view
- Commands:
  - `ToggleModeCommand` — switches between standalone and multi-user (reuse existing `ToggleLocalModeCommand` / `ToggleServiceModeCommand` logic)
  - `ConnectCommand` — initiates broker connection + login
  - `DisconnectCommand` — disconnects from broker
  - `LaunchServiceManagerCommand` — launches `Adam.ServiceManager` as separate process (reuse `OpenServiceManager` logic)
  - `RefreshServiceStatusCommand` — polls broker for current status
- Wire to existing `ModeManager`, `BrokerClient`, and `AuthSession`
- Host and port from `App.Config` (existing)

**Files:**
- `src/Adam.CatalogBrowser/ViewModels/AdminPanelViewModel.cs` (new)

**Est:** 6h  
**Depends:** None

---

### T5.2 — Admin Panel View (Avalonia XAML)

**Goal:** Create an `AdminPanelView.axaml` dashboard that lays out all admin features.

**Details:**
- Create `AdminPanelView.axaml` + `.axaml.cs` in `src/Adam.CatalogBrowser/Views/`
- Sections in the dashboard:
  1. **Mode Toggle** — Local/Service radio buttons, host/port fields, Connect/Disconnect button, connection status indicator
  2. **Service Status** — Card showing connected clients count, uptime (formatted), service state with traffic-light indicator. "Refresh" button. "Launch Service Manager" button.
  3. **Database Migration** — Embedded `MigrationWizardView` (reuse existing, wrapped in an Expander or toggle)
- Register DataTemplate in `MainWindow.axaml` for `AdminPanelViewModel`
- Add "Admin" button to the main navigation bar
- Keep mode indicator in the status bar but remove mode toggle from the title bar

**Files:**
- `src/Adam.CatalogBrowser/Views/AdminPanelView.axaml` (new)
- `src/Adam.CatalogBrowser/Views/AdminPanelView.axaml.cs` (new)
- `src/Adam.CatalogBrowser/Views/MainWindow.axaml` (add nav button + DataTemplate; remove mode toggle from title bar)

**Est:** 8h  
**Depends:** T5.1

---

### T5.3 — Linux systemd Service Installer

**Goal:** Implement full `LinuxServiceInstaller` that manages the broker as a systemd service.

**Details:**
- Replace stub in `src/Adam.Shared/Services/LinuxServiceInstaller.cs`
- `ServiceName` → `"adam-broker"`
- `ServiceDisplayName` → `"Adam Broker Service"`
- `InstallAsync(brokerPath, port)`:
  - Write systemd unit file to `/etc/systemd/system/adam-broker.service` using shell commands via `Process.Start("systemctl", ...)` or write file directly with `sudo` elevation prompt
  - Unit file: `Type=simple`, `ExecStart=<brokerPath> --port <port>`, `Restart=on-failure`, `User=<current user>` or configurable
  - Run `systemctl daemon-reload`, `systemctl enable adam-broker.service`, `systemctl start adam-broker.service`
- `UninstallAsync()`: `systemctl stop adam-broker.service`, `systemctl disable adam-broker.service`, delete unit file, `systemctl daemon-reload`
- `StartAsync()`: `systemctl start adam-broker.service`
- `StopAsync()`: `systemctl stop adam-broker.service`
- `GetStatusAsync()`: Parse `systemctl is-active adam-broker.service` → `active` → `Running`, `inactive` → `Stopped`, else → `NotInstalled` or `Unknown`
- `IsSupported` → `OperatingSystem.IsLinux()`
- Use `ILogger` for diagnostics, `PortChecker` for port availability check before install
- Elevation: Check root via `id -u` (UID 0); if not root, attempt to run commands via `pkexec` or `sudo` and throw descriptive error if unavailable

**Files:**
- `src/Adam.Shared/Services/LinuxServiceInstaller.cs` (rewrite stub)
- `tests/Adam.Shared.Tests/Services/ServiceInstallerTests.cs` (add Linux tests)

**Est:** 6h  
**Depends:** None

---

### T5.4 — macOS launchd Service Installer

**Goal:** Implement full `MacOsServiceInstaller` that manages the broker as a launchd service.

**Details:**
- Replace stub in `src/Adam.Shared/Services/MacOsServiceInstaller.cs`
- `ServiceName` → `"com.adam.broker"`
- `InstallAsync(brokerPath, port)`:
  - Write launchd plist to `~/Library/LaunchAgents/com.adam.broker.plist` (user-level) or `/Library/LaunchDaemons/` (system-level)
  - Plist keys: `Label`, `ProgramArguments` (`<brokerPath>`, `--port`, `<port>`), `KeepAlive`, `RunAtLoad`, `StandardOutPath`, `StandardErrorPath`
  - Run `launchctl load <plist_path>` / `launchctl bootstrap gui/<uid> <plist_path>`
- `UninstallAsync()`: `launchctl bootout gui/<uid> <plist_path>`, delete plist file
- `StartAsync()`: `launchctl kickstart -k gui/<uid>/com.adam.broker`
- `StopAsync()`: `launchctl kill SIGTERM gui/<uid>/com.adam.broker`
- `GetStatusAsync()`: Parse `launchctl print gui/<uid>/com.adam.broker` → look for `state = running` → `Running`, `state = not running` → `Stopped`, file missing → `NotInstalled`
- `IsSupported` → `OperatingSystem.IsMacOS()`

**Files:**
- `src/Adam.Shared/Services/MacOsServiceInstaller.cs` (rewrite stub)
- `tests/Adam.Shared.Tests/Services/ServiceInstallerTests.cs` (add macOS tests)

**Est:** 5h  
**Depends:** None

---

### T5.5 — In-App Service Status Display

**Goal:** Fetch and display broker service status (connected clients, uptime, health) in the Admin Panel.

**Details:**
- In `AdminPanelViewModel`, add `RefreshServiceStatusAsync()`:
  - In multi-user mode: send `GetServiceStatusRequest` to broker via existing TCP protocol
  - Parse `GetServiceStatusResponse`: `ActiveConnections`, `UptimeSeconds`, `ServiceState`
  - In standalone mode: status is "N/A — not connected to service"
- Display in Admin Panel view:
  - Connected clients count
  - Uptime (formatted: "2h 15m 30s" or "1d 3h 45m")
  - Service state (Running/Stopped/Unknown) with traffic-light color indicator
- Auto-refresh: poll every 10 seconds when Admin Panel is visible (use a DispatcherTimer, pause when view is not active)
- Handle connection errors gracefully (show "Status unavailable" instead of crashing)

**Files:**
- `src/Adam.CatalogBrowser/ViewModels/AdminPanelViewModel.cs` (add service status properties + refresh logic)
- `src/Adam.CatalogBrowser/Views/AdminPanelView.axaml` (add status card section)

**Est:** 4h  
**Depends:** T5.1, T5.2

---

### T5.6 — Mode Toggle Integration & Navigation

**Goal:** Wire the Admin Panel mode toggle to the full mode-switching flow and update navigation.

**Details:**
- Move mode toggle logic from `MainWindowViewModel` title bar to `AdminPanelViewModel`
- `ToggleModeCommand` calls existing `SwitchToLocalAsync()` / `ShowLoginAndConnectAsync()` methods
- Keep the mode indicator (`ModeManager.Mode`) in the status bar for visibility
- Add "Admin" navigation button to the main window nav bar (alongside Gallery, Ingest, Metadata, Users, Audit)
- Remove the Local/Service toggle buttons from the title bar
- Ensure `AdminPanelViewModel` shares the same `ModeManager` instance via DI

**Files:**
- `src/Adam.CatalogBrowser/ViewModels/MainWindowViewModel.cs` (remove mode toggle from toolbar, keep mode indicator in status bar)
- `src/Adam.CatalogBrowser/ViewModels/AdminPanelViewModel.cs` (add mode toggle wiring)
- `src/Adam.CatalogBrowser/Views/MainWindow.axaml` (add Admin nav button; remove mode toggle buttons; keep mode text in status bar)
- `src/Adam.CatalogBrowser/App.axaml.cs` (register `AdminPanelViewModel` in DI)

**Est:** 4h  
**Depends:** T5.2

---

### T5.7 — Migration Wizard Polish & Verification

**Goal:** Verify the migration wizard works end-to-end and handle edge cases.

**Details:**
- Review `MigrationWizardViewModel` for any gaps:
  - Add browse button functionality for source file selection (currently has no Browse implementation)
  - Validate connection strings before starting migration
  - Add cancellation support
- Verify `DbMigrationService` handles tables in correct order (respect foreign keys): Collections → MetadataProfiles → Keywords → DigitalAssets → Users → Roles → AccessLogs
- Add batch chunking (1000 rows at a time) for large datasets
- Test: Create a SQLite DB with 1000 sample assets, migrate to a new SQLite target, verify all records transferred

**Files:**
- `src/Adam.CatalogBrowser/ViewModels/MigrationWizardViewModel.cs` (browse button, validation, cancellation)
- `src/Adam.BrokerService/Services/DbMigrationService.cs` (batch chunking)

**Est:** 4h  
**Depends:** None (can run in parallel with T5.1)

---

### T5.8 — Tests for Phase 5

**Goal:** Automated tests for new and modified code.

**Details:**
- **LinuxServiceInstallerTests** (safe, non-elevated):
  - `IsSupported_OnNonLinux_ReturnsFalse`
  - `GetStatusAsync_WhenNotInstalled_ReturnsNotInstalled` (mock systemctl output)
  - Service name matches expected
- **MacOsServiceInstallerTests** (safe, non-elevated):
  - `IsSupported_OnNonMacOS_ReturnsFalse`
  - `GetStatusAsync_WhenNotInstalled_ReturnsNotInstalled`
  - Service name matches expected
- **AdminPanelViewModelTests**:
  - Toggle mode commands update `IsServiceMode` correctly
  - Service status refresh parses broker response correctly
  - CanConnect/CanDisconnect logic based on connection state
- **Service status display tests**:
  - `GetServiceStatusRequest` sent over TCP returns expected response
  - Uptime formatting for various durations

**Files:**
- `tests/Adam.Shared.Tests/Services/ServiceInstallerTests.cs` (add Linux + macOS test classes)
- `tests/Adam.CatalogBrowser.Tests/ViewModels/MainWindowViewModelTests.cs` (add admin panel tests)
- `tests/Adam.CatalogBrowser.Tests/ViewModels/AdminPanelViewModelTests.cs` (new)

**Est:** 6h  
**Depends:** T5.3, T5.4, T5.5, T5.6

---

## Dependency Graph

```
T5.1 ──→ T5.2 ──→ T5.6
  │         │
  │         └──→ T5.5
  │
T5.3 ──→ T5.8 (tests)
T5.4 ──→ T5.8 (tests)
T5.7 ──→ T5.8 (tests — can run in parallel with T5.1)
```

**Parallel workstreams:**
- Stream A (Admin Panel): T5.1 → T5.2 → T5.5 → T5.6 → T5.8
- Stream B (Linux installer): T5.3 → T5.8 (tests)
- Stream C (macOS installer): T5.4 → T5.8 (tests)
- Stream D (Migration polish): T5.7 → T5.8 (tests)

## Risk Register

| Risk | Impact | Mitigation |
|------|--------|------------|
| Linux/macOS installer commands differ across distro versions | MEDIUM | Use well-known command flags; test on Ubuntu 22.04+ and macOS 13+; document compatibility |
| Admin Panel becomes too complex | LOW | Keep as a simple dashboard with sub-sections; use existing sub-views (MigrationWizardView) |
| Cannot test Linux/macOS installers on Windows CI | MEDIUM | Unit tests for logic that doesn't invoke system commands; manual testing on target platforms |
| Migration wizard doesn't handle 100K+ assets | MEDIUM | Add batch chunking (1000 rows); test with large dataset |
| Mode toggle removal from title bar confuses users | LOW | Keep mode indicator text in status bar; add tooltips |

## Success Criteria Checklist

- [ ] Admin panel toggles between standalone and multi-user mode and reconnects correctly
- [ ] Database migration wizard exports SQLite to PostgreSQL/SQL Server with progress reporting
- [ ] Linux systemd service installer creates/removes unit files and manages service lifecycle
- [ ] macOS launchd service installer creates/removes plist files and manages service lifecycle
- [ ] Service status in Admin Panel shows accurate connected client count and uptime
- [ ] Service Manager app launches from Admin Panel with one click
- [ ] All 24 existing service installer tests continue to pass
- [ ] New admin panel tests pass

## Completion Definition

Phase 5 is complete when:
1. All tasks T5.1–T5.8 are implemented and committed
2. New features are covered by passing tests
3. Admin Panel is accessible from navigation and fully functional
4. Linux and macOS service installers are implemented (tested on available platforms)
5. Migration wizard has browse, validation, cancellation, and batch support
6. No regression in existing Phase 1–4 functionality
7. `STATE.md` and `REQUIREMENTS.md` are updated

---
*Plan created: 2026-06-03*
