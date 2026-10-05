# Testing

**As-of:** 2026-10-05 (branch `main` @ `cab6a78`)
**SDK:** .NET `10.0.401`
**Re-verify:** `dotnet build Adam.slnx && dotnet test Adam.slnx`

> This file was regenerated. It previously claimed "~10 tests" and "No UI tests detected" — both
> were wrong by two orders of magnitude. Real counts are below.

## Test Projects

| Project | Files | Measured result | Notes |
|---------|-------|-----------------|-------|
| `Adam.Shared.Tests` | 42 | ✅ 412 passed, 0 failed | Core services, metadata round-trip, FTS, AI tagging, DateTimeOffset ORDER BY |
| `Adam.ServiceManager.Tests` | 13 | ✅ 156 passed, 0 failed | Admin panel, user management, audit log |
| `Adam.BrokerService.Tests` | 17 | ✅ 176 passed, 2 skipped | Handlers, auth, TLS, integration; 2 skipped without Docker |
| `Adam.CatalogBrowser.Tests` | 36 | ❌ **aborts** | 1 deterministic failure + 1 hang that crashes the test host |

**Headless UI tests ARE integrated** (`Avalonia.Headless`) — the previous claim that they are not
was incorrect.

## Known defects (see `.planning/codebase/CONCERNS.md` §1)

1. `DropCommandHandlersTests.AssignCategoryDropCommand_WithCategoryNode_EnqueuesCategoryOperation`
   fails with `SQLite Error 5: 'database is locked'` (reproducible in isolation).
2. `MetadataEditorPanelPersistenceTests.RestoreMetadataPanelStates_RestoresSavedPanels` hangs in
   full-project runs, crashing the host and skipping every test after it.

## The `IUiDispatcher` rule (important for new tests)

ViewModels take an optional `IUiDispatcher? dispatcher = null` that defaults to
`AvaloniaUiDispatcher`. In a headless test host **no dispatcher loop is pumping**, so any test that
constructs such a ViewModel *without* injecting a synchronous dispatcher can hang forever the first
time it touches a `RunOnUiThreadAsync` path.

Always inject a synchronous test dispatcher:

```csharp
private sealed class SyncUiDispatcher : IUiDispatcher
{
    public Task InvokeAsync(Action action) { action(); return Task.CompletedTask; }
    public void Post(Action action) => action();
    public bool CheckAccess() => true;
}

var vm = new MetadataEditorViewModel(modeManager, new SyncUiDispatcher(), writeback: new MetadataWritebackService());
```

Existing examples: `CommentPanelViewModelTests`, `DropCommandHandlersTests`,
`MainWindowPanelPersistenceTests`, `MetadataEditorWritebackTests`.

## Per-test database isolation

Each test gets a unique SQLite database under a temp path, and must clean up the connection pool:

```csharp
_basePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
_modeManager = new ModeManager(_basePath);
await _modeManager.InitializeAsync();      // creates {basePath}/.adam/catalog.db

// Dispose:
SqliteConnection.ClearAllPools();          // required, otherwise the file stays locked
Directory.Delete(_basePath, recursive: true);
```

`ModeManager.CreateDbContextAsync()` opens the connection eagerly and returns a context the caller
must dispose (`await using`). Leaving one open holds a SQLite lock and is the likely source of the
`database is locked` failures.

## Test parallelization

`Adam.CatalogBrowser.Tests/xunit.runner.json` disables both `parallelizeTestCollections` and
`parallelizeAssembly`, with `longRunningTestSeconds: 60`. The other test projects have no runner
config and use xUnit defaults.

## Coverage gaps

- [ ] Metadata Editor save-path regression coverage (only partially closed —
      `MetadataEditorWritebackTests` covers the RAW/sidecar write-back path).
- [ ] Performance benchmarks (search at 100K assets, loupe render, folder scan).
- [ ] External file move/rename handling in `FolderWatcherService`.
- [ ] `DbMigrationService` conflict detection.
