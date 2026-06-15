# Phase 16 — Provenance & Trust

**Goal:** Add `IsAiGenerated` provenance flag to keywords/categories, wire through the service layer and TCP wire protocol, surface AI badge in the UI tag editor, and ensure provenance is preserved through all save/update paths.

**Depends on:** Phase 15 (Quality & Platform) ✅

**Test baseline:** 1,061 passing (0 failed, 2 Docker skipped)
**Target:** 1,090+ passing

---

## 🔍 Research Findings

### Codebase Validation

| Area | Milestone Assumption | Reality | Impact |
|------|--------------------|---------|--------|
| **Keyword model** | Add `bool IsAiGenerated` | ✅ No field exists — additive change | Trivial |
| **Category model** | Add `bool IsAiGenerated` | ✅ No field exists — additive change | Trivial |
| **KeywordService** | Add param to `AssociateKeywordsAsync` | ✅ Method exists at `KeywordService.cs:14` | Add bool param |
| **CategoryService** | Add param to `AssociateCategoriesAsync` | ✅ Method exists at `CategoryService.cs:14` | Add bool param |
| **AiTaggingService.TagAssetAsync** | Pass `isAiGenerated: true` | ✅ Calls both services at lines 186-189 | Pass the param |
| **AssetHandler.UpdateAssetAsync** | Preserve provenance | ❌ `Keywords.Clear()` + re-associate at line 321-327 loses it | Post-fix required |
| **MetadataEditorViewModel.SaveAsync** | Preserve provenance | ❌ `Keywords.Clear()` + re-associate at line 250-258 loses it | Post-fix required |
| **Wire: KeywordInfo** | Field 5 for IsAiGenerated | ✅ Field 4 is AssetCount — field 5 available | Add field |
| **Wire: CategoryInfo** | Field 5 for IsAiGenerated | ✅ Field 4 is AssetCount — field 5 available | Add field |
| **Wire: AssetDetail** | Field 27 for TagsAreAiGenerated | ✅ Fields go to 26 (Orientation) — field 27 available | Add field |
| **UI tag template** | AI badge on chips | Need to locate AXAML template | Research needed |

### Critical Architectural Insight: Provenance Preservation

Both `AssetHandler.UpdateAssetAsync` and `MetadataEditorViewModel.SaveAsync` do:
```csharp
asset.Keywords.Clear();
if (req.Tags.Count > 0)
    await new KeywordService(db).AssociateKeywordsAsync(asset, req.Tags, ct);
```

This **drops** the `IsAiGenerated` flag on re-association because:
1. `EnsureKeywordHierarchyAsync` finds-or-creates keywords
2. Newly created keywords get `IsAiGenerated = false` (default)
3. The flag from the original AI tagging is lost

**Fix strategy:** After re-association, the `MetadataEditorViewModel.SaveAsync` path needs a `HashSet<string>` tracking which tags came from AI tagging. The `AssetHandler.UpdateAssetAsync` path needs to check existing keywords' flags before clearing and re-apply them. The simplest approach: iterate through the asset's keywords after re-association and set `IsAiGenerated` for any in the AI-gen set.

### Semantic Choice: Per-Keyword vs Per-Association

The flag is placed on the **Keyword entity** (not a join table). This means if a keyword was first created by AI tagging, it shows the AI badge for **all** assets it's associated with. For v3.0 this is acceptable — manual override (per-association tracking) can be added in a future version by creating a `DigitalAssetKeyword` join entity.

---

## Tasks

### T16.1 — Data Model: Add IsAiGenerated

**Files to modify:**
- `src/Adam.Shared/Models/Keyword.cs`
- `src/Adam.Shared/Models/Category.cs`

**Changes:**
```csharp
// Keyword.cs
public bool IsAiGenerated { get; set; }

// Category.cs
public bool IsAiGenerated { get; set; }
```

