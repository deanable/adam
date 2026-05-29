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
- `/gsd-discuss-phase 1` — Gather context before planning Phase 1
- `/gsd-plan-phase 1` — Create detailed plan for Phase 1
- `/gsd-execute-phase 1` — Execute all plans in Phase 1
- `/gsd-verify-work` — Validate completed features against requirements
- `/gsd-code-review 1` — Review code changes in Phase 1

## Project-Specific Guidance

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
- ~~Port mismatch~~ **RESOLVED**: `BrokerClient` now receives host/port from `AdamConfig` (configurable via AdminPanel UI / `settings.json`). No longer hardcoded.
- **Static JWT key**: `AuthHandler._signingKey` is static mutable — potential race condition
- See `.planning/codebase/CONCERNS.md` for full list

### Service Infrastructure

#### Port Configuration
- **BrokerService** reads port from `appsettings.json` → `Broker:Port` (default 9100)
- **PortChecker** (`Adam.Shared.Services.PortChecker`): Static utility to check port availability
  - `IsPortFree(int port)` / `IsPortInUse(int port)` — checks via `TcpListener(IPAddress.Any, port)`
  - `FindFreePort(int preferredPort, int maxAttempts)` — scans sequentially, returns first free port
- **TcpListenerHostedService** reads `Broker:Port` from `IConfiguration` at startup
- **AdminPanelView** in CatalogBrowser exposes a `NumericUpDown` (1-65535) bound to `ServicePort` for user configuration
- **AdamConfig** persists `ServiceHost`/`ServicePort` to `%LOCALAPPDATA%/Adam/CatalogBrowser/settings.json` (Windows) or `~/.local/share/Adam/CatalogBrowser/settings.json` (Linux)

#### Service Installers
`IServiceInstaller` interface (`Adam.Shared.Services`) provides platform-specific installation:
- **WindowsServiceInstaller** — Uses `sc.exe` for service management. Before install: checks port via `PortChecker`, alerts if in use. After install: adds Windows Firewall rule via `FirewallRuleManager`. Uninstall: removes the firewall rule.
- **LinuxServiceInstaller** / **MacOsServiceInstaller** — Accept the port parameter as a no-op for future implementation (platform stubs)
- **NullServiceInstaller** — Fallback when no platform installer is available; throws `PlatformNotSupportedException`

#### Firewall Rules
`FirewallRuleManager` (`Adam.Shared.Services`) manages Windows Firewall inbound rules via `netsh advfirewall`:
- `AddRuleAsync(int port, CancellationToken)` — Creates rule named "Adam Broker Service (TCP)"
- `RemoveRuleAsync(CancellationToken)` — Removes the rule
- `RuleExistsAsync(CancellationToken)` — Checks if the rule exists
- Gracefully degrades on non-Windows (checks `OperatingSystem.IsWindows()`) and when netsh is unavailable (e.g., non-elevated context, Nano Server)
- Rule name: "Adam Broker Service (TCP)", description includes the specific port number

#### File Streaming
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
dotnet test tests/Adam.BrokerService.Tests
```

Run BrokerService tests by category (filter by test name):
```bash
dotnet test tests/Adam.BrokerService.Tests --filter "FullyQualifiedName~ConfigurablePort"
dotnet test tests/Adam.BrokerService.Tests --filter "FullyQualifiedName~FileStreaming"
dotnet test tests/Adam.BrokerService.Tests --filter "FullyQualifiedName~Collections"
```

Run CatalogBrowser headless tests (uses Avalonia.Headless):
```bash
dotnet test tests/Adam.CatalogBrowser.Tests
```

Integration tests use Testcontainers for database provider matrix testing. 2 tests are skipped when Docker is unavailable.

## Workflow Preferences

- **Mode**: YOLO (auto-approve plans, execute directly)
- **Granularity**: Standard (5-8 phases)
- **Agents**: Research, Plan Check, and Verifier enabled
- **Commit docs**: Yes (planning docs tracked in git)

<!-- GSD END -->
