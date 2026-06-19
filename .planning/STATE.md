# State: adam

**Project:** adam — Digital Asset Management System  
**Initialized:** 2026-05-23  
**Current Phase:** ✅ Phase 24 — Metadata Panels & Preferences Persistence (Complete)
**Current Milestone:** ✅ v5.0 — AI-Native DAM (Complete)

## Project Reference

See: `.planning/PROJECT.md` (updated 2026-05-23)

**Core value:** Users can browse, search, and manage digital assets with full metadata round-trip across platforms  
**Status:** All 24 phases complete (v1.0–v5.0).

All milestones (v1.0 through v4.x) 🏁 archived. v5.0 milestone complete with Phases 22-24 (AI-Native DAM + User Preferences).

## Phase Progress

| Phase | Status | Plans | Progress |
|-------|--------|-------|----------|
| 1 | ✅ | 1/1 | 100% | Archived |
| 2 | ✅ | 1/1 | 100% | Archived |
| 3 | ✅ | 1/1 | 100% | Archived |
| 4 | ✅ | 1/1 | 100% | Archived |
| 5 | ✅ | 1/1 | 100% | Archived |
| 6 | ✅ | 1/1 | 100% | Archived |
| 7 | ✅ | 1/1 | 100% | Archived |
| 8 | ✅ | 1/1 | 100% | Archived |
| 9 | ✅ | 1/1 | 100% | Archived |
| 10 | ✅ | 1/1 | 100% | Archived |
| 11 | ✅ | 1/1 | 100% | Archived |
| 12 | ✅ | 1/1 | 100% | Archived |
| **13** | ✅ Complete | 1/1 | 100% | Production hardening: solution file, CI fix, handler guards, protobuf docs, logging, FSW batching, EF Core prep |
| **14** | ✅ Complete | 1/1 | 100% | T14.1 batch editing, T14.2 CSV import/export, T14.3 presets + activity feed, T14.4 Office XMP, T14.5 bulk advanced filters, T14.6 AI tag refinement, AI model selector, execution provider |
| **15** | ✅ Complete | 1/1 | 100% | Quality & Platform: EF Core 10 stable, CSV streaming, feed pruning, re-scan wiring, 34 deferred tests created |
| **16** | ✅ Complete | 1/1 | 100% | Provenance & Trust: AiGenerated flag, wire protocol, UI badge, review dialog |
| **17** | ✅ Complete | 1/1 | 100% | Collaboration: Comment threads (COLL-V2-02) |
| **18** | ✅ Complete | 1/1 | 100% | Integration: Plugin system for metadata extractors (INTG-V2-01) |
| **19** | ✅ Complete | 1/1 | 100% | Advanced Search & Discovery: saved searches, search history, smart collections, semantic search, visual similarity |
| **20** | ✅ Complete | 1/1 | 100% | UX Modernization: theme engine, loupe view, compare view, drag-reorder collections, design templates |
| **21** | ✅ Complete | 1/1 | 100% | Virtualized Gallery & DB Optimization: async thumbnails, keyset pagination, composite indexes, non-blocking I/O |
| **22** | ✅ **Complete** | `.planning/plans/phase-22/22-PLAN.md` | 100% | AI-Native DAM: smart search ranking, auto-album generation, near-duplicate detection |
| **23** | ✅ **Complete** | `.planning/plans/phase-23/23-PLAN.md` | 100% | Facial Recognition: YuNet + ArcFace ONNX pipeline, HDBSCAN clustering, person management |
| **24** | ✅ **Complete** | `.planning/plans/phase-24/24-PLAN.md` | 100% | Metadata panels (§25-B), user preferences (§27), populated Settings tab, panel persistence, EF Core migration, broker prefs |

## Milestone Archives

| Milestone | Document | Status |
|-----------|----------|--------|
| v1.0 — Core Foundation (Phases 1-4) | `.planning/milestones/v1.0-in-progress.md` | 🏁 Archived |
| v1.1 — Server Maturity (Phases 5-6) | `.planning/milestones/v1.0-in-progress.md` | 🏁 Archived |
| v1.2 — Client Polish (Phases 7-8) | `.planning/milestones/v1.0-in-progress.md` | 🏁 Archived |
| v2.0 — Advanced Features & Performance (Phases 10-12) | `.planning/milestones/v2.0-release.md` | 🏁 Archived |
| v2.x — Production Hardening & Feature Growth (Phases 13-14) | `.planning/milestones/v2x-release.md` | 🏁 Archived |
| v3.0 — Provenance & Trust (Phases 15-16) | `.planning/milestones/v3.0-release.md` | 🏁 Archived |
| v3.1 — Collaboration (Phase 17) | `.planning/milestones/v3.1-release.md` | 🏁 Archived |
| v3.2 — Integration (Phase 18) | `.planning/milestones/v3.2-release.md` | 🏁 Archived |
| **v4.0 — Advanced Discovery & Experience (Phases 19-20)** | **`.planning/milestones/v4.0-release.md`** | **🏁 Archived** |
| **v4.x — Performance & UX (Phases 20-21)** | **`.planning/milestones/v4.0-release.md`** | **🏁 Archived** |
| **v5.0 — AI-Native DAM (Phases 22-24)** | **`.planning/plans/phase-22/22-PLAN.md`** | **🏁 Complete** |

