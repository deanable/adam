---
goal: Deliver v2 user-facing features — batch metadata editing, CSV/XMP import/export, activity feed, compare/loupe views, AI tag refinement.
version: 1.0
date_created: 2026-06-13
last_updated: 2026-06-13
status: 'Planned'
tags: [features, batch, csv, collaboration, ui, ai]
---

# Phase 14: Feature Growth

**Goal:** Deliver the v2 requirements defined in `REQUIREMENTS.md` — batch metadata editing (META-V2-02), CSV/XMP import/export (META-V2-03), activity feed (COLL-V2-01, COLL-V2-03), compare/loupe views (CATA-05, CATA-06), and AI tag refinement.

**Depends on:** Phase 13 (Production Hardening)

---

## 1. Tasks

### Wave 1 — Batch Operations

#### T14.1 — Batch Metadata Editing

**Requirements:** META-V2-02 — select multiple assets → edit rating, label, keywords, flag, copyright, GPS in one action.

**Files changed:**
- `src/Adam.CatalogBrowser/ViewModels/AssetGalleryViewModel.cs` — add batch edit mode
- `src/Adam.CatalogBrowser/ViewModels/MetadataEditorViewModel.cs` — add batch edit state
- `src/Adam.CatalogBrowser/Views/MetadataEditorView.axaml` — batch edit UI variant
- `src/Adam.CatalogBrowser/Views/AssetGalleryView.axaml` — batch action toolbar

**Implementation:**

**A. Gallery multi-select infrastructure (already partially exists):**
- `_selectedAssets` ObservableCollection already exists in `AssetGalleryViewModel`
- Ctrl+Click and Shift+Click for multi-select is already wired via `AssetTileControl.IsSelected`
- Need: visual multi-select overlay (checkmark badge on selected tiles), selected count label

**B. Batch edit mode:**
- When 2+ assets selected, show "Batch Edit" button in toolbar
- Clicking "Batch Edit" switches the right panel to batch mode
- In batch mode, fields show:
  - Current value (if all selected assets agree) or "(mixed)" if they differ
  - Checkbox to enable/disable each field for batch update
  - Apply button writes changes to all selected assets

**C. MetadataEditorViewModel batch state:**
```csharp
public class MetadataEditorViewModel
{
    private bool _isBatchMode;
    public bool IsBatchMode
    {
        get => _isBatchMode;
        set { _isBatchMode = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsSingleMode)); }
    }
    
    public bool IsSingleMode => !IsBatchMode;
    
    // Mixed-value indicators
    public bool IsRatingMixed { get; set; }
    public bool IsLabelMixed { get; set; }
    public bool IsFlagMixed { get; set; }
    
    // Per-field enable toggles
    public bool UpdateRating { get; set; }
    public bool UpdateLabel { get; set; }
    public bool UpdateKeywords { get; set; }
    public bool UpdateFlag { get; set; }
    public bool UpdateCopyright { get; set; }
    
    // Apply command
    public ICommand ApplyBatchEditCommand { get; }
    
    private async Task ApplyBatchEditAsync()
    {
        foreach (var asset in _selectedAssets)
        {
            // Apply only checked fields
            if (UpdateRating) asset.Rating = Rating;
            if (UpdateLabel) asset.Label = Label;
            // ... etc
        }
        await SaveAsync();
    }
}
```

**D. Batch edit view:** Variant of MetadataEditorView with:
- "(mixed)" placeholder text for fields with different values
- Checkbox column for each field to include/exclude from batch
- "Apply to N assets" button instead of single Save
- Progress indicator during save

**E. Permission gating:** Reuse `CanEditMetadata` — batch edit requires Editor+ role

**F. Multi-user support:**
- Batch save loops through each asset, sends individual update messages to broker
- Or add a new `BatchUpdateAssets` broker message type (opcode) for atomic batch update
- Broadcast single ChangeNotification per asset (or one notification for the batch)

