# Concerns

**As-of:** 2026-10-05 (audited against branch `main` @ `cab6a78`)
**Verified by:** `dotnet build Adam.slnx` + per-project `dotnet test` + source inspection.
**How to re-verify:** see `.planning/STATE-AUDIT.md` §6 for the exact commands.

> This file was regenerated. Several previously-listed items were resolved in Phases 13–24 but
> were never removed, which made this document actively misleading. Resolved items are now
> listed under §5 with their fix, not silently deleted.

---

## 1. Critical

### Test suite cannot complete — `Adam.CatalogBrowser.Tests` aborts
- **Symptom:** `dotnet test tests/Adam.CatalogBrowser.Tests` reports 1 failure and then the test
  host crashes; later test classes never run.
- **Two distinct defects:**
  1. `DropCommandHandlersTests.AssignCategoryDropCommand_WithCategoryNode_EnqueuesCategoryOperation`
     fails with `Microsoft.Data.Sqlite.SqliteException : SQLite Error 5: 'database is locked'`.
     Reproduces deterministically (17 pass / 1 fail) even when the class runs alone.
  2. `MetadataEditorPanelPersistenceTests.RestoreMetadataPanelStates_RestoresSavedPanels`
     hangs indefinitely in full-project runs.
- **Confirmed cause of hangs:** ViewModels constructed with the *default* `AvaloniaUiDispatcher`
  **can block forever** when they exercise a `RunOnUiThreadAsync` path, because no dispatcher loop
  is pumping in the headless test host. Verified directly: a new test class hung with the default
  dispatcher and ran in ~1s once given a `SyncUiDispatcher`. `MetadataEditorPanelPersistenceTests`
  constructs `new MetadataEditorViewModel(_modeManager, prefs: prefs)` **without** a dispatcher and
  is therefore exposed; it passes in isolation but hangs in full-project runs, so test ordering may
  contribute as well.
- **Impact:** the "N tests passing, 0 failing" claims in `STATE.md` / `PROJECT.md` / `AGENTS.md`
  are not reproducible.
- **Action:** inject a `SyncUiDispatcher` in the affected tests; fix the SQLite lock in
  `DropCommandHandlersTests` (serialize DB access, or unique DB + `ClearAllPools()` per test).
  See `.planning/STATE-AUDIT.md` §4.1.

### JWT signing key must never be committed — **partially addressed**
- `src/Adam.BrokerService/appsettings.json` previously contained a live Base64 `Jwt:SigningKey`.
  The repo is public, so anyone could forge tokens for any user/role.
- **Fixed 2026-10-05:** the committed value was replaced with the `${ADAM_JWT_KEY}` placeholder.
  `AuthHandler` treats that placeholder as "not configured" and falls back to the `ADAM_JWT_KEY`
  environment variable, failing fast if neither is present. Covered by
  `AuthHandlerSigningKeyTests` (5 tests).
- **Still outstanding:** the old key is still present in git history. Rotate/invalidate it and
  consider a history rewrite or documented revocation.

---

## 2. High

### Metadata write-back dropped copyright edits — **fixed**
- `MetadataEditorViewModel.SaveAsync` wrote `Copyright` to `MetadataProfile` only, while
  `MetadataWritebackService.BuildXmpPacket(DigitalAsset)` builds the source-file XMP from
  `DigitalAsset.Copyright`. Result: copyright edits were saved to the catalog but never written to
  the file's XMP — a silent break of the metadata round-trip. `PropertyInspectorViewModel` did it
  correctly, so the two editors disagreed.
- **Fixed 2026-10-05:** `SaveAsync` now mirrors `Copyright` onto `DigitalAsset`. Covered by
  `MetadataEditorWritebackTests` (3 tests).

### Known-vulnerable dependency
- `dotnet build` raises `NU1903`: `System.Security.Cryptography.Xml` 10.0.9 has a known **high**
  severity vulnerability (GHSA-mmjf-rqrv-855v), pulled in via `Adam.ServiceManager`.
- **Action:** bump to a patched version or remove the dependency if unused directly.

### 184 build warnings, concentrated in security-sensitive handlers
- The majority are `CS8602` (possible null dereference) in `Adam.BrokerService/Handlers/*`:
  `AssetHandler`, `SidebarHandler`, `UserHandler`, `CollectionHandler`, `SavedSearchHandler`,
  `SearchRankingHandler`, `SemanticSearchHandler`.
- The 2026-10-05 refactor added warnings of its own, one of which looks like a real defect:
  `SidebarViewModel.cs(35,42): CS0649: Field '_categoryTreeService' is never assigned to, and will
  always have its default value null`, plus `TrashViewModel.CloseRequested` never used.
- **Action:** nullability sweep, then enable warnings-as-errors for `Adam.BrokerService`.

