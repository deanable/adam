---
goal: Add threaded comment threads on digital assets for multi-user collaboration (COLL-V2-02)
version: 1.0
date_created: 2026-06-14
last_updated: 2026-06-14
status: 'Planned'
tags: [phase-17, comments, collaboration, multi-user, ui]
---

# Phase 17 — Collaboration: Asset Comment Threads

**COLL-V2-02**: Users can add, reply to, and delete comments on individual assets.

## Architecture Summary

```
┌─────────────────────────────────────────────────────┐
│                  CatalogBrowser                       │
│  ┌──────────────────────────────────────────────┐    │
│  │        CommentPanelViewModel                  │    │
│  │  ┌────────────────────────────────────────┐  │    │
│  │  │  Threaded comments, collapse/reply UI   │  │    │
│  │  └────────────────────────────────────────┘  │    │
│  └──────────────────────────────────────────────┘    │
│  ┌──────────────────────────────────────────────┐    │
│  │           CommentService (Shared)              │    │
│  │  ┌──────────────────┐ ┌────────────────────┐ │    │
│  │  │ Standalone: DbCtx │ │ Multi-user: Broker │ │    │
│  │  └──────────────────┘ └────────────────────┘ │    │
│  └──────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────┘
                            │
                    ┌───────┴───────┐
                    │   BrokerService  │
                    │ ┌──────────────┐ │
                    │ │ CommentHandler│ │
                    │ │ Crud + Notify │ │
                    │ └──────────────┘ │
                    └─────────────────┘
```

## Tasks

### T17.1 — Comment Entity Model

**Files:**
- `src/Adam.Shared/Models/Comment.cs` (new)
- `src/Adam.Shared/Data/AppDbContext.cs` (add DbSet + config)

**Comment.cs** entity:
```csharp
public class Comment
{
    public Guid Id { get; set; }
    public Guid AssetId { get; set; }
    public Guid? ParentCommentId { get; set; }   // null = top-level, non-null = reply
    public Guid UserId { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? EditedAt { get; set; }
    public bool IsDeleted { get; set; }           // soft-delete (keep replies intact)
    public int Version { get; set; } = 1;

    // Navigation
    public DigitalAsset Asset { get; set; } = null!;
    public Comment? ParentComment { get; set; }
    public ICollection<Comment> Replies { get; set; } = [];
    public User User { get; set; } = null!;
}
```

**AppDbContext changes:**
- `DbSet<Comment> Comments { get; set; }`
- EF config:
  - Table: `Comments`
  - FK to `DigitalAssets` with cascade delete
  - FK to `Users` with no cascade
  - Self-referencing FK `ParentCommentId → Id` with no cascade
  - Global query filter: `.HasQueryFilter(c => !c.IsDeleted)`
  - Index on `AssetId` for fast lookup
  - Index on `CreatedAt` for chronological ordering

**Estimated LOC:** ~80 (model + EF config)

### T17.2 — CommentService (Shared, Dual-Mode)

**Files:**
- `src/Adam.Shared/Services/CommentService.cs` (new)

Encapsulates all comment CRUD with the same dual-mode pattern as `KeywordService`:

| Method | Signature |
|--------|-----------|
| `ListCommentsAsync(Guid assetId)` | Returns `List<CommentDto>` flattened from tree, sorted by `CreatedAt` |
| `CreateCommentAsync(Guid assetId, Guid? parentId, string body, Guid userId)` | Creates top-level or reply comment |
| `UpdateCommentAsync(Guid commentId, string body, Guid userId)` | Verifies ownership (or Editor+ role), updates body + EditedAt |
| `DeleteCommentAsync(Guid commentId, Guid userId)` | Soft-delete, verifies ownership (or Admin role) |
| `CountCommentsAsync(Guid assetId)` | Returns total (non-deleted) count for badge display |

**CommentDto** (inner class for UI binding):
```csharp
public sealed record CommentDto(
    string Id,
    string AssetId,
    string? ParentCommentId,
    string Body,
    string Username,
    long CreatedAtUnix,
    long? EditedAtUnix,
    bool CanEdit,
    bool CanDelete,
    List<CommentDto> Replies
);
```