**Tests:** 10+ tests covering:
- Batch mode activation (2+ assets selected)
- Mixed value detection
- Per-field toggle behavior
- Apply writes to all selected assets
- Permission gating
- Multi-user batch update via broker

#### T14.2 — CSV/XMP Metadata Import/Export

**Requirements:** META-V2-03 — export metadata to CSV, edit externally, reimport.

**Files changed:**
- `src/Adam.CatalogBrowser/ViewModels/ExportDialogViewModel.cs` — add CSV export option
- `src/Adam.CatalogBrowser/ViewModels/ImportViewModel.cs` (new) — CSV import logic
- `src/Adam.CatalogBrowser/Services/CsvMetadataService.cs` (new) — CSV read/write
- `src/Adam.CatalogBrowser/Views/ExportDialogView.axaml` — add CSV format option
- `src/Adam.CatalogBrowser/Views/ImportDialogView.axaml` (new) — CSV import UI
- `src/Adam.Shared/Services/XmpSidecarService.cs` — verify bulk XMP sidecar import

**Implementation:**

**A. CSV Export:**
```csharp
public class CsvMetadataService
{
    public async Task ExportToCsvAsync(List<DigitalAsset> assets, string outputPath, CancellationToken ct)
    {
        // Columns: FileName, Title, Description, Keywords (pipe-separated), Rating,
        //          Label, Flag, Copyright, GpsLatitude, GpsLongitude, CameraMake, CameraModel
        
        // CSV format with header row
        // Escape commas/quotes per RFC 4180
        // Use StreamWriter with UTF-8 BOM for Excel compatibility
    }
    
    public async Task<List<CsvRow>> ReadCsvAsync(string inputPath, CancellationToken ct)
    {
        // Parse CSV, validate columns, return list of row updates
        // Match by FileName (must be unique within the exported set)
    }
    
    public async Task<int> ImportFromCsvAsync(List<CsvRow> rows, AppDbContext db, CancellationToken ct)
    {
        // Match each row to asset by FileName
        // Apply changes (title, description, rating, label, flag, copyright, GPS)
        // Skip keywords (complex merge — warn in docs)
        // Return count of updated assets
    }
}
```

**B. CSV Import UI:**
- File picker dialog filtered to `.csv`
- Preview of first 5 rows with column mapping
- Conflict resolution options: "Overwrite", "Skip if empty", "Append keywords"
- Dry-run mode: show what would change before committing
- Progress bar for large imports (10K+ assets)

**C. XMP sidecar bundle export:**
- Export XMP sidecar files for RAW assets alongside CSV for metadata
- Option: "Include XMP sidecars" checkbox
- Sidecar files named `{filename}.xmp` in output directory

**D. Integration with existing ExportDialog:**
- Add "CSV (Metadata)" option to format dropdown alongside JPEG/TIFF
- When CSV selected, show additional options:
  - Include XMP sidecars? (for RAW assets)
  - Which fields to export (checkboxes)
  - Delimiter preference (comma/tab)

**Tests:** 8+ tests covering:
- CSV format compliance (RFC 4180)
- Round-trip (export → reimport preserves values)
- FileName matching (case-insensitive)
- Partial update (only specified columns changed)
- Large file handling (10K rows)

---

### Wave 2 — Collaboration

#### T14.3 — Activity Feed / Notification Panel

**Requirements:** COLL-V2-01, COLL-V2-03 — surface ChangeNotification events as a recent-changes panel in the UI.

**Files changed:**
- `src/Adam.CatalogBrowser/ViewModels/ActivityFeedViewModel.cs` (new)
- `src/Adam.CatalogBrowser/Views/ActivityFeedView.axaml` (new)
- `src/Adam.CatalogBrowser/ViewModels/MainWindowViewModel.cs` — wire activity feed
- `src/Adam.CatalogBrowser/Views/MainWindow.axaml` — add activity feed panel
- `src/Adam.CatalogBrowser/Models/ActivityEntry.cs` (new) — data model

