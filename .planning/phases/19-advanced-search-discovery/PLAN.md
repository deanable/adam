# Phase 19 - Advanced Search & Discovery

## Overview
Implement advanced search features including saved searches, smart collections, search history, semantic search, and visual similarity.

## Features
- T19.1: Saved Searches - save/load named filter+query combinations
- T19.2: Smart Collections - dynamic collections from saved search criteria
- T19.3: Search History - persistent multi-user search history with auto-purge
- T19.4: Semantic Search - natural language search via ONNX text embedding
- T19.5: Visual Similarity - Find Similar via vision encoder embeddings

## Key Decisions (from CONTEXT.md)
- Smart Collections: Refresh only on open/manual refresh (no custom intervals)
- Semantic Search: Can combine with traditional filters
- Visual Similarity: Compute embeddings for all images in Phase 19
- Saved Searches UI: No drag-and-drop reordering (simple alphabetical)

## Next Steps
1. Research phase (if needed)
2. Create implementation tasks based on this plan
3. Execute tasks to implement features
4. Verify with tests
5. Move to Phase 20 (UX Modernization)

---
*Generated: 2026-06-17
