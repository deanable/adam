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
| 1 | ○ | 0/0 | 0% |
| 2 | ○ | 0/0 | 0% |
| 3 | ○ | 0/0 | 0% |
| 4 | ○ | 0/0 | 0% |
| 5 | ○ | 0/0 | 0% |
| 6 | ○ | 0/0 | 0% |
| 7 | ○ | 0/0 | 0% |

## Active Decisions

| Decision | Status | Notes |
|----------|--------|-------|
| Dual-mode architecture | — Pending | Standalone first, then multi-user |
| Avalonia 12 (not 11 as spec'd) | ✓ Confirmed | Project already uses 12.0.3 |
| EF Core 10 preview | ⚠️ Risk | Monitor for stable release |
| Manual protobuf | — Pending | No protoc generator in use |

## Blockers

None.

## Context Notes

- Brownfield codebase exists with domain models, EF Core config, TCP transport, and Avalonia shell
- See `.planning/codebase/` for architecture, conventions, and concerns
- Critical issues identified: client/server port mismatch (5000 vs 9100), static mutable JWT key

## Next Actions

1. `/gsd-discuss-phase 1` — gather context and plan standalone mode implementation
2. `/gsd-plan-phase 1` — create detailed plan for Phase 1

---
*State updated: 2026-05-23 after initialization*
