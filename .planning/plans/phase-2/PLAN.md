# Plan: Phase 2 — Multi-User Foundation

**Phase:** 2 of 7  
**Goal:** The catalog browser connects to a running broker service over TCP, authenticates, and browses the shared catalog.  
**Requirements:** AUTH-01, AUTH-02, AUTH-03, AUTH-06, BROK-01, BROK-05  
**Created:** 2026-05-23 (after architecture review)  
**Architecture Review:** `.planning/ARCHITECTURE-REVIEW.md`

---

## Executive Summary

Phase 2 delivers the multi-user foundation by hardening the TCP broker, securing the authentication layer, completing the client/server integration, and enabling shared catalog browsing. This plan incorporates **all critical findings** from the architecture review — security vulnerabilities, protocol bugs, and client resilience gaps are addressed as first-class tasks, not afterthoughts.

**Approach:** Architecture fixes first (security + correctness), then feature completion (sidebar + folder watcher), then polish (reconnection + retry).

---

## Architecture Review Blockers (Must Fix in Phase 2)

| ID | Issue | Severity | Task |
|----|-------|----------|------|
| CRITICAL-4 | No TLS on transport | Critical | T2.1 |
| CRITICAL-5 | Hardcoded JWT secret in repo | Critical | T2.2 |
| CRITICAL-6 | No authorization on Asset/Collection handlers | Critical | T2.3 |
| CRITICAL-8 | StatusMessages raw tag bug | Critical | T2.4 |
| CRITICAL-9 | String MessageType routing | Critical | T2.5 |
| CRITICAL-10 | BrokerClient no reconnection | Critical | T2.6 |
| HIGH-13 | Multi-user sidebar non-functional | High | T2.7 |

---

## Work Streams

### Stream A: Security Hardening
**Owner:** BrokerService  
**Goal:** Close all critical security gaps before multi-user mode is usable.

#### T2.1: Implement TLS on TCP Transport
- Wrap `NetworkStream` with `SslStream` on both client and server
- Server: load certificate from local machine store or configurable path
- Client: validate server certificate (with dev override for self-signed)
- Update `TcpFrame.SendAsync` / `ReceiveAsync` to use `SslStream`
- Add configuration: `Broker:CertificateThumbprint` (Windows) or `Broker:CertificatePath` (cross-platform)
- **Files:** `TcpListenerService.cs`, `BrokerClient.cs`, `TcpFrame.cs`, `appsettings.json`

#### T2.2: Remove Hardcoded JWT Secret
- Remove secret from `appsettings.json`
- Load from environment variable `ADAM_JWT_KEY` or Key Vault
- Fail fast at startup if key is missing or < 32 bytes
- Document setup in `quickstart.md`
- **Files:** `AuthHandler.cs`, `appsettings.json`, `Program.cs`

#### T2.3: Add Authorization to All Handlers
- Inject `AuthorizationMiddleware` into `AssetHandler`, `CollectionHandler`, `ChangeHandler`
- Check `asset:read`, `asset:update`, `collection:read`, `collection:update` permissions
- Return `StatusCode = 7` (Forbidden) for unauthorized requests
- Add `asset:read` check to `ListAssetsAsync`, `GetAssetAsync`
- Add `asset:update` check to `UpdateAssetAsync`, `DeleteAssetAsync`
- Add `collection:read` check to `ListCollectionsAsync`
- **Files:** `AssetHandler.cs`, `CollectionHandler.cs`, `ChangeHandler.cs`, `ConnectionHandler.cs`

#### T2.4: Fix StatusMessages Raw Tag Bug
- Change `switch (tag)` to `switch (WireFormat.GetTagFieldNumber(tag))`
- Add unknown-field skip loop to `GetServiceStatusRequest.MergeFrom`
- Add round-trip serialization test for `StatusMessages`
- **Files:** `StatusMessages.cs`

#### T2.5: Replace String MessageType with Opcode Enum
- Create `MessageType` enum with stable uint16 values
- Update `Envelope` to use `MessageType` instead of string
- Update `ConnectionHandler` dispatch switch
- Update all client code that sets `Envelope.MessageType`
- Maintain backward compatibility: support both fields during transition (optional)
- **Files:** `Envelope.cs`, `ConnectionHandler.cs`, all ViewModels that send broker requests