## Key Metrics

| Metric | Value |
|--------|-------|
| **Total phases** | 24 complete |
| **Total tests** | 1,335 passing (2 skipped Docker-dependent) |
| **Projects** | Adam.CatalogBrowser, Adam.ServiceManager, Adam.BrokerService, Adam.Shared |
| **Phase 13 plan** | `.planning/plans/phase-13/13-PLAN.md` |
| **Phase 14 plan** | `.planning/plans/phase-14/14-PLAN.md` |
| **Phase 14 UAT** | `.planning/plans/phase-14/14-UAT.md` |
| **Failing tests** | 0 ✅ |
| **Phase 19 plan** | `.planning/plans/phase-19/19-PLAN.md` |
| **Phase 20 plan** | `.planning/plans/phase-20/20-PLAN.md` |
| **Phase 21 UAT** | `.planning/plans/phase-21/21-UAT.md` |
| **Phase 22 plan** | `.planning/plans/phase-22/22-PLAN.md` |
| **Phase 24 plan** | `.planning/plans/phase-24/24-PLAN.md` |
| **Phase 24 context** | `.planning/plans/phase-24/24-CONTEXT.md` |
| **Blockers** | None |

## Active Decisions

| Decision | Status | Notes |
|----------|--------|-------|
| Dual-mode architecture | ✓ Confirmed | Standalone first, then multi-user |
| Avalonia 12 | ✓ Confirmed | Project uses 12.0.3 |
| EF Core 10 stable | ✓ Confirmed | Upgraded in Phase 15 |
| AiGenerated provenance | ✓ Implemented | Phase 16 — wire protocol + UI badge |
| Manual protobuf | ✓ Confirmed | Wire format documented in Phase 13 |
| TLS required for multi-user | ✓ Implemented | Self-signed dev cert + production CA |
| Phase order | ✓ Hardening -> Features | Tech debt fixed before building features |

## Next Actions

1. ✅ **Phase 19** — Advanced Search & Discovery committed as `d489c83`
2. ✅ **Phase 20** — UX Modernization committed as `b1bc667`
3. ✅ **Phase 21** — Virtualized Gallery & DB Optimization committed as `bcdfd82`
4. ✅ **Phase 21 UAT** — all checks passed (`.planning/plans/phase-21/21-UAT.md`)
5. ✅ **v4.0/v4.x milestone** archived — all 21 phases complete
6. ✅ **Phase 22 discussion** — decisions locked in `22-CONTEXT.md`
7. ✅ **Phase 22 plan** created at `.planning/plans/phase-22/22-PLAN.md`
8. ✅ **Phase 22 wave allocation** — 6 waves with precise sub-tasks, file paths, LOC estimates, and dependency graph
9. ✅ **Phase 23 discussion** — decisions locked in `23-CONTEXT.md`
10. ✅ **Phase 23 model research** — ArcFace (`garavv/arcface-onnx`), YuNet (`opencv/face_detection_yunet`), SCRFD evaluated
11. ✅ **Phase 23 plan** created at `.planning/plans/phase-23/23-PLAN.md`
12. ✅ **Phase 22 executed** — smart search ranking, auto-album generation, near-duplicate detection (already implemented)
13. ✅ **Phase 23 executed** — facial recognition with YuNet + ArcFace ONNX pipeline
14. ✅ **Phase 24 discussed** — decisions locked in `24-CONTEXT.md` (Dec 2026-06-18)
15. ✅ **Phase 24 plan** created at `.planning/plans/phase-24/24-PLAN.md`
16. ✅ **Phase 24 executed** — 8 collapsible metadata panels, user preferences service, populated Settings tab with 11 categories, panel expand persistence via `metadata.expandedPanels`, EF Core migration, broker PreferenceHandler, 1,335 tests passing
17. ✅ **v5.0 milestone complete** — Phases 22-24 complete

---
*State updated: 2026-06-18 — Phases 22-24 complete. v5.0 milestone complete. 1,335 tests passing.*

