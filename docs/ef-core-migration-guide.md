# EF Core 10 Migration Guide

> **Phase 13, T13.7** — EF Core 10 Stable Migration Preparation
>
> Created: 2026-06-14
> Status: Prep complete — ready for package update when stable release is adopted

---

## 1. Package Inventory

### Active EF Core Packages

| Package | Projects | Current Version | Latest Stable (2026-06-14) |
|---------|----------|----------------|---------------------------|
| `Microsoft.EntityFrameworkCore.Sqlite` | `Adam.Shared`, `Adam.BrokerService` | `10.0.0-preview.3.25171.6` | `10.0.9` |
| `Microsoft.EntityFrameworkCore.SqlServer` | `Adam.Shared`, `Adam.BrokerService` | `10.0.0-preview.3.25171.6` | `10.0.9` |
| `Microsoft.EntityFrameworkCore.Design` | `Adam.BrokerService` | `10.0.0-preview.3.25171.6` | `10.0.9` |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | `Adam.Shared`, `Adam.BrokerService` | `10.0.0-preview.3` | `10.0.2` |

### Transitive / Tooling Dependencies

| Package | Projects | Current Version | Notes |
|---------|----------|----------------|-------|
| `Microsoft.Data.Sqlite` | `scripts/migrate` | `10.0.0-preview.3.25171.6` | Used by standalone migration script |
| `Microsoft.Extensions.Configuration.Abstractions` | `Adam.Shared` | `10.0.8` | Stable — not EF Core, no change needed |
| `Microsoft.Extensions.Hosting` | `Adam.BrokerService` | `10.0.7` | Stable — no change needed |
| `Microsoft.Extensions.DependencyInjection` | `Adam.CatalogBrowser`, `Adam.ServiceManager` | `10.0.8` | Stable — no change needed |

### Test Dependencies (Docker-based integration tests)

| Package | Project | Current Version | Notes |
|---------|---------|----------------|-------|
| `Testcontainers.MsSql` | `Adam.BrokerService.Tests` | `4.11.0` | Latest: `4.12.0` |
| `Testcontainers.PostgreSql` | `Adam.BrokerService.Tests` | `4.11.0` | Latest: `4.12.0` |

---

## 2. Known Breaking Changes (EF Core 9 → 10)

Based on the EF Core 10 release notes and migration guides, the following areas may require attention:

### 2.1 `HasFilter` SQL Dialect Incompatibility

**Current code** (`src/Adam.Shared/Data/AppDbContext.cs:64`):
```csharp
e.HasIndex(x => x.ChecksumSha256).IsUnique().HasFilter("NOT IsDeleted");
```

**Issue:** `"NOT IsDeleted"` is SQLite-specific syntax. It causes:
- **SQL Server**: `Incorrect syntax near the keyword 'NOT'`
- **PostgreSQL**: `column "isdeleted" does not exist` (case sensitivity)

**Fix:** After upgrading, either:
1. Remove the filter and use application-level enforcement, or
2. Use provider-specific model configuration:
   ```csharp
   if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
       e.HasIndex(x => x.ChecksumSha256).IsUnique().HasFilter("NOT IsDeleted");
   ```

**Priority:** HIGH — Blocks PostgreSQL/SQL Server integration tests from passing.

### 2.2 `EnsureCreatedAsync` vs Migrations

**Current code uses** `Database.EnsureCreatedAsync()` extensively in:
- `DbMigrationService.cs`
- `DbProviderMatrixTests.cs`
- `FolderWatcherHostedService.cs` (indirectly via `AppDbContext`)

**EF Core 10 change:** `EnsureCreated` API is stable but the generated schema may differ between preview and stable. After upgrading:
1. Delete all existing migration files (`src/Adam.Shared/Migrations/`)
2. Run `dotnet ef migrations add InitialSchema` to regenerate for stable
3. Update seed data if schema changes

**Priority:** MEDIUM — `EnsureCreated` will still work, but migrations should match stable schema.

### 2.3 Npgsql PostgreSQL Provider

The Npgsql provider (`Npgsql.EntityFrameworkCore.PostgreSQL`) is also in preview (`10.0.0-preview.3`). The stable version (`10.0.2`) is compatible with EF Core 10.0.9.

**Check compatibility:** Ensure the Npgsql provider version matches the EF Core major version (both 10.x).

**Priority:** MEDIUM — Both must be updated together.

### 2.4 Query Filters with Default Values

**Current code** (`AppDbContext.cs:58,75`):
```csharp
e.Property(x => x.IsDeleted).HasDefaultValue(false);
e.HasQueryFilter(x => !x.IsDeleted);
```

**EF Core 10 note:** EF Core 10 may change how query filters interact with default value constraints. The `HasDefaultValue(false)` combined with `HasQueryFilter(x => !x.IsDeleted)` should continue to work, but verify after upgrade.

**Priority:** LOW — Unlikely to break in practice.

### 2.5 `IProtoSerializable` / Custom Value Comparers

The project uses manual protobuf serialization for the wire protocol (`Adam.Shared/Contracts/`). EF Core 10 may change internal serialization APIs that affect custom value converters or comparers.

**Audit:** No EF Core value converters or value comparers are currently registered. No action needed.

**Priority:** NONE — Not applicable.

---

## 3. Upgrade Plan

### Step 1 — Prerequisites (done: T13.7)
- ✅ Document current package versions
- ✅ Identify breaking changes
- ✅ Create migration test plan

