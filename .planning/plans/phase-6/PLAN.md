---
goal: Validate and optimize the multi-provider backend for production environments.
version: 1.0
date_created: 2026-06-09
last_updated: 2026-06-09
status: 'Complete'
tags: [database, providers, infrastructure, testing]
---

# Phase 6: Database Provider Matrix

![Status: Complete](https://img.shields.io/badge/status-Complete-green)

This phase validates and optimizes the multi-provider backend for production environments. Phase 5 moved `DbProviderConfig` to `Adam.Shared`; this phase hardens schema generation, migration SQL, and integration testing across all three supported providers — SQLite, PostgreSQL, and SQL Server.

**Dependencies:** Phase 5 (Shared Data Foundation — `DbProviderConfig` in `Adam.Shared`)

## 1. Requirements & Constraints

- **DB-02**: System supports PostgreSQL provider for multi-user production
- **DB-03**: System supports SQL Server provider for multi-user enterprise
- **DB-04**: Provider selection is configuration-only (no code changes required)
- **CON-001**: All provider-specific SQL must be encapsulated in helper functions, not scattered across code
- **CON-002**: Integration tests must run automatically when Docker is available and skip gracefully when it is not
- **PAT-001**: EF Core `OnModelCreating` must detect the active provider at runtime, not via conditional compilation

## 2. Implementation Steps

### Work Stream 1: Provider-Aware Schema Generation

**GOAL:** `AppDbContext.OnModelCreating` generates correct SQL for each provider's filtered index syntax.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| T6.1 | Fix `HasFilter("NOT IsDeleted")` in `AppDbContext.OnModelCreating` — use `Database.ProviderName` to emit provider-correct SQL: SQLite → `NOT IsDeleted`, PostgreSQL → `"IsDeleted" = FALSE`, SQL Server → `[IsDeleted] = 0` | ✅ | 2026-06-08 |

### Work Stream 2: Provider-Aware Migration SQL

**GOAL:** `MigrationRunner.MigrateSchemaAsync` uses a provider-aware `Col()` local function that generates correct ALTER TABLE syntax for each provider.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| T6.2 | Replace raw SQL in `MigrationRunner.MigrateSchemaAsync` with `Col()` local function that generates correct ALTER TABLE quoting: SQLite (unquoted), PostgreSQL (double-quoted), SQL Server (bracketed).`WatchedFolders` table creation per-provider (UUID, varchar/TEXT types). | ✅ | 2026-06-08 |

### Work Stream 3: Docker Integration Tests

**GOAL:** Conditional Docker integration tests that auto-run when Docker is available and skip otherwise.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| T6.3 | No change needed — `ModeManager.ApplyMigrationsAsync` is standalone-only (always SQLite) | ✅ | 2026-06-08 |
| T6.4 | Create `DockerAvailability` helper + `DockerFactAttribute` for conditional Testcontainers test execution. PostgreSQL and SQL Server integration tests auto-run/skip based on Docker availability. | ✅ | 2026-06-08 |
| T6.5 | Add `DbProvider: "sqlite"` and `DbConnection: "Data Source=catalog.db"` defaults to `appsettings.json` | ✅ | 2026-06-08 |
| T6.6 | Add `DbProviderConfig_Configure_builds_options` theory test — verifies correct EF Core provider extension (Sqlite/Npgsql/SqlServer) is registered for each provider string | ✅ | 2026-06-08 |

## 3. Alternatives

- **ALT-001**: Use EF Core's built-in `HasFilter` with a single expression. (Rejected — EF Core cannot translate `x => !x.IsDeleted` into provider-correct SQL for filtered unique indexes across all three providers.)
- **ALT-002**: Maintain separate `OnModelCreating` overrides per provider. (Rejected — violates DRY; the ternary approach is contained in one place.)
- **ALT-003**: Use `IConfiguration` at migration time for provider detection. (Adopted — `DbProviderConfig` is injected into `MigrationRunner`, making the `Col()` function provider-aware without environment detection.)

## 4. Dependencies

- **DEP-001**: `Testcontainers.PostgreSql` 4.x — PostgreSQL container management for integration tests
- **DEP-002**: `Testcontainers.MsSql` 4.x — SQL Server container management for integration tests
- **DEP-003**: Docker runtime (optional) — required for PostgreSQL/SQL Server integration tests; tests auto-skip when Docker is unavailable
- **DEP-004**: `xunit` `FactAttribute` subclass (`DockerFactAttribute`) — conditional test execution via `DockerAvailability.IsAvailable`

## 5. Files

| File | Role |
|------|------|
| `src/Adam.Shared/Data/AppDbContext.cs` | Provider-aware `HasFilter` in `OnModelCreating` (lines 60-66) |
| `src/Adam.BrokerService/Data/MigrationRunner.cs` | `Col()` local function + per-provider `WatchedFolders` table creation (lines 44-130) |
| `tests/Adam.BrokerService.Tests/Infrastructure/DockerAvailability.cs` | Static `IsAvailable` check via `docker ps` |
| `tests/Adam.BrokerService.Tests/Infrastructure/DockerFactAttribute.cs` | `FactAttribute` subclass that skips when Docker unavailable |
| `tests/Adam.BrokerService.Tests/Integration/DbProviderMatrixTests.cs` | `DbProviderConfig_Configure_builds_options` theory + `DbProviderConfig_supports_all_providers` theory |
| `src/Adam.BrokerService/appsettings.json` | Default `DbProvider` and `DbConnection` entries |

## 6. Testing

| Test | Type | Command |
|------|------|---------|
| `DbProviderConfig_Configure_builds_options` | Unit (theory) | `dotnet test tests/Adam.BrokerService.Tests --filter "FullyQualifiedName~DbProvider"` |
| `DbProviderConfig_supports_all_providers` | Unit | (same filter) |
| `Can_use_postgresql_with_testcontainers` | Integration (Docker) | (same filter; requires Docker) |
| `Can_use_sqlserver_with_testcontainers` | Integration (Docker) | (same filter; requires Docker) |

**Quick command:** `dotnet test tests/Adam.BrokerService.Tests --filter "FullyQualifiedName~DbProvider"`

**Expected results:** 8 tests pass, 2 skipped (when Docker unavailable)

## 7. Risks & Assumptions

- **RISK-001**: Docker not available in CI environment → handled via `DockerFactAttribute` auto-skip; integration tests are supplementary, not blocking
- **RISK-002**: EF Core 10 preview may change `Database.ProviderName` behavior → monitor for RC/RTM; `StringComparison.OrdinalIgnoreCase` guard handles case variance
- **ASSUMPTION-001**: All three providers use the same EF Core `AppDbContext` model configuration — only the filtered index syntax and migration ALTER TABLE SQL differ per provider
- **ASSUMPTION-002**: The `Col()` local function's try/catch pattern is acceptable for migration idempotency (column-already-exists is not an error)

## 8. Related Specifications / Further Reading

- `.planning/REQUIREMENTS.md` — DB-02, DB-03, DB-04 traceability
- `.planning/milestones/v1.1-audit-report.md` — Phase 6 deliverable verification
- `src/Adam.Shared/Configuration/DbProviderConfig.cs` — Shared DB configuration
- `src/Adam.Shared/Data/AppDbContext.cs` — Provider-aware filtered index
- `src/Adam.BrokerService/Data/MigrationRunner.cs` — Provider-aware migration SQL
