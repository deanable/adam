# Phase 5 Context: Admin Panel & Mode Management

**Phase:** 5 of 7
**Goal:** Administrators can toggle modes, deploy the broker as a native service, and migrate databases
**Requirements:** ADMIN-01, ADMIN-02, ADMIN-03, ADMIN-04
**Created:** 2026-06-03

## Prior Context (from earlier phases)

- Mode toggle (Local/Service) already exists in `MainWindow.axaml` title bar
- Full login dialog with host/port/username/password exists
- `MigrationWizardView` + `MigrationWizardViewModel` UI exists
- `DbMigrationService` backend implementation in BrokerService exists (reads from SQLite, writes to target via EF Core)
- `Adam.ServiceManager` separate app exists with full install/uninstall/start/stop UI and traffic-light health indicator
- `StatusHandler` in BrokerService returns activeConnections, rejectedConnections, uptimeSeconds, port, serviceState
- `GetServiceStatusResponse` protobuf contract complete
- `WindowsServiceInstaller` is complete (sc.exe + firewall management)
- `LinuxServiceInstaller` and `MacOsServiceInstaller` are currently stubs (throw PlatformNotSupportedException)
- Elevation/relaunch flow exists for Windows UAC

## Decisions (from discuss-phase)

### 1. Admin Panel Structure: Consolidated Dashboard
**Decision:** Create a single **Admin Panel** view accessible from the main navigation that consolidates:
- Mode toggle (standalone ↔ multi-user) — currently in title bar, move into admin panel
- Database migration wizard
- Service status overview (connected clients, uptime, health)
- Link to launch the Service Manager application
- Keep basic mode indicator in the status bar, but move the toggle into the admin panel

### 2. Service Installers: Implement All Three Platforms
**Decision:** Implement full native service installers for all platforms:
- **Windows**: Already complete (WindowsServiceInstaller)
- **Linux**: Implement systemd service installer (create unit file, enable/disable, start/stop via systemctl)
- **macOS**: Implement launchd service installer (create plist, load/unload via launchctl)
- All installers should support InstallAsync, UninstallAsync, StartAsync, StopAsync, GetStatusAsync

### 3. Service Status: Both In-App and ServiceManager
**Decision:** Show service status in both places:
- **CatalogBrowser Admin Panel**: Show basic status in a "Service Status" section — connected clients count, uptime, service state (running/stopped)
- **ServiceManager app**: Full management UI (install, uninstall, start, stop, logs, admin elevation) — launched from Admin Panel via button
- The Admin Panel fetches status from BrokerService via `GetServiceStatusRequest` TCP message

### 4. Database Migration
- Migration wizard UI already exists; backend (`DbMigrationService`) already exists
- Verify migration works end-to-end and handles large datasets (100K+ assets)
- Add migration progress to Admin Panel (reuse existing `MigrationWizardView` as a sub-view)

## Scope Boundaries

### In Scope (Phase 5)
- Consolidate admin features into a single Admin Panel view
- Add Admin Panel navigation item to main window
- Implement Linux (systemd) and macOS (launchd) service installers
- Add service status display to CatalogBrowser Admin Panel
- Wire Admin Panel mode toggle to existing ModeManager
- Verify and polish migration wizard flow
- Tests for new installers, admin panel view, and service status display
- Update STATE.md and REQUIREMENTS.md on completion

### Out of Scope (deferred)
- RBAC enforcement for admin-only access to admin panel (Phase 7)
- Advanced service metrics (CPU/memory usage, request rates)
- Multi-server dashboard
- OAuth/SSO for service authentication

## Risk Register

| Risk | Impact | Mitigation |
|------|--------|------------|
| Linux/macOS installer testing | MEDIUM | Implement with unit tests; manual testing on real platforms needed before release |
| Admin panel view becomes too complex | LOW | Keep it as a simple dashboard with links to sub-views (migration wizard, service manager) |
| Service status polling adds network overhead | LOW | Poll infrequently (every 10s) or only when admin panel is visible |
| Migration wizard doesn't handle 100K assets | MEDIUM | Batch migration in chunks of 1000; test with large dataset |

---
*Context created: 2026-06-03*
