# Phase 10 — Sidebar CRUD & Tree Interaction: Context

> Generated: 2026-06-13 via discuss-phase (Codebuff)

## Decisions

| ID | Decision | Value | Rationale |
|----|----------|-------|-----------|
| D10.1 | Context menus via **XAML ContextFlyout** | XAML-defined MenuFlyout on TreeViewItem templates | Cleaner separation than code-behind; templatable and easier to maintain; matches Avalonia best practices |
| D10.2 | **Cascade delete all** children when deleting a parent node | Delete parent + all children recursively | Eliminates orphaned nodes; user confirms via ConfirmationDialog before deletion; simplest and most predictable behavior |
| D10.3 | Permission gating via **existing `CanEditMetadata` flag** | Reuse Phase 7 infrastructure | All CRUD commands share the same permission check; Viewer role cannot create/rename/delete |
| D10.4 | **Inline rename via BeginRename/CommitRename/CancelRename** pattern | Already implemented on CollectionNode, KeywordNode, CategoryNode | Existing pattern works; T10.2 partial implementation just needs context-menu integration |
| D10.5 | **Real-time sync via ChangeNotification broadcast** | After each CRUD operation, broadcast ChangeNotification to all connected clients | Reuses existing Phase 3 notification infrastructure; all clients get instant updates |

## Gray Areas Resolved

1. **Context menu trigger**: Right-click on tree node → ContextRequested event → SearchableTreeView fires NodeContextMenuCommand → SidebarViewModel shows ContextFlyout defined in XAML
2. **Delete cascade for children**: When deleting a parent with children, delete all descendant nodes recursively in a single transaction. Confirmation dialog explains the cascade.
3. **Multi-user delete through broker**: Delete handler receives parent ID, recursively resolves all descendant IDs, deletes all in a single transaction, broadcasts ChangeNotification

## Files Touched (identified)

See `10-PLAN.md` for complete file list with specific method-level changes.

## Deviations from Original Plan

- T10.2 (inline rename) is already partially implemented in the working tree — SearchableTreeView has double-click handler, TextBox in template, and RenameCompletedCommand wiring
- T10.3 (permission gating) is already wired in SidebarViewModel.RefreshPermissions()
- T10.6-10.8 (broker handlers) — some handlers already exist (CreateCollectionHandler, UpdateCollectionHandler, DeleteCollectionHandler, etc.)
- T10.13 (visual filter state) — added as a new task (wasn't in original plan)
