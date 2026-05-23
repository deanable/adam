# Plan: Phase 3 — Multi-User Concurrency

**Phase:** 3 of 7  
**Goal:** Multiple users access the broker simultaneously with consistent state and real-time change propagation.  
**Requirements:** BROK-02, BROK-03, BROK-04  
**Depends on:** Phase 2 (Multi-User Foundation)  
**Created:** 2026-05-23  

---

## Executive Summary

Phase 3 transforms the broker from a request-response server into a real-time collaborative system. Three requirements drive the design:

1. **BROK-02** — Performance under concurrency: 10 users, <3s response
2. **BROK-03** — Real-time sync: changes propagate within 5 seconds
3. **BROK-04** — Conflict resolution: concurrent edits use last-write-wins

The current architecture uses 5-second client polling via `ChangePoller`. This plan replaces polling with **server-initiated push notifications** while keeping the request-response model for user actions. The optimistic concurrency token (`Version`) added in Phase 2 becomes the foundation for conflict detection.

---

## Architecture

### Current State (Phase 2 End)

```
Client A ──TCP──> Broker <──TCP── Client B
  │                  │
  │  Poll 5s         │  SQLite/PostgreSQL/SQL Server
  ▼                  ▼
ChangePoller     ChangeHandler (query DB)
```

### Target State (Phase 3 End)

```
Client A ──TCP──> Broker <──TCP── Client B
  │    ▲            │    ▲
  │    │ notify     │    │ notify
  │    └─────────────┘    │
  │   ChangeNotificationService
  │         │
  └─────────┘ (triggers local refresh)
```

**Key insight:** We keep the existing request-response TCP framing. The server sends **unsolicited** `ChangeNotification` envelopes to all authenticated connections when an asset is modified. The client's existing `ReceiveLoopAsync` already processes incoming envelopes — we extend it to handle notifications that don't match any pending request correlation ID.

---

## Tasks

### T3.1: Connection Registry

Track active authenticated connections for broadcast targeting.

- **Create** `ConnectionRegistry` singleton service
  - `Register(connectionId, userId, stream/connectionState)` — called after successful login
  - `Unregister(connectionId)` — called on disconnect
  - `GetAllConnections()` — returns all active connection IDs
  - `GetConnectionsExcept(connectionId)` — returns all *other* connections (for broadcast exclusion)
  - Thread-safe using `ConcurrentDictionary`
- **Modify** `TcpListenerService` to call `Unregister` on disconnect
- **Modify** `AuthHandler.LoginAsync` to call `Register` after successful authentication
- **Files:** `ConnectionRegistry.cs`, `TcpListenerService.cs`, `AuthHandler.cs`

**Acceptance:** Unit test: register 3 connections, query returns 3; unregister 1, query returns 2.

---

### T3.2: Change Notification Protocol

Add message types and client-side handling for unsolicited server pushes.

- **Add** `MessageTypeCode` values (110–112):
  - `ChangeNotification = 110` — server -> client push
  - `SubscribeChangesRequest = 111` — client -> server (explicit subscription)
  - `SubscribeChangesResponse = 112`
- **Create** `ChangeNotification` protobuf contract:
  - `EntityId` (string) — asset ID
  - `Action` (string) — "updated", "deleted", "created"
  - `Timestamp` (long) — Unix seconds
  - `ChangedByUserId` (string) — who made the change
- **Modify** `BrokerClient.ReceiveLoopAsync`:
  - If `envelope.CorrelationId` matches a pending request -> complete TCS (existing behavior)
  - If `envelope.MessageType == ChangeNotification` -> raise `NotificationReceived` event
  - Otherwise -> log warning about unexpected message
- **Add** `event EventHandler<ChangeNotification>? NotificationReceived` to `BrokerClient`
- **Files:** `MessageTypeCode.cs`, `NotificationMessages.cs`, `BrokerClient.cs`

**Acceptance:** Unit test: server sends ChangeNotification, client event fires with correct data.

---

### T3.3: Change Notification Service

Central service that receives change events and broadcasts to connected clients.

- **Create** `ChangeNotificationService` singleton:
  - `BroadcastAsync(ChangeNotification notification, string? excludeConnectionId = null)`
  - Iterates `ConnectionRegistry.GetConnectionsExcept(excludeConnectionId)`
  - Sends `ChangeNotification` envelope to each (fire-and-forget with error logging)
  - Uses `Task.WhenAll` for parallel sends, with individual try/catch per connection
  - If a send fails, log and unregister the connection (it is dead)
