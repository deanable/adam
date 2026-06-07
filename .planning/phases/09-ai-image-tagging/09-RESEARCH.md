# Phase 9: AI Image Tagging - Research

**Researched:** 2026-06-07
**Domain:** Local ONNX vision-model integration into an Avalonia 12 / .NET 10 desktop app (in-repo library wiring, MVVM triggers, EF Core tag merge)
**Confidence:** HIGH

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- **D-01:** Add `src/LiquidVision.Core/LiquidVision.Core.csproj` to `Adam.slnx` under `/src/`.
- **D-02:** Add a `ProjectReference` to `LiquidVision.Core` from **`Adam.Shared.csproj`** (both target `net10.0`). Makes AI tagging reusable across client and any future server-side use. Accept the transitive `Microsoft.ML.OnnxRuntime` (1.26.0) + `SkiaSharp` (3.119.4) dependencies flowing into the CatalogBrowser output.
- **D-03:** Create `AiTaggingService` wrapping `ILiquidVisionAnalyzer`. Responsibilities: lazy `InitializeAsync` on first use (model download), image-only guard, analyze, and merge results into the catalog.
- **D-04:** **Image-only guard** — only act on `AssetType.Image`; skip all other asset types silently.
- **D-05:** **Merge policy = auto-apply, union, no provenance.** Map `ImageTagResult.Keywords` → `AppDbContext.AssociateKeywordsAsync` and `.Categories` → `AssociateCategoriesAsync`. Union with existing tags; no provenance tracking/column.
- **D-06:** Fill `DigitalAsset.Description` from `ImageTagResult.Description` **only when the existing description is null/empty** (never overwrite a human-written description).
- **D-07:** Expose single-asset `TagAssetAsync(Guid assetId, ct)` and batch `TagAssetsAsync(IEnumerable<Guid>, IProgress<...>, ct)`. All DB writes go through `ModeManager.CreateDbContextAsync`.
- **D-08:** Tagging is **cancellable** (honor `CancellationToken` throughout).
- **D-09:** Register via `AddLiquidVision(...)` in `App.axaml.cs` with `Precision = ModelPrecision.Q4F16` and `ExecutionProvider = ExecutionProviderKind.Cpu`. Register `AiTaggingService` as a singleton.
- **D-10:** Add a `bool EnableAiTagging` property on `IngestionViewModel` bound to a checkbox in the ingestion panel.
- **D-11:** **Do NOT run inference inside `Parallel.ForEachAsync`.** Collect ingested **image** asset IDs during the loop and run a **sequential post-pass** after ingest completes, reusing the existing progress/ETA plumbing with an "AI tagging…" status. Cancellable via the existing `_cts`.
- **D-12:** Add an `AutoTagCommand` to `MetadataEditorViewModel`. Analyzes the loaded image and **unions results into the editable `Tags` ObservableCollection** (sets `Description` if empty), marking `IsDirty` so the existing `SaveAsync` persists them. Reuse `IsLoading` as the busy indicator.
- **D-13:** Add a gallery command ("AI tag selected") that enqueues selected **image** asset IDs for **sequential** processing via `AiTaggingService.TagAssetAsync`. Refresh gallery / property inspector on completion using existing refresh events.
- **D-14:** Surface model-download progress in the **status bar** (likely via `StatusBarViewModel`), driven by the analyzer's `DownloadProgress` / `IProgress<double>`. First-ever tag triggers a large Hugging Face download; must show progress and must not block parallel ingestion.
- **D-15:** ONNX Runtime + SkiaSharp installer size increase is **acceptable for v1**.
- **D-16:** Unit-test `AiTaggingService` against a **fake `ILiquidVisionAnalyzer`** (canned `ImageTagResult`). Cover: image-only filtering, keyword/category merge, description fill-only-when-empty, cancellation. No real model download in tests.

### Claude's Discretion
- Exact property/command naming, status-bar wiring details, where the "AI tag selected" gallery command surfaces, and how progress is threaded from the analyzer's `INotifyPropertyChanged` to `StatusBarViewModel`.
- Whether `AiTaggingService` takes `ILiquidVisionAnalyzer` + `ModeManager` directly or via a small interface for testability.

### Deferred Ideas (OUT OF SCOPE)
- GPU execution providers (CUDA/DirectML).
- Broker-side shared inference for multi-user mode.
- Tag provenance / "AI-generated" badge and selective removal.
- AI tagging for non-image asset types.
</user_constraints>

<phase_requirements>
## Phase Requirements

This phase implements v2 requirement **META-V2-01** (Automatic keyword suggestions based on image content — AI/ML), which is also listed in `REQUIREMENTS.md` "Out of Scope" as "AI-powered image recognition — Deferred to v2." Phase 9 is the v2 realization. No v1 REQ-IDs were supplied by the orchestrator; the table below maps the locked decisions to the verifiable deliverables a VALIDATION.md will derive.

| ID | Description | Research Support |
|----|-------------|------------------|
| META-V2-01 | Automatic keyword/category/description suggestions from image content, local-only | `LiquidVisionAnalyzer` produces `ImageTagResult{Description, Keywords[], Categories[]}`; maps 1:1 onto `AssociateKeywordsAsync`/`AssociateCategoriesAsync` (verified in source). |
| D-03/D-07 | `AiTaggingService` with single + batch entry points | Wraps `ILiquidVisionAnalyzer`; DB via `ModeManager.CreateDbContextAsync` (verified pattern in `BulkOperationQueue`). |
| D-10/D-11 | Trigger A — opt-in ingest post-pass | `IngestionViewModel.StartIngestionAsync` loop + `_cts` + `ReportProgressAsync` ETA plumbing (verified). |
| D-12 | Trigger B — per-asset Auto-tag button | `MetadataEditorViewModel.Tags`/`IsDirty`/`SaveAsync`/`IsLoading` (verified). |
| D-13 | Trigger C — bulk re-tag selection | `AssetGalleryViewModel.SelectedAssets` (`ObservableCollection<AssetListItem>`, `.Id`) — but existing `BulkOperationQueue` cannot host this unchanged (see Pitfall 4). |
| D-14 | Model-download progress in status bar | Analyzer `DownloadProgress` (INPC) + `IProgress<double>`; `StatusBarViewModel` already uses `Dispatcher.UIThread.Post` pattern (verified). |
</phase_requirements>