#### T2.6: Implement BrokerClient Auto-Reconnection
- Detect disconnect in `ReceiveLoopAsync` (IOException, ObjectDisposedException)
- Trigger exponential backoff reconnect (1s, 2s, 4s, 8s, max 30s)
- Surface connection state to UI (`IsConnected`, `ConnectionStatus`)
- Queue pending requests during reconnection; replay on success or fail fast on auth expiry
- Add `MaxReconnectAttempts` (default 10)
- **Files:** `BrokerClient.cs`, `MainWindowViewModel.cs`

#### T2.7: Complete Multi-User Sidebar
- Implement server handlers:
  - `ListFoldersRequest` / `ListFoldersResponse`
  - `ListKeywordsRequest` / `ListKeywordsResponse`
  - `ListMediaFormatCountsRequest` / `ListMediaFormatCountsResponse`
  - `ListMetadataCategoriesRequest` / `ListMetadataCategoriesResponse`
  - `ListDateTakenTreeRequest` / `ListDateTakenTreeResponse`
- Wire multi-user branches in `SidebarViewModel.LoadFoldersAsync`, `LoadKeywordsAsync`, `LoadMediaFormatCountsAsync`, `LoadMetadataCategoriesAsync`, `LoadDateTakenTreeAsync`
- **Files:** `SidebarViewModel.cs`, new server handler methods, new contract messages

---

### Stream B: Broker Reliability
**Owner:** BrokerService  
**Goal:** Make the TCP broker production-ready.

#### T2.8: Observe and Track Connection Tasks
- Replace fire-and-forget `_ = HandleConnectionAsync(...)` with `ConcurrentDictionary<string, Task>` registry
- Add `ContinueWith` cleanup when task completes
- Log unhandled exceptions from connection tasks
- **Files:** `TcpListenerService.cs`

#### T2.9: Implement Graceful Shutdown
- Convert `Stop()` to async `StopAsync()`
- Signal cancellation, then await `Task.WhenAll(connectionTasks)` with 30s timeout
- Log active connections that fail to drain
- Update `TcpListenerHostedService.StopAsync` to call `StopAsync()`
- **Files:** `TcpListenerService.cs`, `TcpListenerHostedService.cs`

#### T2.10: Add Write Timeout to TcpFrame
- Wrap `SendAsync` with linked `CancellationTokenSource`
- Default send timeout: 30 seconds
- **Files:** `TcpFrame.cs`

#### T2.11: Add Idle Connection Timeout
- Track `LastActivity` timestamp per connection
- Background task checks every 60s; closes idle connections after 5 minutes
- **Files:** `TcpListenerService.cs`

---

### Stream C: Auth Layer Fixes
**Owner:** BrokerService + Shared  
**Goal:** Close authentication correctness and robustness gaps.

#### T2.12: Fix Static Mutable JWT Signing Key
- Replace `private static SymmetricSecurityKey _signingKey = null!;` with instance field
- Register `AuthHandler` as singleton in DI so only one instance exists
- Or use `Lazy<SymmetricSecurityKey>` for thread-safe initialization
- **Files:** `AuthHandler.cs`, `Program.cs`

#### T2.13: Add Brute-Force Protection
- Create `LoginRateLimiter` with in-memory sliding window
- 5 attempts per 15 minutes per username + IP combination
- Return `StatusCode = 16` (TooManyRequests) when exceeded
- Add `X-RateLimit-Remaining` style info in error response
- **Files:** `AuthHandler.cs`, new `LoginRateLimiter.cs`

#### T2.14: Add JWT jti and iat Claims
- Generate `jti` (Guid) per token
- Add `iat` (Unix timestamp) claim
- Enable future token revocation by `jti` blacklist
- **Files:** `AuthHandler.cs`

#### T2.15: Add Structured Security Logging
- Log all failed login attempts with correlation ID
- Log token validation failures with specific reason (expired, invalid signature, etc.)
- Log permission denials with user ID and attempted action
- Use a consistent `SECURITY:` prefix for SIEM filtering
- **Files:** `AuthHandler.cs`, `AuthorizationMiddleware.cs`, all handlers

---

### Stream D: Database & Data Layer
**Owner:** Shared + BrokerService  
**Goal:** Ensure data integrity and multi-user concurrency safety.

