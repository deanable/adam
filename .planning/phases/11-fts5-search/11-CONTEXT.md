# Phase 11 — Full-Text Search (FTS5): Context

> Generated: 2026-06-13 via discuss-phase (Codebuff)

## Decisions

| ID | Decision | Value | Rationale |
|----|----------|-------|-----------|
| D11.1 | **Dedicated search bar** above the gallery grid | Prominent search box with autocomplete suggestions | Replaces/extends current filter area; matches user expectations (search bar at top); will show suggestions |
| D11.2 | **SearchService is replaced, not wrapped** | SearchService.Like-based logic is replaced with IFtsService call | No need for dual implementation; FTS is strictly better; keep SearchService for non-FTS filter parameters only |
| D11.3 | **All three providers** get full FTS support | SQLite FTS5, PostgreSQL tsvector/GIN, SQL Server CONTAINS | Phase 6 established multi-provider patterns; each provider gets its own IFtsService implementation |
| D11.4 | **FTS index synced via triggers** (SQLite), **generated columns** (PostgreSQL), **full-text catalog** (SQL Server) | Provider-native mechanisms | Lowest overhead; no external sync service needed; consistent with existing provider patterns |

## Current State (code already written in working tree)

The following are **already implemented** (untracked/new in working tree):
- `IFtsService` interface with `SearchAsync`, `GetSuggestionsAsync`, `IsAvailableAsync`, `EnsureReadyAsync`, `RebuildIndexAsync`
- `SqliteFtsService` — full FTS5 implementation with virtual table, triggers, bm25 ranking, rowid mapping
- `PostgresFtsService` — tsvector/tsquery implementation with GIN index
- `SqlServerFtsService` — CONTAINS implementation with full-text catalog
- `SearchResult` model with `Asset`, `Rank`, `MatchedFields`
- `ThumbnailCache` — in-memory LRU cache (Phase 12 overlap)
- DI registration in `BrokerService/Program.cs` and `CatalogBrowser/App.axaml.cs`
- `AssetGalleryViewModel` — partial FTS integration (SearchText, OnSearchTextChangedAsync with debounce, ExecuteSearchAsync)

## Remaining Work

1. **SearchService update**: Replace LIKE logic with IFtsService call while preserving filter parameter support
2. **Search bar in gallery**: Add dedicated search TextBox above the gallery
3. **Search result highlighting**: Show matched snippets in gallery tiles
4. **Suggestions UI**: Wire autocomplete dropdown to GetSuggestionsAsync
5. **Multi-provider tests**: End-to-end FTS tests for all providers
