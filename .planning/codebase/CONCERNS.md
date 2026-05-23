# Concerns

## Critical

### Port Mismatch: Client vs Server
- **CatalogBrowser** hardcodes broker connection to `localhost:5000`
- **BrokerService** `TcpListenerService` defaults to port `9100`
- **Impact**: Client cannot connect to broker out of the box; connection will fail
- **Action**: Align ports or make client port configurable

### Static Mutable JWT Signing Key
- `AuthHandler._signingKey` is `static` and assigned in instance constructor
- Race condition risk if multiple `AuthHandler` instances are created concurrently
- Ephemeral key fallback means all tokens invalidated on restart if key not configured
- **Action**: Use singleton pattern properly or lock; require persistent key in config

## High

### EF Core 10 Preview Dependency
- All EF Core packages are `10.0.0-preview.3` — unstable API surface
- Npgsql provider is also preview (`10.0.0-preview.3`)
- **Risk**: Breaking changes between preview and RTM; migration issues
- **Action**: Monitor for RC/RTM updates; pin to stable when available

### No Solution File (.sln)
- Repository lacks a `.sln` file — build/IDE experience degraded
- **Action**: Generate solution file: `dotnet new sln` + `dotnet sln add` for each project

### Incomplete Multi-DB Provider Support
- `DbProviderConfig` and provider classes exist but migration path between providers is not fully wired
- SQLite provider is in Shared; SQL Server and PostgreSQL providers are in BrokerService only
- **Action**: Ensure all providers are tested via `DbProviderMatrixTests`

## Medium

### Limited Test Coverage
- Core business logic (metadata extraction, thumbnail generation, search) has minimal or no tests
- Several `UnitTest1.cs` placeholder files remain
- **Action**: Backfill tests for critical paths before further feature work

### Manual Protobuf Maintenance
- No `.proto` schema files; all serialization is hand-written
- Adding a field requires manual field number management across all message types
- No contract versioning strategy
- **Action**: Consider protoc generation or at least schema documentation

### Folder Watcher Scalability
- `FolderWatcherService` in BrokerService watches root folder for changes
- No evidence of batching or debouncing for high-volume file system events
- **Action**: Review watcher implementation for performance at scale

## Low

### Avalonia 12 vs Spec Version 11
- Project uses Avalonia 12.0.3; spec mentions 11
- Not a functional concern — 12 is newer and compatible
- **Action**: Update spec to reflect actual version

### Missing Null Checks in Some Handlers
- Some handler methods don't validate envelope payload before deserialization
- Could throw on malformed requests instead of returning error envelopes
- **Action**: Add defensive null checks and validation
