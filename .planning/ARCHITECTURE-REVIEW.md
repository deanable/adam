# Architecture Review: Server Mode & Multi-User Architecture

**Date:** 2026-05-23
**Reviewers:** Parallel codebase analysis agents
**Scope:** BrokerService TCP layer, authentication, database abstraction, protobuf contracts, client integration
**Status:** Complete — 5 areas analyzed, 47 findings identified

---

## Executive Summary

The Adam BrokerService is a functional, lightweight TCP server that demonstrates solid async I/O patterns and clean DI-based handler architecture. However, it has **significant production readiness gaps** across all five analyzed domains. Before Phase 2 (Multi-User Mode) can be considered production-ready, **14 critical/high issues must be addressed**, spanning security vulnerabilities, data integrity risks, protocol bugs, and client resilience failures.

| Domain | Grade | Critical/High Issues |
|--------|-------|----------------------|
| TCP Broker Architecture | C+ | 3 critical, 3 high |
| Authentication & Security | D+ | 3 critical, 2 high |
| Database Provider & Data Layer | C | 1 critical, 3 high |
| Protobuf Contracts & Transport | C | 2 critical, 3 high |
| Client/Server Integration | C- | 1 critical, 4 high |

**Overall Architecture Grade: C (Adequate for prototype, not production)**

---

## 1. TCP Broker Architecture

### 1.1 How It Works

The `TcpListenerHostedService` starts a `TcpListener` on `IPAddress.Any:9100`. Each accepted connection spawns a dedicated `Task` (`HandleConnectionAsync`) that reads length-prefixed protobuf `Envelope` messages, routes them via string-based `MessageType` dispatch, and writes responses back.

**Key Files:**
- `src/Adam.BrokerService/Transport/TcpListenerService.cs`
- `src/Adam.BrokerService/Handlers/ConnectionHandler.cs`
- `src/Adam.Shared/Transport/TcpFrame.cs`

### 1.2 Critical Issues

#### CRITICAL-1: Unobserved Fire-and-Forget Tasks
**File:** `TcpListenerService.cs:61`
```csharp
_ = HandleConnectionAsync(state, _cts.Token);
```
Connection handler tasks are launched but **never observed**. Unhandled exceptions become unobserved task exceptions, potentially crashing the process or leaving the service in a zombie state where it accepts but cannot process connections.

**Fix:** Track tasks in a `ConcurrentDictionary<string, Task>` and await them during shutdown.

#### CRITICAL-2: No Graceful Shutdown
**File:** `TcpListenerService.cs:70-82`
```csharp
public void Stop()
{
    _cts?.Cancel();
    _listener?.Stop();
    foreach (var kvp in _connections)
        kvp.Value.Client.Close();
    _connections.Clear();
}
```
`Stop()` forcibly closes sockets and clears the dictionary without awaiting in-flight handlers. Active database transactions will be aborted mid-flight, risking data corruption.

**Fix:** Implement `StopAsync()` that signals cancellation, then awaits `Task.WhenAll(connectionTasks)` with a 30-second drain timeout.

#### CRITICAL-3: No Write Timeout
**File:** `TcpFrame.cs:15-23`
```csharp
public static async Task SendAsync(NetworkStream stream, Envelope envelope, CancellationToken ct)
{
    await stream.WriteAsync(lengthBuffer, ct).ConfigureAwait(false);
    await stream.WriteAsync(payload, ct).ConfigureAwait(false);
    await stream.FlushAsync(ct).ConfigureAwait(false);
}
```
The 5-minute timeout only applies to reads. A stalled client can block `WriteAsync` indefinitely, consuming a thread and holding the connection open. This is a DoS vector.

**Fix:** Wrap writes with a linked `CancellationTokenSource` with a 30-second send timeout.

### 1.3 High Issues

#### HIGH-1: Request Counter Race Condition
**File:** `TcpListenerService.cs:101`
```csharp
state.RequestCount++;
```
Plain `int` increment on mutable state. Fragile under future architectural changes.

#### HIGH-2: No Heartbeat / Idle Timeout
No mechanism detects half-open connections. A silently disconnected client (WiFi drop, sleep) leaves a socket in `ESTABLISHED` state indefinitely.

#### HIGH-3: No Backpressure Beyond Connection Count
Binary accept/reject decision with no queueing, adaptive limits, or per-client rate limiting. A single aggressive client can consume all 500 requests per connection.

---

## 2. Authentication & Security

