---
phase: 9
slug: ai-image-tagging
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-06-07
---

# Phase 9 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.3 + FluentAssertions 7.2.0 + NSubstitute 5.3.0 (existing in `Adam.Shared.Tests`) |
| **Config file** | `tests/Adam.Shared.Tests/Adam.Shared.Tests.csproj` |
| **Quick run command** | `dotnet test tests/Adam.Shared.Tests/Adam.Shared.Tests.csproj` |
| **Full suite command** | `dotnet test Adam.slnx` |
| **Estimated runtime** | ~30–90 seconds (Shared.Tests); full suite longer |

---

## Sampling Rate

- **After every task commit:** Run the quick command (`dotnet test tests/Adam.Shared.Tests/...`)
- **After every plan wave:** Run `dotnet build Adam.slnx` + the quick command
- **Before `/gsd-verify-work`:** Full suite must be green
- **Max feedback latency:** ~90 seconds

---

## Per-Task Verification Map

> Task IDs are illustrative until the planner finalizes plan/task breakdown. Each maps a
> deliverable to its automated proof. The `AiTaggingService` is the high-value unit-tested
> surface (against a fake `ILiquidVisionAnalyzer`); the build itself proves the native-dependency
> wiring; UI triggers are partly manual.

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| 9-01-01 | 01 | 1 | D-01,D-02 | — | N/A | build | `dotnet build Adam.slnx` | ❌ W0 | ⬜ pending |
| 9-02-01 | 02 | 2 | D-03,D-04 | — | Non-image assets are skipped (no analyze call) | unit | `dotnet test tests/Adam.Shared.Tests/Adam.Shared.Tests.csproj` | ❌ W0 | ⬜ pending |
| 9-02-02 | 02 | 2 | D-05 | — | AI keywords/categories union into asset via Associate* | unit | (same) | ❌ W0 | ⬜ pending |
| 9-02-03 | 02 | 2 | D-06 | — | Description filled only when empty; never overwrites human text | unit | (same) | ❌ W0 | ⬜ pending |
| 9-02-04 | 02 | 2 | D-08 | — | Cancellation token honored (throws/stops cleanly) | unit | (same) | ❌ W0 | ⬜ pending |
| 9-02-05 | 02 | 2 | D-12a | — | Categories applied through AssociateCategoriesAsync | unit | (same) | ❌ W0 | ⬜ pending |
| 9-03-01 | 03 | 3 | D-09 | — | DI resolves AiTaggingService + ILiquidVisionAnalyzer singleton | build | `dotnet build src/Adam.CatalogBrowser` | ❌ W0 | ⬜ pending |
| 9-04-01 | 04 | 3 | D-10,D-11 | — | Ingest post-pass runs after parallel loop, not inline | manual+unit | (VM-level test if extractable) | ❌ W0 | ⬜ pending |
| 9-05-01 | 05 | 3 | D-12,D-12a | — | AutoTagCommand unions keywords; categories applied on save | manual | manual UAT | — | ⬜ pending |
| 9-06-01 | 06 | 3 | D-13 | — | Bulk command filters to images, calls TagAssetsAsync | manual | manual UAT | — | ⬜ pending |
| 9-07-01 | 07 | 3 | D-14 | — | Download progress surfaces in status bar; non-blocking | manual | manual UAT | — | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `tests/Adam.Shared.Tests/Services/AiTaggingServiceTests.cs` — new test file with a fake/NSubstitute `ILiquidVisionAnalyzer` covering D-04, D-05, D-06, D-08, D-12a.
- [ ] In-memory or SQLite `AppDbContext` fixture for merge assertions (reuse the pattern already in `Adam.Shared.Tests`).

*Existing xUnit/FluentAssertions/NSubstitute infrastructure covers the framework needs — no new framework install.*

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Ingest checkbox runs AI tagging as a sequential post-pass | D-10, D-11 | Avalonia UI + real model inference | Ingest a folder of images with "AI tagging" checked; confirm keywords/categories appear and parallel ingest speed is unaffected during the file loop |
| Per-asset Auto-tag button | D-12 | UI command + real inference | Select an image, click Auto-tag, confirm keywords populate the Tags list and Save persists |
| Bulk re-tag selection | D-13 | UI + queue + inference | Select multiple images, run "AI tag selected", confirm sequential progress and applied tags |
| First-run model download progress | D-14 | Network + large download + status bar | Clear model cache, trigger any tag, confirm status bar shows download progress and the UI stays responsive |
| Native dependency runtime load | D-02, D-15 | Requires running the published app | `dotnet run` CatalogBrowser, perform a tag, confirm ONNX Runtime + SkiaSharp load without missing-native errors |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies (UI triggers documented as manual)
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 90s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
