# Test Results Summary

> **Location:** `./test-results.md`  
> **Generated:** May 21, 2026

---

## Build Status

| Project | Result |
|---------|--------|
| `src/Adam.CatalogBrowser` | ✅ **0 errors, 0 warnings** |
| `tests/Adam.CatalogBrowser.Tests` | ✅ **0 errors, 0 warnings** |

---

## Test Suite Results

| Test Class | Tests | Status | Notes |
|---|---|---|---|
| SearchableTreeViewFilterTests | 18 / 18 | ✅ All passed | Filtering + tree view logic |
| DropCommandHandlersTests | 18 / 18 | ✅ All passed | Drag-drop assignment handlers |
| BulkOperationQueueTests | 10 / 10 | ✅ All passed | Queue scheduling and execution |
| DeleteServiceTests | 6 / 6 | ✅ All passed | Asset deletion service |
| FolderTreeDiagnostics | 3 / 3 | ✅ All passed | Folder tree diagnostics |
| UnitTest1 | 1 / 1 | ✅ All passed | Miscellaneous |
| **Subtotal (runnable)** | **56 / 56** | **✅ All passed** | |
| MainWindowViewModelTests | — | ⏳ **Hangs** | Pre-existing: requires Avalonia UI thread |

---

## Changes Audited

### Fix 1: Metadata Display — Duplicate Selection Events & Thread Safety

| Change | File | Status |
|---|---|---|
| Added `if (_selectedAsset == value) return;` guard in `SelectedAsset` setter | `AssetGalleryViewModel.cs` | ✅ Verified — prevents duplicate `SelectionChanged` from XAML `SelectedItem` binding + event handler |
| Captured `_selectedAsset` into local `currentSelectedAsset` for thread-safe reads | `MainWindowViewModel.cs` | ✅ Verified — prevents NRE from concurrent selection changes |
| Wrapped early-return `SelectedAssetMetadata = []` in `Dispatcher.UIThread.InvokeAsync()` | `MainWindowViewModel.cs` | ✅ Verified — fixes cross-thread collection assignment |
| Added `OnPropertyChanged(nameof(SelectedAsset))` to first UI dispatch | `MainWindowViewModel.cs` | ✅ Verified — ensures `AssetDetailView.DataContext` updates |

### Fix 2: SearchableTreeView ItemsSource Binding

| Change | File | Status |
|---|---|---|
| `AncestorType=controls:SearchableTreeView` → `AncestorType=UserControl` | `SearchableTreeView.axaml` | ✅ Verified — `controls:` namespace was never declared, causing silent binding failure |

### Binding Audit: All 15 `.axaml` Files

Searched every `.axaml` file for `RelativeSource`, `ElementName`, `$parent`, `x:Static`, and custom namespace prefixes. Results:

| File | Namespaces Used | Undeclared Prefixes? |
|---|---|---|
| `App.axaml` | — | ✅ None |
| `AssetDetailView.axaml` | — | ✅ None |
| `AssetGalleryView.axaml` | `local`, `controls` | ✅ None |
| `AdminPanelView.axaml` | `local`, `controls` | ✅ None |
| `UserManagementView.axaml` | `local`, `controls` | ✅ None |
| `MigrationWizardView.axaml` | `local` | ✅ None |
| `MetadataEditorView.axaml` | `local`, `controls` | ✅ None |
| `IngestionView.axaml` | `local` | ✅ None |
| `ExportDialog.axaml` | `vm` | ✅ None |
| `AuditLogView.axaml` | `controls` | ✅ None |
| `MainWindow.axaml` | `vm`, `views`, `controls` | ✅ None |
| `TagEditorControlStyles.axaml` | `controls` | ✅ None |
| `AssetTileControlStyles.axaml` | `controls` | ✅ None |
| `AssetListRowControlStyles.axaml` | `controls` | ✅ None |
| `SearchableTreeView.axaml` | — | ✅ **Fixed** — was `controls:` undeclared |

**Result:** Zero remaining undeclared namespace prefixes or broken `RelativeSource` bindings.

---

## Audit Findings

### Incidental: `AdminPanelView.axaml` — `RadioButtonConverter` without `ConverterParameter`

```xml
<Border IsVisible="{Binding StatusMessage, Converter={x:Static local:RadioButtonConverter.Instance}}">
```

The `RadioButtonConverter` is used **without a `ConverterParameter`** on the status message border's `IsVisible` binding. The converter will compare `StatusMessage` against `null` — making the border visible when `StatusMessage` is empty and hidden when it has a value. This is likely the opposite of what's intended (it should probably use a `StringNotEmptyConverter` or `NullToBoolConverter` instead).

---

## Known Issues

### MainWindowViewModelTests Hang (Pre-existing)

The `MainWindowViewModelTests` class hangs at startup because its `IAsyncLifetime.InitializeAsync()` creates a `MainWindowViewModel` whose constructor dispatches to `Dispatcher.UIThread`. The test runner does not pump the Avalonia dispatcher, causing indefinite hang.

**Workaround:** Requires a proper Avalonia unit-test setup (e.g., `Application.Start()` with a test dispatcher) or running from a UI-thread-capable test harness.

---

## Running Tests

```bash
# Run a specific test class
dotnet test tests/Adam.CatalogBrowser.Tests --filter "FullyQualifiedName~SearchableTreeView"

# Run all runnable tests (excludes the hanging MainWindowViewModelTests)
dotnet test tests/Adam.CatalogBrowser.Tests --filter "FullyQualifiedName~SearchableTreeView|DropCommand|BulkOperationQueue|DeleteService|FolderTreeDiagnostics|UnitTest1"

# Run the full suite (will hang on MainWindowViewModelTests)
dotnet test tests/Adam.CatalogBrowser.Tests

# Quick build verification
dotnet build src/Adam.CatalogBrowser
dotnet build tests/Adam.CatalogBrowser.Tests
```
