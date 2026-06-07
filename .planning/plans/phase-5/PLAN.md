---
goal: Mature the ServiceManager tool for Windows-first administration with cross-platform shared foundations.
version: 1.0
date_created: 2026-06-05
last_updated: 2026-06-05
status: 'Planned'
tags: [feature, architecture, windows]
---

# Introduction

![Status: Planned](https://img.shields.io/badge/status-Planned-blue)

This plan covers the maturation of the `Adam.ServiceManager` tool as the primary administration interface for the `adam` DAM system. It focuses on delivering a robust Windows experience while ensuring the underlying architecture supports Linux and macOS. Key goals include completing the user management lifecycle and hardening the service installation/management logic.

## 1. Requirements & Constraints

- **REQ-001**: Administrator must be able to Add, Edit, and Deactivate users within `ServiceManager`.
- **REQ-002**: `ServiceManager` must manage the same database as the `BrokerService`.
- **REQ-003**: Windows Service installation must handle UAC elevation and Firewall rules automatically.
- **CON-001**: Multi-user mode must support SQLite, PostgreSQL, and SQL Server via configuration.
- **CON-002**: UI must be implemented using Avalonia to maintain cross-platform potential.
- **PAT-001**: Use `IUiDispatcher` abstraction for ViewModel testability.
- **PAT-002**: Use `DbProviderConfig` for all database context configuration.

## 2. Implementation Steps

### Phase 1: Shared Data Foundation

- GOAL: Unify database configuration and access across all server-side components.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-1.1 | Move `DbProviderConfig.cs` from `Adam.BrokerService` to `Adam.Shared` | | |
| TASK-1.2 | Add `Microsoft.EntityFrameworkCore.SqlServer`, `Npgsql.EntityFrameworkCore.PostgreSQL`, and `Microsoft.Extensions.Configuration.Abstractions` to `Adam.Shared.csproj` | | |
| TASK-1.3 | Update `ModeManager.cs` in `Adam.Shared` to use `DbProviderConfig` instead of hardcoded SQLite in `CreateDbContext` | | |
| TASK-1.4 | Update `BrokerService\Program.cs` to use the shared `DbProviderConfig` | | |

### Phase 2: ServiceManager User Lifecycle

- GOAL: Complete the User & Role management UI in the standalone `ServiceManager` app.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-2.1 | Update `ServiceManagerConfig.cs` to persist `DbProvider` and `DbConnection` strings | | |
| TASK-2.2 | Update `ServiceManager\App.axaml.cs` to initialize `ModeManager` with the configured DB provider | | |
| TASK-2.3 | Implement "Deactivate" logic in `UserManagementViewModel.cs` (Soft-delete via `IsActive` flag) | | |
| TASK-2.4 | Finalize `UserManagementView.axaml` layout for Add/Edit forms (Validation states, error messages) | | |
| TASK-2.5 | Add unit tests for `UserManagementViewModel` covering validation and database persistence | | |

### Phase 3: Windows Service Hardening

- GOAL: Ensure reliable service management on Windows with proper elevation handling.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-3.1 | Verify `WindowsServiceInstaller.cs` handles UAC dismissal gracefully without hanging | | |
| TASK-3.2 | Enhance `FirewallRuleManager.cs` to verify if rule already exists before attempting addition | | |
| TASK-3.3 | Add "Open Logs" button to `ServiceManagerView` to easily inspect Broker/Manager logs on Windows | | |
| TASK-3.4 | Implement status polling in `ServiceManagerViewModel` using a background timer | | |

### Phase 4: Cross-Platform Parity

- GOAL: Ensure Linux and macOS projects benefit from the shared foundations.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-4.1 | Update `LinuxServiceInstaller.cs` to use the shared `DbProviderConfig` patterns for start arguments | | |
| TASK-4.2 | Update `MacOsServiceInstaller.cs` to use the shared `DbProviderConfig` patterns for start arguments | | |
| TASK-4.3 | Verify `ServiceManager` launches on a Linux/macOS dev environment (UI rendering check) | | |

## 3. Alternatives

- **ALT-001**: Re-implementing User UI in the main client. (Rejected by user to maintain strict separation).
- **ALT-002**: Using `gsd-sdk` for user management. (Rejected, needs first-class GUI).

## 4. Dependencies

- **DEP-001**: `Microsoft.EntityFrameworkCore` 10.0-preview
- **DEP-002**: `Avalonia` 12.0

## 5. Files

- **FILE-001**: `src/Adam.Shared/Configuration/DbProviderConfig.cs` (New location)
- **FILE-002**: `src/Adam.Shared/Services/ModeManager.cs`
- **FILE-003**: `src/Adam.ServiceManager/ViewModels/UserManagementViewModel.cs`
- **FILE-004**: `src/Adam.ServiceManager/Services/ServiceManagerConfig.cs`
- **FILE-005**: `src/Adam.Shared/Services/WindowsServiceInstaller.cs`

## 6. Testing

- **TEST-001**: `UserManagementViewModelTests.cs` — Test all CRUD operations on users.
- **TEST-002**: `WindowsServiceInstallerTests.cs` — Test (mocked) elevation and service control logic.
- **TEST-003**: Manual test — Install service on Windows, add user, verify user exists in database via Broker login.

## 7. Risks & Assumptions

- **RISK-001**: UAC elevation might be blocked by aggressive corporate policies.
- **ASSUMPTION-001**: The Administrator running `ServiceManager` has permission to write to the database and manage OS services.

## 8. Related Specifications / Further Reading

- `.planning/PROJECT.md`
- `.planning/ROADMAP.md`
