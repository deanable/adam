# Phase 9: AI Image Tagging - Context

**Gathered:** 2026-06-07
**Status:** Ready for planning
**Source:** Conversational discuss (decisions captured during library exploration)

<domain>
## Phase Boundary

Integrate the in-repo `LiquidVision.Core` library (a native .NET wrapper around the
Liquid AI **LFM2-VL** vision-language model running on ONNX Runtime) into ADAM so users
can auto-generate **descriptions, keywords, and categories** for image assets entirely
locally — no Python, no cloud, no API keys.

**In scope:**
- Build wiring: add `LiquidVision.Core` to `Adam.slnx`, reference from `Adam.Shared`.
- An `AiTaggingService` in `Adam.Shared/Services` wrapping `ILiquidVisionAnalyzer`.
- DI registration in `Adam.CatalogBrowser/App.axaml.cs`.
- Three user-facing triggers (ingest opt-in, per-asset button, bulk selection).
- Model-download progress in the status bar.
- Unit tests against a fake `ILiquidVisionAnalyzer`.

**Out of scope (v1):**
- GPU execution provider (CUDA/DirectML) — CPU default; the library exposes
  `ExecutionProvider` / `ConfigureSessionOptions` for later opt-in.
- Broker-side / multi-user shared inference (the model runs client-side only for now).
- Non-image assets (video/audio/docs) — the model is image-only.
- Tag provenance tracking (distinguishing AI vs human tags) — explicitly deferred.
</domain>

<decisions>
## Implementation Decisions

### Library Placement
- **D-01:** Add `src/LiquidVision.Core/LiquidVision.Core.csproj` to the **root `Adam.slnx`**
  under `/src/` (the canonical solution — it does not yet list LiquidVision; the newer
  `src/Adam.slnx` already does, but root is the one used by the build/branch workflow).
- **D-02:** Add a `ProjectReference` to `LiquidVision.Core` from **`Adam.Shared.csproj`**
  (both target `net10.0`). This makes AI tagging reusable across client and any future
  server-side use. Accept the transitive `Microsoft.ML.OnnxRuntime` (1.26.0) + `SkiaSharp`
  (3.119.4) dependencies flowing into the CatalogBrowser output.

### AiTaggingService (Adam.Shared/Services)
- **D-03:** Create `AiTaggingService` wrapping `ILiquidVisionAnalyzer`. Responsibilities:
  lazy `InitializeAsync` on first use (model download), image-only guard, analyze, and
  merge results into the catalog.
- **D-04:** **Image-only guard** — only act on `AssetType.Image`; skip all other asset
  types silently (the LFM2-VL model is image-only).
- **D-05:** **Merge policy = auto-apply, union, no provenance.** Map `ImageTagResult.Keywords`
  → `AppDbContext.AssociateKeywordsAsync` and `.Categories` → `AssociateCategoriesAsync`
  (these already dedupe, normalize, build hierarchy, and bump `UsageCount`). Union with
  existing tags; do not track that a tag came from AI; do not add a provenance column.
- **D-06:** Fill `DigitalAsset.Description` from `ImageTagResult.Description` **only when the
  existing description is null/empty** (never overwrite a human-written description).
- **D-07:** Expose both a single-asset entry point `TagAssetAsync(Guid assetId, ct)` and a
  batch `TagAssetsAsync(IEnumerable<Guid>, IProgress<...>, ct)`. All DB writes go through
  `ModeManager.CreateDbContextAsync`.
- **D-08:** Tagging is **cancellable** (honor `CancellationToken` throughout).

### DI / Configuration
- **D-09:** Register via `AddLiquidVision(...)` in `App.axaml.cs` with
  `Precision = ModelPrecision.Q4F16` and `ExecutionProvider = ExecutionProviderKind.Cpu`.
  Register `AiTaggingService` as a singleton. The analyzer is already a singleton with a
  download `HttpClient` (correct — the model serializes inference on one semaphore).

### Trigger A — Opt-in during ingestion
- **D-10:** Add a `bool EnableAiTagging` property on `IngestionViewModel` bound to a checkbox
  in the ingestion panel.
- **D-11:** **Do NOT run inference inside `Parallel.ForEachAsync`** — the analyzer serializes
  on one `SemaphoreSlim`, so inline tagging would bottleneck the whole parallel ingest.
  Instead collect ingested **image** asset IDs during the loop and run a **sequential
  post-pass** after ingest completes, reusing the existing progress/ETA plumbing with an
  "AI tagging…" status. Cancellable via the existing `_cts`.

### Trigger B — Per-asset Auto-tag button
- **D-12:** Add an `AutoTagCommand` to `MetadataEditorViewModel`. It analyzes the loaded
  image and **unions the AI keywords into the editable `Tags` ObservableCollection** (and
  sets `Description` if empty), marking `IsDirty` so the existing `SaveAsync` persists them.
  Reuse `IsLoading` as the busy indicator.