**Multi-user path:** Sends protobuf envelopes via `BrokerClient.SendAsync()`.
**Standalone path:** Direct `AppDbContext` access.

**Estimated LOC:** ~200

### T17.3 — Protobuf Wire Messages

**Files:**
- `src/Adam.Shared/Contracts/CommentMessages.cs` (new)
- `src/Adam.Shared/Contracts/MessageTypeCode.cs` (add opcodes)

**New opcodes (120-129):**

| Opcode | Message | Direction | Notes |
|--------|---------|-----------|-------|
| 120 | `ListCommentsRequest` | Client → Broker | Field 1: asset_id (string) |
| 121 | `ListCommentsResponse` | Broker → Client | Repeated `CommentWire` messages |
| 122 | `CreateCommentRequest` | Client → Broker | Fields: asset_id, parent_id (optional), body |
| 123 | `CreateCommentResponse` | Broker → Client | Fields: id, created_at |
| 124 | `UpdateCommentRequest` | Client → Broker | Fields: comment_id, body |
| 125 | `UpdateCommentResponse` | Broker → Client | Fields: id, edited_at |
| 126 | `DeleteCommentRequest` | Client → Broker | Field 1: comment_id |
| 127 | `DeleteCommentResponse` | Broker → Client | (empty) |
| 128 | `CommentNotification` | Broker → Client | Push notification for live updates |

Each follows the `IProtoSerializable` pattern exactly like `AssetMessages.cs`:
- `CalculateSize()`, `WriteTo()`, `MergeFrom()`
- Strings as `string`, optional fields use `isDefault` guards
- Repeated sub-messages handled via `ReadBytes()` / nested `CodedInputStream`

**`CommentWire`** (reusable DTO for wire):
- `id`, `asset_id`, `parent_comment_id` (optional), `body`, `user_name`, `created_at`, `edited_at` (optional), `is_deleted`, `replies` (nested repeated)

**Estimated LOC:** ~250 (7 message types × ~35 LOC each)

### T17.4 — CommentHandler (Broker)

**Files:**
- `src/Adam.BrokerService/Handlers/CommentHandler.cs` (new)

Follows the `AssetHandler` constructor pattern:
```csharp
public sealed class CommentHandler(
    IServiceProvider serviceProvider,
    ILogger<CommentHandler> logger,
    AuthorizationMiddleware authz,
    AuthHandler authHandler,
    ChangeNotificationService notificationService)
```

**Methods:**

| Method | Authz | Behavior |
|--------|-------|----------|
| `ListCommentsAsync` | `asset:read` | Queries `AppDbContext.Comments` → builds tree → returns list |
| `CreateCommentAsync` | `asset:update` | Creates comment, verifies asset exists, broadcasts `CommentNotification` |
| `UpdateCommentAsync` | `asset:update` | Checks ownership (userId matches or role has `user:update`), updates body, broadcasts |
| `DeleteCommentAsync` | `asset:update` | Soft-deletes, checks ownership, broadcasts |

All follow `Deserialize → do work → return Envelope` pattern from `AssetHandler`.

**Registration:** Add `CommentHandler` DI registration in `Program.cs` + switch-case in `ConnectionHandler.HandleAsync`.

**Estimated LOC:** ~180

### T17.5 — CommentPanelViewModel (Client)

**Files:**
- `src/Adam.CatalogBrowser/ViewModels/CommentPanelViewModel.cs` (new)

**State:**
```csharp
ObservableCollection<CommentThread> Threads  // top-level comments + expanded replies
int TotalCommentCount                          // badge number
bool IsLoading
bool IsExpanded                               // collapsible panel
string NewCommentText
string? ReplyTargetId                         // which comment is being replied to
```

**Commands:**
- `AddCommentCommand` — posts new top-level comment
- `ReplyToCommand(commentId)` — sets ReplyTargetId
- `EditCommentCommand(commentId)` — enters edit mode (inline)
- `DeleteCommentCommand(commentId)` — confirm then delete
- `CancelReplyCommand` — clears ReplyTargetId
- `RefreshCommand` — reloads from service

**Dependencies:**
- `CommentService` (injected, singleton from DI)
- `ModeManager` (for auth token, standalone flag)
- `IUiDispatcher` (standard pattern for Avalonia)

