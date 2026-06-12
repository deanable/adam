---
goal: Replace LIKE-based search with provider-specific full-text search — FTS5 for SQLite, tsvector/tsquery for PostgreSQL, CONTAINS for SQL Server.
version: 2.0
date_created: 2026-06-12
last_updated: 2026-06-12
status: 'Planned'
tags: [search, fts, sqlite, postgresql, sqlserver, performance]
---

# Phase 11: Full-Text Search (FTS5)

![Status: Planned](https://img.shields.io/badge/status-Planned-blue)

Replace the LIKE-based `SearchService` with provider-specific full-text search. SQLite uses FTS5 virtual tables with trigger-based sync, PostgreSQL uses `tsvector`/`tsquery`, SQL Server uses full-text indexes. An `IFtsService` strategy pattern abstracts the provider differences.

**Depends on:** Phases 1–9 (v1.0 codebase), Phase 6 (multi-provider infrastructure)

---

## 1. Requirements & Constraints

- **PERF-01**: Full-text search across Title, Description, FileName, and keyword names returns results within 2 seconds at 100K assets
- **META-V2-01**: Search results include relevance ranking (best match first)
- **DB-02/03/04**: FTS must work across all three providers (SQLite, PostgreSQL, SQL Server) without changing caller code
- **PAT-003**: FTS index must stay in sync with the main `DigitalAssets` table automatically

---

## 2. Current State

The existing `SearchService` (src/Adam.Shared/Services/SearchService.cs) uses EF Core LINQ `.Contains()` which translates to `LIKE '%term%'` — an O(n) full table scan. The benchmark (T8.1) established baseline performance; FTS5 will bring this to O(log n).

---

## 3. Implementation Steps

### Work Stream 1: FTS Infrastructure

**GOAL:** Create the provider-agnostic FTS abstraction and SQLite FTS5 implementation.

| Task | Description | Status |
|------|-------------|--------|
| T11.1 | **`IFtsService` interface** — Define `Task<List<SearchResult>> SearchAsync(string query, int maxResults, CancellationToken ct)` in `Adam.Shared/Services/`. `SearchResult` includes `DigitalAsset`, `Rank`, and `MatchedFields`. | ⬜ |
| T11.2 | **SQLite FTS5 virtual table** — Create EF Core migration that adds `DigitalAssets_FTS USING fts5(Title, Description, FileName, Keywords, content='DigitalAssets', content_rowid='Id')`. Keywords are a concatenated field (space-separated keyword names). | ⬜ |
| T11.3 | **Sync triggers** — SQL triggers on `DigitalAssets` for INSERT, UPDATE, DELETE that keep `DigitalAssets_FTS` in sync. Include a trigger on the `AssetKeywords` join table for keyword changes. | ⬜ |
| T11.4 | **`SqliteFtsService`** — Implement `IFtsService` using `FromSqlRaw` with FTS5 `MATCH` and `bm25()` ranking. Handle FTS5 query syntax (prefix matching with `*`, phrase queries with `""`). | ⬜ |
| T11.5 | **Initial index population** — Script to populate `DigitalAssets_FTS` from existing `DigitalAssets` table after migration. Run as part of `ModeManager.InitializeAsync` if FTS table is empty. | ⬜ |

### Work Stream 2: PostgreSQL & SQL Server

**GOAL:** Implement FTS for the remaining two providers.

| Task | Description | Status |
|------|-------------|--------|
| T11.6 | **`PostgresFtsService`** — Implement using `tsvector`/`tsquery`. Add generated column `SearchVector` to `DigitalAssets` with `to_tsvector('english', Title || ' ' || Description || ' ' || FileName || ' ' || Keywords)`. Index with GIN. | ⬜ |
| T11.7 | **`SqlServerFtsService`** — Implement using `CONTAINS` table-valued function. Create full-text catalog and index on `DigitalAssets(Title, Description, FileName)` plus a computed `Keywords` column. | ⬜ |
| T11.8 | **DI registration** — Register the correct `IFtsService` implementation based on `DbProviderConfig.CurrentProvider` in `App.axaml.cs` (CatalogBrowser) and `Program.cs` (BrokerService). | ⬜ |

### Work Stream 3: Search UI Integration

**GOAL:** Wire the new FTS service into the existing search UX.

| Task | Description | Status |
|------|-------------|--------|
| T11.9 | **Update `SearchService`** — Replace LINQ `.Contains()` with `IFtsService.SearchAsync()`. Preserve existing filter parameters (type, collection, tags, date range) as post-filter on FTS results. | ⬜ |
| T11.10 | **Search result highlighting** — Return matched snippets with `<mark>` tags from FTS highlight functions. Display in gallery list view and search results panel. | ⬜ |
| T11.11 | **Search suggestions** — As user types, show top-5 autocomplete suggestions from FTS `distinct` queries on Title and keyword names. Debounce 300ms. | ⬜ |

---

## 4. Execution Waves

| Wave | Tasks | Depends On | Rationale |
|------|-------|------------|-----------|
| **Wave 1 — SQLite FTS** | T11.1, T11.2, T11.3, T11.4, T11.5 | — | Core FTS infrastructure. SQLite first since it's the standalone default. |
| **Wave 2 — Multi-Provider** | T11.6, T11.7, T11.8 | Wave 1 | PostgreSQL and SQL Server implementations. Interface is stable from Wave 1. |
| **Wave 3 — UI Integration** | T11.9, T11.10, T11.11 | Wave 1 | Search UX improvements. Can start with SQLite, extend to all providers. |

---

## 5. Files

| File | Role |
|------|------|
| `src/Adam.Shared/Services/IFtsService.cs` | New — provider-agnostic FTS interface |
| `src/Adam.Shared/Services/SqliteFtsService.cs` | New — SQLite FTS5 implementation |
| `src/Adam.Shared/Services/PostgresFtsService.cs` | New — PostgreSQL tsvector implementation |
| `src/Adam.Shared/Services/SqlServerFtsService.cs` | New — SQL Server CONTAINS implementation |
| `src/Adam.Shared/Services/SearchService.cs` | Update — replace LIKE with IFtsService |
| `src/Adam.Shared/Data/AppDbContext.cs` | Migration: FTS virtual table + triggers |
| `src/Adam.Shared/Models/SearchResult.cs` | New — result model with rank and matched fields |
| `src/Adam.CatalogBrowser/App.axaml.cs` | DI registration for IFtsService |
| `src/Adam.BrokerService/Program.cs` | DI registration for IFtsService |

---

## 6. Testing

| Test | Type | Command |
|------|------|---------|
| FTS5 index creation | Automated | Migration test: verify virtual table exists after migration |
| FTS sync triggers | Automated | Insert/update/delete asset, verify FTS table stays in sync |
| Search relevance ranking | Automated | Insert assets with varying relevance, verify bm25 ranking order |
| Multi-provider FTS | Automated | `dotnet test tests/Adam.BrokerService.Tests --filter "FullyQualifiedName~DbProvider"` |
| Search performance | Manual | Load 100K assets, verify search < 2s |
| Autocomplete | Manual | Type in search box, verify suggestions appear within 300ms |

---

## 7. Risks

- **RISK-001**: FTS5 may not be available in all SQLite builds. Mitigation: check `sqlite3_compileoption_used('ENABLE_FTS5')` at startup, fall back to LIKE if unavailable.
- **RISK-002**: Trigger sync adds write overhead. Mitigation: triggers are lightweight (single INSERT/UPDATE/DELETE per operation); benchmark write throughput at 100K assets.
- **RISK-003**: PostgreSQL GIN index requires `CREATE INDEX CONCURRENTLY` for large tables to avoid locks. Mitigation: use non-concurrent index creation during initial migration only.