#### T2.16: Configure Version as Concurrency Token
- Add `e.Property(x => x.Version).IsConcurrencyToken()` in `OnModelCreating`
- Catch `DbUpdateConcurrencyException` in `AssetHandler.UpdateAssetAsync`
- Return `StatusCode = 9` (Conflict) with current server version
- **Files:** `AppDbContext.cs`, `AssetHandler.cs`

#### T2.17: Fix Query Performance (N+1 / Cartesian Product)
- Add `.AsSplitQuery()` and `.AsNoTracking()` to all read-only list queries in handlers
- `AssetHandler.ListAssetsAsync`
- `CollectionHandler.ListCollectionsAsync`
- `AuditLogHandler.ListAuditLogsAsync`
- **Files:** All handler list methods

#### T2.18: Add Connection Resiliency
- Configure `EnableRetryOnFailure()` for PostgreSQL (3 retries, 30s max delay)
- Configure `EnableRetryOnFailure()` for SQL Server (3 retries, 30s max delay)
- SQLite standalone: keep as-is (no pooling, busy_timeout)
- **Files:** `DbProviderConfig.cs`

---

### Stream E: Client Resilience & UX
**Owner:** CatalogBrowser  
**Goal:** Make multi-user mode a first-class, robust experience.

#### T2.19: Fix BrokerClient.SendAsync TCS Memory Leak
- Remove TCS from `_pending` before throwing when `_stream == null`
- Add unit test for disconnect-during-send scenario
- **Files:** `BrokerClient.cs`

#### T2.20: Add Request/Response Timeout and Retry
- Wrap `SendAsync` with Polly policy or custom retry (3 attempts, exponential backoff)
- Distinguish retryable (network, timeout) from non-retryable (auth, validation) errors
- Add `CancellationToken` default timeout of 30s per request
- **Files:** `BrokerClient.cs`, ViewModels that call broker

#### T2.21: Fix ChangePoller to Retry on Transient Errors
- Distinguish auth failures (stop poller) from network errors (retry with backoff)
- Add `MaxRetryAttempts` with exponential backoff
- Surface poller state to UI
- **Files:** `ChangePoller.cs`

#### T2.22: Add Connection Status Indicator to UI
- Show connected / disconnected / reconnecting state in status bar
- Show last sync timestamp
- Add manual "Reconnect" button in admin panel
- **Files:** `MainWindow.axaml`, `MainWindowViewModel.cs`, `AdminPanelView.axaml`

#### T2.23: Add Token Expiration Handling
- Check `AuthSession.IsTokenExpired()` before each broker request
- If expired, prompt login dialog with "Session expired — please log in again"
- Or implement refresh token flow (stretch goal)
- **Files:** `AuthSession.cs`, `MainWindowViewModel.cs`

---

### Stream F: Folder Watcher (BROK-05)
**Owner:** BrokerService  
**Goal:** Auto-index new or modified files within 30 seconds.

#### T2.24: Implement FolderWatcher Service
- Create `FolderWatcherHostedService` using `FileSystemWatcher`
- Watch configured root folder recursively
- On `Created` / `Changed` events:
  - Validate file type
  - Compute SHA256
  - Skip if duplicate
  - Extract metadata
  - Generate thumbnail
  - Persist to database
- On `Renamed` event: update `StoragePath`
- On `Deleted` event: soft-delete asset
- Debounce rapid events (500ms window)
- **Files:** `FolderWatcherHostedService.cs`, `Program.cs`

#### T2.25: Add Folder Watcher Configuration
- `Broker:WatchedFolders[]` array in appsettings
- UI to add/remove watched folders in admin panel
- Persist watched folder list to database
- **Files:** `appsettings.json`, `AdminPanelViewModel.cs`

---

## Verification Plan

### Success Criteria (from ROADMAP)

| # | Criterion | How to Verify |
|---|-----------|---------------|
| 1 | Broker service starts and accepts TCP connections | Integration test: start broker, connect raw TCP, send `GetServiceStatusRequest` |
| 2 | Catalog browser authenticates and receives JWT | E2E test: launch client in multi-user mode, log in, verify `AuthSession.Token` is set |
| 3 | Asset list, search, metadata display via broker | E2E test: seed broker with 50 assets, browse gallery, search, verify metadata panel |
| 4 | Folder watcher auto-indexes within 30 seconds | Integration test: drop file into watched folder, poll `ListAssetsRequest` until it appears |
| 5 | Audit log records every authenticated request | Unit test: verify `AccessLog` entry created for each handler invocation |