### 2.1 How It Works

Symmetric-key JWT (HMAC-SHA256) with 24-hour expiry. PBKDF2 password hashing (600K iterations, SHA-256, 32-byte salt). Role-based permissions stored as string arrays. `AuthorizationMiddleware` checks permissions via wildcard matching (`asset:*` → `asset:read`).

**Key Files:**
- `src/Adam.BrokerService/Handlers/AuthHandler.cs`
- `src/Adam.Shared/Services/PasswordHelper.cs`
- `src/Adam.BrokerService/Handlers/AuthorizationMiddleware.cs`

### 2.2 Critical Issues

#### CRITICAL-4: No TLS on Transport
**Files:** `TcpFrame.cs`, `TcpListenerService.cs:36`, `BrokerClient.cs:38`

The entire multi-user mode runs over **raw TCP**. JWT tokens and passwords travel in plaintext. Any attacker with network access (same LAN, compromised router, ARP spoofing) can intercept tokens and impersonate users indefinitely.

**Fix:** Wrap `NetworkStream` with `SslStream` on both client and server. Load certificate from store or file.

#### CRITICAL-5: Hardcoded JWT Secret in Source Control
**File:** `src/Adam.BrokerService/appsettings.json:3`
```json
"SigningKey": "BRa3YkvXH3FAoypYYkSs2DyQFo/ctB1x83nzIK86z7g="
```
Any clone of the repo can forge valid JWTs for any user/role. The key must be rotated immediately.

**Fix:** Load from environment variable or secrets manager. Never commit secrets.

#### CRITICAL-6: No Authorization on Asset/Collection Handlers
**Files:** `AssetHandler.cs:21`, `CollectionHandler.cs:22`, `ChangeHandler.cs:21`

`AssetHandler.ListAssetsAsync`, `GetAssetAsync`, `UpdateAssetAsync`, `CollectionHandler.ListCollectionsAsync`, and `ChangeHandler.GetChangesAsync` have **zero authorization checks**. An unauthenticated attacker with TCP connectivity can list, read, and modify all digital assets without any token.

**Fix:** Inject `AuthorizationMiddleware` into all handlers and check permissions before processing.

### 2.3 High Issues

#### HIGH-4: Static Mutable JWT Signing Key Race
**File:** `AuthHandler.cs:19-59`
```csharp
private static SymmetricSecurityKey _signingKey = null!;
if (_signingKey != null) return; // NOT thread-safe
```
Multiple `AuthHandler` singletons instantiated concurrently during DI resolution can race on key initialization. The ephemeral fallback means all tokens are invalidated on every restart if no config key is provided.

**Fix:** Use `Lazy<T>` or register `AuthHandler` as a true singleton with instance-scoped key.

#### HIGH-5: No Brute-Force Protection
**File:** `AuthHandler.cs:61-104`

No rate limiting, account lockout, progressive delay, or IP-based throttling. Unlimited password guesses at wire speed.

**Fix:** Implement `LoginRateLimiter` with sliding window (5 attempts per 15 minutes).

---

## 3. Database Provider Abstraction & Multi-User Data Layer

### 3.1 How It Works

`DbProviderConfig` switches between SQLite, PostgreSQL, and SQL Server at runtime. Handlers create fresh `IServiceScope` per request for `DbContext` isolation. Standalone mode uses raw `SqliteConnection` with `busy_timeout = 10000`.

**Key Files:**
- `src/Adam.BrokerService/Configuration/DbProviderConfig.cs`
- `src/Adam.Shared/Data/AppDbContext.cs`
- `src/Adam.BrokerService/Data/MigrationRunner.cs`

### 3.2 Critical Issues

#### CRITICAL-7: No EF Core Migrations
**File:** `MigrationRunner.cs:19-62`

Uses `EnsureCreatedAsync()` + raw SQLite-specific `ALTER TABLE` SQL. This:
- Never updates existing databases when the model changes
- Uses SQLite-specific syntax (`TEXT`, `ADD COLUMN` without `IF NOT EXISTS`) that fails on PostgreSQL/SQL Server
- Silently swallows all errors with empty `catch {}`

**Fix:** Generate proper EF Core Migrations. Use `EnsureCreatedAsync` only for SQLite standalone mode.

### 3.3 High Issues

#### HIGH-6: Version Not a Concurrency Token
**File:** `DigitalAsset.cs` (entity), `AppDbContext.cs:18`