- **Consider:** Debounce rapid changes:
  - `ConcurrentDictionary<string, Timer>` per entity — if same asset changes twice within 500ms, only broadcast once with latest state
- **Files:** `ChangeNotificationService.cs`

**Acceptance:** Unit test: 3 connections registered, broadcast excludes sender, 2 clients receive notification.

---

### T3.4: Hook Asset Operations to Broadcast

Trigger notifications when assets are created, updated, or deleted.

- **Modify** `AssetHandler.UpdateAssetAsync`:
  - After `db.SaveChangesAsync()`, build `ChangeNotification` and call `_notificationService.BroadcastAsync(..., excludeConnectionId: request.CorrelationId)`
- **Modify** `AssetHandler.DeleteAssetAsync`:
  - After soft-delete save, broadcast with `Action = "deleted"`
- **Modify** `AssetHandler` to add `CreateAssetAsync`:
  - After creation, broadcast with `Action = "created"`
- **Modify** `SidebarHandler` folder/category/keyword operations:
  - Optional: broadcast structural changes if they affect all users
- **Modify** `WatchedFolderHandler.CreateAsync/DeleteAsync`:
  - Optional: broadcast folder watcher config changes
- **Modify** `FolderWatcherHostedService.IngestFileAsync`:
  - After auto-indexing, broadcast with `Action = "created"`
- **Files:** `AssetHandler.cs`, `SidebarHandler.cs`, `WatchedFolderHandler.cs`, `FolderWatcherHostedService.cs`

**Acceptance:** Integration test: client A updates asset, client B receives ChangeNotification within 1 second.

---

### T3.5: Client-side Notification Handling

React to server push notifications in the UI.

- **Modify** `MainWindowViewModel` (or create a `NotificationService`):
  - Subscribe to `_modeManager.BrokerClient.NotificationReceived`
  - On notification:
    - If action is "updated" or "created": refresh the asset gallery (selective or full)
    - If action is "deleted": remove asset from gallery if present
    - Log: "Asset {Id} {action}d by another user"
- **Consider:** Throttle rapid notifications (e.g., 200ms debounce on gallery refresh)
- **Files:** `MainWindowViewModel.cs` or new `NotificationService.cs`

**Acceptance:** Manual test: two clients open, user A updates asset title, user B sees change within 2 seconds.

---

### T3.6: Concurrent Edit Conflict Resolution

Implement last-write-wins with explicit conflict detection.

**Current state:** `Version` concurrency token exists (T2.16). Client sends `Version` with `UpdateAssetRequest`. Server checks `asset.Version == request.Version`. If not, returns `StatusCode = 9` (conflict).

- **Modify** `AssetHandler.UpdateAssetAsync`:
  - Current conflict returns generic error. Enhance to return:
    - `StatusCode = 9` (conflict)
    - `ErrorMessage = "Asset was modified by another user"`
    - Optional: include current asset data in `Payload` so client can merge or show diff
- **Modify** `AssetGalleryViewModel` (or whoever handles UpdateAssetResponse):
  - If `StatusCode == 9`, show dialog: "This asset was modified by another user. Refresh to see latest version."
  - Offer "Refresh" button that reloads the asset
- **Verify** `UpdateAssetRequest` includes `Version` field:
  - If missing, add to protobuf contract
- **Files:** `AssetHandler.cs`, `AssetMessages.cs`, `AssetGalleryViewModel.cs`

**Acceptance:** Integration test: two clients load same asset (Version = 1). Both edit simultaneously. First save succeeds (Version -> 2). Second save gets StatusCode = 9. Second client refreshes and sees first client's changes.

---

### T3.7: Performance Optimization

Ensure 10 concurrent users achieve <3s response times.

- **Profile** `AssetHandler.ListAssetsAsync`:
  - Current query includes `.Include(a => a.Collection).Include(a => a.Keywords).AsSplitQuery()`
  - Add paging limit enforcement (max 100 per page)
  - Consider adding `CountAsync()` optimization (separate count query vs count in same query)
- **Optimize** `SidebarHandler`:
  - Cache folder tree, keyword tree, and media format counts in memory (with 5-second TTL)
  - These are expensive queries that don't change rapidly
  - Invalidate cache on asset create/update/delete
- **Optimize** broadcast:
  - `ChangeNotificationService.BroadcastAsync` should not block the request handler
  - Use `Task.Run` or fire-and-forget for broadcast
  - Cap broadcast to 50 connections (already MaxConnections)