### `Adam.ServiceManager.Tests` hangs — elevated-helper recursion
- Reproducible: the run stops at 139/156 and the host aborts.
- **Mechanism:** the elevated-helper flow resolves its executable to the *running process*, which in
  tests is `testhost.exe`. It therefore re-launches a **test host** as the "helper", which then
  reaches `installer.InstallAsync()` and attempts a real `sc.exe install` — requiring administrator
  rights that a test run does not have, so it blocks. Observed log:
  `=== ELEVATED HELPER STARTED === ... Process path: ...\testhost.exe` → `Starting 'install' operation...`
- Earlier in the same session this project passed 156/156 in 17s, so the trigger is
  environment/ordering dependent. Investigate `tests/Adam.ServiceManager.Tests/Services/ElevatedHelperTests.cs`.
- **Action:** make the helper refuse to run inside a test host, or inject a no-op installer in tests.

### Flaky test: `NearDuplicateServiceTests.ScanAllAsync_ReportsProgress`
- `Adam.Shared.Tests` — fails intermittently with `Expected last.completed to be 3, but found 2`.
  Measured 2 failures in 3 isolated runs. A progress-reporting race, not a hard regression.

---

## 3. Medium

### God-object ViewModels — **partially resolved 2026-10-05**
Measured sizes (post-refactor): `MainWindowViewModel` 1,578 lines · `AssetGalleryViewModel` 1,552 ·
`SidebarViewModel` 1,175 · `PropertyInspectorViewModel` 1,037.
- The remote refactor extracted `NavigationService`, `BulkAssetOperationService`, `PanelStateService`,
  and four tree services (`FolderTreeService`, `KeywordTreeService`, `CollectionTreeService`,
  `CategoryTreeService`), cutting `SidebarViewModel` from 2,573 → 1,175 and `MainWindowViewModel`
  from 2,040 → 1,578.
- **Remaining action:** `MainWindowViewModel` and `AssetGalleryViewModel` are still >1,500 lines.

### `Adam.Generators` missing from the solution
- Exists at `src/Adam.Generators/` and is referenced by `Adam.Shared.csproj` as an analyzer, but is
  absent from `Adam.slnx`, so solution-scoped tooling does not see it.
- **Action:** add it to `Adam.slnx`.

### No automated performance benchmarks
- Success criteria ><2s search at 100K assets, <3s loupe render, 10K scan <5min are unverified
  assertions. Indexes + keyset pagination make them plausible but untested.
- **Action:** add a CI benchmark gate (a `scripts/benchmark` project already exists).

### Manual protobuf maintenance
- No `.proto` schema; hand-written `IProtoSerializable` encode/decode. Field numbers are managed by
  hand. A stable opcode enum is in place (see §5), which removed the worst of this risk.

---

## 4. Low

### Repository hygiene
- Tracked at root: `Adam.CatalogBrowser-v1.0.0-win-x64.wixpdb` (build artifact), nine one-off
  `fix_*.py|sh|ps1` scripts, superseded `code_review.md` / `test-results.md` / `next_steps.md`,
  and tool artifacts (`llms.txt`, `opencode.json`, `opencode.jsonc`, `.claw-launch.json`,
  `.git-commit-msg.txt`, `skills-lock.json`). A `nul` file also sits on disk (Windows redirect
  artifact; gitignored).
- **No `README.md` at the repository root** — a public repo whose only entry point is `AGENTS.md`.
- **Action:** add a README; `git rm --cached` the artifact/script cruft.

### Stale OpenAPI-style spec artifacts
- `CreateAssetRequest.Content` is defined in the wire contract but never populated (indexing is
  in-place), so it is vestigial.

---

## 5. Previously listed here — now resolved (do not re-report)

| Old concern | Status | Evidence |
|-------------|--------|----------|
| Port mismatch (client 5000 vs server 9100) | ✅ Resolved | Host/port from `AdamConfig`; connection bar UI |
| Static mutable JWT signing key | ✅ Resolved | `AuthHandler._signingKey` is an instance field |
| No solution file | ✅ Resolved | `Adam.slnx` exists |
| EF Core 10 preview dependency | ✅ Resolved | EF Core 10 stable (Phase 15) |
| No TLS on transport | ✅ Resolved | `SslStream` in `TcpListenerService.cs:220`, `BrokerClient.cs:134` |
| No authorization on asset/collection handlers | ✅ Resolved | `AuthorizationMiddleware` injected |
| No EF Core migrations | ✅ Resolved | `src/Adam.Shared/Migrations/` (6 migrations + snapshot) |
| `Version` not a concurrency token | ✅ Resolved | `IsConcurrencyToken()` in `AppDbContext.cs:67,226` |
| No connection resiliency | ✅ Resolved | `EnableRetryOnFailure(3, 30s)` in `DbProviderConfig.cs:31,36` |
| No brute-force protection | ✅ Resolved | `LoginRateLimiter` + security logging |
| Non-functional multi-user sidebar | ✅ Resolved | `SidebarHandler` implements folder/keyword/date endpoints |
| Missing null checks in some handlers | ✅ Resolved | `HandlerBase` null-payload guards |
| Avalonia 12 vs spec version 11 | ✅ Resolved | Spec updated |