### Architecture Review Closure

| Review Item | Task(s) | How to Verify |
|-------------|---------|---------------|
| TLS transport | T2.1 | Packet capture shows TLS handshake; no plaintext JWT visible |
| JWT secret removed | T2.2 | `appsettings.json` has no secret; startup fails without env var |
| Auth on all handlers | T2.3 | Unit test: send request without token, verify `StatusCode = 7` |
| StatusMessages tag fix | T2.4 | Round-trip serialize/deserialize test passes with all fields |
| Opcode enum | T2.5 | No `nameof()` in `Envelope.MessageType`; enum values stable |
| Auto-reconnect | T2.6 | Kill broker, restart, client reconnects within 10s without manual action |
| Sidebar complete | T2.7 | Multi-user mode shows folder tree, keyword tree, media formats, date tree |
| Graceful shutdown | T2.9 | Send SIGTERM, active requests complete within 30s, no DB corruption |
| Concurrency token | T2.16 | Concurrent update test: two clients edit same asset, one gets `StatusCode = 9` |
| Brute-force protection | T2.13 | 6 failed logins from same IP, 7th returns `StatusCode = 16` |

---

## Task Dependency Graph

```
T2.2 (JWT secret) ──> T2.12 (Fix key race) ──> T2.3 (Auth handlers)
T2.4 (Tag bug) ──> T2.5 (Opcode enum)
T2.1 (TLS) ──> T2.6 (Reconnect) ──> T2.19 (TCS leak)
T2.8 (Track tasks) ──> T2.9 (Graceful shutdown)
T2.16 (Concurrency) ──> T2.17 (Query perf)
T2.3 (Auth handlers) ──> T2.7 (Sidebar) ──> T2.24 (Folder watcher)
T2.13 (Rate limit) ──> T2.15 (Security logging)
```

**Parallel execution groups:**
- Group 1 (Security): T2.1, T2.2, T2.4, T2.12, T2.13
- Group 2 (Broker reliability): T2.8, T2.9, T2.10, T2.11
- Group 3 (Auth correctness): T2.3, T2.14, T2.15
- Group 4 (Data layer): T2.16, T2.17, T2.18
- Group 5 (Client): T2.5, T2.6, T2.19, T2.20, T2.21, T2.22, T2.23
- Group 6 (Features): T2.7, T2.24, T2.25

---

## Risk Register

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| TLS certificate setup is complex for local dev | High | Medium | Provide dev self-signed cert script; allow `--insecure` flag for local testing only |
| Opcode enum change breaks existing tests | Medium | Medium | Update all tests in same commit; add backward-compat shim if needed |
| Sidebar server endpoints are large API surface | Medium | Medium | Implement incrementally; start with folders + keywords |
| Folder watcher race conditions with ingestion | Medium | High | Use file locks or SHA256 dedup; debounce events |
| EF Core 10 preview instability | Medium | High | Pin version; monitor for breaking changes; have rollback plan |

---

## Estimates

| Stream | Tasks | Estimated Effort |
|--------|-------|-----------------|
| A: Security Hardening | T2.1–T2.7 | 5 days |
| B: Broker Reliability | T2.8–T2.11 | 2 days |
| C: Auth Layer Fixes | T2.12–T2.15 | 2 days |
| D: Database & Data Layer | T2.16–T2.18 | 1.5 days |
| E: Client Resilience & UX | T2.19–T2.23 | 3 days |
| F: Folder Watcher | T2.24–T2.25 | 2 days |
| **Total** | **25 tasks** | **~15.5 days** |

---

## Definition of Done

- [ ] All 7 architecture review blockers resolved and verified
- [ ] All 6 Phase 2 success criteria pass
- [ ] 100% of existing tests still pass (no regressions)
- [ ] New integration tests cover TLS handshake, reconnection, rate limiting, concurrency conflict
- [ ] Multi-user mode sidebar is functionally equivalent to standalone mode
- [ ] Code review complete (self-review or external)
- [ ] AGENTS.md updated with any new conventions or critical issues

---

*Plan created: 2026-05-23*  
*Based on architecture review: `.planning/ARCHITECTURE-REVIEW.md`*