## Summary

This is an **integration phase, not a greenfield one**. The hard ML work is already done and verified in `src/LiquidVision.Core`: a sealed `LiquidVisionAnalyzer` implements `ILiquidVisionAnalyzer` (INPC + IAsyncDisposable), downloads/verifies/loads the LFM2-VL ONNX model on first use, and serializes **both** `InitializeAsync` and `AnalyzeAsync` on a single `SemaphoreSlim(1,1)`. The output record `ImageTagResult(Description, Keywords[], Categories[], …)` maps 1:1 onto ADAM's existing `AppDbContext.AssociateKeywordsAsync`/`AssociateCategoriesAsync` merge helpers — which already trim, dedupe, normalize, build keyword hierarchy, and bump `UsageCount`. So D-05's "no transformation needed" claim is confirmed by source.

The single genuinely risky technical question — **will LiquidVision's `SkiaSharp 3.119.4` coexist with Avalonia's own Skia?** — resolves favorably. Avalonia.Skia 12.0.3 (used by this repo via Avalonia 12.0.3) **itself depends on SkiaSharp `>= 3.119.4`** [VERIFIED: nuget.org/packages/Avalonia.Skia/12.0.3]. LiquidVision pins the exact same `3.119.4`. The native `libSkiaSharp` compatibility window is `[119.0, 120.0)`; both managed assemblies fall inside it, so there is **no version conflict** and Avalonia already ships the Windows native asset. `Microsoft.ML.OnnxRuntime 1.26.0` (latest stable, verified) is independent of Skia and ships its own `runtimes/win-x64/native/onnxruntime.dll`, flowed transitively through the `Adam.Shared` ProjectReference into the CatalogBrowser executable output. The remaining work is pure wiring: DI registration, three MVVM triggers, status-bar progress threading, and fake-analyzer unit tests.

**Primary recommendation:** Build `AiTaggingService` in `Adam.Shared/Services` depending on `ILiquidVisionAnalyzer` + `ModeManager`. Make it lazy-initialize the analyzer on first tag (never at startup, never inline in `Parallel.ForEachAsync`). Reuse the exact `await using var db = await _modeManager.CreateDbContextAsync(ct)` → load with `.Include(Keywords).Include(Categories)` → `AssociateKeywordsAsync`/`AssociateCategoriesAsync` → `SaveChangesAsync` pattern already used by `BulkOperationQueue` and `MetadataEditorViewModel.SaveAsync`. **Do NOT reuse `BulkOperationQueue` for Trigger C unchanged** — its `BulkOperation` record only carries a single keyword/category name and cannot represent an "AI-tag this asset" job (see Pitfall 4).

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Model download + ONNX inference | Client (in-process, `Adam.Shared` lib) | — | D-09/D-26: client-side only for v1; analyzer is a singleton in the CatalogBrowser process. Broker-side inference explicitly deferred. |
| `AiTaggingService` (orchestrate analyze + merge) | Shared logic (`Adam.Shared/Services`) | — | AGENTS.md: "shared logic belongs in Adam.Shared." Reusable across client and future server use (D-02). |
| Tag/category/description persistence | Database / Storage (`AppDbContext` via `ModeManager`) | — | Existing merge helpers live on `AppDbContext`; dual-mode routing via `ModeManager.CreateDbContextAsync`. |
| Trigger A (ingest opt-in checkbox + post-pass) | Client ViewModel (`IngestionViewModel`) | Shared (`AiTaggingService`) | Ingest UI is client-only; post-pass calls into shared service. |
| Trigger B (per-asset Auto-tag button) | Client ViewModel (`MetadataEditorViewModel`) | Shared (`AiTaggingService`) | Editor is client-only; unions into in-memory `Tags` then persists via existing `SaveAsync`. |
| Trigger C (bulk "AI tag selected") | Client ViewModel + a sequential queue | Shared (`AiTaggingService`) | Gallery selection is client-only; queue runs sequentially because inference is serialized. |
| Download-progress display | Client ViewModel (`StatusBarViewModel`) | — | Pure UI surface bound to analyzer's INPC `DownloadProgress`. |

**Boundary note (AGENTS.md):** Everything here lives in `Adam.CatalogBrowser` (the client) and `Adam.Shared`. Nothing touches `Adam.ServiceManager` or `Adam.BrokerService`. This is consistent with "if it browses assets / edits metadata, it belongs in CatalogBrowser." Multi-user mode still works: writes go through `ModeManager.CreateDbContextAsync`, which routes to the shared DB in MultiUser mode (inference still runs client-side).

## Standard Stack

