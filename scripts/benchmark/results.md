# Adam Benchmark Results — 100K Assets

- **Date**: 2026-06-09 20:47:56 UTC
- **Database**: C:\Users\Dean\source\repos\adam\scripts\benchmark\.adam\catalog.db
- **Asset count**: 100 000
- **SQLite version**: 3.46.1

| # | Scenario | Cold (ms) | Warm (ms) |
|---|----------|-----------|-----------|
| 1 | Full table count (no filter) | 12,1 | 2,9 |
| 2 | Type filter (Images ~60%) | 50,3 | 10,2 |
| 3 | Date range filter (last 90 days) | 35,4 | 20,7 |
| 4 | Combined filter (Image + 2025 + <100MB) | 2,5 | 0,3 |
| 5 | Sort by Date Added (page 1 of 50) | 8,7 | 0,8 |
| 6 | Sort by File Size (page 1 of 50) | 7,3 | 0,3 |
| 7 | Deep pagination (page 1000 of 50) | 36,5 | 29,8 |
| 8 | Keyword filter (Nature) | 192,5 | 147,6 |
| 9 | Category filter | 120,8 | 101,8 |
| 10 | Folder prefix filter (/photos/) | 51,5 | 48,3 |
| 11 | Title text search (contains 'Photo') | 53,2 | 52,6 |
| 12 | Gallery load (page 1 of 50, all columns) | 13,7 | 0,5 |

## Observations

- **Average cold query**: 48,7 ms
- **Average warm query**: 34,6 ms
- **Slowest cold query**: "8. Keyword filter (Nature)" at 192,5 ms
- **Fastest warm query**: "4. Combined filter (Image + 2025 + <100MB)" at 0,3 ms

✅ **Cold queries within 2s target** — 100K baseline acceptable.

## Next Steps

1. Review slow queries and add missing composite indexes (T8.2)
2. Evaluate FTS5 full-text search for Title/Description/Keyword searches (T8.3)
3. Profile thumbnail generation and gallery rendering at 100K (T8.4–T8.5)