- **Files:** `AssetHandler.cs`, `SidebarHandler.cs`, `ChangeNotificationService.cs`

**Acceptance:** Integration test: 10 concurrent clients each browse, search, and poll. 95th percentile response time < 3 seconds.

---

### T3.8: Deprecate ChangePoller (or reduce interval)

With push notifications, polling becomes a fallback.

- **Option A (Recommended):** Reduce `ChangePoller` interval from 5s to 30s as a safety net
  - Most changes arrive via push within 1s
  - Polling catches missed notifications (e.g., client temporarily disconnected)
- **Option B:** Remove `ChangePoller` entirely and rely on reconnect logic to refresh state
- **Modify** `ChangePoller` interval to 30s if keeping
- **Files:** `ChangePoller.cs`

**Acceptance:** ChangePoller still works but fires infrequently. Push notifications handle 99% of change propagation.

---

### T3.9: Integration Tests

Comprehensive tests for concurrency scenarios.

- **Create** `ConcurrentClientsTests.cs` additions:
  - `Ten_concurrent_clients_can_browse_and_search_simultaneously` — measure response times
  - `Change_notification_propagates_to_all_connected_clients` — connect N clients, modify asset, verify all receive notification
  - `Concurrent_edit_to_same_asset_returns_conflict_for_second_client` — verify last-write-wins
  - `Client_reconnects_and_receives_missed_changes` — disconnect, modify, reconnect, verify sync
- **Create** `ChangeNotificationTests.cs`:
  - `Broadcast_excludes_sender` — sender does not receive its own notification
  - `Broadcast_reaches_all_other_authenticated_clients` — all others get it
  - `Dead_connections_are_removed_from_registry` — simulate failed send
- **Files:** `tests/Adam.BrokerService.Tests/Integration/ConcurrentClientsTests.cs`

**Acceptance:** All new tests pass. No regressions in existing tests.

---

## Task Dependency Graph

```
T3.1 (Connection Registry)
    |
    ├──> T3.2 (Notification Protocol)
    |       |
    |       ├──> T3.3 (Notification Service)
    |       |       |
    |       |       ├──> T3.4 (Hook Broadcast)
    |       |       |       |
    |       |       |       ├──> T3.5 (Client Handling)
    |       |       |       |
    |       |       |       └──> T3.8 (ChangePoller adjust)
    |       |       |
    |       |       └──> T3.7 (Performance)
    |       |
    |       └──> T3.6 (Conflict Resolution)
    |
    └──> T3.9 (Integration Tests)  [depends on T3.4 + T3.6]
```

**Parallel execution groups:**
- Group 1 (Infrastructure): T3.1, T3.2, T3.3
- Group 2 (Integration): T3.4, T3.6
- Group 3 (Client): T3.5, T3.8
- Group 4 (Polish): T3.7
- Group 5 (Tests): T3.9

---

## Risk Register

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Broadcast fails under high load | Medium | High | Debounce + async fire-and-forget; cap to MaxConnections |
| Client misses notification during reconnect | Medium | Medium | Keep ChangePoller as 30s fallback |
| Conflict resolution confuses users | Medium | Medium | Clear error message: "Modified by another user" with Refresh button |
| Memory leak in ConnectionRegistry | Low | High | Auto-unregister on failed send + disconnect hook |
| Notification storm on bulk import | Medium | Medium | Debounce per entity; batch notifications for folder watcher |

---

## Estimates

| Stream | Tasks | Estimated Effort |
|--------|-------|-----------------|
| A: Infrastructure | T3.1–T3.3 | 1.5 days |
| B: Integration | T3.4, T3.6 | 1.5 days |
| C: Client | T3.5, T3.8 | 1 day |
| D: Performance | T3.7 | 0.5 days |
| E: Tests | T3.9 | 1 day |
| **Total** | **9 tasks** | **~5.5 days** |

---

## Definition of Done

- [ ] All 3 Phase 3 success criteria pass
- [ ] 10 concurrent users browse/search with 95th percentile < 3s
- [ ] Change notifications propagate within 5 seconds (target: < 2s)
- [ ] Concurrent edits return conflict (StatusCode = 9) for second client
- [ ] All existing tests pass (no regressions)
- [ ] New integration tests cover broadcast, conflict, and performance
- [ ] ChangePoller reduced to 30s or removed
- [ ] AGENTS.md updated with notification protocol details

---

*Plan created: 2026-05-23*  
*Based on Phase 2 completion: `.planning/milestones/v1.0-in-progress.md`*