**Implementation:**

**A. Data model:**
```csharp
public class ActivityEntry
{
    public string EntityType { get; set; } // "Asset", "Keyword", "Collection", "Category"
    public string ChangeType { get; set; } // "Created", "Updated", "Deleted"
    public string EntityId { get; set; }
    public string? AssetName { get; set; } // Resolved from DB for display
    public string? UserName { get; set; }  // From ChangeNotification
    public DateTime Timestamp { get; set; }
    public bool IsRead { get; set; }
}
```

**B. ActivityFeedViewModel:**
```csharp
public class ActivityFeedViewModel : INotifyPropertyChanged
{
    public ObservableCollection<ActivityEntry> Entries { get; }
    public int UnreadCount { get; set; }
    public ICommand MarkAsReadCommand { get; }
    public ICommand ClearAllCommand { get; }
    
    // In standalone mode: poll ChangePoller for recent activity
    // In multi-user mode: listen to ChangeNotification broadcasts
    
    public async Task LoadRecentActivityAsync(int maxEntries = 50)
    {
        // Query from ChangeNotification history (stored in AccessLog table)
        // Display most recent first, grouped by date
    }
    
    public void OnChangeNotification(ChangeNotification notification)
    {
        // Add to Entries and increment UnreadCount
        // Show toast notification if window is not focused
    }
}
```

**C. Activity feed panel:**
- Collapsible panel on the right side (below or alongside metadata editor)
- Or dedicated tab in the main content area
- List of activity entries with:
  - Icon per change type (➕ Created, ✏️ Updated, 🗑️ Deleted)
  - Entity name (clickable → navigates to asset)
  - User name + relative time ("2 min ago")
  - Unread indicator (bold/dot)
- "Mark all as read" button
- Filter by entity type or user

**D. ChangeNotification persistence:** Currently ChangeNotification is fire-and-forget. For activity feed, store recent notifications:
- New table `ActivityLog` or reuse `AccessLog`
- Store: EntityType, ChangeType, EntityId, UserId, Timestamp
- Prune entries older than 30 days
- Index on Timestamp for fast recent-activity queries

**Tests:** 8+ tests covering:
- Activity entry creation and display
- Unread count management
- Standalone vs multi-user mode
- ChangeNotification → ActivityEntry mapping
- Pruning old entries

---

### Wave 3 — Views & AI

#### T14.4 — Compare / Loupe View Completion

**Requirements:** CATA-05 (loupe: full-resolution pan/zoom), CATA-06 (compare: side-by-side).

**Files changed:**
- `src/Adam.CatalogBrowser/Views/LoupeView.axaml` (new)
- `src/Adam.CatalogBrowser/Views/CompareView.axaml` (new)
- `src/Adam.CatalogBrowser/ViewModels/LoupeViewModel.cs` (new)
- `src/Adam.CatalogBrowser/ViewModels/CompareViewModel.cs` (new)
- `src/Adam.CatalogBrowser/ViewModels/AssetGalleryViewModel.cs` — view mode switching
- `src/Adam.CatalogBrowser/Views/AssetGalleryView.axaml` — view mode buttons

**Implementation:**

**A. Loupe View (full-resolution pan/zoom):**
- Open from gallery by double-clicking a tile or pressing Enter
- Full-resolution image rendered (not thumbnail)
- Pan: click-and-drag when zoomed in
- Zoom: mouse wheel, Ctrl++/Ctrl+-, zoom-to-fit / 1:1 toggle
- Image info overlay: filename, resolution, file size, camera, lens
- Navigation: ← → arrows to cycle through assets in current view
- ESC or Close button to return to gallery

**B. Compare View (side-by-side):**
- Select 2 assets → "Compare" button in context menu or toolbar
- Two loupe panels side-by-side, each independently zoomable/pannable
- Synchronized scrolling/zooming option (lock icon toggle)
- Side-by-side or top/bottom layout toggle
- Match rating/label comparison overlay
- Keyboard shortcuts: Tab to focus left/right panel

