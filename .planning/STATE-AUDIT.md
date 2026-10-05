# State Audit: adam

**Audit date:** 2026-10-05
**Auditor:** Buffy (Codebuff)
**Method:** Direct inspection of the working tree — `dotnet build`, `dotnet test`, `git ls-files`, targeted `grep`, and cross-reference of every `.planning/` and `specs/` claim against source.
**Branch:** `main` @ `cab6a78` ("Add video export and resolution/aspect UI")
**Scope:** Whole repository. No files were modified by this audit.

> This report is an *independent* verification. Where the existing GSD docs state a status, this audit re-derives it from the code and command output, and flags every divergence.

---

## 1. Bottom line

The codebase is **architecturally sound and genuinely feature-rich** — far ahead of its documentation. The build is green. But the repository has a **verification gap** that undermines every "all tests passing" claim in the planning docs, plus **significant documentation drift** that makes the GSD artifacts actively misleading.

| Dimension | Verdict |
|-----------|---------|
| Production build | ✅ **Green** — 0 errors, 170 warnings, ~19s |
| Test suite | ❌ **Cannot be verified green** — 1 deterministic failure + 1 indefinite hang that crashes the test host |
| Architecture & layering | ✅ Strong — clean separation, correct use of shared library |
| Security posture | 🟡 Mostly good, one real exposure (committed JWT signing key) |
| Documentation accuracy | ❌ **Poor** — every planning artifact contains at least one stale claim |
| Repo hygiene | 🟡 Noisy — tracked build artifacts, one-off scripts, session dirs, no README |
| Feature completeness vs spec | 🟡 High, but ~8 known gaps remain unclosed |

**Headline:** the project cannot currently pass its own test suite. Until that is fixed, no phase can legitimately be called "verified," and no milestone should be declared complete.

---

## 2. Measured verification results

All values below were produced on this machine on 2026-10-05 with `.NET SDK 10.0.401`.

### 2.1 Build

```
dotnet build Adam.slnx -c Debug --nologo
  170 Warning(s)
    0 Error(s)
Time Elapsed 00:00:19.06
```

✅ **Builds clean.** No errors anywhere. This is a real strength — 6 projects across 3 OS targets.

### 2.2 Tests — the critical finding

Test status per project, run individually against the same `Debug` build:

| Test project | Result | Failed | Skipped | Total |
|--------------|--------|--------|---------|-------|
| `Adam.Shared.Tests` | ✅ Passed | 0 | 0 | 412 |
| `Adam.ServiceManager.Tests` | ✅ Passed | 0 | 0 | 156 |
| `Adam.BrokerService.Tests` | ✅ Passed | 0 | 2 | 173 |
| `Adam.CatalogBrowser.Tests` | ❌ **Aborted** | 1 | — | 573+ (never completed) |

`Adam.CatalogBrowser.Tests` output:

```
Failed Adam.CatalogBrowser.Tests.ViewModels.DropCommandHandlersTests
  .AssignCategoryDropCommand_WithCategoryNode_EnqueuesCategoryOperation [5 ms]
Error Message: Microsoft.Data.Sqlite.SqliteException : SQLite Error 5: 'database is locked'.

[xUnit.net 00:09:00.94] Adam.CatalogBrowser.Tests:
  [Long Running Test] '...MetadataEditorPanelPersistenceTests
  .RestoreMetadataPanelStates_RestoresSavedPanels', Elapsed: 00:08:41
The active test run was aborted. Reason: Test host process crashed

Failed!  - Failed: 1, Passed: 572, Skipped: 0, Total: 573
```

**Two distinct defects:**

1. **Deterministic failure — `database is locked`.**
   `DropCommandHandlersTests.AssignCategoryDropCommand_WithCategoryNode_EnqueuesCategoryOperation` fails with `SQLite Error 5: 'database is locked'`. It fails even when the class runs alone (17 passed / 1 failed), so it is reproducible, not flaky. Root cause class: concurrent `DbContext`/connection access to a shared SQLite file without serialization or `ClearAllPools()` before writes.