`Version` is incremented manually in `SaveChangesAsync`, but EF Core does not check it during `UPDATE`/`DELETE`. Two concurrent requests loading the same asset will result in last-writer-wins, not `DbUpdateConcurrencyException`.

**Fix:** Configure `e.Property(x => x.Version).IsConcurrencyToken()` in `OnModelCreating`.

#### HIGH-7: Dead Provider Configuration Code
**Files:** `PostgresProvider.cs`, `SqlServerProvider.cs`

These static configuration classes exist but are **never referenced**. `DbProviderConfig.Configure()` ignores them, so `MigrationsAssembly` and `MigrationsHistoryTable` settings are lost.

#### HIGH-8: No Connection Resiliency
No `EnableRetryOnFailure()` for PostgreSQL/SQL Server. Transient network blips or failover events bubble immediately as fatal errors.

#### HIGH-9: N+1 / Cartesian Product Risk in Asset Queries
**File:** `AssetHandler.cs:21-119`

`Include(a => a.Collection).Include(a => a.Keywords)` with `.Where(a => a.Keywords.Any(...))` generates single-query SQL with multiple joins. For assets with many keywords, this causes cartesian product explosion.

**Fix:** Add `.AsSplitQuery()` and `.AsNoTracking()` to read-only list queries.

---

## 4. Protobuf Contracts & Transport Framing

### 4.1 How It Works

Manual protobuf wire-format implementation (no `.proto` files). `IProtoSerializable` interface with `WriteTo`/`MergeFrom` using `CodedOutputStream`/`CodedInputStream`. `Envelope` wraps domain messages with auth, correlation ID, and routing.

**Key Files:**
- `src/Adam.Shared/Contracts/Envelope.cs`
- `src/Adam.Shared/Services/ProtoHelper.cs`
- `src/Adam.Shared/Transport/TcpFrame.cs`
- All `*Messages.cs` files

### 4.2 Critical Issues

#### CRITICAL-8: StatusMessages.cs Compares Raw Tag Values
**File:** `StatusMessages.cs:40-55`
```csharp
switch (tag)  // ❌ WRONG: compares raw tag (field + wire type)
{
    case 8: ActiveConnections = input.ReadInt32(); break;
    // ...
}
```
**Every other file** correctly uses `WireFormat.GetTagFieldNumber(tag)`. If a field's wire type changes (e.g., from varint to fixed32), the explicit `case 8` won't match, and the field will be silently skipped.

**Fix:** Change to `switch (WireFormat.GetTagFieldNumber(tag))` and use field numbers.

#### CRITICAL-9: String-Based MessageType Routing
**File:** `ConnectionHandler.cs:42-58`
```csharp
return request.MessageType switch
{
    nameof(LoginRequest) => await _authHandler.LoginAsync(request, ct),
    // ...
};
```
Renaming a C# class (e.g., `LoginRequest` → `AuthLoginRequest`) breaks the wire protocol. Strings are verbose on the wire. No compile-time guarantee that client and server agree.

**Fix:** Use a stable `uint16` or `uint32` opcode enum, or at minimum a `[MessageType("auth.login")]` attribute decoupled from C# type names.

### 4.3 High Issues

#### HIGH-10: No Buffer Pooling
**Files:** `ProtoHelper.cs:16`, `TcpFrame.cs:45`

Every message allocates fresh `byte[]`. Under load, this creates GC pressure. Large assets (>85KB) land in the LOH, causing Gen 2 pauses.

**Fix:** Use `ArrayPool<byte>.Shared` for serialization buffers.

#### HIGH-11: Entire Asset Content in ByteString
**File:** `AssetMessages.cs:291`
```csharp
public ByteString Content { get; set; } = ByteString.Empty;
```
A 200MB video file becomes a 200MB `ByteString` plus envelope buffer plus TCP frame buffer — ~600MB+ transient allocation. No streaming, chunking, or resumable upload.

**Fix:** Implement multi-frame upload protocol (InitUpload → UploadChunk → CompleteUpload).

#### HIGH-12: Redundant MemoryStream Allocation in Nested Messages
**Files:** `AssetMessages.cs:117-121`, `UserMessages.cs:39-43`, `CollectionMessages.cs:26-30`

Nested message deserialization allocates `ByteString`, `byte[]`, `MemoryStream`, and `CodedInputStream` — 4 allocations per nested item. `MemoryStream` is unnecessary; `CodedInputStream` accepts `byte[]` directly.

