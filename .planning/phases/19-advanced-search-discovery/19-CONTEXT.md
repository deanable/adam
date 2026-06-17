# 19-CONTEXT.md

## Phase: 19 - Advanced Search & Discovery

**Domain:** Extend search with persistent saved searches, smart collections, search history, semantic search, visual similarity

## Carrying Forward

From earlier phases:
- Phase 10 (Sidebar CRUD & Tree Interaction) decisions: XAML ContextFlyout for context menus, cascade delete always, inline rename (F2/double-click), filter commands, visual filter state indicators, keyboard shortcuts (F2 rename, P/X flags, Enter reveal, Ctrl+F search focus). These UI patterns will be applied to saved searches and smart collections where applicable.
- Phase 11 (Full-Text Search) decisions: IFtsService interface with SearchAsync, GetSuggestionsAsync; dedicated search bar with 300ms debounce; Ctrl+F shortcut; search suggestions popup; result highlighting with bold match. These FTS patterns inform the semantic search UI implementation.
- Phase 12 (Performance Optimization) decisions: Decode-to-size via ImageSharp DecoderOptions.TargetSize; thumbnail cache timestamps; bitmap lifecycle with IDisposable; startup profiling Stopwatch telemetry; ConnectionDebugLogger for perf logging. These performance patterns apply to embedding computation and caching.

## Decisions

### Smart Collections Refresh Behavior
**Question:** Should smart collections allow the user to set a custom refresh interval (e.g., every 5 minutes, hourly, daily)?  
**Decision:** No custom refresh interval. Smart collections will refresh only on open and on manual refresh (via context menu or keyboard shortcut).  
**Why:** Keeping the behavior simple avoids complexity, potential performance issues from background refresh loops, and aligns with the "dynamic but not real-time" nature of smart collections described in the plan. Users can manually trigger refresh when needed.

### Semantic Search Filter Combination
**Question:** Should we allow combining semantic search with traditional filters (e.g., find similar images that are also tagged with "landscape")?  
**Decision:** Yes, semantic search can be combined with traditional filters. The SavedSearch entity will store the natural language query in QueryText and traditional filters (tags, date ranges, etc.) in FiltersJson. During search, the semantic similarity score is computed first, then traditional filters are applied as a post-filter to the ranked results.  
**Why:** This provides maximum flexibility for users to refine AI-powered search results with traditional metadata criteria, matching the expectation that advanced search should be both powerful and precise.

### Visual Similarity Embedding Scope
**Question:** Should we compute image embeddings only for images that have been viewed or AI-tagged, to save storage?  
**Decision:** For Phase 19, compute embeddings for all images in the catalog to provide the feature fully and simply.  
**Why:** While the plan notes storage concerns and suggests future opt-in, Phase 19 aims to deliver a complete visual similarity experience. Computing for all images ensures the feature works consistently without requiring user pre-selection. Storage impact will be monitored and addressed in a future phase if needed (e.g., lazy computation on first view or AI-tag).

### Saved Searches UI Reordering
**Question:** Should the saved searches section in the sidebar have a drag-and-drop reordering feature?  
**Decision:** No drag-and-drop reordering for saved searches in the sidebar.  
**Why:** To keep the UI simple and consistent with other sidebar sections (keywords, categories, collections) which do not currently support drag-and-drop reordering. Users can organize saved searches via naming conventions or rely on alphabetical ordering. This decision can be revisited if user feedback indicates strong demand for manual ordering.

## Noted for Later

- **Smart collections:** Automatic periodic refresh (e.g., every hour) as a user-configurable option.
- **Visual similarity:** Lazy embedding computation (on first view or after AI-tagging) to reduce upfront storage cost.
- **Saved searches:** Hierarchical organization via folders or tags for large numbers of saved searches.
- **Search history:** Cross-device synchronization via the broker service (currently per-client only).
- **Semantic search:** Ability to adjust similarity threshold or combine multiple embedding models.
- **Performance:** Background precomputation of embeddings for new/imported assets to avoid UI delay.

## Next Up

After finalizing these decisions, the next steps are:
1. Researcher agents investigate implementation details (ONNX model integration, embedding storage strategies, UI component patterns).
2. Planner agents create executable tasks based on these decisions and research findings.
3. Execute the planned tasks to implement Phase 19 features.

---
*Context generated: 2026-06-17