**Expands/collapses:** Top-level comments shown; replies expanded on click.

**Live updates:** If `BrokerClient.NotificationReceived` fires a `CommentNotification`, reload threads for the current asset.

**Estimated LOC:** ~250

### T17.6 — CommentPanel UI (Avalonia)

**Files:**
- `src/Adam.CatalogBrowser/Views/CommentPanelView.axaml` (new)
- `src/Adam.CatalogBrowser/Views/CommentPanelView.axaml.cs` (new code-behind)

**Layout (vertical stack):**

```
┌─ CommentPanel ─────────────────────────────────┐
│  Comments (3)                          [↻]     │
├────────────────────────────────────────────────┤
│  ┌────────────────────────────────────────┐    │
│  │ [Avatar] Alice  2 min ago     [✏️][🗑️]   │    │
│  │ This is a great photo! The lighting...  │    │
│  │ └─ [↩ Reply]  [▶ 2 replies]            │    │
│  └────────────────────────────────────────┘    │
│  ┌────────────────────────────────────────┐    │
│  │ [Avatar] Bob  5 min ago       [✏️][🗑️]   │    │
│  │ I agree with Alice!                     │    │
│  │ └─ [↩ Reply]                           │    │
│  └────────────────────────────────────────┘    │
├────────────────────────────────────────────────┤
│  ┌────────────────────────────────────────┐    │
│  │ Write a comment...              [Send] │    │
│  └────────────────────────────────────────┘    │
└────────────────────────────────────────────────┘
```

**Details:**
- `ScrollViewer` wrapping the comment threads
- Each comment: `Border` + `TextBlock` for body + buttons for edit/delete
- Reply input appears inline below the parent comment (not a modal)
- Edit replaces body text with a `TextBox` + Save/Cancel
- "Reply" count shown as `▶ N replies` — clicking expands children
- Empty state: "No comments yet. Be the first to comment!"
- Time formatting: relative ("2 min ago", "5 hours ago", "3 days ago"), then absolute

**Binding to MainWindow:**
- Add as a new tab/panel in the right-side area, toggled per asset selection
- Comment count badge shown in the tab header

**Estimated LOC:** ~200 (XAML) + ~30 (code-behind)

### T17.7 — Standalone Mode & Integration

**Files:**
- `src/Adam.CatalogBrowser/ViewModels/MainWindowViewModel.cs` (wire panel)
- `src/Adam.CatalogBrowser/App.axaml.cs` (register DI services)
- `src/Adam.CatalogBrowser/Services/ServiceCollectionExtensions.cs` (or inline DI)

**Wiring:**
1. Register `CommentService` as singleton in DI
2. Register `CommentPanelViewModel` as transient in DI
3. Add `CommentPanelView` to the right-panel area in `MainWindow.axaml`
4. Wire `MainWindowViewModel.SelectedAsset` change to reload comments
5. Show comment count badge in the tab header
6. Permission gate: show only if `asset:update` (can comment) or at minimum `asset:read`

**Standalone path:**
- `CommentService` uses `ModeManager.CreateDbContextAsync()` directly
- No broker needed — same pattern as other standalone services

**Estimated LOC:** ~60

## Success Criteria

- ✅ Top-level comments can be created on any asset
- ✅ Replies can be nested under any top-level comment
- ✅ Comments can be edited by the author (within 24h — soft enforcement)
- ✅ Comments can be soft-deleted by author or admin
- ✅ Threaded view shows replies collapsed by default with count badge
- ✅ Comments survive app restart (persisted in SQLite/PostgreSQL/SQL Server)
- ✅ Multi-user: comments update in real-time via broker push notification
- ✅ Standalone: comments work via direct DB access (no broker needed)
- ✅ Comment count shown as badge in the asset detail panel header
- ✅ 20+ new tests pass across all three DB providers

## Test Plan

