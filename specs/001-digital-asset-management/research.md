# Research: Digital Asset Management System (adam)

**Phase 0** — Technology decisions, rationale, and alternatives considered.

## Database Abstraction Strategy

- **Decision**: Entity Framework Core with provider-specific NuGet packages
- **Rationale**: EF Core is the standard .NET ORM with first-party providers for SQL Server, PostgreSQL (via Npgsql), and SQLite. The DbContext + DI pattern allows provider swapping at configuration time.
- **Alternatives considered**: Dapper (too low-level, no migration tooling), NHibernate (less active ecosystem), raw ADO.NET (excessive boilerplate).

## Application Architecture

- **Decision**: Dual-mode architecture — standalone (self-contained) and multi-user (client-server)
- **Standalone mode**: The catalog browser runs as a fully self-contained desktop app with direct EF Core SQLite access, its own IFileService, and no external dependencies. No background process required.
- **Multi-user mode**: The catalog browser connects to a broker service that hosts the shared database (PostgreSQL/SQL Server) and coordinates concurrent access.
- **Rationale**: The standalone mode provides immediate value with zero infrastructure. The multi-user mode scales to teams. Both share the same domain model and UI codebase — only the data access layer differs.
- **Alternatives considered**: Service-only architecture (requires infrastructure for all use cases), web-only (loses desktop-native UX for large asset libraries).

## Communication Protocol (Multi-User Mode)

- **Decision**: Raw TCP sockets with Google.Protobuf serialization
- **Rationale**: TCP is the most universal cross-platform transport with zero framework dependencies. Google.Protobuf provides strongly-typed, efficient binary serialization. Messages use length-prefixed framing: `[4-byte payload length][protobuf bytes]`. JWT tokens accompany each request. No web framework, no HTTP stack, no platform-specific APIs.
- **Alternatives considered**: gRPC (server hosting requires ASP.NET Core or deprecated Grpc.Core native library), named pipes (local-only, cannot support remote multi-user), REST/HTTP (requires a web framework).


## File Storage Strategy

- **Decision**: Local filesystem with path configuration; metadata-only in database
- **Rationale**: Decouples storage from database, keeps DB lean, allows future migration to cloud storage (S3/Azure Blob) by implementing the same abstraction interface.
- **Alternatives considered**: Database BLOB storage (degrades query performance, backup bloat), cloud-only (adds deployment dependency).

## Native Service Hosting

- **Decision**: .NET console application; managed by native service managers
- **Rationale**: A pure console application with `Microsoft.Extensions.Hosting` provides background service patterns without any ASP.NET dependency. Native integration (SCM on Windows, launchd on macOS, systemd on Linux) provides lifecycle management, auto-start, and logging.
- **Alternatives considered**: Self-hosted console app (no lifecycle management), Docker (adds deployment dependency).

## Testing Strategy

- **Decision**: xUnit + FluentAssertions for all .NET tests; Testcontainers for DB integration tests
- **Rationale**: xUnit is the standard .NET test framework. Testcontainers provides disposable Docker containers per test run, enabling true integration testing against all DB providers. Avalonia.Headless enables UI logic testing without a display server.
- **Alternatives considered**: MSTest/NUnit (less ecosystem), in-memory EF Core (doesn't exercise real provider behavior).

## Authentication Approach

- **Decision**: JWT bearer tokens issued by broker service; password hashing with System.Security.Cryptography (PBKDF2)
- **Rationale**: Stateless auth suitable for TCP transport; no session storage needed. .NET 10 provides PBKDF2 via `Rfc2898DeriveBytes` with HMAC-SHA256 + salt — no ASP.NET dependency required.
- **Alternatives considered**: Session-based (requires server-side state), OAuth/OpenID Connect (overkill for initial scope, can be added later).

## Deterministic Build & Tooling

- **Decision**: .NET CLI + Roslyn analyzers; dotnet format for code style
- **Rationale**: .NET 10 SDK includes all tooling — `dotnet build`, `dotnet test`, `dotnet format`. Google.Protobuf provides runtime serialization with no code generation step needed.
- **Alternatives considered**: Cake/Fake build scripts (unnecessary for single-solution projects).
