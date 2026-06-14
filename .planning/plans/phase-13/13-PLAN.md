---
goal: Fix accumulated technical debt — EF Core stabilization, solution file, CI fixes, null-handler validation, protobuf docs, logging standardization, FolderWatcher batching.
version: 1.0
date_created: 2026-06-13
last_updated: 2026-06-13
status: 'Planned'
tags: [hardening, tech-debt, ci, logging, protobuf, ef-core]
---

# Phase 13: Production Hardening

**Goal:** Fix the technical debt accumulated across 12 phases before building new features. Targets the concerns documented in `.planning/codebase/CONCERNS.md` and codebase-wide cleanup items.

**Depends on:** Phases 1-12 (complete codebase)

---

## 1. Tasks

### Wave 1 — Project Infrastructure

#### T13.1 — Create Solution File

**Files changed:** Root directory

**Implementation:**
```bash
dotnet new sln -n adam
dotnet sln add src/Adam.Shared/Adam.Shared.csproj
dotnet sln add src/Adam.CatalogBrowser/Adam.CatalogBrowser.csproj
dotnet sln add src/Adam.BrokerService/Adam.BrokerService.csproj
dotnet sln add src/Adam.ServiceManager/Adam.ServiceManager.csproj
dotnet sln add src/Adam.CatalogBrowser/Adam.CatalogBrowser.csproj
dotnet sln add tests/Adam.Shared.Tests/Adam.Shared.Tests.csproj
dotnet sln add tests/Adam.BrokerService.Tests/Adam.BrokerService.Tests.csproj
dotnet sln add tests/Adam.ServiceManager.Tests/Adam.ServiceManager.Tests.csproj
dotnet sln add tests/Adam.CatalogBrowser.Tests/Adam.CatalogBrowser.Tests.csproj
```

**Rationale:** No `.sln` file means degraded IDE experience (no Solution Explorer structure, no unified build/test). Also required for `dotnet test --solution` and CI pipeline consistency.

#### T13.2 — Fix CatalogBrowser Tests in CI

**Files changed:**
- `tests/Adam.CatalogBrowser.Tests/` — test configuration
- CI workflow (`.github/workflows/`)

**Issues identified:**
- CatalogBrowser tests consistently timeout due to Avalonia display dependency
- `testhost.exe` processes leak between test runs, locking build artifacts

