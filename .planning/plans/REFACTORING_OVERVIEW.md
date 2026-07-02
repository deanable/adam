---
goal: Master overview document linking the modularity audit findings to all 6 refactoring plans.
version: 1.0
date_created: 2026-07-02
last_updated: 2026-07-02
status: 'Draft'
tags: [refactoring, modularity, overview, architecture]
---

# Modularity Audit & Refactoring Roadmap

## 1. Executive Summary

The codebase has **excellent project-level modularity** (clean DAG, no circular dependencies) but **significant ViewModel-layer megolith problems**. 4 files exceed 1,000 lines in the CatalogBrowser UI layer alone. The refactoring roadmap consists of **6 plans** that work from the inside out — starting with the largest files and addressing the most impactful structural issues first.

### Modularity Scorecard

| Layer | Grade | Key Issue |
|-------|-------|-----------|
| **Project boundaries** | ✅ **A** | Clean DAG, no cycles, each project has a clear identity |
| **Adam.Shared scope** | ⚠️ **B−** | 143 files, 54 services in one project — becoming a general dump |
| **BrokerService handlers** | ✅ **B+** | AssetHandler (859 lines) is large but justified; SidebarHandler should be split |
| **CatalogBrowser ViewModels** | 🔴 **D** | **4 files over 1,000 lines** — SidebarVM (2573), MainWindowVM (2040), AssetGalleryVM (1568), PropertyInspectorVM (1037) |
| **ServiceManager ViewModel** | ⚠️ **B−** | ServiceManagerViewModel (930 lines) — moderately large |
| **Shared Services** | ✅ **B+** | Most well-sized (50-300 lines), a few 400+ liners |
| **Node type duplication** | 🔴 **C−** | 7 nearly identical tree node types with INotifyPropertyChanged boilerplate |
| **Layer violations** | ⚠️ **C+** | ProtoHelper.Serialize/Deserialize called in ViewModels — should be in services |
| **Tree algorithm duplication** | 🔴 **C−** | BuildTree + PropagateCounts pattern repeated 5x with different types |

### File Sizes Heat Map

```
2,573  SidebarViewModel.cs          ████████████████████████████████████████  🔴 MEGALITH
2,040  MainWindowViewModel.cs       ██████████████████████████████████       🔴 MEGALITH
1,568  AssetGalleryViewModel.cs     █████████████████████████                🔴 MEGALITH
1,037  PropertyInspectorViewModel   █████████████████                        🔴 MEGALITH
  930  ServiceManagerViewModel.cs   ███████████████                          ⚠️ LARGE
  859  AssetHandler.cs              ██████████████                           ⚠️ LARGE
  684  MetadataEditorViewModel.cs   ███████████                              ⚠️ Large
  531  SidebarHandler.cs            ████████                                 ⚠️ Large
  528  CsvMetadataService.cs        ████████                                 ⚠️ Large
  523  EmbeddingService.cs          ████████                                 ⚠️ Large
  514  MetadataWritebackService.cs  ████████                                 ⚠️ Large
```

## 2. Refactoring Roadmap

The 6 plans are ordered by **impact and dependency** — earlier plans are smaller, safer, and unblock later ones:

```
Phase A: MainWindowVM Extractions (Plans 01-03)
  ┌─────────────────────┐
  │ 01 NavigationService│ ← Independent, low risk, ~80 lines removed
  ├─────────────────────┤
  │ 02 BulkOpService    │ ← Independent, low risk, ~200 lines removed
  ├─────────────────────┤
  │ 03 PanelStateService│ ← Independent, medium risk (XAML changes), ~150 lines removed
  └─────────┬───────────┘
            ▼
Phase B: Method-Level Decomposition (Plan 04)
  ┌─────────────────────┐
  │ 04 LoadPageAsync    │ ← Independent, low risk (pure code motion), ~275 lines improved
  └─────────┬───────────┘
            ▼
Phase C: Structural Decomposition (Plans 05-06)
  ┌─────────────────────┐
  │ 06 TreeNode Base    │ ← Prerequisite for 05, reduces boilerplate risk
  ├─────────────────────┤
  │ 05 Sidebar Decomp   │ ← Largest refactoring, saves ~1,873 lines
  └─────────────────────┘
```

### Phase A: Low Hanging Fruit (Plans 01-03)

These 3 plans extract cleanly separable concerns from MainWindowViewModel. None of them change behavior or XAML structure (except PanelStateService needs minor binding path updates).

| Plan | Lines Removed from MWVM | Risk | Test Changes |
|------|------------------------|------|--------------|
| 01 NavigationService | ~80 | Low | None |
| 02 BulkAssetOperationService | ~200 | Low | None |
| 03 PanelStateService | ~150 | Low-Medium | None |

**Cumulative impact:** MainWindowViewModel reduces from 2,040 to ~1,610 lines.

### Phase B: Method-Level Decomposition (Plan 04)

PLAN 04 tackles the 300-line `LoadPageAsync` method. It's pure code extraction — no behavior changes.

| Plan | Lines Improved | Risk | Test Changes |
|------|---------------|------|--------------|
| 04 LoadPageAsync Decomposition | ~275 | Very Low | None |

