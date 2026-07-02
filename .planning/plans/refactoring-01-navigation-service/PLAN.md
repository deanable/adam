---
goal: Extract view lifecycle management from MainWindowViewModel into a reusable NavigationService.
version: 1.0
date_created: 2026-07-02
last_updated: 2026-07-02
status: 'Draft'
tags: [refactoring, navigation, mainwindow-viewmodel, service-extraction]
---

# Refactoring 01: NavigationService

## Problem

`MainWindowViewModel` (~1450 lines) manually manages the lifecycle of 7 different views through repetitive wiring in its constructor. Each modal view follows the same pattern:

```csharp
// Pattern repeated 7+ times:
var vm = new SomeViewModel(...);
vm.CloseRequested += () => {
    vm.Dispose();
    CurrentView = assetGallery;
};
await vm.LoadAsync();
CurrentView = vm;
```

This creates ~80 lines of boilerplate, couples `MainWindowViewModel` to every ViewModel's constructor, and makes the constructor hard to read.

## Solution

Extract a **`NavigationService`** class that encapsulates:
1. ViewModel creation via a factory delegate
2. `CloseRequested` event subscription
3. `Dispose` on navigation away
4. Navigation history (back stack)
5. Optional async loading before display

## Proposed Design

### New file: `src/Adam.CatalogBrowser/Services/NavigationService.cs`

```csharp
public sealed class NavigationService
{
    private readonly Stack<Func<object>> _backStack = new();
    private readonly ILogger<NavigationService>? _logger;
    private object? _currentView;
    private Action<object?>? _viewChanged;

    /// <summary>
    /// Called when the current view changes. Wire this to the ViewModel's CurrentView property.
    /// </summary>
    public Action<object?>? ViewChanged { get; set; }

    /// <summary>
    /// Navigate to a ViewModel, with optional async initialization.
    /// </summary>
    public async Task NavigateToAsync<T>(
        Func<T> createVm,
        Func<T, Task>? loadAsync = null)
        where T : class, IDisposable
    {
        var vm = createVm();
        _backStack.Push(() => _currentView!);

        if (vm is ICloseable closeable)
        {
            closeable.CloseRequested += () => GoBack();
        }

        if (loadAsync != null)
            await loadAsync(vm);

        _currentView = vm;
        ViewChanged?.Invoke(vm);
    }

    /// <summary>
    /// Navigate to a ViewModel that is already ready (e.g., gallery, settings).
    /// </summary>
    public void NavigateTo<T>(T vm) where T : class
    {
        _backStack.Push(() => _currentView!);
        _currentView = vm;
        ViewChanged?.Invoke(vm);
    }

    /// <summary>
    /// Navigate to a ViewModel with a close handler but no async load.
    /// </summary>
    public void NavigateToDisposable<T>(Func<T> createVm)
        where T : class, IDisposable
    {
        var vm = createVm();
        _backStack.Push(() => _currentView!);

        if (vm is ICloseable closeable)
        {
            closeable.CloseRequested += () => GoBack();
        }

        _currentView = vm;
        ViewChanged?.Invoke(vm);
    }

    /// <summary>
    /// Go back to the previous view.
    /// </summary>
    public void GoBack()
    {
        if (_backStack.Count == 0) return;

        // Dispose current view if it implements IDisposable
        if (_currentView is IDisposable disposable)
            disposable.Dispose();

        var previousGetter = _backStack.Pop();
        _currentView = previousGetter();
        ViewChanged?.Invoke(_currentView);
    }
}

/// <summary>
/// Interface for ViewModels that raise a CloseRequested event.
/// </summary>
public interface ICloseable
{
    event Action? CloseRequested;
}
```

### Affected ViewModels (already implement CloseRequested)

| ViewModel | Pattern | Already Has CloseRequested? |
|-----------|---------|---------------------------|
| `LoupeViewModel` | `new + OpenAsync + CloseRequested` | ✅ Yes |
| `CompareViewModel` | `new + SetLeft/Right + CloseRequested` | ✅ Yes |
| `AutoAlbumViewModel` | `new + ComputePreview + CloseRequested` | ✅ Yes |
| `DuplicateReviewViewModel` | `new + FindFor/ScanAll + CloseRequested` | ✅ Yes |
| `TrashViewModel` | `new + LoadDeletedAssets + no CloseRequested` | ❌ No |
| `FaceTaggingViewModel` | `new + LoadAsync + no CloseRequested` | ❌ No |
| `SettingsViewModel` | `GetService + inject` | ❌ No (not disposable) |