| Test File | Tests | Coverage |
|-----------|-------|----------|
| `tests/Adam.Shared.Tests/Services/CommentServiceTests.cs` | 10 | Create, list (tree), edit, delete, reply, count, ownership guard, standalone path |
| `tests/Adam.BrokerService.Tests/Handlers/CommentHandlerTests.cs` | 6 | Each CRUD method + authz guard + notification broadcast |
| `tests/Adam.CatalogBrowser.Tests/ViewModels/CommentPanelViewModelTests.cs` | 8 | Add/reply/edit/delete, loading state, unread updates, empty state |

**Target:** 24 new tests → 1,085 total

## Execution Order (Waves)

```
Wave 1 ─── T17.1 Model + T17.3 Wire messages ────── independent, parallel
Wave 2 ─── T17.2 CommentService (depends on T17.1, T17.3)
Wave 3 ─── T17.4 CommentHandler  (depends on T17.2, T17.3)
Wave 4 ─── T17.5 ViewModel       (depends on T17.2)
Wave 5 ─── T17.6 UI + T17.7 Integration ─────────── depends on T17.5
Wave 6 ─── Tests (all 24 new) ────────────────────── after all code
Wave 7 ─── Full test suite, UAT document, plan status update
```

## Files Created

| # | File | Task |
|---|------|------|
| 1 | `src/Adam.Shared/Models/Comment.cs` | T17.1 |
| 2 | `src/Adam.Shared/Contracts/CommentMessages.cs` | T17.3 |
| 3 | `src/Adam.Shared/Services/CommentService.cs` | T17.2 |
| 4 | `src/Adam.BrokerService/Handlers/CommentHandler.cs` | T17.4 |
| 5 | `src/Adam.CatalogBrowser/ViewModels/CommentPanelViewModel.cs` | T17.5 |
| 6 | `src/Adam.CatalogBrowser/Views/CommentPanelView.axaml` | T17.6 |
| 7 | `src/Adam.CatalogBrowser/Views/CommentPanelView.axaml.cs` | T17.6 |

## Files Modified

| # | File | Change |
|---|------|--------|
| 1 | `src/Adam.Shared/Data/AppDbContext.cs` | Add `DbSet<Comment>`, EF config, index |
| 2 | `src/Adam.Shared/Contracts/MessageTypeCode.cs` | Add opcodes 120-128 |
| 3 | `src/Adam.BrokerService/Program.cs` | Register `CommentHandler` in DI |
| 4 | `src/Adam.BrokerService/Handlers/ConnectionHandler.cs` | Add switch cases for 120-127 |
| 5 | `src/Adam.CatalogBrowser/App.axaml.cs` | Register `CommentService`, `CommentPanelViewModel` |
| 6 | `src/Adam.CatalogBrowser/ViewModels/MainWindowViewModel.cs` | Wire comment reload on asset selection |
| 7 | `src/Adam.CatalogBrowser/Views/MainWindow.axaml` | Add CommentPanelView to right panel |
| 8 | `tests/Adam.Shared.Tests/Services/CommentServiceTests.cs` | 10 new tests |
| 9 | `tests/Adam.BrokerService.Tests/Handlers/CommentHandlerTests.cs` | 6 new tests |
| 10 | `tests/Adam.CatalogBrowser.Tests/ViewModels/CommentPanelViewModelTests.cs` | 8 new tests |

## Key Decisions

1. **Soft-delete with parent retention**: Deleting a comment sets `IsDeleted = true` but keeps the body visible as "[deleted]" to preserve reply thread context. Replies remain visible.
2. **Ownership model**: Author can edit/delete own comments. Admin/Editor role can delete any comment. No admin edit — that would distort the conversation.
3. **Self-referencing tree**: `ParentCommentId` pointing to the same table. Max nesting depth of 1 (replies to top-level only — no nested replies). This matches GitHub's threading model and avoids recursive query complexity.
4. **No real-time edit conflict detection**: Comments are not versioned for concurrent editing (unlike asset metadata). Last-writer-wins for edits is acceptable given the social nature of comments.
5. **Push notifications via CommentNotification** (opcode 128): A new notification type distinct from `ChangeNotification` (opcode 110). Separate type keeps the payload small and avoids breaking existing change tracking.
6. **No Markdown rendering**: v1 uses plain text bodies. Markdown support can be added as a future enhancement.
7. **No email/push notifications**: v1 scoped to in-app only. External notifications deferred to future phases.