**C. Implementation approach:**

For high-quality rendering, use Avalonia's `Image` control with:
```csharp
// Load full-resolution for loupe
var fullRes = await Task.Run(() => 
{
    using var stream = File.OpenRead(asset.StoragePath);
    return Bitmap.DecodeToWidth(stream, (int)screenWidth); // decode at screen resolution
});

// For pan/zoom, use ScrollViewer with image inside
// Or use RenderTransform with ScaleTransform and TranslateTransform for smooth zoom
```

**Performance considerations:**
- Decode at display resolution (not native file resolution)
- Dispose full-res bitmaps when leaving loupe view
- Preload adjacent assets for smooth ← → navigation
- Limit to images only (skip video/audio/document in loupe)

**Tests:** 5+ tests covering:
- Loupe opens with correct asset
- Zoom in/out maintains aspect ratio
- Pan constrained within image bounds
- Compare view loads two assets correctly
- Synchronized scroll mode

#### T14.5 — AI Tag Refinement

**Requirements:** Build on Phase 9's AI tagging — add confidence scores, manual override, and model management.

**Files changed:**
- `src/Adam.Shared/Services/AiTaggingService.cs` — confidence scores, model switching
- `src/Adam.CatalogBrowser/ViewModels/MetadataEditorViewModel.cs` — AI tag review UI
- `src/Adam.CatalogBrowser/Views/MetadataEditorView.axaml` — confidence indicators
- `src/LiquidVision.Core/ILiquidVisionAnalyzer.cs` — confidence API

**Implementation:**

**A. Confidence scores:**
- Modify `AiTaggingService.AnalyzeAsync` to return confidence for each keyword/category
```csharp
public class AiTagResult
{
    public List<KeywordScore> Keywords { get; set; }
    public List<CategoryScore> Categories { get; set; }
    public string? Description { get; set; }
    public double DescriptionConfidence { get; set; }
}

public class KeywordScore
{
    public string Name { get; set; }
    public double Confidence { get; set; } // 0.0 to 1.0
}
```

**B. AI tag review UI:**
- After AI tagging, show a review panel with:
  - Suggested keywords sorted by confidence (highest first)
  - Checkboxes to accept/reject each suggestion
  - Confidence indicator bar (green=high, yellow=medium, red=low)
  - Threshold slider: "Only suggest keywords above X% confidence"
- "Accept all above threshold" button
- Auto-apply keywords with confidence > 0.9 (configurable)

**C. Manual override:**
- Show a "Suggested by AI" / "Added by user" indicator on each keyword in the metadata panel
- User can remove AI-suggested keywords without affecting user-added ones
- Track provenance: `AiGenerated` boolean on Keyword/AssetKeyword relation

**D. Model switching:**
- If multiple models available, show a dropdown in AI settings:
  - "LFM2-VL (balanced)" — current default
  - "LFM2-VL (fast)" — lower precision, faster inference
  - "Custom ONNX" — user-provided model path
- Model download/swap triggered from settings

**E. Settings UI:**
- Add AI settings section in preferences dialog
  - Default confidence threshold
  - Default model selection
  - Auto-apply behavior
  - GPU/CUDA toggle (if available)

**Tests:** 10+ tests covering:
- Confidence score returned and sorted
- Threshold filtering
- Accept/reject individual keywords
- Provenance tracking (AiGenerated flag)
- Model switching

---

## 2. Execution Waves

| Wave | Tasks | Depends On | Rationale |
|------|-------|------------|-----------|
| **Wave 1 — Batch Ops** | T14.1, T14.2 | Phase 13 | Batch editing and CSV import/export are the most-requested user features, build on existing multi-select infrastructure |
| **Wave 2 — Collaboration** | T14.3 | Phase 3 (ChangeNotification), Phase 13 | Activity feed reuses existing change notification infrastructure |
| **Wave 3 — Views & AI** | T14.4, T14.5 | Phase 9 (AI tagging), Phase 12 (decode-to-size) | Loupe/compare views reuse decode-to-size from Phase 12; AI refinement builds on Phase 9 |