**TrashViewModel** — Needs `CloseRequested` event added and must implement `IDisposable` (or have the navigation service handle non-disposable cleanup).

**FaceTaggingViewModel** — Needs `CloseRequested` event added.

**SettingsViewModel** — Already obtained via `App.ServiceProvider.GetService<>()`. Not disposable. The navigation service should handle this as a simple `NavigateTo()`.

### Implementation Steps

| Step | Description | Effort |
|------|-------------|--------|
| 1 | Create `NavigationService.cs` and `ICloseable.cs` in `src/Adam.CatalogBrowser/Services/` | Small |
| 2 | Add `CloseRequested` event + `IDisposable` to `TrashViewModel` | Small |
| 3 | Add `CloseRequested` event to `FaceTaggingViewModel` (it already has `NavigateToPersonGallery`) | Small |
| 4 | Register `NavigationService` as singleton in DI (`App.axaml.cs`) | Small |
| 5 | Inject `NavigationService` into `MainWindowViewModel`, replace all 7 navigation patterns | Medium (~60 lines changed) |
| 6 | Wire `NavigationService.ViewChanged` to `CurrentView` setter | Tiny |
| 7 | Remove now-unnecessary `using` directives from `MainWindowViewModel` | Small |
| 8 | Verify all tests still pass | Small |

### Expected Outcome

- **~80 lines removed** from `MainWindowViewModel` constructor
- **~50 lines added** in `NavigationService`
- All navigation logic centralized in one testable service
- `MainWindowViewModel` no longer needs to know about `LoupeViewModel`, `CompareViewModel`, `AutoAlbumViewModel`, `DuplicateReviewViewModel`, `FaceTaggingViewModel` constructors

## Files Changed

| File | Change |
|------|--------|
| `src/Adam.CatalogBrowser/Services/NavigationService.cs` | **New** — Navigation service |
| `src/Adam.CatalogBrowser/Services/ICloseable.cs` | **New** — Closeable interface (or inline in NavigationService.cs) |
| `src/Adam.CatalogBrowser/ViewModels/TrashViewModel.cs` | Add `CloseRequested` event + `IDisposable` |
| `src/Adam.CatalogBrowser/ViewModels/FaceTaggingViewModel.cs` | Add `CloseRequested` event |
| `src/Adam.CatalogBrowser/ViewModels/MainWindowViewModel.cs` | Replace 7 view-lifecycle blocks with `NavigationService` calls |
| `src/Adam.CatalogBrowser/App.axaml.cs` | Register `NavigationService` in DI |

## Testing

After implementation, run:
```bash
dotnet test tests/Adam.CatalogBrowser.Tests
```

Key tests to verify:
- Loupe view opens and closes correctly
- Compare view opens and closes correctly
- Auto-album view navigation
- Duplicate review navigation
- Trash view navigation
- Back-stack behavior (navigating back returns to gallery)

## Modularity Assessment Findings — Cross-Reference

This refactoring contributes directly to resolving the **MainWindowViewModel megalith** identified in the codebase modularity audit:

| Metric | Before | After |
|--------|--------|-------|
| MainWindowViewModel lines | 2,040 | ~1,960 |
| Navigation concern locations | 7 (scattered in constructor) | 1 (NavigationService) |
| Constructor complexity | High (14 event subscriptions) | Medium (3-4 NavigationService calls) |

**Broader modularity context:** This is one of 4 planned extractions from MainWindowViewModel (alongside BulkAssetOperationService, PanelStateService, and LoadPageAsync decomposition). Together they reduce the ViewModel from 2,040 to ~1,000 lines and extract 3 independently testable services.

**Related plans:**
- `refactoring-02-bulk-operation-service/PLAN.md` — Removes bulk operation methods (~200 lines)
- `refactoring-03-panel-state-service/PLAN.md` — Removes panel state persistence (~150 lines)

## Risk Assessment

- **Low**: The `NavigationService` is an additive change — it wraps existing behavior without modifying any view's internal logic
- **Low**: No changes to the View code-behind or XAML
- **Medium**: `TrashViewModel` and `FaceTaggingViewModel` need small interface additions, but these are backward-compatible