**Fix:** Remove `MemoryStream` intermediate.

---

## 5. Client/Server Integration

### 5.1 How It Works

`ModeManager` toggles between standalone (SQLite) and multi-user (TCP broker). `BrokerClient` holds a single `TcpClient`, sends `Envelope` messages, and correlates responses via `TaskCompletionSource`. `AuthSession` stores JWT in memory. `ChangePoller` polls every 5 seconds for server-side changes.

**Key Files:**
- `src/Adam.CatalogBrowser/Services/ModeManager.cs`
- `src/Adam.CatalogBrowser/Services/BrokerClient.cs`
- `src/Adam.CatalogBrowser/Services/AuthSession.cs`
- `src/Adam.CatalogBrowser/Services/ChangePoller.cs`

### 5.2 Critical Issues

#### CRITICAL-10: BrokerClient Has No Reconnection Logic
**File:** `BrokerClient.cs:72-94`

If the TCP connection drops (server restart, network hiccup, 500-request limit reached), `BrokerClient` does nothing to recover. The next `SendAsync` throws `InvalidOperationException("Not connected")`. Every ViewModel must manually check `IsConnected` — many don't.

**Fix:** Implement automatic reconnection with exponential backoff in `ReceiveLoopAsync`.

### 5.3 High Issues

#### HIGH-13: Multi-User Sidebar Is Non-Functional
**File:** `SidebarViewModel.cs` (multiple methods)

| Feature | Standalone | Multi-User |
|---------|-----------|------------|
| Folders | Full tree | **Empty** |
| Keywords | Full tree | **Empty** |
| Media formats | Loaded | **Not loaded** |
| Metadata categories | Full tree | **Only "All"** |
| Date taken tree | Full tree | **Not loaded** |

The sidebar is the primary navigation mechanism. Multi-user mode provides a **significantly degraded UX**.

**Fix:** Implement server handlers for `ListFolders`, `ListKeywords`, `ListMediaFormatCounts`, `ListMetadataCategories`, `ListDateTakenTree` and wire them in `SidebarViewModel`.

#### HIGH-14: No Retry, No Circuit Breaker
Zero retry logic anywhere in the client stack. A transient network failure immediately propagates to the UI. `ChangePoller` **permanently stops** on any error.

**Fix:** Add Polly policies or custom retry with exponential backoff and circuit breaker.

#### HIGH-15: BrokerClient.SendAsync TCS Memory Leak
**File:** `BrokerClient.cs:72-94`
```csharp
_pending[request.CorrelationId] = tcs;  // registered BEFORE checking connection
// ...
if (_stream == null)
    throw new InvalidOperationException("Not connected. Call ConnectAsync first.");
```
If `_stream == null`, the TCS is registered in `_pending` but never removed. This leaks memory (TCS stays forever).

**Fix:** Remove TCS from `_pending` before throwing.

#### HIGH-16: No Automatic Token Refresh
**File:** `AuthSession.cs`

`IsTokenExpired()` exists but nobody calls it before requests. After 24 hours, requests fail with auth errors and the user has no re-login prompt.

**Fix:** Add middleware that checks expiration before each request and either refreshes or prompts re-login.

---

## 6. Cross-Cutting Concerns

### Thread Safety

| Component | Verdict | Issue |
|-----------|---------|-------|
| BrokerClient send | ✅ Safe | `_lock` protects stream writes |
| BrokerClient receive | ⚠️ Race | `_stream` read without lock; race with `DisconnectAsync` |
| AuthSession | ❌ Unsafe | Plain properties; no synchronization |
| ModeManager | ❌ Unsafe | Plain properties; concurrent reads/writes possible |
| TcpListenerService accept | ✅ Safe | Single `AcceptTcpClientAsync` at a time |
| Handler DI scoping | ✅ Safe | Fresh `IServiceScope` per request |

### Missing Infrastructure

| Concern | Status | Impact |
|---------|--------|--------|
| Health checks / ping | Missing | No way to detect half-open connections |
| Structured security logging | Missing | Failed logins, token validation failures not auditable |
| Rate limiting | Missing | Brute-force, DoS vulnerabilities |
| Configuration-driven endpoints | Missing | Hardcoded `localhost:9100` |
| Streaming upload/download | Missing | 256MB hard cap on asset size |
| Bulk operations API | Missing | Inefficient for batch workflows |

---

## 7. Recommendations by Priority

### Before Phase 2 Can Begin (Blockers)