2. **Indefinite hang → test host crash.**
   `MetadataEditorPanelPersistenceTests.RestoreMetadataPanelStates_RestoresSavedPanels` hangs forever in full-project runs (observed >9 minutes) and crashes the test host. It **passes in isolation** (1 test, 1s) and its whole class passes alone (9/9, 2s). This is a **test-isolation / accumulated-state leak**, not a bug in the ViewModel under test. Something carried across classes — leaked SQLite connections, a live `Dispatcher`/headless Avalonia session, or an event-handler/`IDisposable` leak — blocks it.

   Note: `parallelizeTestCollections` and `parallelizeAssembly` are **already `false`** in `tests/Adam.CatalogBrowser.Tests/xunit.runner.json`, so this is *not* parallelism. Sequential accumulation of state is the suspect.

**Consequence:** the suite is not a usable gate. `dotnet test` cannot return green, and a crashing test host means tests *after* the hang never run at all — so actual coverage of the later test classes is unknown.

### 2.3 Claim vs. reality — the numbers disagree

Three different "passing test" counts are asserted across the repo, and none matches the observed state:

| Source | Claim |
|--------|-------|
| `.planning/PROJECT.md` | "1,244 tests passing" |
| `.planning/STATE.md` | "1,335 tests passing (2 skipped)" |
| `AGENTS.md` | "1,371 tests pass" |
| **This audit** | **739 confirmed passing; CatalogBrowser suite cannot complete (1 fail + 1 hang)** |

---

## 3. What works well

These are genuine strengths, verified in code — not doc echo.

1. **Build health.** Full 6-project solution compiles with 0 errors on .NET 10. CI (`.github/workflows/build.yml`) builds and tests on `ubuntu/macos/windows` plus publishes self-contained binaries per RID. CI configuration is competent and real.

2. **Architecture and layering.** The `Adam.Shared` / `Adam.BrokerService` / `Adam.CatalogBrowser` / `Adam.ServiceManager` split is honored in practice. Domain models, EF Core `AppDbContext`, and protobuf contracts genuinely live in `Adam.Shared`; the client does not reach around the broker for server concerns. The client/server panel separation described in `AGENTS.md` matches the trees.

3. **Security hardening that most projects skip was actually done.** Contrary to the stale `CONCERNS.md`, verification found:
   - **TLS is implemented** — `SslStream` on both ends (`TcpListenerService.cs:220`, `BrokerClient.cs:134`, with a server-certificate validation callback).
   - **Optimistic concurrency is wired** — `IsConcurrencyToken()` on `Version` in `AppDbContext.cs:67,226`.
   - **Connection resiliency exists** — `EnableRetryOnFailure(3, 30s)` for Npgsql and SqlServer in `DbProviderConfig.cs:31,36`.
   - **Password hashing is strong** — PBKDF2, per the architecture review's own notes.

4. **Proper EF Core migrations replaced hand-rolled SQL.** `src/Adam.Shared/Migrations/` contains real, timestamped migrations (`InitialSchema`, `AddRolePermissionsTable`, `AddIsAiGenerated`, `AddSortOrderColumn`, `AddCommentsTable`, `AddUserPreferencesTable`) plus a model snapshot. This closes the review's CRITICAL-7 and is a genuine maturity step.

5. **Provider abstraction is real and provider-agnostic.** SQLite / PostgreSQL / SQL Server are selected by config only; the new composite indexes and keyset pagination use `HasIndex` and plain query composition that work on all three, per the documented SQLite `DateTimeOffset` ORDER BY workaround (that workaround is a subtle, correct piece of engineering — client-side ordering plus two-query pagination).

6. **Handler-per-message architecture with a dispatcher.** `MessageDispatcher` + `HandlerBase` + per-domain handlers is a clean, testable shape.

