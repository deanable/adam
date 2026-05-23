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
- **Port mismatch**: `BrokerClient` hardcodes `localhost:5000` but `TcpListenerService` uses port `9100`
- **Static JWT key**: `AuthHandler._signingKey` is static mutable — potential race condition
- See `.planning/codebase/CONCERNS.md` for full list

### Testing
Run tests before committing:
```bash
dotnet test
```

Integration tests use Testcontainers for database provider matrix testing.

## Workflow Preferences

- **Mode**: YOLO (auto-approve plans, execute directly)
- **Granularity**: Standard (5-8 phases)
- **Agents**: Research, Plan Check, and Verifier enabled
- **Commit docs**: Yes (planning docs tracked in git)

<!-- GSD END -->