---

## 3. File Change Matrix

| # | File | Change Type | Details |
|---|------|-------------|---------|
| 1 | `src/Adam.CatalogBrowser/ViewModels/AssetGalleryViewModel.cs` | Modify | Batch edit mode, multi-select state |
| 2 | `src/Adam.CatalogBrowser/ViewModels/MetadataEditorViewModel.cs` | Modify | Batch edit state, mixed-value detection, ApplyBatchEditAsync |
| 3 | `src/Adam.CatalogBrowser/Views/MetadataEditorView.axaml` | Modify | Batch edit UI variant with checkboxes + "(mixed)" |
| 4 | `src/Adam.CatalogBrowser/Services/CsvMetadataService.cs` | New | CSV read/write, RFC 4180 compliance |
| 5 | `src/Adam.CatalogBrowser/ViewModels/ImportViewModel.cs` | New | CSV import with preview + dry-run |
| 6 | `src/Adam.CatalogBrowser/ViewModels/ActivityFeedViewModel.cs` | New | Activity entry management, ChangeNotification listener |
| 7 | `src/Adam.CatalogBrowser/Views/ActivityFeedView.axaml` | New | Activity feed panel |
| 8 | `src/Adam.CatalogBrowser/Models/ActivityEntry.cs` | New | Activity data model |
| 9 | `src/Adam.CatalogBrowser/Views/LoupeView.axaml` | New | Full-resolution pan/zoom view |
| 10 | `src/Adam.CatalogBrowser/ViewModels/LoupeViewModel.cs` | New | Loupe state, zoom/pan commands |
| 11 | `src/Adam.CatalogBrowser/Views/CompareView.axaml` | New | Side-by-side comparison view |
| 12 | `src/Adam.CatalogBrowser/ViewModels/CompareViewModel.cs` | New | Dual-loupe state, sync lock |
| 13 | `src/Adam.Shared/Services/AiTaggingService.cs` | Modify | Confidence scores, AiTagResult, model switching |
| 14 | `src/LiquidVision.Core/ILiquidVisionAnalyzer.cs` | Modify | Confidence API |
| 15 | `src/Adam.CatalogBrowser/Views/ExportDialogView.axaml` | Modify | Add CSV format option |

---

## 4. Testing Strategy

| Wave | Tests | Focus |
|------|-------|-------|
| **Batch Ops** | 18+ | Batch edit activation/per-field toggle/apply/mixed-value/CSV round-trip/large file |
| **Collaboration** | 8+ | Activity entry/unread/persistence/ChangeNotification integration/pruning |
| **Views & AI** | 15+ | Loupe zoom/pan/Compare sync/AI confidence/threshold/manual override/model switch |

---

## 5. Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| CSV import with 10K+ rows is slow | UI freeze during import | Stream in batches; show progress bar; run on background thread |
| LiquidVision model doesn't expose confidence scores | T14.5 blocked | Estimate confidence from model logits; or use softmax on output probabilities |
| Loupe full-res decode for 100MP images OOM | App crash | Clamp decode to max display resolution (4K = 8MP); show warning for extreme resolutions |
| Activity feed DB queries slow with 1M+ entries | Panel load time >1s | Index on Timestamp; limit to 50 entries; prune >30 days automatically |

---

## 6. Success Criteria

- ✅ Batch metadata editing works for 2-100 selected assets, all fields
- ✅ CSV export → external edit → CSV import round-trip preserves values
- ✅ Activity feed shows recent changes with unread counts and clickable entries
- ✅ Loupe view supports zoom, pan, and ← → navigation
- ✅ Compare view shows 2 assets side-by-side with lock/unlock sync
- ✅ AI tagging shows confidence scores; user can accept/reject individual suggestions
- ✅ All existing 661+ tests still pass