7. **Test volume is substantial and mostly healthy.** 107 test files, ~1,310 `[Fact]`/`[Theory]` methods. Three of four projects pass 100% cleanly. `Avalonia.Headless` UI testing *is* integrated (the stale `TESTING.md` claims it isn't).

8. **AI features are in-repo and local.** `LiquidVision.Core`, ONNX embedding search, and face recognition (YuNet + ArcFace) run without Python or cloud dependencies — a notable differentiator.

9. **Documentation corpus is large.** `docs/` (admin guide, user guide, wire protocol, migration guide), `.design_templates/`, and detailed phase plans exist. The *raw material* for good docs is present; it just needs to be kept true.

---

## 4. What needs improvement

Ordered by priority. Each item is evidence-backed.

### P0 — Cannot verify the product (blocks everything else)

**4.1 The test suite does not pass.** Fix the two CatalogBrowser defects (§2.2).
- Fix the `database is locked` race in the drop-handler tests (serialize DB access, or `SqliteConnection.ClearAllPools()` + unique DB per test — the pattern `AGENTS.md` already prescribes for ServiceManager tests).
- Diagnose the state leak that hangs `MetadataEditorPanelPersistenceTests` in full-project runs. Bisect by running the project with classes disabled until the hanging one passes; then find the leaking `IDisposable`/static/Dispatcher.
- Add a **guard so this cannot silently regress**: `dotnet test` exit status in CI must be authoritative. The current CI invokes `scripts/test.sh`; confirm an aborted run fails the job rather than passing.
- Consider a per-suite timeout so a hang fails fast instead of burning 9+ minutes and taking the host down.

**4.2 Verification claims must be derived, not asserted.** `AGENTS.md`, `PROJECT.md`, and `STATE.md` all publish test counts that no longer match reality. Any number in a planning doc should be regenerated from a real run, and the docs should say *when* and *how* it was measured.

### P1 — Documentation drift (a GSD-methodology failure in its own right)

The planning artifacts are self-contradictory. This is the highest-leverage non-code fix, because these docs are what every agent and contributor reads first.

| Artifact | Stale claim | Reality |
|----------|-------------|---------|
| `.planning/codebase/CONCERNS.md` | "Port Mismatch: Client vs Server" listed as **Critical** (client hardcodes 5000, server 9100) | Resolved — configurable via `AdamConfig`/connection bar. `AGENTS.md` marks it RESOLVED. |
| `.planning/codebase/CONCERNS.md` | "Static Mutable JWT Signing Key" listed as **Critical** | Resolved — instance field (T2.12). |
| `.planning/codebase/CONCERNS.md` | "No Solution File (.sln)" | `Adam.slnx` exists. |
| `.planning/codebase/CONCERNS.md` | "EF Core 10 Preview Dependency" | EF Core 10 is stable (Phase 15). |
| `.planning/codebase/TESTING.md` | "Adam.Shared.Tests ~2 tests", "No UI tests detected", "Minimal" | 412 tests; Avalonia.Headless *is* integrated. |
| `.planning/codebase/STRUCTURE.md` | "No solution file" | `Adam.slnx` exists. |
| `.planning/ARCHITECTURE-REVIEW.md` | 21 critical/high issues, many now fixed | No resolution status recorded — reads as if all still open. |
| `.planning/ROADMAP.md` | Phase 22 "🔜 Planned"; "21 complete, 1 planned" | Phases 22–24 are complete per `STATE.md`. |
| `.planning/ROADMAP.md` | v5.0 milestone "🔜 Planning" | `STATE.md`: "v5.0 Complete". |
| `AGENTS.md` | "Current Phase: 21" | `STATE.md`: Phase 24 complete. |
| `next_steps.md` | "Status baseline: v1.0 shipped (2026-06-12)" | 12+ phases past that baseline. |
| `test-results.md` | "Generated May 21, 2026 … 56/56 passed" | ~10x more tests exist now. |

**Recommended fix:** treat `.planning/` like code. Add a short **"as-of" header with a verification command** to each codebase-map file, and either delete or explicitly mark superseded documents (`ARCHITECTURE-REVIEW.md`, `code_review.md`, `test-results.md`) as historical. A single `CONCERNS.md` regeneration pass would remove the most dangerous misinformation.

### P1 — One real security exposure

**4.3 Committed JWT signing key in a public repository.**
```
src/Adam.BrokerService/appsettings.json:3
  "SigningKey": "4ariTzsHK9hYcVqy6em8hA6UDcPFGsrgquKof8H1aFg="
```
The file is tracked and the repo is public. Anyone can mint valid tokens for any user/role. It was rotated once (the old value appears in the architecture review) but rotating a key does not fix a *pattern* — the secret is still in git history.

- Load the key from an environment variable / user-secrets / `AdamConfig`, and fail fast if it is absent outside dev.
- Purge it from history (history rewrite) or, minimally, permanently invalidate it and document that the repo must never hold production keys.
- Audit for any other committed config secrets (none found in `appsettings.json` beyond this).

### P2 — Maintainability

**4.4 God-object ViewModels.** Sizes measured directly:

| File | Lines |
|------|-------|
| `src/Adam.CatalogBrowser/ViewModels/SidebarViewModel.cs` | **2,573** |
| `src/Adam.CatalogBrowser/ViewModels/MainWindowViewModel.cs` | **2,040** |
| `src/Adam.CatalogBrowser/ViewModels/AssetGalleryViewModel.cs` | **1,568** |
| `src/Adam.CatalogBrowser/ViewModels/PropertyInspectorViewModel.cs` | **1,037** |

`SidebarViewModel` and `MainWindowViewModel` are the two highest-churn, highest-risk files in the project and each exceeds 2,000 lines. `code_review.md` independently flagged this and it remains unaddressed. Extracting focused sub-view-models (a `PropertyInspectorViewModel` already exists but is itself >1,000 lines) would reduce the blast radius of every future UI change.

**4.5 170 build warnings, concentrated in security-sensitive server code.** The vast majority are `CS8602` (possible null dereference) in `Adam.BrokerService/Handlers/*` — `AssetHandler` (many), `SidebarHandler` (many), `UserHandler`, `CollectionHandler`, `SavedSearchHandler`, `SearchRankingHandler`, `SemanticSearchHandler`. Warnings in *handler* code are exactly where a null deref becomes a failed request or an unhandled exception. Worth an explicit pass, ideally with warnings-as-errors for `Adam.BrokerService`.

**4.6 `Adam.Generators` is missing from the solution.** It exists at `src/Adam.Generators/` and is referenced by `Adam.Shared.csproj` as an analyzer (`OutputItemType="Analyzer"`), but it is **absent from `Adam.slnx`**. Solution-scoped tooling and IDE views therefore do not see it. Add it.

### P2 — Repository hygiene

**4.7 Tracked cruft.** `git ls-files` shows these committed at root:

| Item | Problem |
|------|---------|
| `Adam.CatalogBrowser-v1.0.0-win-x64.wixpdb` | Committed build artifact; already matched by `.gitignore` (`*.wixpdb`) but tracked before the rule existed |
| `fix_active_search.py`, `fix_all.sh`, `fix_prop.py`, `fix_sidebar.ps1`, `fix_sidebar.py`, `fix_sidebar2/3/4.py`, `fix_via_lines.py` | 9 one-off migration scripts with no home |
| `code_review.md`, `test-results.md`, `next_steps.md` | Superseded point-in-time docs (see §4.2) |
| `llms.txt`, `opencode.json`, `opencode.jsonc`, `.claw-launch.json`, `.git-commit-msg.txt`, `skills-lock.json` | Tool/session artifacts |
| `nul` (on disk) | Windows redirect artifact; gitignored but present |
| `.claw`, `.claude`, `.agents`, `.opencode`, `.specify`, `.sandbox-home`, `.sandbox-tmp` | Overlapping agent/session directories |

**No `README.md` at the repository root** — a public repo whose entry point is a contributor-guide `AGENTS.md`. A short README (what adam is, how to build, how to run, where docs live) is the single cheapest quality improvement available.

**4.8 `.gitignore` shows evidence of past accidents** — entries such as `%TEMP%/`, `query`, `WindowsTemptest_request.json`, and a "Stray root files (accidentally committed)" section. Functional, but a signal that the repo has been used as a scratch space. A `git rm --cached` cleanup pass over §4.7 would pay off.

### P3 — Feature gaps still open

`specs/001-digital-asset-management/audit-gsd-gaps.md` (itself a strong artifact) documents these; they were not closed by Phases 22–24:

- ~~**Metadata write-back from the editor is not wired.**~~ ❌ **THIS CLAIM WAS WRONG — corrected 2026-10-05.** The audit repeated `audit-gsd-gaps.md` FR-013 without verifying it. In fact `MetadataEditorViewModel.SaveAsync` *does* call `MetadataWritebackService`, and the service is registered in the client DI (`App.axaml.cs:90`), so write-back is live. The real (and narrower) defect is that `SaveAsync` wrote `Copyright` only to `MetadataProfile`, while the XMP builder reads `DigitalAsset.Copyright` — so copyright edits reached the catalog but never the file. **Fixed** and covered by `MetadataEditorWritebackTests`. Remaining genuine gap: the `DigitalAsset` XMP path never writes `dc:creator`.
- **No first-launch onboarding.** The spec's US1 (pick root folder → scan → index on first run) is absent; users must manually open the Ingestion tab.
- **External file move/rename is untracked.** `FolderWatcherService` sees separate delete+create events, leaving a broken link at the old path and a duplicate at the new one.
- **8 extracted metadata fields are stored but invisible in the UI** (`UsageTerms`, `ContactInfo`, `City/State/Country`, `Orientation`, `GpsAltitude`, …); `Creator`/`Copyright`/`Headline` are read-only.
- **No broker-unavailable → standalone fallback**; the app sits disconnected.
- **No conflict detection in `DbMigrationService`**; it copies blindly.
- **No automated performance benchmarks in CI.** The 100K-asset / <2s search, <3s loupe, and 10K-scan-in-5min criteria are unverified assertions.
- **Vestigial API surface**: `CreateAssetRequest.Content` is defined but never populated (indexing is in-place).

---

## 5. Recommended sequencing

Following this project's own GSD convention (hardening before features):

**Wave 1 — Restore verifiability (do first, nothing else is trustworthy until this lands)**
1. Fix `DropCommandHandlersTests` `database is locked` (§4.1).
2. Diagnose and fix the `MetadataEditorPanelPersistenceTests` hang / state leak (§4.1).
3. Prove `dotnet test` returns green end-to-end, and make CI fail on an aborted run.
4. Publish the real, measured test count to `AGENTS.md`, `STATE.md`, `PROJECT.md`.

**Wave 2 — Truth in planning**
5. Regenerate or delete `.planning/codebase/CONCERNS.md`, `TESTING.md`, `STRUCTURE.md`.
6. Mark `ARCHITECTURE-REVIEW.md`, `code_review.md`, `test-results.md` as historical, with resolution columns.
7. Reconcile `ROADMAP.md` / `STATE.md` / `AGENTS.md` on phase and milestone status.

**Wave 3 — Security & hygiene**
8. Move the JWT `SigningKey` out of source; invalidate the committed value (§4.3).
9. Add a root `README.md`; `git rm --cached` the artifact/script/session cruft (§4.7).

**Wave 4 — Maintainability**
10. Warn-as-errors or a nullability sweep for `Adam.BrokerService/Handlers` (§4.5).
11. Begin decomposing `SidebarViewModel` / `MainWindowViewModel` (§4.4).
12. Add `Adam.Generators` to `Adam.slnx` (§4.6).

**Wave 5 — Close the core-value gap**
13. ~~Wire `MetadataEditorViewModel.SaveAsync` → `MetadataWritebackService`~~ ✅ **Already wired** (false claim in this audit; see the correction in §4). Fixed the underlying copyright round-trip defect instead. Still open: write `dc:creator` on the `DigitalAsset` XMP path, and surface the 8 hidden metadata fields.

---

## 6. Appendix — commands used

```bash
dotnet --version                                        # 10.0.401
dotnet build Adam.slnx -c Debug --nologo                # 0 errors, 170 warnings
dotnet test tests/Adam.Shared.Tests --no-build          # 412 passed
dotnet test tests/Adam.ServiceManager.Tests --no-build  # 156 passed
dotnet test tests/Adam.BrokerService.Tests --no-build   # 171 passed, 2 skipped
dotnet test tests/Adam.CatalogBrowser.Tests --no-build  # 1 failed, 572 passed, HOST CRASH
dotnet test tests/Adam.CatalogBrowser.Tests --filter "FullyQualifiedName~DropCommandHandlersTests"
dotnet test tests/Adam.CatalogBrowser.Tests --filter "FullyQualifiedName~MetadataEditorPanelPersistenceTests"
git ls-files | grep -v "/"                              # root-level tracked files
grep -rn "SslStream|IsConcurrencyToken|EnableRetryOnFailure" src   # all present
grep -rn "SigningKey" src --include=*.json              # committed secret
find src -name "*.cs" -exec wc -l {} + | sort -rn       # ViewModel sizes
```

**Caveat:** the `Adam.CatalogBrowser.Tests` result is machine- and ordering-dependent (both defects vanish when the affected classes run alone). It should be reproduced on a clean checkout and in CI before being treated as final; the observed behavior is sufficient to establish that the suite is not reliably green today.

*Audit complete. The original audit made no source changes; the follow-up work recorded below did.*

---

## 7. Addendum — follow-up work (2026-10-05, same session)

After the audit, three threads were actioned: reconcile the planning docs, harden the JWT key, and
close the metadata write-back gap.

### 7.1 Correction to this audit

**The "metadata write-back gap" I reported was wrong.** I repeated the claim from
`specs/001-digital-asset-management/audit-gsd-gaps.md` (FR-013) without verifying it — exactly the
failure mode this audit criticises elsewhere. Verification showed `SaveAsync` already calls
`MetadataWritebackService` and that the service is wired in DI. Lesson applied: the spec-audit
document is *also* an unverified claim and has now been corrected in place.

The **real** defect found by checking: `MetadataEditorViewModel.SaveAsync` set `Copyright` on
`MetadataProfile` only, while `MetadataWritebackService.BuildXmpPacket(DigitalAsset)` builds the
file's XMP from `DigitalAsset.Copyright`. Copyright edits therefore saved to the catalog but never
reached the source file, silently breaking the round-trip for that field.

### 7.2 Changes made

| Area | Change |
|------|--------|
| **Security** | `src/Adam.BrokerService/appsettings.json`: committed Base64 `Jwt:SigningKey` replaced with the `${ADAM_JWT_KEY}` placeholder. |
| **Security** | `AuthHandler`: fixed key resolution so the placeholder is treated as "not configured" and falls back to `ADAM_JWT_KEY` (previously the non-null placeholder short-circuited the `??`, so the env var was ignored). |
| **Security** | New `AuthHandlerSigningKeyTests` (5 tests) locking placeholder-fallback, fail-fast, and Base64 validation. |
| **Round-trip** | `MetadataEditorViewModel.SaveAsync`: mirror `Copyright` onto `DigitalAsset` so it is written to the file, matching `PropertyInspectorViewModel`. |
| **Round-trip** | New `MetadataEditorWritebackTests` (3 tests) covering RAW sidecar write-back, catalog persistence, and the no-write-back-service path. The copyright assertion fails before the fix and passes after. |
| **Docs** | `CONCERNS.md`, `TESTING.md`, `STRUCTURE.md` regenerated; `ARCHITECTURE-REVIEW.md`, `code_review.md`, `test-results.md` marked historical; `ROADMAP.md`, `STATE.md`, `PROJECT.md`, `AGENTS.md` reconciled; `audit-gsd-gaps.md` FR-013 corrected. |

### 7.3 Root cause of the CatalogBrowser hang — found incidentally

Building the write-back tests exposed why the suite hangs. A test that constructs a ViewModel with
the **default** `AvaloniaUiDispatcher` and then hits a `RunOnUiThreadAsync` path **blocks forever**
in the headless test host — no dispatcher loop is pumping. Confirmed by experiment: the class hung
with the default dispatcher and completed in ~1s once given a `SyncUiDispatcher`.

`MetadataEditorPanelPersistenceTests` constructs its ViewModel without a dispatcher, which is why
it hangs in full-project runs. Injecting a `SyncUiDispatcher` there is the likely fix. The separate
`database is locked` failure in `DropCommandHandlersTests` is a distinct SQLite-concurrency defect.

### 7.4 Newly discovered findings

- **`NU1903`:** `System.Security.Cryptography.Xml` 10.0.9 carries a known **high** severity
  vulnerability (GHSA-mmjf-rqrv-855v). Recorded in `CONCERNS.md` §2.
- **`dc:creator` never written:** the `DigitalAsset` XMP path has no `Creator` field, so creator
  metadata never round-trips to file XMP (only `MetadataProfile.Creator` exists).

### 7.5 Verification results

| Check | Result |
|-------|--------|
| `dotnet build Adam.slnx` | ✅ 0 errors |
| `Adam.BrokerService.Tests` | ✅ **176 passed**, 2 skipped (+5 new) |
| `Adam.CatalogBrowser.Tests` — `MetadataEditorViewModelTests` + `MetadataEditorWritebackTests` | ✅ **23 passed** (no regression from the `Copyright` change) |
| `Adam.CatalogBrowser.Tests` — full project | ❌ still aborts (unchanged; not in scope this session) |
| `Adam.Shared.Tests` / `Adam.ServiceManager.Tests` | Not re-run — no changes touched code they cover |

### 7.6 Post-rebase re-verification (2026-10-05, after pushing)

The push was rejected as non-fast-forward: `origin/main` had advanced by **9 commits** containing a
large `SidebarViewModel` / `MainWindowViewModel` decomposition. The branch was rebased cleanly (no
file overlap), which **invalidated the verification above** and changed some conclusions.

**Superseded findings (audit §4.4):** the god-object ViewModel sizes in §4.4 are no longer accurate.
Re-measured after the rebase:

| File | Audit §4.4 | Post-refactor |
|------|-----------|---------------|
| `SidebarViewModel` | 2,573 | **1,175** |
| `MainWindowViewModel` | 2,040 | **1,578** |
| `AssetGalleryViewModel` | 1,568 | 1,552 |
| `PropertyInspectorViewModel` | 1,037 | 1,037 |

New services extracted upstream: `NavigationService`, `BulkAssetOperationService`,
`PanelStateService`, and `Folder`/`Keyword`/`Collection`/`Category` tree services. The §4.4 item is
therefore **partially resolved**; `MainWindowViewModel` and `AssetGalleryViewModel` remain >1,500 lines.

**Re-measured test status after the rebase — this is materially worse than §2.2:**

| Project | Before rebase | After rebase |
|---------|---------------|--------------|
| `Adam.Shared.Tests` | ✅ 412 passed | ⚠️ 411 passed, **1 flaky** |
| `Adam.ServiceManager.Tests` | ✅ 156 passed in 17s | ❌ **hangs at 139/156** |
| `Adam.BrokerService.Tests` | ✅ 176 passed, 2 skipped | ✅ 176 passed, 2 skipped |
| `Adam.CatalogBrowser.Tests` | ❌ aborts | ❌ aborts |

- The Shared failure is a **flake**: `NearDuplicateServiceTests.ScanAllAsync_ReportsProgress`
  (`Expected last.completed to be 3, but found 2`) failed 2 of 3 isolated runs — a progress race.
- The ServiceManager hang is new to this session. Mechanism: the elevated-helper flow resolves its
  executable to the running process, which under test is `testhost.exe`; it therefore re-launches a
  **test host** as the helper, proceeds to `Starting 'install' operation...`, and blocks on a real
  `sc.exe install` that needs administrator rights. See `CONCERNS.md` §2.
- Build remains green: **0 errors, 184 warnings** (up from 170; the refactor added its own,
  including `CS0649: SidebarViewModel._categoryTreeService is never assigned`).

**Implication:** `origin/main` — not just this session's working tree — now has **no reliably green
test project**. My earlier "744 tests pass across three projects" figure is no longer accurate.

### 7.7 Still outstanding

1. The `Adam.CatalogBrowser.Tests` defects (§2.2) — now with a known root cause for the hang.
2. The old JWT key remains in git history; it should be invalidated/rotated.
3. `NU1903` vulnerable package upgrade.
4. The 170 nullability warnings in `Adam.BrokerService/Handlers`.
5. Repository hygiene: root `README.md`, tracked artifact/script cruft.