### Core (already in-repo — nothing new to choose)
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| `LiquidVision.Core` | 0.1.0 (in-repo) | LFM2-VL image→{description,keywords,categories} | The in-repo library being integrated. Read its source, not a NuGet. [VERIFIED: src/LiquidVision.Core/*] |
| `Microsoft.ML.OnnxRuntime` | 1.26.0 | ONNX inference runtime (transitive via LiquidVision) | Latest stable on NuGet (no 1.26.x patch beyond .0). [VERIFIED: nuget flatcontainer index] |
| `SkiaSharp` | 3.119.4 | Image decode/preprocess in LiquidVision (transitive) | Latest stable 3.119.x; **same version Avalonia 12.0.3 depends on** → no conflict. [VERIFIED: nuget flatcontainer index + Avalonia.Skia 12.0.3 deps] |
| `Microsoft.Extensions.Http` | 10.0.8 (transitive via LiquidVision) | `IHttpClientFactory` for model download | Already aligned with repo's M.E.* 10.0.x packages. [VERIFIED: LiquidVision.Core.csproj] |

### Supporting (already in CatalogBrowser / Shared — reuse, do not add)
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `Microsoft.Extensions.DependencyInjection` | 10.0.7 | DI container in `App.axaml.cs` | Register analyzer via `AddLiquidVision(...)` + `AiTaggingService` singleton. [VERIFIED: csproj] |
| `Avalonia` (+ Threading) | 12.0.3 | `Dispatcher.UIThread.Post/InvokeAsync` for marshaling progress | Status-bar/ETA updates. [VERIFIED: csproj] |
| `xunit` / `FluentAssertions` / `NSubstitute` | 2.9.3 / 7.2.0 / 5.3.0 | Test stack in `Adam.Shared.Tests` | Fake `ILiquidVisionAnalyzer` via NSubstitute or a hand-written stub. [VERIFIED: Adam.Shared.Tests.csproj] |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| New sequential AI-tag queue for Trigger C | Extend existing `BulkOperationQueue` | `BulkOperation` is a sealed record with `{AssetIds, Name, IsKeyword}` only — it has no shape for "run AI analysis." Extending it means adding a discriminated job type and changing `ProcessLoopAsync`. A small dedicated queue (or reusing `TagAssetsAsync` batch with `IProgress`) is cleaner. See Pitfall 4. |
| Q4F16 precision (D-09) | Fp32 (library default) / Q4 / Quantized | Locked to Q4F16 — smaller/faster, good accuracy balance. Do not research alternatives. |
| Lazy init on first tag (recommended) | Eager init at app startup | Eager would download a large model at launch and stall startup; lazy keeps cold-start fast and only pays the cost when a user opts in. |

**Installation:** No new packages are installed directly. The only build-graph change is the ProjectReference (D-01/D-02):

```xml
<!-- Add to src/Adam.Shared/Adam.Shared.csproj -->
<ItemGroup>
  <ProjectReference Include="..\LiquidVision.Core\LiquidVision.Core.csproj" />
</ItemGroup>
```

And add the project to the **root** solution (D-01):

```xml
<!-- Adam.slnx — add under the /src/ folder -->
<Project Path="src/LiquidVision.Core/LiquidVision.Core.csproj" />
```

**Version verification (run before locking the plan; training data may be stale):**
```bash
# Confirms 1.26.0 is still latest stable, no 1.26.x patch
dotnet nuget locals all --clear   # optional
# Already verified this session via NuGet flatcontainer:
#   Microsoft.ML.OnnxRuntime → 1.26.0 (latest stable)  [VERIFIED 2026-06-07]
#   SkiaSharp                → 3.119.4 (latest 3.119.x) [VERIFIED 2026-06-07]
#   Avalonia.Skia 12.0.3 depends on SkiaSharp >= 3.119.4 [VERIFIED 2026-06-07]
```

## Architecture Patterns

### System Architecture Diagram

```
                         ┌─────────────────────────────────────────────┐
  User action            │            Adam.CatalogBrowser (client)      │
  ───────────            │                                              │
  (A) ingest checkbox ──▶│ IngestionViewModel                          │
      EnableAiTagging    │   Parallel.ForEachAsync (ingest, no AI) ──┐  │
                         │   collect image asset IDs                 │  │
                         │   SEQUENTIAL post-pass over IDs ──────────┼─▶│
                         │                                           │  │
  (B) Auto-tag button ──▶│ MetadataEditorViewModel.AutoTagCommand ───┼─▶│   ┌──────────────────────────────┐
      (loaded image)     │   union into Tags, set Desc if empty,     │  │   │ AiTaggingService (Adam.Shared)│
      sets IsDirty       │   IsDirty=true → existing SaveAsync       │  │   │                              │
                         │                                           │  │   │ TagAssetAsync(id, ct)        │
  (C) "AI tag selected"─▶│ Gallery cmd → sequential AI-tag queue ────┼─▶│   │ TagAssetsAsync(ids, prog,ct) │
      SelectedAssets     │   (filter AssetType.Image)                │  │   │  1. EnsureInitialized (lazy) │
                         │                                           │  │   │  2. image-only guard         │
                         │ StatusBarViewModel ◀── DownloadProgress   │  │   │  3. read bytes, AnalyzeAsync │──┐
                         │   (INPC) via Dispatcher.UIThread.Post     │  │   │  4. merge into catalog       │  │
                         └───────────────────────────────────────────┘  │   └──────────────┬───────────────┘  │
                                                                         │                  │                  │
                                                                         ▼                  ▼                  ▼
                                                    ┌───────────────────────────┐  ┌──────────────────┐  ┌────────────────────────┐
                                                    │ ILiquidVisionAnalyzer     │  │ ModeManager       │  │ AppDbContext           │
                                                    │ (singleton)               │  │ .CreateDbContext  │  │ .AssociateKeywords     │
                                                    │ SemaphoreSlim(1,1) ───────┤  │  Async(ct)        │  │ .AssociateCategories   │
                                                    │  serializes Init+Analyze  │  │ (Standalone→SQLite│  │  (dedupe/normalize/    │
                                                    │ InitializeAsync (download)│  │  MultiUser→shared)│  │   hierarchy/UsageCount)│
                                                    │ AnalyzeAsync(byte[])      │  └──────────────────┘  │ + Description fill-if  │
                                                    │  → ImageTagResult         │                        │   empty (D-06)         │
                                                    └─────────────┬─────────────┘                        └────────────────────────┘
                                                                  │ first use
                                                                  ▼
                                                    ┌───────────────────────────┐
                                                    │ Hugging Face download      │
                                                    │ onnx-community/LFM2-VL-450M│
                                                    │ cached under LocalAppData  │
                                                    └───────────────────────────┘
```

Primary use case (B, per-asset): user opens an image in the editor → clicks Auto-tag → `AutoTagCommand` calls `AiTaggingService` → analyzer lazily downloads model (progress to status bar) → `AnalyzeAsync(bytes)` returns `ImageTagResult` → service unions keywords/categories into the editor's `Tags` and sets `Description` if empty → `IsDirty=true` → existing `SaveAsync` persists via `AssociateKeywordsAsync`.

### Recommended Project Structure
```
src/Adam.Shared/Services/
├── AiTaggingService.cs          # NEW — wraps ILiquidVisionAnalyzer, merges into catalog
src/Adam.CatalogBrowser/
├── App.axaml.cs                 # EDIT — AddLiquidVision(...) + AiTaggingService singleton
├── ViewModels/
│   ├── IngestionViewModel.cs    # EDIT — EnableAiTagging + sequential post-pass (Trigger A)
│   ├── MetadataEditorViewModel.cs # EDIT — AutoTagCommand (Trigger B)
│   ├── StatusBarViewModel.cs    # EDIT — bind DownloadProgress (Trigger/UX D-14)
│   └── (gallery host VM)        # EDIT — "AI tag selected" command (Trigger C)
├── Services/
│   └── AiTagQueue.cs            # NEW (recommended) — sequential queue for Trigger C
tests/Adam.Shared.Tests/Services/
└── AiTaggingServiceTests.cs     # NEW — fake ILiquidVisionAnalyzer
```

### Pattern 1: DB merge — copy the existing `BulkOperationQueue` shape exactly
**What:** Load asset with both collections included, call the existing helpers, save.
**When to use:** Every write path in `AiTaggingService` (single + batch).
**Example (matches verified house style):**
```csharp
// Source: src/Adam.CatalogBrowser/Services/BulkOperationQueue.cs (verified)
await using var db = await _modeManager.CreateDbContextAsync(ct).ConfigureAwait(false);
var asset = await db.DigitalAssets
    .Include(a => a.Keywords)
    .Include(a => a.Categories)
    .FirstOrDefaultAsync(a => a.Id == assetId, ct).ConfigureAwait(false);
if (asset is null || asset.Type != AssetType.Image) return;     // image-only guard (D-04)

var result = await _analyzer.AnalyzeAsync(await File.ReadAllBytesAsync(asset.StoragePath, ct), ct);
await db.AssociateKeywordsAsync(asset, result.Keywords, ct);     // dedupe/normalize/hierarchy/UsageCount
await db.AssociateCategoriesAsync(asset, result.Categories, ct);
if (string.IsNullOrWhiteSpace(asset.Description) && !string.IsNullOrWhiteSpace(result.Description))
    asset.Description = result.Description;                       // D-06: fill-only-when-empty
await db.SaveChangesAsync(ct).ConfigureAwait(false);
```
Note: `StoragePath` is stored forward-slashed (`filePath.Replace('\\','/')` at ingest); `File.ReadAllBytesAsync` handles that fine on Windows. `Description` column is `HasMaxLength(2000)` — truncate or trust the model (descriptions are short).

### Pattern 2: Lazy, idempotent init — let the analyzer's own semaphore do the work
**What:** Call `await _analyzer.InitializeAsync(progress, ct)` before the first `AnalyzeAsync`. `InitializeAsync` already early-returns if `IsInitialized` and is guarded by the same semaphore, so concurrent callers are safe and only one download happens.
**When to use:** At the top of every `AiTaggingService` analyze path. No double-checked locking needed in the service — the library handles it.
```csharp
// Source: src/LiquidVision.Core/LiquidVisionAnalyzer.cs (verified: InitializeAsync early-returns if IsInitialized, semaphore-guarded)
await _analyzer.InitializeAsync(_downloadProgress, ct);   // safe to call every time
```

### Pattern 3: Progress threading to the status bar (Trigger/UX D-14)
**What:** The analyzer raises `PropertyChanged("DownloadProgress")` and also reports to the `IProgress<double>` passed to `InitializeAsync`. Prefer the `IProgress<double>` channel (cleaner than subscribing to INPC), and marshal to UI via `Dispatcher.UIThread.Post` — the exact pattern `StatusBarViewModel` already uses for `BulkOperationQueue`.
```csharp
// Source: src/Adam.CatalogBrowser/ViewModels/StatusBarViewModel.cs (verified pattern)
var progress = new Progress<double>(p => Dispatcher.UIThread.Post(() => {
    ModelDownloadPercentage = p * 100;          // analyzer reports [0,1]
    IsModelDownloading = p > 0 && p < 1.0;
}));
```
`Progress<T>` captures the UI `SynchronizationContext` when constructed on the UI thread, so the `Dispatcher.UIThread.Post` is belt-and-suspenders. Either is acceptable (Claude's discretion per D-14).

### Anti-Patterns to Avoid
- **Calling `AnalyzeAsync` inside `Parallel.ForEachAsync`:** the analyzer serializes on one semaphore → N parallel callers queue behind one inference, wasting threads and starving the parallel ingest. D-11 forbids this; use the sequential post-pass.
- **Eager `InitializeAsync` at startup:** triggers a large download before the user opts in. Lazy-init on first tag.
- **Re-implementing dedupe/normalize/hierarchy:** `AssociateKeywordsAsync` already does it (and splits on `|`/`>` for hierarchy). Pass raw `result.Keywords` straight through.
- **Overwriting a human description:** D-06 — only fill when null/empty.
- **Reusing `BulkOperationQueue` for Trigger C as-is:** its job record can't express an AI-tag job (Pitfall 4).

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Model download / cache / verify / corruption-retry | A custom HttpClient downloader | `LiquidVisionAnalyzer.InitializeAsync` | Already handles cache layout, verification marker, retry-on-corruption, exponential backoff. [VERIFIED: LiquidVisionAnalyzer.cs] |
| Serializing inference | A custom lock/queue around the model | The analyzer's built-in `SemaphoreSlim(1,1)` | Init + analyze are already serialized; adding your own lock just double-locks. [VERIFIED] |
| Keyword/category dedupe + hierarchy + UsageCount | String parsing + EF lookups | `AppDbContext.AssociateKeywordsAsync` / `AssociateCategoriesAsync` | Trims, distincts, normalizes, builds parent hierarchy (`\|`/`>`), checks ChangeTracker for pending adds, bumps UsageCount. [VERIFIED: AppDbContext.cs] |
| Dual-mode DB routing | Branching on `IsStandalone` in the service | `ModeManager.CreateDbContextAsync(ct)` | Single call returns the correct provider/connection for Standalone or MultiUser. [VERIFIED: ModeManager.cs] |
| JSON parse of model output | Manual JSON parsing | The analyzer's internal `TagResultParser` (already invoked) | `AnalyzeAsync` returns a parsed `ImageTagResult` with a tolerant raw-text fallback. [VERIFIED] |
| UI-thread marshaling | Manual `SynchronizationContext` juggling | `Dispatcher.UIThread.Post` / `Progress<double>` | Matches existing `StatusBarViewModel` / `ReportProgressAsync`. [VERIFIED] |

**Key insight:** Almost everything tricky in this phase is already solved in `LiquidVision.Core` or `AppDbContext`. The phase's risk is **wiring correctness** (threading, lazy init, queue choice), not algorithms.

## Runtime State Inventory

This is an additive integration phase, not a rename/refactor. The trigger condition (rename/refactor/migration/string-replace) does not apply, so a full inventory is not required. Two adjacent items worth noting for the planner:

| Category | Items Found | Action Required |
|----------|-------------|------------------|
| Stored data | New keyword/category rows + populated `Description` written into the existing catalog DB. No schema change (D-05: no provenance column). | None beyond normal writes. `Description` column already exists (`HasMaxLength(2000)`). |
| Model cache (new external state) | First run downloads `onnx-community/LFM2-VL-450M-ONNX` to `LocalApplicationData` (per `ModelCacheManager`, `CacheDirectory` null → per-user default). | None — managed entirely by the library. Not committed to git; not part of the app installer. |
| Build artifacts | OnnxRuntime native (`runtimes/win-x64/native/onnxruntime.dll`) + SkiaSharp native flow into `bin/.../net10.0/runtimes/`. SkiaSharp native is **already present** because Avalonia 12 ships the same 3.119.4. | Verify `dotnet build` copies `onnxruntime.dll` to CatalogBrowser output (see Environment Availability). |
| OS-registered state / secrets / env vars | None. No new env vars, no registry keys, no service registration. | None — verified by reading App.axaml.cs and LiquidVisionOptions (download is anonymous HTTP to Hugging Face). |

## Common Pitfalls

### Pitfall 1: SkiaSharp native/managed mismatch (the classic "libSkiaSharp 88.1 incompatible")
**What goes wrong:** A `SkiaSharp` managed/native version mismatch throws at first image decode ("native libSkiaSharp incompatible, supported range [119.0, 120.0)").
**Why it happens:** Historically Avalonia bundled Skia 2.88 while a newer 3.x got pulled transitively. **Not a risk here:** Avalonia 12.0.3 depends on SkiaSharp `>= 3.119.4` and LiquidVision pins exactly `3.119.4` — they unify to one version. [VERIFIED: Avalonia.Skia 12.0.3 deps + LiquidVision.Core.csproj]
**How to avoid:** Keep both at 3.119.4 (already the case). If anyone bumps Avalonia or SkiaSharp later, bump both together. After the ProjectReference is added, clean `bin`/`obj` once to avoid stale native binaries.
**Warning signs:** `TypeInitializationException` on `SKFontManager` / `SKBitmap` at runtime; the version string in the message tells you which native lib loaded.

### Pitfall 2: DirectX 12 requirement on old Windows (pre-existing, not introduced by this phase)
**What goes wrong:** Avalonia 12 + SkiaSharp 3.119 require DirectX 12 (Windows 10 19045/22H2+). Old Windows crashes at startup.
**Why it happens:** SkiaSharp 3.119 dropped DX11; Avalonia closed this as by-design. [CITED: github.com/AvaloniaUI/Avalonia/issues/20710]
**How to avoid:** This is **already** the app's baseline (the repo runs Avalonia 12.0.3). LiquidVision adds no new GPU requirement (CPU execution provider, D-09). No action — just don't be surprised if it surfaces; it is orthogonal to AI tagging.
**Warning signs:** APPCRASH at startup on Windows < 10 22H2. Pre-existing.

### Pitfall 3: First-download blocks the ingest (D-14 must-not-block)
**What goes wrong:** The opt-in ingest post-pass calls `InitializeAsync` synchronously and the multi-hundred-MB download stalls perceived progress with no feedback.
**Why it happens:** `InitializeAsync` does the download under its semaphore; if invoked without surfacing `IProgress<double>`, the UI looks frozen.
**How to avoid:** Run the AI post-pass **after** the parallel ingest completes (D-11), surface `DownloadProgress` to the status bar (D-14), keep it cancellable via `_cts`. The parallel ingest itself never calls the analyzer, so it is never blocked by the download.
**Warning signs:** Status bar stuck at "AI tagging…" with no percentage during first run.

### Pitfall 4: `BulkOperationQueue` cannot host Trigger C unchanged (IMPORTANT)
**What goes wrong:** D-13 says "enqueue through the existing `BulkOperationQueue`," but `BulkOperation` is `sealed record { List<Guid> AssetIds; string Name; bool IsKeyword }` and `ProcessLoopAsync` only knows how to call `AssociateKeywordsAsync`/`AssociateCategoriesAsync` with a single `Name`. There is no field for "run AI analysis," and `Name` would be meaningless.
**Why it happens:** The queue was purpose-built for "assign one tag to many assets," not "run a per-asset computation."
**How to avoid (recommend for the planner):** Either (a) add a small dedicated `AiTagQueue` (Channel-based, sequential, same INPC progress shape as `BulkOperationProgress`, reuse `StatusBarViewModel` wiring), or (b) have the gallery command call `AiTaggingService.TagAssetsAsync(selectedImageIds, progress, ct)` directly on a background task with the existing status-bar progress surface. Option (b) is the least new code and naturally sequential (the analyzer serializes anyway). Do **not** silently shoehorn AI jobs into `BulkOperation`. This is a decision the planner should make explicit; D-13's intent ("sequential, via existing queue/refresh events") is honored by either option.
**Warning signs:** A plan task that adds an `IsAiTag` bool to `BulkOperation` and branches in `ProcessLoopAsync` — workable but couples two concerns; flag for review.

### Pitfall 5: `Parallel.ForEachAsync` per-iteration DbContext vs. the post-pass
**What goes wrong:** Trying to tag inside the ingest loop reuses the per-iteration `dbLock` + DbContext and serializes the whole ingest behind inference.
**Why it happens:** D-11's exact warning. The ingest loop already uses a `SemaphoreSlim dbLock` for DB writes; adding inference inside that critical section is catastrophic for throughput.
**How to avoid:** Collect `asset.Id` for `assetType == AssetType.Image` into a thread-safe collection (e.g., `ConcurrentBag<Guid>`) during the loop; after `Parallel.ForEachAsync` returns, iterate sequentially calling `AiTaggingService.TagAssetAsync` with progress + `_cts.Token`.
**Warning signs:** Ingest throughput collapses to ~1 file at a time when the checkbox is on.

### Pitfall 6: Cancellation semantics mid-inference
**What goes wrong:** Cancelling during `AnalyzeAsync` throws `OperationCanceledException` from inside `Task.Run`/generation; callers must catch it and not treat it as a failure.
**Why it happens:** `AnalyzeAsync` honors `ct` in `Task.Run` and the generator loop. [VERIFIED: LiquidVisionAnalyzer.cs lines 159-172]
**How to avoid:** In post-pass and queue loops, catch `OperationCanceledException` separately (re-throw or break cleanly) like `BulkOperationQueue.ProcessLoopAsync` already does. Tests must cover a cancelled token (D-16).
**Warning signs:** A cancelled bulk re-tag logged as N errors.

## Code Examples

### Trigger A — sequential post-pass after parallel ingest
```csharp
// Source: composed from IngestionViewModel.cs (verified loop) + D-11
var imageIds = new System.Collections.Concurrent.ConcurrentBag<Guid>();
// ... inside the existing Parallel.ForEachAsync, right after db.SaveChangesAsync for a new asset:
if (asset.Type == AssetType.Image) imageIds.Add(asset.Id);
// ... after Parallel.ForEachAsync(...) returns and IsIngesting bookkeeping:
if (EnableAiTagging && !ct.IsCancellationRequested)
{
    int done = 0; var total = imageIds.Count;
    foreach (var id in imageIds)
    {
        ct.ThrowIfCancellationRequested();
        await _aiTaggingService.TagAssetAsync(id, ct);
        await ReportProgressAsync(++done, total, "AI tagging…");
    }
}
```

### Trigger B — AutoTagCommand unions into the editor's Tags
```csharp
// Source: composed from MetadataEditorViewModel.cs (verified Tags/IsDirty/SaveAsync) + D-12
AutoTagCommand = new RelayCommand(async _ => await AutoTagAsync(), _ => HasAsset && !IsLoading);

private async Task AutoTagAsync(CancellationToken ct = default)
{
    if (_asset is null || _asset.Type != AssetType.Image) return;
    IsLoading = true;
    try
    {
        var result = await _aiTaggingService.AnalyzeAssetAsync(_asset.Id, ct); // returns ImageTagResult (no DB write)
        foreach (var kw in result.Keywords)
            if (!Tags.Contains(kw, StringComparer.OrdinalIgnoreCase)) Tags.Add(kw); // union → CollectionChanged sets IsDirty
        if (string.IsNullOrWhiteSpace(Description) && !string.IsNullOrWhiteSpace(result.Description))
            Description = result.Description;                                       // setter sets IsDirty
        // existing SaveAsync persists via AssociateKeywordsAsync on SaveCommand
    }
    finally { IsLoading = false; }
}
```
Note: For Trigger B the service should expose an **analyze-only** path (returns `ImageTagResult` without touching the DB) so results flow into the in-memory `Tags` and ride the existing dirty/Save flow — categories aren't shown in this editor, so they can be persisted on Save via a service call or deferred. The planner should decide whether categories are applied directly in B (small DB write) or skipped in the editor. (Discretion area.)

### Trigger C — gallery command, direct batch (recommended over BulkOperationQueue)
```csharp
// Source: composed from AssetGalleryViewModel.SelectedAssets (verified) + D-13
AiTagSelectedCommand = new RelayCommand(async _ =>
{
    var imageIds = _gallery.SelectedAssets
        .Select(a => a.Id)                        // AssetListItem.Id (Guid) — verified
        .ToList();                                // filter to images inside the service via guard
    var progress = new Progress<(int done,int total)>(/* → status bar */);
    await _aiTaggingService.TagAssetsAsync(imageIds, progress, ct);
    // raise existing refresh event so gallery/property inspector reload
});
```

### DI registration (D-09)
```csharp
// Source: composed from App.axaml.cs (verified) + LiquidVision ServiceCollectionExtensions
services.AddLiquidVision(o =>
{
    o.Precision = ModelPrecision.Q4F16;
    o.ExecutionProvider = ExecutionProviderKind.Cpu;
});
services.AddSingleton<AiTaggingService>();   // ctor: (ILiquidVisionAnalyzer, ModeManager, ILogger<AiTaggingService>)
```
`AddLiquidVision` registers `ILiquidVisionAnalyzer` as a singleton with an `IHttpClientFactory` client — correct for a serialized, long-lived analyzer.

### Fake analyzer for tests (D-16)
```csharp
// Source: NSubstitute already referenced in Adam.Shared.Tests.csproj (verified)
var fake = Substitute.For<ILiquidVisionAnalyzer>();
fake.IsInitialized.Returns(true);
fake.InitializeAsync(Arg.Any<IProgress<double>?>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
fake.AnalyzeAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
    .Returns(new ImageTagResult("a cat on a mat", new[]{"cat","mat"}, new[]{"animals"}, "{...}", 12.3, "1.0"));
```
Note: `AiTaggingService` reads the file at `asset.StoragePath`; tests should either point at a tiny temp file or have the service accept bytes via an overload to avoid disk I/O. A hand-written stub class is equally fine (xUnit shop already mixes both styles).

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Foundry Local / `FoundryLocalTaggingService` (earlier exploration; `azure-ai-foundry-local` skill) | In-repo `LiquidVision.Core` (native ONNX LFM2-VL, no Python/cloud/keys) | This phase (CONTEXT.md) | The chosen approach is LiquidVision; the foundry-local skill is **context only**, do not use it. |
| Avalonia bundles SkiaSharp 2.88 | Avalonia 12 targets SkiaSharp 3.119.x (DX12) | Avalonia 12.0 (PR #18981) | Eliminates the historical Skia coexistence conflict — LiquidVision's 3.119.4 aligns. |
| AI tagging deferred (REQUIREMENTS "Out of Scope") | Implemented locally in v2 (META-V2-01) | Phase 9 added 2026-06-07 | This phase moves AI recognition from out-of-scope to shipped. |

**Deprecated/outdated:**
- `azure-ai-foundry-local` skill / `FoundryLocalTaggingService` — superseded by LiquidVision.Core for ADAM. Ignore for implementation.

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | LFM2-VL-450M ONNX download is "multi-hundred-MB" (large enough to need progress UX). Exact size not measured this session; the model id is `onnx-community/LFM2-VL-450M-ONNX` with Q4F16 weights. | Pitfalls 3, Summary | Low. Even if smaller, surfacing progress (D-14) is correct regardless. |
| A2 | `Microsoft.ML.OnnxRuntime` ships `runtimes/win-x64/native/onnxruntime.dll` and it flows transitively through a library ProjectReference into the CatalogBrowser exe output under default (non-single-file) `dotnet build`/`publish`. This is standard ORT/.NET behavior but not executed/verified in this session. | Environment Availability | Medium. If single-file publish is ever used, native asset extraction needs `IncludeNativeLibrariesForSelfExtract`. The repo currently uses plain `WinExe` (no single-file evidence), so default copy applies. |
| A3 | The default `InstructionPrompt` reliably yields parseable JSON for the catalog contract. The library has a tolerant raw-text fallback if parsing fails, so a parse miss degrades to description-only, not a crash. | Summary, Pattern 1 | Low. Fallback path verified in source (lines 176-186). |
| A4 | Reusing `BulkOperationQueue` for Trigger C is undesirable as-is. Verified from `BulkOperation` record shape; the recommendation (dedicated queue or direct `TagAssetsAsync`) is a judgment call the planner should confirm. | Pitfall 4 | Low — both paths satisfy D-13; this only affects which code gets written. |

## Open Questions

1. **Trigger C queue mechanism (BulkOperationQueue vs. new).**
   - What we know: `BulkOperation`'s record shape can't express an AI-tag job; the analyzer serializes anyway.
   - What's unclear: D-13's "via the existing `BulkOperationQueue`" was written before confirming the record shape.
   - Recommendation: Plan for `AiTaggingService.TagAssetsAsync(ids, progress, ct)` invoked from the gallery command on a background task (sequential, cancellable), with status-bar progress mirroring `BulkOperationProgress`. Surface to discuss-phase if a literal reuse of `BulkOperationQueue` is required.

2. **Categories in Trigger B (editor).**
   - What we know: `MetadataEditorView` edits `Tags` (keywords) but not categories; `ImageTagResult` includes categories.
   - What's unclear: whether AI categories should be applied directly to the DB in Trigger B or dropped (since the editor has no category UI).
   - Recommendation: In B, union keywords into `Tags` (rides existing Save) and apply categories via a direct `AssociateCategoriesAsync` write inside the service, or skip categories in B and rely on A/C for them. Planner/discretion (D-12 only mentions tags + description).

3. **Single-file / publish profile.**
   - What we know: CatalogBrowser is a plain `WinExe`; no single-file evidence in the csproj.
   - What's unclear: whether any release pipeline uses `PublishSingleFile`.
   - Recommendation: Default copy of native assets works for `dotnet build`/framework-dependent publish. If single-file is later adopted, add `<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>`. Document, don't block.

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| .NET 10 SDK | All projects (`net10.0`) | ✓ (repo builds today on net10.0) | net10.0 | — |
| `Microsoft.ML.OnnxRuntime` native (`onnxruntime.dll`, win-x64) | Inference | ✓ (ships in the 1.26.0 NuGet `runtimes/`) | 1.26.0 | None needed — CPU EP is in-box |
| SkiaSharp native (`libSkiaSharp`) | Image preprocess + Avalonia render | ✓ (already present via Avalonia 12.0.3 → SkiaSharp 3.119.4) | 3.119.4 (native range [119.0,120.0)) | — |
| Internet access to Hugging Face | First-run model download | ✗ verify per machine | — | Offline machines cannot tag until model cached; surface a clear error. Cache persists after first download. |
| DirectX 12 (Windows 10 22H2+) | Avalonia 12 rendering (pre-existing) | n/a to AI feature | — | Pre-existing app baseline; not introduced here. |

**Missing dependencies with no fallback:**
- Hugging Face reachability on first use only. After the model is cached under `LocalApplicationData`, tagging works offline. The plan should surface a friendly "could not download model" status (the analyzer throws/propagates download failures after retries).

**Missing dependencies with fallback:**
- None blocking. CPU execution provider is always available; no GPU drivers required (D-09).

**Verification step for the planner (run once during execution):**
```bash
dotnet build src/Adam.CatalogBrowser/Adam.CatalogBrowser.csproj -c Debug
# Confirm onnxruntime.dll lands in output:
ls src/Adam.CatalogBrowser/bin/Debug/net10.0/runtimes/win-x64/native/onnxruntime.dll
```

## Validation Architecture

> nyquist_validation is enabled (config.json workflow.nyquist_validation = true).

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.3 + FluentAssertions 7.2.0 + NSubstitute 5.3.0 |
| Config file | none (SDK-style; `Using Include="Xunit"`) |
| Quick run command | `dotnet test tests/Adam.Shared.Tests --filter "FullyQualifiedName~AiTaggingService"` |
| Full suite command | `dotnet test` |

### Phase Requirements → Test Map
| Req | Behavior | Test Type | Automated Command | File Exists? |
|-----|----------|-----------|-------------------|-------------|
| D-04 | Non-image asset → analyzer NOT called, no DB change | unit | `dotnet test tests/Adam.Shared.Tests --filter FullyQualifiedName~AiTaggingServiceTests` | ❌ Wave 0 |
| D-05 | Keywords/categories union-merged via Associate* (dedupe, hierarchy, UsageCount) | unit (real SQLite) | same filter | ❌ Wave 0 |
| D-06 | Description filled only when empty; never overwrites existing | unit | same filter | ❌ Wave 0 |
| D-08 | Cancelled token → `OperationCanceledException`, partial state sane | unit | same filter | ❌ Wave 0 |
| D-07 | `TagAssetsAsync` reports progress and tags all image IDs | unit | same filter | ❌ Wave 0 |
| D-02 | Build graph: ProjectReference resolves; `onnxruntime.dll` copies to output | build/smoke | `dotnet build src/Adam.CatalogBrowser` + check `runtimes/` | ❌ Wave 0 (build gate) |
| D-11 | (manual/headless) ingest with checkbox runs post-pass sequentially | integration/headless | `dotnet test tests/Adam.CatalogBrowser.Tests` | partial — headless infra exists |
| D-14 | Status-bar percentage updates on `IProgress<double>` | unit (VM) | `dotnet test tests/Adam.CatalogBrowser.Tests` | ❌ Wave 0 |

### Sampling Rate
- **Per task commit:** `dotnet test tests/Adam.Shared.Tests --filter FullyQualifiedName~AiTaggingService`
- **Per wave merge:** `dotnet test tests/Adam.Shared.Tests tests/Adam.CatalogBrowser.Tests`
- **Phase gate:** `dotnet test` (full suite green) before `/gsd-verify-work`. Baseline: 510 tests pass, 2-3 Docker-dependent skipped.

### Wave 0 Gaps
- [ ] `tests/Adam.Shared.Tests/Services/AiTaggingServiceTests.cs` — covers D-04/05/06/07/08 via a fake `ILiquidVisionAnalyzer` (NSubstitute) + a per-test SQLite DB (use the `ModeManager` + temp-path pattern from AGENTS.md "Isolated Database Per Test").
- [ ] CatalogBrowser VM test for status-bar progress threading (D-14) — optional but cheap.
- [ ] No framework install needed — xUnit/FluentAssertions/NSubstitute already referenced.

## Security Domain

> security_enforcement not present in config.json → treated as enabled. Scope is narrow: a local file → local model → local DB feature with one outbound download.

### Applicable ASVS Categories
| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | no | No new auth surface; tagging runs in the already-authenticated client session. |
| V3 Session Management | no | Unchanged. |
| V4 Access Control | partial | In MultiUser mode, writes go through `ModeManager.CreateDbContextAsync` → shared DB; existing role checks (`asset:update`) govern. Don't bypass them. |
| V5 Input Validation | yes | Model output is untrusted text. `AssociateKeywordsAsync` trims/normalizes; `Description` is `HasMaxLength(2000)` (truncate/guard). Treat `result.Keywords/Categories/Description` as untrusted strings (no SQL injection risk — EF parameterizes — but length/encoding guards apply). |
| V6 Cryptography | partial | Model download integrity is via the library's verification marker; the download is plain HTTP(S) to Hugging Face. No secrets involved. Don't hand-roll any crypto. |
| V12 Files/Resources | yes | Service reads `asset.StoragePath` from disk. Path comes from the trusted catalog (set at ingest), not user input — but read defensively (file-missing handling like `AssetHandler.GetFileAsync` does). |

### Known Threat Patterns
| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Malicious/oversized model output (huge keyword list, giant description) | DoS / Tampering | Length caps (DB `MaxLength(2000)` on Description; `Associate*` distincts); the prompt requests 5-15 keywords. |
| Compromised model download (MITM) | Tampering | HTTPS to Hugging Face + library verification marker. Out of phase scope to harden further; note for security review. |
| Reading a path outside the catalog | Information disclosure | `StoragePath` originates from trusted ingest, not user input. Treat as low risk; handle file-missing gracefully. |
| Untrusted text reaching XMP write-back later | Injection (downstream) | AI keywords flow into the same keyword pipeline as human tags; existing XMP write-back (Phase 4) already handles arbitrary keyword strings. No new path. |

## Sources

### Primary (HIGH confidence)
- In-repo source (read this session): `src/LiquidVision.Core/{ILiquidVisionAnalyzer,LiquidVisionAnalyzer,ImageTagResult}.cs`, `Configuration/LiquidVisionOptions.cs`, `DependencyInjection/ServiceCollectionExtensions.cs`, `LiquidVision.Core.csproj`
- In-repo ADAM source: `src/Adam.Shared/Data/AppDbContext.cs` (Associate* lines 200-259), `src/Adam.Shared/Services/{ModeManager,RelayCommand}.cs`, `src/Adam.Shared/Models/{DigitalAsset,AssetType}.cs`, `src/Adam.CatalogBrowser/App.axaml.cs`, `ViewModels/{IngestionViewModel,MetadataEditorViewModel,StatusBarViewModel,AssetGalleryViewModel,MainWindowViewModel}.cs`, `Services/BulkOperationQueue.cs`, `Models/AssetListItem.cs`, `Adam.slnx`, csproj files
- nuget.org flatcontainer index — Microsoft.ML.OnnxRuntime (1.26.0 latest stable), SkiaSharp (3.119.4 latest 3.119.x)
- nuget.org/packages/Avalonia.Skia/12.0.3 — depends on SkiaSharp >= 3.119.4 (the key coexistence finding)

### Secondary (MEDIUM confidence)
- github.com/AvaloniaUI/Avalonia PR #18981 ("[12.0] Target SkiaSharp 3.0, drop 2.88") — confirms Avalonia 12 Skia 3.x alignment
- github.com/AvaloniaUI/Avalonia issue #20710 — Avalonia 12 / SkiaSharp 3.119 DX12 requirement (closed by-design)

### Tertiary (LOW confidence)
- WebSearch summaries on SkiaSharp native/managed mismatch symptoms (cross-checked against the Avalonia.Skia 12.0.3 dependency declaration, which upgrades the conclusion to HIGH).

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — versions verified against NuGet; library read from source.
- Architecture: HIGH — every integration point (DB merge, ModeManager, ViewModels, DI) read and confirmed in repo.
- SkiaSharp coexistence: HIGH — Avalonia.Skia 12.0.3 declares the same SkiaSharp 3.119.4 dependency.
- Native-asset copy into output (A2): MEDIUM — standard behavior, not executed this session; one cheap build-check resolves it.
- Pitfalls: HIGH — derived from source (semaphore, Parallel loop, BulkOperation shape) + verified Avalonia/Skia history.

**Research date:** 2026-06-07
**Valid until:** 2026-07-07 (stable; re-verify OnnxRuntime/SkiaSharp/Avalonia versions if any of those packages are bumped before then)