**Cumulative impact:** AssetGalleryViewModel reduces from 1,568 to ~1,400 lines. `LoadPageAsync` goes from 300 to ~25 lines.

### Phase C: Structural Redesign (Plans 05-06)

These are the largest changes. PLAN 06 (tree node base class) is a prerequisite for PLAN 05 (sidebar decomposition) because it makes the tree services cleaner.

| Plan | Lines Removed | Risk | Test Changes |
|------|--------------|------|--------------|
| 06 Tree Node Base Class | ~400 | Medium | None |
| 05 Sidebar Decomposition | ~1,873 | High | Existing tests should pass |

**Cumulative impact after all 6 plans:**

| File | Before | After | Change |
|------|--------|-------|--------|
| SidebarViewModel | 2,573 | ~600 | **-1,973** |
| MainWindowViewModel | 2,040 | ~1,000 | **-1,040** |
| AssetGalleryViewModel | 1,568 | ~1,250 | **-318** |
| **Total** | **6,181** | **~2,850** | **-3,331 (54% reduction)** |

Plus: 9 new files (NavigationService + BulkOpService + PanelStateService + 7 tree services) and 10+ smaller node type files.

## 3. Deeper Structural Issues (Post-Refactoring)

After all 6 plans are complete, these longer-term issues remain:

### Issue A: Adam.Shared Scope Creep

**Current state:** 143 files covering AI inference, platform installers, metadata extraction, thumbnail pipelines, contracts, transport, validation, and more — all in one assembly.

**Future options:**
- Split platform-specific installers into `Adam.Platform.Windows`, `Adam.Platform.Linux`, `Adam.Platform.Mac`
- Extract thumbnail infrastructure into `Adam.Thumbnails` (already has dedicated `ThumbnailExtractors/` directory as a seed)
- Keep domain models + contracts + EF Core config in Shared (the core purpose)

**Priority:** Low-Medium. The code works fine, but as the codebase grows, compile times and dependency weight will increase.

### Issue B: ProtoHelper in ViewModels (Layer Violation)

**Current state:** `ProtoHelper.Serialize()` and `ProtoHelper.Deserialize()` are called directly in:
- `SidebarViewModel` — via `SendBrokerRequestAsync`
- `MainWindowViewModel` — `SaveCurrentSearchAsync`
- `AssetGalleryViewModel` — `ExecuteSemanticSearchAsync`, `LoadPageAsync`

**Why it matters:** ViewModels should not know about protobuf serialization. If the transport protocol changes (e.g., to gRPC or JSON), every ViewModel that calls ProtoHelper needs modification.

**Future fix:** Wrap broker communication in a typed client:

```csharp
// Instead of:
var envelope = new Envelope { ..., Payload = ByteString.CopyFrom(ProtoHelper.Serialize(req)) };
var response = await broker.SendAsync(envelope);
var data = ProtoHelper.Deserialize<XxxResponse>(response.Payload.ToByteArray());

// Use:
var data = await _brokerApi.ListAssetsAsync(listReq);
```

**Priority:** Low. The current pattern works, and the ViewModels already use `ModeManager` as an abstraction layer. The `SendBrokerRequestAsync` helper in SidebarViewModel is a step in the right direction.

### Issue C: Dual-Mode Duplication

**Current state:** Every data-access method has `if (standalone) { ... } else if (multiUser) { ... }` branching. This creates:
- 2x the code paths to test
- Risk of drift between the two modes
- Cognitive overhead when reading any method

**Future fix:** A `Repository<T>` pattern that abstracts the data source:

```csharp
public interface IAssetRepository
{
    Task<List<DigitalAsset>> ListAsync(GalleryFilter filter, PaginationToken token);
    Task UpdateAsync(DigitalAsset asset);
    // etc.
}

public sealed class DirectDbAssetRepository : IAssetRepository { ... }
public sealed class BrokerAssetRepository : IAssetRepository { ... }
```

**Priority:** Medium. This would be a significant refactoring (essentially rewriting the data access layer). The current pattern is working but verbose.

### Issue D: Node Type Duplication (Pre-refactoring)

**Current state:** 7 near-identical node types with INotifyPropertyChanged boilerplate in SidebarViewModel.

**Fix:** PLAN 06 directly addresses this with a `SidableTreeNode<T>` base class. This is a prerequisite for PLAN 05.

## 4. Dependency Graph of Plans

```
Plan 01 (NavigationService)     ← independent
Plan 02 (BulkOpService)         ← independent
Plan 03 (PanelStateService)     ← independent
Plan 04 (LoadPageAsync)         ← independent
Plan 06 (Tree Node Base)        ← standalone, but unblocks Plan 05
    └─ Plan 05 (Sidebar Decomp) ← depends on Plan 06 (reduces risk)
```

Plans 01-04 can be executed in **any order** and in **parallel**. Plan 06 should precede Plan 05.

## 5. Quick Commands

```bash
# Run all tests to establish baseline
dotnet test

# Run specific test suites after each plan
dotnet test tests/Adam.CatalogBrowser.Tests
dotnet test tests/Adam.Shared.Tests
dotnet test tests/Adam.BrokerService.Tests
dotnet test tests/Adam.ServiceManager.Tests
```
