# Structure

**As-of:** 2026-10-05 (branch `main` @ `cab6a78`)
**Re-verify:** `cat Adam.slnx && find src -name "*.cs" | wc -l`

## Solution

`Adam.slnx` (the XML `.slnx` format) is the solution file — an earlier version of this document
incorrectly said none existed.

```
Adam.slnx
├── /src/
│   ├── Adam.Shared            # domain models, EF Core DbContext, contracts, shared services
│   ├── Adam.BrokerService     # TCP broker, auth, handlers, hosting, migrations runner
│   ├── Adam.CatalogBrowser    # Avalonia desktop client (MVVM)
│   ├── Adam.ServiceManager    # server admin panel (users, audit, service mgmt)
│   └── LiquidVision.Core      # in-repo LFM2-VL ONNX vision model
└── /tests/
    ├── Adam.Shared.Tests
    ├── Adam.BrokerService.Tests
    ├── Adam.CatalogBrowser.Tests
    └── Adam.ServiceManager.Tests
```

**Not in the solution:** `src/Adam.Generators/` — a source generator referenced by
`Adam.Shared.csproj` as an analyzer (`OutputItemType="Analyzer"`). It builds via the project
reference but does not appear in solution-scoped tooling. See `CONCERNS.md` §3.

## Repository Layout

```
adam/
├── Adam.slnx
├── AGENTS.md                      # contributor + agent guidance
├── .planning/                     # GSD planning + codebase map
├── specs/                         # feature specifications
│   ├── 001-digital-asset-management/
│   ├── 001-thumbnail-extraction-pipeline/
│   ├── 002-control-definitions/
│   └── 003-ai-engine/
├── docs/                          # admin guide, user guide, wire protocol, migration guide
├── .design_templates/             # design tokens + component style guide
├── scripts/                       # test.sh, publish.sh, migrate/, benchmark/
├── src/
│   ├── Adam.Shared/
│   │   ├── Models/                # domain entities
│   │   ├── Data/                  # AppDbContext
│   │   ├── Migrations/            # EF Core migrations + model snapshot
│   │   ├── Contracts/             # protobuf message types + opcode enum
│   │   ├── Services/              # ModeManager, metadata, thumbnail, search, AI, write-back
│   │   ├── Configuration/         # DbProviderConfig
│   │   └── Transport/             # TcpFrame (length-prefixed framing)
│   ├── Adam.BrokerService/
│   │   ├── Handlers/              # message handlers + MessageDispatcher, AuthorizationMiddleware
│   │   ├── Transport/             # TcpListenerService, ConnectionRegistry
│   │   ├── Data/                  # MigrationRunner
│   │   ├── Hosting/               # OS service installers (Windows/macOS/Linux)
│   │   └── Services/              # DbMigrationService, FolderWatcher
│   ├── Adam.CatalogBrowser/
│   │   ├── Views/                 # Avalonia views
│   │   ├── ViewModels/            # MVVM view models (30 files)
│   │   ├── Services/              # BrokerClient, AuthSession, AdamConfig, ModeManager consumers
│   │   └── Controls/              # custom Avalonia controls (incl. ZoomBorder)
│   ├── Adam.ServiceManager/
│   └── LiquidVision.Core/
└── tests/                         # 4 test projects
```

## File Counts (measured)

| Area | Value |
|------|-------|
| `.cs` files under `src/` | 321 |
| Lines of C# under `src/` (incl. generated migrations) | ~57,400 |
| Test files | 107 |
| Test methods (`[Fact]`/`[Theory]`) | ~1,310 |

## Notable Patterns

- **Handler-per-Message-Type**: each protobuf message type has a dedicated handler in
  `Adam.BrokerService`, dispatched through `MessageDispatcher` (`HandlerBase` provides shared
  null-payload guards).
- **View-per-Screen**: each major screen has a matching `.axaml` + ViewModel.
- **`IUiDispatcher` injection**: ViewModels take an optional dispatcher so headless tests can
  substitute a synchronous one. See `TESTING.md` — omitting it hangs test runs.
- **Dual-mode via `ModeManager`**: standalone SQLite vs multi-user TCP broker, selected at runtime.