- Additive change only (no migration needed — EF Core will add columns)
- Default `false` (C# `bool` default)
- No seed data changes needed
- **Tests:** 2 tests — verify default is false, verify round-trip

---

### T16.2 — Service Layer: Propagate Provenance

**Files to modify:**
- `src/Adam.Shared/Services/KeywordService.cs`
- `src/Adam.Shared/Services/CategoryService.cs`
- `src/Adam.Shared/Services/AiTaggingService.cs`

**KeywordService.cs changes:**
```csharp
public async Task AssociateKeywordsAsync(DigitalAsset asset, IEnumerable<string> keywordNames, 
    bool isAiGenerated = false, CancellationToken ct = default)
```

In `EnsureKeywordHierarchyAsync`, set `IsAiGenerated = isAiGenerated` on newly created keywords:
```csharp
if (current == null)
{
    current = new Keyword
    {
        Id = Guid.NewGuid(),
        Name = part,
        NormalizedName = normalized,
        ParentId = parent?.Id,
        IsAiGenerated = isAiGenerated  // ← new
    };
    db.Keywords.Add(current);
}
```

**CategoryService.cs changes:**
```csharp
public async Task AssociateCategoriesAsync(DigitalAsset asset, IEnumerable<string> categoryNames,
    bool isAiGenerated = false, CancellationToken ct = default)
```

Set `IsAiGenerated` on newly created categories (same pattern):
```csharp
category = new Category
{
    Id = Guid.NewGuid(),
    Name = name,
    NormalizedName = normalized,
    IsAiGenerated = isAiGenerated  // ← new
};
```

**AiTaggingService.cs changes:**
In `TagAssetAsync()` at lines 186-189, pass `isAiGenerated: true`:
```csharp
await new KeywordService(db).AssociateKeywordsAsync(asset, result.Keywords, isAiGenerated: true, ct);
// ...
await new CategoryService(db).AssociateCategoriesAsync(asset, result.Categories, isAiGenerated: true, ct);
```

**Tests:** 4-6 tests
- KeywordService: flag is false by default, true when passed, preserved on existing keyword find
- CategoryService: same pattern
- AiTaggingService: verify `TagAssetAsync` creates AI keywords with flag = true

---

### T16.3 — Wire Protocol: Transport Provenance

**Files to modify:**
- `src/Adam.Shared/Contracts/SidebarMessages.cs` (KeywordInfo, CategoryInfo)
- `src/Adam.Shared/Contracts/AssetMessages.cs` (AssetDetail)

**KeywordInfo (field 5):**
```csharp
public bool IsAiGenerated { get; set; }

public int CalculateSize() =>
    ProtoHelper.FieldSize(1, Id.ToString()) +
    ProtoHelper.FieldSize(2, Name) +
    (ParentId.HasValue ? ProtoHelper.FieldSize(3, ParentId.Value.ToString()) : 0) +
    ProtoHelper.FieldSize(4, AssetCount) +
    ProtoHelper.FieldSize(5, IsAiGenerated);  // ← new
```

**CategoryInfo (field 5):** Same pattern.

**AssetDetail (field 27 — parallel bools for Tags):**
```csharp
public List<bool> TagsAreAiGenerated { get; } = [];

// In CalculateSize/WriteTo:
size += ProtoHelper.RepeatedFieldSize(27, TagsAreAiGenerated);
ProtoHelper.WriteRepeatedField(output, 27, TagsAreAiGenerated);
```

Note: `ProtoHelper` may not have a `RepeatedFieldSize` overload for `List<bool>`. If not, implement manually:
```csharp
// In CalculateSize:
if (TagsAreAiGenerated.Count > 0)
    size += TagsAreAiGenerated.Count * ProtoHelper.FieldSize(27, false);  // 2 bytes per packed bool
```

Actually, protobuf wire format for packed repeated bool is 1 byte per element. We'll need to check `ProtoHelper` and use the right approach.

**MergeFrom for AssetDetail — field 27:**
```csharp
case 27: TagsAreAiGenerated.Add(input.ReadBool()); break;
```

**Tests:** 3-5 tests for wire serialization round-trip of provenance flag (KeywordInfo, CategoryInfo, AssetDetail)

---

### T16.4 — Broker Handler: Surface & Preserve Provenance

**Files to modify:**
- `src/Adam.BrokerService/Handlers/AssetHandler.cs`
- `src/Adam.BrokerService/Handlers/KeywordHandlers.cs` (if Keyword/Category list response needs updating)
- `src/Adam.BrokerService/Handlers/CategoryHandlers.cs` (same)

**GetAssetAsync (line 270-280):** Populate `TagsAreAiGenerated` alongside Tags:
```csharp
foreach (var kw in asset.Keywords)
{
    detail.Tags.Add(kw.Name);
    detail.TagsAreAiGenerated.Add(kw.IsAiGenerated);
}
```

**UpdateAssetAsync (lines 321-327):** Preserve provenance on re-association.
Strategy: Before clearing keywords, snapshot which ones were AI-generated:
```csharp
// Snapshot AI-generated keyword names before clearing
var aiKeywordNames = asset.Keywords
    .Where(k => k.IsAiGenerated)
    .Select(k => k.NormalizedName)
    .ToHashSet();

asset.Keywords.Clear();
if (req.Tags.Count > 0)
{
    var keywordSvc = scope.ServiceProvider.GetRequiredService<KeywordService>();
    await keywordSvc.AssociateKeywordsAsync(asset, req.Tags, ct);
    
    // Re-apply provenance for previously AI-generated keywords
    if (aiKeywordNames.Count > 0)
    {
        foreach (var kw in asset.Keywords)
        {
            if (aiKeywordNames.Contains(kw.NormalizedName))
                kw.IsAiGenerated = true;
        }
    }
}
```

**ListAssetsAsync → AssetSummary:** The `AssetSummary` currently doesn't carry keyword-level data. Provenance isn't needed at the summary level — it's only relevant on asset detail and in the keyword tree.

**Keyword/Category list handlers:** If `KeywordInfo` and `CategoryInfo` are populated from DB entities, simply pass through `IsAiGenerated`:
```csharp
// In keyword list handler response creation
new KeywordInfo
{
    Id = k.Id,
    Name = k.Name,
    ParentId = k.ParentId,
    AssetCount = k.UsageCount,
    IsAiGenerated = k.IsAiGenerated  // ← new
}
```

**Tests:** 3-5 tests
- GetAssetAsync returns TagsAreAiGenerated matching keyword flags
- UpdateAssetAsync preserves provenance after re-association
- KeywordInfo/CategoryInfo serialization round-trips with flag

---

### T16.5 — ViewModel: Provenance Tracking in Save & Review Paths

**Files to modify:**
- `src/Adam.CatalogBrowser/ViewModels/MetadataEditorViewModel.cs`

**Add field:**
```csharp
private HashSet<string> _aiGeneratedTags = new(StringComparer.OrdinalIgnoreCase);
```

**AutoTagAsync — review dialog path:** After user accepts, track accepted AI keywords:
```csharp
if (ShowAiReviewDialogAsync != null)
{
    var accepted = await ShowAiReviewDialogAsync(scoredResult);
    if (accepted == null) { StatusText = "AI tagging cancelled."; return; }
    
    // Track AI-generated tag names
    foreach (var kw in accepted.Keywords)
        _aiGeneratedTags.Add(kw.Name);
    
    // Apply to Tags collection (existing code)
    ...
}
```

**AutoTagAsync — legacy path (no review):**
```csharp
else
{
    var raw = await _aiTaggingService.AnalyzeAssetAsync(_asset.Id);
    foreach (var kw in raw.Keywords)
    {
        _aiGeneratedTags.Add(kw);
        if (!Tags.Contains(kw, StringComparer.OrdinalIgnoreCase))
            Tags.Add(kw);
    }
    ...
}
```

**SaveAsync — re-apply provenance after re-association:**
```csharp
asset.Keywords.Clear();
var tagNames = Tags.ToArray();
if (tagNames.Length > 0)
{
    await new KeywordService(db).AssociateKeywordsAsync(asset, tagNames, ct);
    
    // Re-apply provenance for AI-generated keywords
    if (_aiGeneratedTags.Count > 0)
    {
        foreach (var keyword in asset.Keywords)
        {
            if (_aiGeneratedTags.Contains(keyword.Name))
                keyword.IsAiGenerated = true;
        }
    }
}
```

**Tests:** 2-3 tests
- Auto-tag with review dialog tracks AI tags
- SaveAsync preserves provenance after re-association
- Legacy auto-tag path also tracks AI tags

---

### T16.6 — UI: AI Badge on Tag Chips

**Files to modify:**
- Locate tag chip template in AXAML (likely `MetadataEditorView.axaml` or a shared `TagEditorControl.axaml`)
- Modify the tag item DataTemplate to show an AI badge

**Design:**
- Small "AI" badge (or sparkle icon ✨) next to AI-generated tag chips
- Gray/dimmed styling for AI-only keywords vs. black for manual
- Tooltip: "Auto-generated by AI tagging"
- Batch mode: AI tags show provenance badge in aggregated tag list

**Approach:**
```xml
<!-- Within the tag item template -->
<StackPanel Orientation="Horizontal" Spacing="4">
    <TextBlock Text="{Binding Name}" />
    <Border IsVisible="{Binding IsAiGenerated}" 
            Background="#8055AA" CornerRadius="3" Padding="2,0">
        <TextBlock Text="AI" FontSize="10" Foreground="White" />
    </Border>
</StackPanel>
```

The challenge: tags in the current editor are `ObservableCollection<string>` (just strings), not `Keyword` entities. So the tag chip template binds to a string, and we don't have `IsAiGenerated` in the list.

**Solution:** Create a wrapper class or use a parallel flag list:
- Option A: Create `TagItem(string Name, bool IsAiGenerated)` wrapper class
- Option B: Keep `ObservableCollection<string>` for Tags and add a parallel `HashSet<string>` for AI-gen names. The tag chip template would need to use a converter or multi-binding to check the AI set.

Option A is cleaner. Create a simple `TagItem` wrapper:
```csharp
public sealed class TagItem : INotifyPropertyChanged
{
    public string Name { get; set; }
    public bool IsAiGenerated { get; set; }
    // PropertyChanged, etc.
}
```

But this would require changing the `Tags` property type from `ObservableCollection<string>` to `ObservableCollection<TagItem>`, which touches many bindings and the `SaveAsync` tag name extraction.

**Recommendation — Option B (simpler, lower risk):** Keep `Tags` as `ObservableCollection<string>`. Add a `ObservableCollection<bool> TagsAreAiGenerated` parallel collection. Or, even simpler: use an `IValueConverter` that takes a tag name and checks if it's in `_aiGeneratedTags`.

Actually, the simplest approach: expose `_aiGeneratedTags` as a public property `IReadOnlySet<string> AiGeneratedTags` and use a multi-binding or converter in the tag item template.

Even simpler: the tag editor control likely has an `ItemTemplate` that displays each tag. If we can add a boolean flag per tag item, we need either:
1. A composite wrapper (TagItem class)
2. A converter that takes the tag name and checks a set

Let me check what the tag editor control looks like.

Actually, let me just search for the tag editor template to see how it's rendered.

**Deferred for research:** I need to find the tag item template in the AXAML files before implementing. This will be done during execution.

**Tests:** Verify visual state through Avalonia headless tests (check class/style triggers)

---

## Wire Protocol Spec

### KeywordInfo (field 5 — bool IsAiGenerated)

| Field | Type | Number | Description |
|-------|------|--------|-------------|
| Id | string (Guid) | 1 | Keyword identifier |
| Name | string | 2 | Display name |
| ParentId | string (Guid, optional) | 3 | Parent keyword ID |
| AssetCount | int32 | 4 | Number of associated assets |
| IsAiGenerated | bool | 5 | Whether created by AI tagging |

### CategoryInfo (same schema as KeywordInfo)

| Field | Type | Number | Description |
|-------|------|--------|-------------|
| Id | string (Guid) | 1 | Category identifier |
| Name | string | 2 | Display name |
| ParentId | string (Guid, optional) | 3 | Parent category ID |
| AssetCount | int32 | 4 | Number of associated assets |
| IsAiGenerated | bool | 5 | Whether created by AI tagging |

### AssetDetail (field 27 — repeated bool TagsAreAiGenerated)

| Field | Type | Number | Description |
|-------|------|--------|-------------|
| ... | ... | 1-26 | Existing fields |
| TagsAreAiGenerated | repeated bool | 27 | Parallel to Tags (field 9) — provenance per tag |

**Breaking change note:** Adding field 27 to `AssetDetail` is backward-compatible for deserialization (new field is simply ignored by old clients). Old clients won't send provenance data but won't crash.

---

## Success Criteria

- ✅ `IsAiGenerated = true` in DB for AI-created keywords/categories
- ✅ `TagAssetAsync` sets provenance on auto-tagged keywords and categories
- ✅ Wire protocol transports provenance in KeywordInfo, CategoryInfo, and AssetDetail
- ✅ `AssetHandler.UpdateAssetAsync` preserves provenance on re-association
- ✅ `MetadataEditorViewModel.SaveAsync` preserves provenance in standalone mode
- ✅ UI shows "AI" badge on AI-generated tag chips with tooltip
- ✅ Review dialog and legacy auto-tag both set provenance
- ✅ All 1,061 existing tests still pass + 20-25 new tests pass
- ✅ Zero build warnings

---

## Execution Strategy

### Wave 1 (Parallel 🟢)
1. **T16.1** — Data model: `IsAiGenerated` on Keyword + Category
2. **T16.2** — Service layer: KeywordService + CategoryService params + AiTaggingService wiring

### Wave 2 (Parallel 🟢)
3. **T16.3** — Wire protocol: KeywordInfo, CategoryInfo, AssetDetail changes
4. **T16.4** — Broker handler: AssetHandler provenance in GetAssetAsync + UpdateAssetAsync

### Wave 3 (Parallel 🟢)
5. **T16.5** — ViewModel: `_aiGeneratedTags` tracking + SaveAsync provenance preservation
6. **T16.6** — UI: Locate tag template, add AI badge style + tooltip

### Wave 4 (Parallel 🟢)
7. **Tests** — 20-25 new tests across Shared, BrokerService, CatalogBrowser
8. **Build + full test suite** — Verify no regressions

---

## New/Modified Files Summary

| File | Status | Change |
|------|--------|--------|
| `src/Adam.Shared/Models/Keyword.cs` | Modified | +`bool IsAiGenerated` |
| `src/Adam.Shared/Models/Category.cs` | Modified | +`bool IsAiGenerated` |
| `src/Adam.Shared/Services/KeywordService.cs` | Modified | +`isAiGenerated` param on `AssociateKeywordsAsync`, set on new keywords |
| `src/Adam.Shared/Services/CategoryService.cs` | Modified | +`isAiGenerated` param on `AssociateCategoriesAsync`, set on new categories |
| `src/Adam.Shared/Services/AiTaggingService.cs` | Modified | Pass `isAiGenerated: true` in `TagAssetAsync` |
| `src/Adam.Shared/Contracts/SidebarMessages.cs` | Modified | Field 5 on KeywordInfo + CategoryInfo |
| `src/Adam.Shared/Contracts/AssetMessages.cs` | Modified | Field 27 — `TagsAreAiGenerated` on AssetDetail |
| `src/Adam.BrokerService/Handlers/AssetHandler.cs` | Modified | GetAssetAsync populates TagsAreAiGenerated; UpdateAssetAsync preserves provenance |
| `src/Adam.CatalogBrowser/ViewModels/MetadataEditorViewModel.cs` | Modified | +`_aiGeneratedTags` tracking; SaveAsync re-applies provenance |
| AXAML tag chip template (TBD) | Modified | AI badge + tooltip |

**New tests target:** ~20-25 across all test projects
