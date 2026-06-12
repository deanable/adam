---
goal: Sidebar tree CRUD, inline rename, folder context menus, and broker-side handlers for multi-user tree operations.
version: 2.0
date_created: 2026-06-12
last_updated: 2026-06-12
status: 'Planned'
tags: [sidebar, crud, tree, ui, broker]
---

# Phase 10: Sidebar CRUD & Tree Interaction

![Status: Planned](https://img.shields.io/badge/status-Planned-blue)

Complete the sidebar tree interaction model — create, rename, and delete operations for Collections, Keywords, and Categories via context menus with inline rename support. Add folder context menus and broker-side handlers for multi-user mode.

**Depends on:** Phases 1–9 (v1.0 codebase)

---

## 1. Requirements & Constraints

- **UI-V2-01**: Users can create, rename, and delete Collections, Keywords, and Categories from the sidebar tree via context menus
- **UI-V2-02**: Inline rename is supported — double-click or F2 on a tree node enters edit mode
- **UI-V2-03**: Folder nodes have a context menu with "Reveal in Explorer" and "Re-scan Folder" options
- **UI-V2-04**: All tree nodes show "Filter by this" / "Clear filter" entries (replacing implicit-on-selection)
- **UI-V2-05**: CRUD operations are permission-gated — only Editor/Admin roles can create/rename/delete
- **UI-V2-06**: Multi-user mode: CRUD operations go through broker protocol handlers
- **UI-V2-07**: Parent-child relationships are preserved during create (new item appears under selected parent)
- **UI-V2-08**: Deleting a parent node prompts to confirm and optionally re-parent children to root

---

## 2. Implementation Steps

### Work Stream 1: Client-Side CRUD Commands

**GOAL:** Wire the existing SidebarViewModel CRUD commands (already implemented in v1) to proper context menus with inline rename support.

| Task | Description | Status |
|------|-------------|--------|
| T10.1 | **Context menu wiring** — Connect `ShowKeywordMenuCommand` and `ShowCategoryMenuCommand` to actually show `MenuFlyout` with Create/Rename/Delete items. Add `ShowCollectionMenuCommand` and `ShowFolderMenuCommand`. Wire to `SearchableTreeView.NodeContextMenuCommand`. | ⬜ |
| T10.2 | **Inline rename** — Add `IsEditing` property to tree node models (`KeywordNode`, `CategoryNode`, `CollectionNode`). Double-click or F2 sets `IsEditing=true`, TreeViewItem template switches to TextBox. Enter/Escape commits/cancels. | ⬜ |
| T10.3 | **Permission gating** — CRUD commands check `CanEditMetadata` before executing. Disabled state shown in context menu items with tooltip explaining why. | ⬜ |
| T10.4 | **Delete with re-parent** — When deleting a parent node that has children, prompt: "Delete this node and all children?" or "Move children to root?" with confirmation dialog. | ⬜ |
| T10.5 | **Folder context menu** — Add context menu to `FolderNode` with "Reveal in Explorer" (uses existing `RevealInFolderCommand`) and "Re-scan Folder" (triggers ingest for the selected folder path). | ⬜ |

### Work Stream 2: Broker-Side Handlers

**GOAL:** Implement server-side handlers for multi-user CRUD operations on tree nodes.

| Task | Description | Status |
|------|-------------|--------|
| T10.6 | **Collection CRUD handlers** — `CreateCollectionHandler`, `UpdateCollectionHandler`, `DeleteCollectionHandler` in BrokerService. Create/Update require `collection:create`/`collection:update` permissions. Delete requires `collection:*`. | ⬜ |
| T10.7 | **Keyword CRUD handlers** — `CreateKeywordHandler`, `UpdateKeywordHandler`, `DeleteKeywordHandler`. Handles parent-child relationships. Permission: `collection:*` (keywords are part of the taxonomy). | ⬜ |
| T10.8 | **Category CRUD handlers** — `CreateCategoryHandler`, `UpdateCategoryHandler`, `DeleteCategoryHandler`. Same permission model as keywords. | ⬜ |
| T10.9 | **Protobuf contracts** — Define `CreateKeywordRequest/Response`, `UpdateKeywordRequest/Response`, `DeleteKeywordRequest/Response` (and Category equivalents) in `Adam.Shared/Contracts/`. Wire to `MessageTypeCode` enum. | ⬜ |
| T10.10 | **Change notification** — After CRUD operations, broadcast `ChangeNotification` to all connected clients so sidebar trees refresh in real-time across sessions. | ⬜ |

### Work Stream 3: Filter Integration

**GOAL:** Replace implicit-on-selection filtering with explicit context menu entries.

| Task | Description | Status |
|------|-------------|--------|
| T10.11 | **"Filter by this" entry** — Add to all tree node context menus (Keywords, Categories, Folders, Collections). Sets the active filter and reloads gallery. | ⬜ |
| T10.12 | **"Clear filter" entry** — Added alongside "Filter by this". Clears the active filter for that tree section and reloads gallery. | ⬜ |
| T10.13 | **Visual filter state** — Tree nodes that are actively filtering show a visual indicator (bold text, icon badge, or highlight color). | ⬜ |

---

## 3. Execution Waves

| Wave | Tasks | Depends On | Rationale |
|------|-------|------------|-----------|
| **Wave 1 — Client CRUD** | T10.1, T10.2, T10.3, T10.4, T10.5 | — | Standalone-mode CRUD with UI. All client-side, no broker dependency. |
| **Wave 2 — Broker Handlers** | T10.6, T10.7, T10.8, T10.9, T10.10 | Wave 1 | Server-side handlers for multi-user mode. Contracts must be defined first (T10.9). |
| **Wave 3 — Filter UX** | T10.11, T10.12, T10.13 | Wave 1 | Filter integration. Independent of broker handlers. |

---

## 4. Files

| File | Role |
|------|------|
| `src/Adam.CatalogBrowser/ViewModels/SidebarViewModel.cs` | Existing CRUD commands — wire context menus, add inline rename state |
| `src/Adam.CatalogBrowser/Controls/SearchableTreeView.axaml` | Add inline rename TextBox template, context menu template |
| `src/Adam.CatalogBrowser/Controls/SearchableTreeView.axaml.cs` | Wire double-click → inline rename, F2 handling |
| `src/Adam.Shared/Contracts/` | New protobuf request/response types for CRUD operations |
| `src/Adam.BrokerService/Handlers/` | New handler classes for keyword/category/collection CRUD |
| `src/Adam.Shared/Services/KeywordService.cs` | Extend with rename/reparent methods |
| `src/Adam.Shared/Services/CategoryService.cs` | Extend with rename/reparent methods |

---

## 5. Testing

| Test | Type | Command |
|------|------|---------|
| Unit tests for CRUD operations | Automated | `dotnet test tests/Adam.CatalogBrowser.Tests` |
| Unit tests for broker handlers | Automated | `dotnet test tests/Adam.BrokerService.Tests` |
| Inline rename smoke test | Manual | Double-click tree node, verify edit mode, Enter to commit, Escape to cancel |
| Multi-user CRUD test | Manual | Two clients connected, create/rename/delete from one, verify sync on other |
| Permission gating test | Manual | Login as Viewer, verify CRUD context menus are disabled |

---

## 6. Risks

- **RISK-001**: Inline rename in `SearchableTreeView` may conflict with the existing search textbox. Mitigation: use a distinct visual style (e.g., blue border) and ensure focus management prevents collision.
- **RISK-002**: Broker-side delete of parent nodes with children requires careful cascade handling. Mitigation: always prompt and offer re-parent option.
- **RISK-003**: Real-time sync of tree changes across clients may cause flicker. Mitigation: diff-based updates rather than full reload where possible.