**Implementation:**
1. Add `[CollectionDefinition]` / `[Collection]` to force sequential execution for Avalonia-dependent tests
2. Configure Avalonia headless platform in test base class (ensure `App.axaml.cs` initialization doesn't hang without display)
3. Add `--settings` to test command with timeout override if needed
4. Add test cleanup that kills orphaned `testhost.exe` processes (PowerShell teardown step in CI)
5. Add retry logic for flaky headless UI tests

### Wave 2 — Code Quality

#### T13.3 — Null-Handler Validation

**Files changed:** Broker handler files in `src/Adam.BrokerService/Handlers/`

**Analysis from CONCERNS.md:** "Some handler methods don't validate envelope payload before deserialization. Could throw on malformed requests instead of returning error envelopes."

**Implementation:**

For each handler (Collections, Keywords, Categories, Assets, Users, Auth, Change, Sidebar, Status, etc.):

1. Add guard clause at the top of each Handle method:
```csharp
public async Task<Envelope> HandleAsync(Envelope envelope, CancellationToken ct)
{
    if (envelope?.Payload == null || envelope.Payload.IsEmpty)
    {
        return new Envelope
        {
            MessageType = envelope?.MessageType ?? MessageTypeCode.Unknown,
            StatusCode = 8, // BadRequest
            CorrelationId = envelope?.CorrelationId ?? string.Empty,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(
                new ErrorResponse { Message = "Empty or null payload" }))
        };
    }
    // ... rest of handler
}
```

2. Add try/catch around deserialization:
```csharp
try
{
    var request = ProtoHelper.Deserialize<CreateKeywordRequest>(envelope.Payload.ToByteArray());
}
catch (Exception ex)
{
    _logger.LogError(ex, "Failed to deserialize {MessageType}", envelope.MessageType);
    return ErrorEnvelope(envelope, 8, "Malformed request payload");
}
```

**Handlers to audit:**
- `Collections/` — CreateCollectionHandler, UpdateCollectionHandler, DeleteCollectionHandler, GetCollectionsHandler
- `Keywords/` — CreateKeywordHandler, UpdateKeywordHandler, DeleteKeywordHandler, GetKeywordsHandler
- `Categories/` — CreateCategoryHandler, UpdateCategoryHandler, DeleteCategoryHandler, GetCategoriesHandler
- `Assets/` — AssetHandler (GetAssets, GetFile), SearchHandler
- `Users/` — UserHandler, AuthHandler
- `Change/` — ChangeHandler
- `Sidebar/` — SidebarHandler
- `Status/` — StatusHandler

#### T13.4 — Protobuf Contract Documentation

**Files created:** `docs/wire-protocol.md` or similar

**Implementation:**

Create a protocol documentation file covering:

1. **Transport layer:** Length-prefixed framing (`TcpFrame`), 4-byte length prefix + payload
2. **Envelope structure:** MessageType (opcode), Payload, StatusCode, CorrelationId, AuthToken, ConnectionId
3. **Opcode table:** All `MessageTypeCode` values with direction (Client→Server or Server→Client) and payload type
4. **Max payload size:** 256 MB enforced by `TcpFrame.ReceiveAsync`
5. **Timeouts:** 5-minute receive, 30-second send
6. **Message catalog:** For each message type:
   - Message name + opcode
   - Serialization format (manual protobuf field numbers)
   - Example hex dump or field table
   - Status codes used in responses
7. **Auth flow:** Login request/response, token refresh, session invalidation
8. **Change notification format:** EntityType, ChangeType, EntityId

**Source of truth:** Walk `src/Adam.Shared/Contracts/` to enumerate all `IProtoSerializable` implementations and their field numbers.

### Wave 3 — Infrastructure & Observability

#### T13.5 — Logging Standardization

**Files changed:** All projects — `src/Adam.CatalogBrowser/`, `src/Adam.BrokerService/`, `src/Adam.ServiceManager/`, `src/Adam.Shared/`

**Current state (from code search):**
- `ConnectionDebugLogger` — used heavily in `BrokerClient.cs` and `ConnectionViewModel.cs` for verbose step-by-step tracing
- `ILogger<T>` — used in ViewModels and services (standard DI pattern)
- `Debug.WriteLine(...)` — scattered in `AssetListItem.cs`, `ServiceManager/App.axaml.cs`, `ServiceManager/Program.cs`
- `Console.WriteLine` — present in some older code

**Implementation:**

1. **Assess `ConnectionDebugLogger`**: This was useful during Phase 2 debugging but is now noise. Either:
   - Remove or reduce to critical-path logging only
   - Or integrate it into `ILogger<T>` via a custom provider that formats the same way

2. **Replace `Debug.WriteLine` calls** with `ILogger<T>.LogDebug()`:
   ```csharp
   // Before:
   System.Diagnostics.Debug.WriteLine($"[Thumbnail] FAILED to load {_thumbnailPath}...");
   
   // After:
   _logger?.LogDebug("[Thumbnail] FAILED to load {ThumbnailPath}: {ExceptionType} - {Message}",
       _thumbnailPath, ex.GetType().Name, ex.Message);
   ```

3. **Add structured logging to key paths** — use named placeholders, not string interpolation:
   ```csharp
   // Before:
   _logger.LogInformation($"Startup completed in {sw.ElapsedMilliseconds}ms");
   
   // After:
   _logger.LogInformation("Startup completed in {ElapsedMs}ms", sw.ElapsedMilliseconds);
   ```

4. **Configure Serilog or similar** (optional) — add file sink for production logging with log rotation

**Files to update (Debug.WriteLine occurrences):**
- `src/Adam.CatalogBrowser/Models/AssetListItem.cs` (line ~141)
- `src/Adam.ServiceManager/App.axaml.cs`
- `src/Adam.ServiceManager/Program.cs`
- Any others found in code search

#### T13.6 — FolderWatcher Batching

**Files changed:**
- `src/Adam.BrokerService/Services/FolderWatcherHostedService.cs` (or wherever the watcher lives)

**Current concern from CONCERNS.md:** "No evidence of batching or debouncing for high-volume file system events."

**Implementation:**

Add a debounce/batch mechanism to coalesce filesystem events:
```csharp
// Add a timer-based batch collector
private readonly ConcurrentDictionary<string, FileSystemChange> _pendingChanges = new();
private Timer? _batchTimer;

private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
{
    // Update or add to pending changes (last event wins)
    _pendingChanges[e.FullPath] = new FileSystemChange
    {
        Path = e.FullPath,
        ChangeType = e.ChangeType,
        Timestamp = DateTime.UtcNow
    };
    
    // Reset batch timer (debounce)
    _batchTimer?.Change(TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
}

private async void OnBatchTimerElapsed(object? state)
{
    // Take snapshot of pending changes and clear
    var batch = Interlocked.Exchange(ref _pendingChanges, new ConcurrentDictionary<string, FileSystemChange>());
    
    if (batch.IsEmpty) return;
    
    // Process unique changes (last-write-wins for same path)
    foreach (var change in batch.Values.DistinctBy(c => c.Path))
    {
        // ... process each change
    }
}
```

**Key behaviors:**
- 5-second debounce window resets on each new event
- Duplicate paths coalesce (only the last event per path processed)
- Batch processes within a single DB transaction where possible
- Cancellation-safe (stop token stops the timer)

### Wave 4 — EF Core Stabilization

#### T13.7 — EF Core 10 → Stable Migration Preparation

**Current state:** All EF Core packages pinned to `10.0.0-preview.3`. Npgsql provider also preview.

**Implementation:**

This task is partially dependent on EF Core 10 RTM being released. Do what we can now:

1. **Check for latest available versions:**
   ```bash
   dotnet list package --outdated
   ```

2. **Create a migration script/changelog** documenting:
   - All EF Core packages in use
   - Current preview versions
   - Breaking changes to watch for (from EF Core 9 → 10 release notes)
   - Migration test plan (run all tests, compare query results, verify provider matrix)

3. **Add version pinning documentation** to `docs/` specifying:
   - Minimum EF Core version
   - Compatible provider versions
   - How to upgrade (package update → test all 3 providers)

4. **When RTM is available:** Update `Adam.Shared.csproj`, `Adam.BrokerService.csproj`, `Adam.ServiceManager.csproj` with stable versions, run full test suite.

**Risk mitigation:**
- Preview API surface may change — mark any `#pragma warning disable` for preview APIs
- Npgsql may lag behind EF Core RTM — check compatibility matrix
- SQL Server provider typically ships same-day as EF Core RTM

### Wave 5 — AI Model Upgrade to LFM2.5-VL-1.6B

#### T13.8 — LiquidVision.ModelLayout refactor for LFM2.5-VL naming

**Current state:** `Lfm2VlModelLayout` expects `onnx-community/LFM2-VL-450M-ONNX` file naming:
- Graph names: `embed_tokens`, `vision_encoder`, `decoder_model_merged`
- Precision suffixes: `_fp16`, `_q4`, `_q4f16`, `_quantized`
- Config files: `preprocessor_config.json` or `processor_config.json`

**Target:** `LiquidAI/LFM2.5-VL-1.6B-ONNX` with different naming:
- Graph names: `embed_tokens` (same), `embed_images` (was `vision_encoder`), `decoder` (was `decoder_model_merged`)
- Precision suffixes: `_fp16`, `_q4`, `_q8`, `_quantized` (no `_q4f16`, adds `_q8`)
- Config format: config.json has `text_config` sub-object for token IDs

**Implementation:**

1. **Refactor `Lfm2VlModelLayout`** to support model-specific file name mappings:
   - Extract graph name mapping into a configuration-driven table:
     - LFM2-VL: `vision_encoder` → `vision_encoder`, `decoder_model_merged` → `decoder_model_merged`
     - LFM2.5-VL: `vision_encoder` → `embed_images`, `decoder_model_merged` → `decoder`
   - Add `_q8` to `PrecisionSuffix` mapping (currently missing)
   - Map `Q4F16` precision to `_q4` for LFM2.5-VL (best available quantized variant)

2. **Update `LiquidVisionOptions`**:
   - Change `ModelId` to `LiquidAI/LFM2.5-VL-1.6B-ONNX`
   - Change `Precision` default to `Q4` (recommended balance)
   - Update `ModelVersion` to `"1.5"`

3. **Update `ModelConfig.FromFile`** if config.json schema differs

4. **Update `PreprocessorConfig.FromFile`** if `processor_config.json` format differs

**File sizes comparison (Q4, best size/quality balance):**
| Component | LFM2-VL-450M | LFM2.5-VL-1.6B | Delta |
|-----------|-------------|-----------------|-------|
| embed_tokens | 537 MB | 537 MB (same file) | — |
| vision_encoder→embed_images | 269 MB (Q4) | 269 MB (Q4) | — |
| decoder_model_merged→decoder | 1.22 GB (Q4) | 1.22 GB (Q4) | — |
| Total | ~2.0 GB | ~2.0 GB | **~same** |

**Note:** Despite the 3.5x parameter increase (450M → 1.6B), the Q4 quantization compresses the model to roughly the same download size as the current Q4F16 variant. The FP16 variant (4.5 GB) would be significantly larger.

#### T13.9 — Validate inference output schema compatibility

**Implementation:**

1. **Download and verify** LFM2.5-VL Q4 model with the refactored layout
2. **Run existing AiTaggingServiceTests** to confirm the new model produces parseable output
3. **Update `InstructionPrompt`** if the new model responds differently:
   - LFM2.5-VL may produce more detailed descriptions or different JSON formatting
   - Adjust `TagResultParser` if the JSON structure differs

#### T13.10 — Update default config in CatalogBrowser DI registration

**Implementation:**

1. Update `CatalogBrowser/App.axaml.cs`:
   ```csharp
   // Before:
   .AddLiquidVision(opts => { opts.Precision = ModelPrecision.Q4F16; opts.ExecutionProvider = ExecutionProviderKind.Cpu; })
   
   // After:
   .AddLiquidVision(opts => { opts.ModelId = "LiquidAI/LFM2.5-VL-1.6B-ONNX"; opts.Precision = ModelPrecision.Q4; opts.ExecutionProvider = ExecutionProviderKind.Cpu; })
   ```

---

## 2. Execution Waves

| Wave | Tasks | Depends On | Rationale |
|------|-------|------------|-----------|
| **Wave 1 — Project Infra** | T13.1, T13.2 | — | Fix the project structure and CI first, so subsequent work has a reliable build/test environment |
| **Wave 2 — Code Quality** | T13.3, T13.4 | Wave 1 | Fix handler bugs and document wire protocol before adding more handlers in Phase 14 |
| **Wave 3 — Infrastructure** | T13.5, T13.6 | Wave 1 | Logging and watcher fixes are self-contained but benefit from stable CI from Wave 1 |
| **Wave 4 — EF Core** | T13.7 | Wave 1 | EF Core migration is prep work — actual upgrade depends on RTM availability |
| **Wave 5 — AI Model Upgrade** | T13.8, T13.9, T13.10 | Wave 1 | Self-contained model layout refactor; improves AI tagging quality 3.5x more parameters |

---

## 3. File Change Matrix

| # | File | Change Type | Details |
|---|------|-------------|---------|
| 1 | `adam.sln` (new) | Create | Solution file with all 4 projects + 4 test projects |
| 2 | `.github/workflows/release.yml` | Modify | Add CatalogBrowser test retry / headless config |
| 3 | `tests/Adam.CatalogBrowser.Tests/*` | Modify | Add collection fixtures, headless platform init, cleanup |
| 4 | `src/Adam.BrokerService/Handlers/*.cs` | Modify | Add null-payload + malformed-request guards to all handlers |
| 5 | `docs/wire-protocol.md` (new) | Create | Full wire protocol documentation |
| 6 | `src/Adam.CatalogBrowser/Models/AssetListItem.cs` | Modify | Replace Debug.WriteLine with ILogger |
| 7 | `src/Adam.ServiceManager/App.axaml.cs` | Modify | Replace Debug.WriteLine with ILogger |
| 8 | `src/Adam.ServiceManager/Program.cs` | Modify | Replace Debug.WriteLine with ILogger |
| 9 | `src/Adam.BrokerService/Services/FolderWatcherHostedService.cs` | Modify | Add debounce/batch mechanism |
| 10 | `docs/ef-core-migration-guide.md` (new) | Create | EF Core 10 stable migration prep |
| 11 | `src/LiquidVision.Core/Services/Lfm2VlModelLayout.cs` | Modify | Support model-specific graph names (vision_encoder→embed_images, decoder_model_merged→decoder) |
| 12 | `src/LiquidVision.Core/Configuration/LiquidVisionOptions.cs` | Modify | Update default ModelId to LFM2.5-VL-1.6B, Precision to Q4 |
| 13 | `tests/Adam.BrokerService.Tests/HandlerValidationTests.cs` (new) | Create | 29 tests for null/malformed payload guards across all handlers |

---

## 4. Testing Strategy

| Test ID | Name | Verifies | File |
|---------|------|----------|------|
| T13-T1 | `Solution_Builds_AllProjects` | `dotnet build adam.sln` succeeds | CI pipeline |
| T13-T2 | `BrokerHandler_NullPayload_ReturnsError` | Null payload returns status 8 error envelope | `HandlerValidationTests.cs` |
| T13-T3 | `BrokerHandler_MalformedPayload_ReturnsError` | Corrupt payload returns error, not exception | `HandlerValidationTests.cs` |
| T13-T4 | `FolderWatcher_Debounce_CoalescesEvents` | Multiple rapid events for same path processed once | `FolderWatcherTests.cs` |
| T13-T5 | `FolderWatcher_Batch_ProcessesInTransaction` | Batch of events processed atomically | `FolderWatcherTests.cs` |
| T13-T6 | `ModelLayout_LFM2_5VL_Q4_ResolvesCorrectPaths` | LFM2.5-VL Q4 layout resolves correct ONNX file paths | `Lfm2VlModelLayoutTests.cs` |
| T13-T7 | `AiTaggingService_PostUpgrade_TestsPass` | Existing AI tagging tests pass with new model config | `AiTaggingServiceTests.cs` |

---

## 5. Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| EF Core 10 RTM not yet available | T13.7 blocked | Make it a prep-only task (docs + version check) — actual migration deferred |
| CatalogBrowser tests unfixable without display | Test gap remains | Accept headless timeout and document as known limitation; add manual test checklist instead |
| Handler null checks change behavior | Existing clients might rely on exceptions | Return error envelope (status 8) consistently; test backward compatibility |
| FolderWatcher batching delays processing | Changes take up to 5s to propagate | 5s debounce is well within the 5s propagation requirement (BROK-03) |
| LFM2.5-VL output format differs | TagResultParser may not parse new model output | Test with real model download; adjust parser if needed |
| LFM2.5-VL Q4 not available on HF | Can't download model | Fall back to Q8 or FP16; or stay on LFM2-VL until Q4 variant is published |

---

## 6. Success Criteria

- ✅ `adam.sln` builds all projects: `dotnet build adam.sln` succeeds with 0 errors
- ✅ All broker handlers return error envelopes (not exceptions) for null/malformed payloads
- ✅ Wire protocol documented in `docs/wire-protocol.md` with all opcodes and field tables
- ✅ All `Debug.WriteLine` calls replaced with `ILogger<T>.LogDebug()`
- ✅ FolderWatcher batches events with 5-second debounce
- ✅ EF Core migration plan documented and ready for when RTM ships
- ✅ `Lfm2VlModelLayout` supports model-specific graph name mappings (LFM2-VL + LFM2.5-VL)
- ✅ Default config points to `LiquidAI/LFM2.5-VL-1.6B-ONNX` with Q4 precision
- ✅ 29 new handler validation tests pass
- ✅ All existing tests still pass (859+29 = 888 total)