### Step 2 — Package Update
```bash
# Update EF Core packages (stable 10.0.9)
dotnet add src/Adam.Shared/Adam.Shared.csproj package Microsoft.EntityFrameworkCore.Sqlite --version 10.0.9
dotnet add src/Adam.Shared/Adam.Shared.csproj package Microsoft.EntityFrameworkCore.SqlServer --version 10.0.9
dotnet add src/Adam.BrokerService/Adam.BrokerService.csproj package Microsoft.EntityFrameworkCore.Sqlite --version 10.0.9
dotnet add src/Adam.BrokerService/Adam.BrokerService.csproj package Microsoft.EntityFrameworkCore.SqlServer --version 10.0.9
dotnet add src/Adam.BrokerService/Adam.BrokerService.csproj package Microsoft.EntityFrameworkCore.Design --version 10.0.9

# Update Npgsql provider (stable 10.0.2)
dotnet add src/Adam.Shared/Adam.Shared.csproj package Npgsql.EntityFrameworkCore.PostgreSQL --version 10.0.2
dotnet add src/Adam.BrokerService/Adam.BrokerService.csproj package Npgsql.EntityFrameworkCore.PostgreSQL --version 10.0.2

# Update Microsoft.Data.Sqlite (migration tool)
dotnet add scripts/migrate/migrate.csproj package Microsoft.Data.Sqlite --version 10.0.9

# Update test containers (optional, latest 4.12.0)
dotnet add tests/Adam.BrokerService.Tests/Adam.BrokerService.Tests.csproj package Testcontainers.MsSql --version 4.12.0
dotnet add tests/Adam.BrokerService.Tests/Adam.BrokerService.Tests.csproj package Testcontainers.PostgreSql --version 4.12.0
```

### Step 3 — Regenerate Migrations
```bash
# Delete old preview migrations
rm src/Adam.Shared/Migrations/*.cs

# Regenerate from scratch
cd src/Adam.BrokerService
dotnet ef migrations add InitialSchema
```

### Step 4 — Fix `HasFilter` Issue
Update `AppDbContext.cs` to use provider-aware filter or remove the filtered unique index:
```csharp
// Option A: Provider-aware (recommended)
e.HasIndex(x => x.ChecksumSha256).IsUnique()
    .HasFilter("NOT IsDeleted")
    .HasAnnotation("Relational:Filter", "NOT IsDeleted"); // SQLite default

// Option B: Remove filter, enforce in application code
e.HasIndex(x => x.ChecksumSha256).IsUnique();
```

### Step 5 — Test All Providers
```bash
# SQLite (always passes)
dotnet test tests/Adam.Shared.Tests
dotnet test tests/Adam.CatalogBrowser.Tests

# PostgreSQL + SQL Server (require Docker)
dotnet test tests/Adam.BrokerService.Tests --filter "FullyQualifiedName~DbProviderMatrix"
```

### Step 6 — Verify Integration Tests
Run the full test matrix across all 4 test projects:
```bash
dotnet test
```

---

## 4. Migration Test Plan

| Test ID | Name | Verifies | Pass/Fail |
|---------|------|----------|-----------|
| T13-T1 | `Can_create_and_query_database` (SQLite) | Basic CRUD works with upgraded SQLite provider | Must pass |
| T13-T2 | `Can_use_postgresql_with_testcontainers` | Full roundtrip with PostgreSQL | Must pass |
| T13-T3 | `Can_use_sqlserver_with_testcontainers` | Full roundtrip with SQL Server | Must pass |
| T13-T4 | `DbProviderConfig_Configure_builds_options` | Provider configuration still works | Must pass |
| T13-T5 | DbMigrationService roundtrip | SQLite → Target migration works | Must pass |

### Expected Issues After Upgrade

| Issue | Likelihood | Impact | Workaround |
|-------|-----------|--------|------------|
| `HasFilter` SQLite-only syntax breaks PG/SQL Server | **HIGH** | 2 test failures | Implement provider-aware filter |
| Migration snapshot mismatch (preview → stable) | MEDIUM | 1 build warning | Regenerate migrations |
| Npgsql version mismatch | LOW | Assembly load error | Pin to compatible version |
| Seed data GUID format change | LOW | 1 test assertion | Update seed data fixture |

---

## 5. Rollback Plan

If the upgrade causes issues:

```bash
# Restore .csproj files from git
git checkout -- src/Adam.Shared/Adam.Shared.csproj
git checkout -- src/Adam.BrokerService/Adam.BrokerService.csproj
git checkout -- scripts/migrate/migrate.csproj

# Restore deleted migration files
git checkout -- src/Adam.Shared/Migrations/

# Verify rollback
dotnet test
```

---

## 6. Version Pinning Policy

> **Rule:** All EF Core packages within a project must share the same major.minor version to avoid assembly binding issues.

| Package Family | Version Constraint | Example |
|----------------|--------------------|---------|
| `Microsoft.EntityFrameworkCore.*` | `>= 10.0.9` `< 11.0.0` | Pin to latest stable 10.x |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | Same major as EF Core | `10.0.x` for EF Core 10.x |
| `Testcontainers.*` | Independent | Latest stable, no constraint |
| `Microsoft.Extensions.*` | Independent | Latest stable, no EF Core dependency |

### When to Upgrade

1. **Minor version** (`10.0.x → 10.1.x`): Apply immediately, run full test suite
2. **Major version** (`10.x → 11.x`): Wait 1 month after RTM for community to vet, then:
   - Read official breaking changes doc
   - Run upgrade on a branch
   - Run full test matrix (all 4 projects, all 3 providers)
   - Merge after CI passes on all platforms

---

*Document updated: 2026-06-14*
