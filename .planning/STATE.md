# State: adam

**Project:** adam — Digital Asset Management System  
**Initialized:** 2026-05-23  
**Current Phase:** 3 — Multi-User Concurrency (planning)
**Current Milestone:** v1.0 — Core DAM

## Project Reference

See: `.planning/PROJECT.md` (updated 2026-05-23)

**Core value:** Users can browse, search, and manage digital assets with full metadata round-trip across platforms  
**Current focus:** Harden TCP broker, secure auth layer, complete client/server integration, enable shared catalog browsing

## Phase Progress

| Phase | Status | Plans | Progress |
|-------|--------|-------|----------|
| 1 | ✅ | 1/1 | 100% | Archived |
| 2 | ✅ | 1/1 | 100% | Archived |
| 3 | 🚧 | 0/0 | 0% | Next |
| 4 | ○ | 0/0 | 0% |
| 5 | ○ | 0/0 | 0% |
| 6 | ○ | 0/0 | 0% |
| 7 | ○ | 0/0 | 0% |

## Active Decisions

| Decision | Status | Notes |
|----------|--------|-------|
| Dual-mode architecture | ✓ Confirmed | Standalone first, then multi-user |
| Avalonia 12 (not 11 as spec'd) | ✓ Confirmed | Project already uses 12.0.3 |
| EF Core 10 preview | ⚠️ Risk | Monitor for stable release |
| Manual protobuf | ✓ Confirmed | No protoc generator in use |
| TLS required for multi-user | ✓ Decided | Architecture review made this mandatory |

## Blockers

None.

## Architecture Review Complete

**Location:** `.planning/ARCHITECTURE-REVIEW.md`

5 domains analyzed, 47 findings identified, 30 recommendations made.

### Critical Issues Found (Must Fix in Phase 2)
- **✅ CRITICAL-4:** No TLS — JWT and passwords in plaintext over TCP → **FIXED** (T2.1)
- **✅ CRITICAL-5:** Hardcoded JWT secret committed to source control → **FIXED** (T2.2 — env var required, documented)
- **✅ CRITICAL-6:** No authorization on Asset/Collection/Change handlers → **FIXED** (T2.3)
- **✅ CRITICAL-8:** StatusMessages.cs compares raw protobuf tag values (bug) → **FIXED** (T2.4)
- **✅ CRITICAL-9:** String-based MessageType routing tied to C# `nameof` → **FIXED** (T2.5 — stable opcode enum)
- **✅ CRITICAL-10:** BrokerClient has zero reconnection logic → **FIXED** (T2.6)
- **✅ HIGH-13:** Multi-user sidebar is non-functional (empty trees) → **FIXED** (T2.7)

### Overall Grade: C (Adequate for prototype, not production)

## Context Notes

- Brownfield codebase exists with domain models, EF Core config, TCP transport, and Avalonia shell
- See `.planning/codebase/` for architecture, conventions, and concerns
- **FIXED:** Client/server port mismatch (5000 vs 9100) — BrokerClient now uses 9100
- **FIXED:** All failing tests — BulkOperationQueue disposal, progress tracking, checksum uniqueness, filter expectations
- **FIXED:** DatePicker binding — SelectedAssetDateTaken changed from DateTime? to DateTimeOffset? with model conversion
- **FIXED:** Static mutable JWT key in AuthHandler (T2.12) — instance field, no longer static
- **FIXED:** TLS transport (T2.1), brute-force protection (T2.13), JWT claims (T2.14), security logging (T2.15)
- **FIXED:** Auto-reconnect (T2.6), timeout/retry (T2.20), ChangePoller retry (T2.21), token expiry (T2.23), connection status UI (T2.22)
- **FIXED:** FolderWatcher service (T2.24) with debounced auto-indexing
- **FIXED:** Watched folder DB persistence + admin panel UI (T2.25)
- All 104 tests pass (2 Docker-dependent skipped)
- **25 of 25 Phase 2 tasks complete**
- **Phase 2 archived to:** `.planning/milestones/v1.0-in-progress.md`
- **Phase 3 plan created:** `.planning/plans/phase-3/PLAN.md` (9 tasks, ~5.5 days)

## Phase 2 Plan

**Location:** `.planning/plans/phase-2/PLAN.md`
**Tasks:** 25 organized into 6 work streams
**Estimated Effort:** ~15.5 days

### Work Streams
1. **Security Hardening** — TLS, JWT secret removal, authorization, protocol fixes
2. **Broker Reliability** — Task observation, graceful shutdown, write timeout, idle detection
3. **Auth Layer Fixes** — Signing key race fix, brute-force protection, structured security logging
4. **Database & Data Layer** — Concurrency tokens, query optimization, connection resiliency
5. **Client Resilience & UX** — Auto-reconnect, retry logic, token expiration, connection status UI
6. **Folder Watcher** — FileSystemWatcher-based auto-indexing

## Next Actions

1. `/gsd-discuss-phase 3` — Gather context for Multi-User Concurrency
2. `/gsd-plan-phase 3` — Create detailed plan for real-time change propagation
3. Create integration tests for TLS, reconnection, and rate limiting

---
*State updated: 2026-05-23 after initialization*
