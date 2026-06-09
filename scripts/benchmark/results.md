# Adam Benchmark Results — 100K Assets

- **Date**: 2026-06-09 01:28:43 UTC
- **Database**: C:\Users\Dean\source\repos\adam\scripts\benchmark\.adam\catalog.db
- **Asset count**: 100 000
- **SQLite version**: 3.46.1

| # | Scenario | Cold (ms) | Warm (ms) |
|---|----------|-----------|-----------|
| 1 | Full table count (no filter) | 11,0 | 2,6 |
| 2 | Type filter (Images ~60%) | 53,1 | 10,5 |
| 3 | Date range filter (last 90 days) | 34,5 | 20,4 |
| 4 | Combined filter (Image + 2025 + <100MB) | 2,3 | 0,2 |
| 5 | Sort by Date Added (page 1 of 50) | 8,7 | 0,9 |
| 6 | Sort by File Size (page 1 of 50) | 7,7 | 0,3 |
| 7 | Deep pagination (page 1000 of 50) | 39,5 | 30,2 |
| 8 | Keyword filter (Nature) | 190,4 | 147,4 |
| 9 | Category filter | 117,8 | 101,1 |
| 10 | Folder prefix filter (/photos/) | 52,4 | 47,1 |
| 11 | Title text search (contains 'Photo') | 52,3 | 50,4 |
| 12 | Gallery load (page 1 of 50, all columns) | 11,7 | 0,6 |

## Observations

- **Average cold query**: 48,4 ms
- **Average warm query**: 34,3 ms
- **Slowest cold query**: "8. Keyword filter (Nature)" at 190,4 ms
- **Fastest warm query**: "4. Combined filter (Image + 2025 + <100MB)" at 0,2 ms

✅ **Cold queries within 2s target** — 100K baseline acceptable.

## Next Steps

1. Review slow queries and add missing composite indexes (T8.2)
2. Evaluate FTS5 full-text search for Title/Description/Keyword searches (T8.3)
3. Profile thumbnail generation and gallery rendering at 100K (T8.4–T8.5)