- **D-12a:** **AI categories ARE applied in Trigger B** (consistent with Triggers A/C), even
  though the editor has no category UI. They map to the IPTC **`photoshop:Category`**
  convention (the repo reads `IptcDirectory.TagCategory` → the `Categories` list on
  extraction; see `MetadataExtractorService.cs`), so AI categories flow symmetrically through
  `AssociateCategoriesAsync` into the `Category` relation. If/when metadata writeback covers
  categories, they must serialize to `photoshop:Category`. Categories are written on Save
  (alongside keywords), not surfaced as editable chips in v1.

### Trigger C — Bulk re-tag selection
- **D-13:** Add a gallery command ("AI tag selected") that calls
  **`AiTaggingService.TagAssetsAsync(imageIds, progress, ct)` directly** (naturally sequential
  since inference serializes — no separate queue needed). Filter the selection to **image**
  assets. Do **not** modify `BulkOperationQueue` — its `BulkOperation` record
  (`{AssetIds, Name, IsKeyword}`) is shaped only for single-tag assignment and cannot carry an
  "AI-tag this asset" job. Refresh gallery / property inspector on completion using the
  existing refresh events.

### Model-download UX
- **D-14:** Surface model-download progress in the **status bar** (likely via
  `StatusBarViewModel`), driven by the analyzer's `DownloadProgress` /
  `IProgress<double>`. First-ever tag triggers a large Hugging Face download; it must show
  progress and must not block parallel ingestion.

### Distribution
- **D-15:** ONNX Runtime + SkiaSharp installer size increase is **acceptable for v1**.

### Testing
- **D-16:** Unit-test `AiTaggingService` against a **fake `ILiquidVisionAnalyzer`** (return a
  canned `ImageTagResult`). Cover: image-only filtering, keyword/category merge, description
  fill-only-when-empty, and cancellation. No real model download in tests.

### Claude's Discretion
- Exact property/command naming, status-bar wiring details, where the "AI tag selected"
  gallery command surfaces in the UI, and how progress is threaded from the analyzer's
  `INotifyPropertyChanged` to `StatusBarViewModel`.
- Whether `AiTaggingService` takes `ILiquidVisionAnalyzer` + `ModeManager` directly or via
  a small interface for testability.
</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### LiquidVision library (the thing being integrated)
- `src/LiquidVision.Core/ILiquidVisionAnalyzer.cs` — public API: `InitializeAsync`,
  `AnalyzeAsync(string|byte[])`, `DownloadProgress`, `IsInitialized` (INPC + IAsyncDisposable).
- `src/LiquidVision.Core/ImageTagResult.cs` — result record (Description/Keywords/Categories/…).
- `src/LiquidVision.Core/Configuration/LiquidVisionOptions.cs` — `ModelPrecision`,
  `ExecutionProviderKind`, precision/provider/prompt config.
- `src/LiquidVision.Core/DependencyInjection/ServiceCollectionExtensions.cs` —
  `AddLiquidVision(...)` registration helper.
- `src/LiquidVision.Core/LiquidVisionAnalyzer.cs` — concurrency model (single semaphore),
  lazy init, download-on-first-use, corruption-retry.

### ADAM integration points
- `src/Adam.Shared/Models/DigitalAsset.cs` — `Keywords`, `Categories`, `Description`, `Type`.
- `src/Adam.Shared/Data/AppDbContext.cs` — `AssociateKeywordsAsync` (line ~200),
  `AssociateCategoriesAsync` (the merge helpers to reuse).
- `src/Adam.Shared/Models/AssetType.cs` — image guard.
- `src/Adam.CatalogBrowser/App.axaml.cs` — DI composition root.
- `src/Adam.CatalogBrowser/ViewModels/IngestionViewModel.cs` — parallel ingest loop +
  existing keyword/category association (Trigger A).
- `src/Adam.CatalogBrowser/ViewModels/MetadataEditorViewModel.cs` — `Tags`, `SaveAsync` (Trigger B).
- `src/Adam.CatalogBrowser/ViewModels/StatusBarViewModel.cs` — download progress surface.
- `src/Adam.CatalogBrowser/Services/BulkOperationQueue.cs` — bulk re-tag (Trigger C).

### Project guidance
- `AGENTS.md` — Client/Server boundary, dual-mode architecture, "shared logic in Adam.Shared".
</canonical_refs>

<specifics>
## Specific Ideas

- Recommended analyzer options: `Precision = Q4F16` (smaller/faster than the Fp32 default),
  `ExecutionProvider = Cpu`. Default greedy decoding and the built-in catalog instruction
  prompt (`LiquidVisionOptions.DefaultInstructionPrompt`) already produce the
  description/keywords/categories JSON the result parser expects.
- The analyzer's output shape (`string[]` keywords/categories) maps 1:1 onto the existing
  `AssociateKeywordsAsync`/`AssociateCategoriesAsync` signatures — no transformation needed.
</specifics>

<deferred>
## Deferred Ideas

- GPU execution providers (CUDA/DirectML).
- Broker-side shared inference for multi-user mode.
- Tag provenance / "AI-generated" badge and selective removal.
- AI tagging for non-image asset types.
</deferred>

---

*Phase: 09-ai-image-tagging*
*Context gathered: 2026-06-07 via conversational discuss*