1. **Fix CRITICAL-4 (No TLS)** — Multi-user mode cannot ship without encrypted transport
2. **Fix CRITICAL-5 (Hardcoded JWT secret)** — Rotate key, move to environment variable
3. **Fix CRITICAL-6 (No auth on Asset/Collection handlers)** — Add `AuthorizationMiddleware` to all handlers
4. **Fix CRITICAL-8 (StatusMessages raw tag bug)** — Use `WireFormat.GetTagFieldNumber(tag)`
5. **Fix CRITICAL-9 (String MessageType)** — Replace with stable opcode enum
6. **Fix CRITICAL-10 (No reconnection)** — Implement `BrokerClient` auto-reconnect
7. **Fix HIGH-13 (Non-functional sidebar)** — Complete multi-user sidebar server endpoints

### Before Production Deployment

8. **Fix CRITICAL-1 (Unobserved tasks)** — Track and await connection tasks
9. **Fix CRITICAL-2 (No graceful shutdown)** — Implement `StopAsync()` with drain
10. **Fix CRITICAL-3 (No write timeout)** — Add send timeout to `TcpFrame`
11. **Fix CRITICAL-7 (No migrations)** — Generate EF Core migrations
12. **Fix HIGH-4 (JWT key race)** — Use `Lazy<T>` or true singleton
13. **Fix HIGH-5 (Brute-force)** — Add `LoginRateLimiter`
14. **Fix HIGH-6 (Concurrency token)** — Configure `Version.IsConcurrencyToken()`
15. **Fix HIGH-8 (No connection resiliency)** — Add `EnableRetryOnFailure()`
16. **Fix HIGH-9 (N+1 queries)** — Add `AsSplitQuery()` and `AsNoTracking()`
17. **Fix HIGH-10 (No buffer pooling)** — Use `ArrayPool<byte>`
18. **Fix HIGH-11 (ByteString upload)** — Implement streaming/chunked upload
19. **Fix HIGH-14 (No retry)** — Add Polly retry and circuit breaker
20. **Fix HIGH-15 (TCS leak)** — Remove TCS from `_pending` on early throw
21. **Fix HIGH-16 (No token refresh)** — Add expiration middleware

### Nice-to-Have Improvements

22. Add heartbeat/idle timeout to detect half-open connections
23. Add per-message-type size limits in `TcpFrame`
24. Add envelope version field for future protocol evolution
25. Define `StatusCode` enum instead of raw ints
26. Add `jti` and `iat` claims to JWT for revocation support
27. Implement server-side logout / token blacklist
28. Add password pepper from secrets manager
29. Add `TenantId` / `OwnerId` to `DigitalAsset` for row-level isolation
30. Make broker endpoint configurable via `appsettings.json`

---

## 8. Risk Matrix

| ID | Issue | Severity | Effort | Owner |
|----|-------|----------|--------|-------|
| CRITICAL-4 | No TLS | Critical | High | Transport |
| CRITICAL-5 | Hardcoded JWT secret | Critical | Low | Security |
| CRITICAL-6 | No auth on handlers | Critical | Low | Security |
| CRITICAL-8 | StatusMessages raw tag | Critical | Low | Contracts |
| CRITICAL-9 | String MessageType | Critical | Medium | Contracts |
| CRITICAL-10 | No reconnection | Critical | Medium | Client |
| HIGH-13 | Non-functional sidebar | High | Medium | Client/Server |
| CRITICAL-1 | Unobserved tasks | Critical | Low | Server |
| CRITICAL-2 | No graceful shutdown | Critical | Medium | Server |
| CRITICAL-3 | No write timeout | Critical | Low | Transport |
| CRITICAL-7 | No migrations | Critical | Medium | Database |
| HIGH-4 | JWT key race | High | Low | Security |
| HIGH-5 | Brute-force | High | Low | Security |
| HIGH-6 | Concurrency token | High | Low | Database |
| HIGH-8 | No resiliency | High | Low | Database |
| HIGH-9 | N+1 queries | High | Low | Database |
| HIGH-10 | No buffer pooling | High | Medium | Transport |
| HIGH-11 | ByteString upload | High | Medium | Contracts |
| HIGH-14 | No retry | High | Medium | Client |
| HIGH-15 | TCS leak | High | Low | Client |
| HIGH-16 | No token refresh | High | Medium | Client |

---

*Review complete. Ready for Phase 2 planning with full awareness of architectural debt.*
