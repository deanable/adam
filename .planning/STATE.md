# State: adam

**Project:** adam — Digital Asset Management System  
**Initialized:** 2026-05-23  
**Current Phase:** 1 — Standalone Mode  
**Current Milestone:** v1.0 — Core DAM

## Project Reference

See: `.planning/PROJECT.md` (updated 2026-05-23)

**Core value:** Users can browse, search, and manage digital assets with full metadata round-trip across platforms  
**Current focus:** Build standalone catalog browser with SQLite, browse/search/view, and metadata extraction

## Phase Progress

| Phase | Status | Plans | Progress |
|-------|--------|-------|----------|
| 1 | ◆ | 1/1 | 25% |
| 2 | ○ | 0/0 | 0% |
| 3 | ○ | 0/0 | 0% |
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

## Blockers

None.

## Context Notes

- Brownfield codebase exists with domain models, EF Core config, TCP transport, and Avalonia shell
- See `.planning/codebase/` for architecture, conventions, and concerns
- **FIXED:** Client/server port mismatch (5000 vs 9100) — BrokerClient now uses 9100
- **FIXED:** All failing tests — BulkOperationQueue disposal, progress tracking, checksum uniqueness, filter expectations
- **REMAINING:** Static mutable JWT key in AuthHandler (race condition risk)
- All 49 tests pass (2 Docker-dependent skipped)

## Next Actions

1. Continue Phase 1 implementation — verify standalone mode end-to-end functionality
2. `/gsd-plan-phase 1` — create detailed execution plan for remaining Phase 1 work

---
*State updated: 2026-05-23 after initialization*
