---
goal: Complete sidebar tree CRUD, inline rename, XAML ContextFlyout context menus, broker-side handlers for multi-user, and filter integration.
version: 2.1
date_created: 2026-06-13
last_updated: 2026-06-13
status: 'Planned'
tags: [sidebar, crud, tree, ui, broker, context-menu]
---

# Phase 10: Sidebar CRUD & Tree Interaction

![Status: Planned](https://img.shields.io/badge/status-Planned-blue)

Complete the sidebar tree interaction model — create, rename, and delete operations for Collections, Keywords, and Categories via **XAML ContextFlyout context menus** with inline rename support. Add folder context menus, broker-side handlers for multi-user mode, and explicit "Filter by this" / "Clear filter" context menu entries to replace implicit-on-selection filtering.

**Depends on:** Phases 1–9 (v1.0 codebase), Phase 7 (permission infrastructure)

---

## 1. Requirements & Constraints

- **UI-V2-01**: Users can create, rename, and delete Collections, Keywords, and Categories from the sidebar tree via **XAML ContextFlyout** context menus
- **UI-V2-02**: Inline rename via **double-click or F2** on a tree node enters edit mode (TextBox replaces TextBlock, Enter commits, Escape cancels)
- **UI-V2-03**: Folder nodes have a context menu with "Reveal in Explorer" and "Re-scan Folder" options
- **UI-V2-04**: All tree nodes show "Filter by this" / "Clear filter" entries in their context menu (replacing implicit-on-selection)
- **UI-V2-05**: CRUD operations are **permission-gated** — only Editor/Admin roles can create/rename/delete (reuses `CanEditMetadata`/`CanCreateMetadata` from Phase 7)
- **UI-V2-06**: Multi-user mode: CRUD operations go through broker protocol handlers with ChangeNotification broadcast
- **UI-V2-07**: Deleting a parent node with children **cascade-deletes all descendants** recursively in a single transaction
- **UI-V2-08**: Visible filter state indicator on tree nodes that are actively filtering the gallery

---

## 2. Codebase Analysis (pre-execution)

### 2.1 Tree Node Models

All in `src/Adam.CatalogBrowser/ViewModels/SidebarViewModel.cs` (bottom of file):

**`CollectionNode`** — Fields: `Id`, `ParentId`, `Name`, `AssetCount`, `IsEditing`, `EditName`, `Children`. Methods: `BeginRename()`, `CommitRename()`, `CancelRename()`. Already has inline rename support.

**`KeywordNode`** — Fields: `KeywordId`, `Name`, `Path`, `AssetCount`, `IsEditing`, `EditName`, `IsExpanded`, `IsSelected`, `Children`. Methods: `BeginRename()`, `CommitRename()`, `CancelRename()`. Already has inline rename support.

**`CategoryNode`** — Fields: `CategoryId`, `Name`, `Count`, `IsEditing`, `EditName`, `IsExpanded`, `IsSelected`, `Children`. Methods: `BeginRename()`, `CommitRename()`, `CancelRename()`. Already has inline rename support.

**`FolderNode`** — Fields: `Name`, `Path`, `AssetCount`, `IsExpanded`, `IsSelected`, `Children`. No inline rename (folders are read-only).

**`DateTakenNode`** — Fields: `Name`, `Year`, `Month`, `AssetCount`, `IsExpanded`, `IsSelected`, `Children`. No inline rename.

### 2.2 SidebarViewModel (existing)

File: `src/Adam.CatalogBrowser/ViewModels/SidebarViewModel.cs`

Commands that exist:
- `CreateCollectionCommand`, `RenameCollectionCommand`, `DeleteCollectionCommand`
- `CreateKeywordCommand`, `RenameKeywordCommand`, `DeleteKeywordCommand`
- `CreateCategoryCommand`, `RenameCategoryCommand`, `DeleteCategoryCommand`
- `ShowKeywordMenuCommand`, `ShowCategoryMenuCommand`, `ShowFolderMenuCommand`
- `CommitRenameCommand`
- `RevealFolderCommand`, `RescanFolderCommand`

CRUD methods that exist (fully implemented for standalone):
- `PromptCreateCollectionAsync()`, `PromptRenameCollectionAsync()`, `PromptDeleteCollectionAsync()`
- `PromptCreateKeywordAsync()`, `PromptRenameKeywordAsync()`, `PromptDeleteKeywordAsync()`
- `PromptCreateCategoryAsync()`, `PromptRenameCategoryAsync()`, `PromptDeleteCategoryAsync()`

Permission gating exists:
- `CanEditMetadata` (bool property), `CanCreateMetadata` (bool property)
- `RefreshPermissions()` — raises PropertyChanged and re-evaluates all command CanExecute

Broker communication helpers:
- `SendBrokerRequestAsync<T>(request, messageType, ct)` — sends request to broker, returns Envelope or null
- `PersistKeywordRenameAsync(kw)`, `PersistCategoryRenameAsync(cat)`, `PersistCollectionRenameAsync(col)` — handle rename persistence (both standalone and multi-user)

### 2.3 SearchableTreeView (existing)

File: `src/Adam.CatalogBrowser/Controls/SearchableTreeView.axaml.cs`

Properties:
- `ItemsSource` (IEnumerable)
- `SelectedItem` (object, TwoWay)
- `DropCommand` (ICommand)
- `NodeContextMenuCommand` (ICommand) — receives right-clicked node as parameter
- `RenameCompletedCommand` (ICommand) — receives node after Enter in rename TextBox
- `CountLabel` (string)

Events: DragOver, DragLeave, Drop, ContextRequested (both TreeView and FlatList), PointerPressed (double-click for inline rename), KeyDown (Enter/Escape for rename commit/cancel)

Methods: `FocusSearch()`, `ClearDropHighlight()`, `ReadDropText()`, `FlattenAndFilter()`

File: `src/Adam.CatalogBrowser/Controls/SearchableTreeView.axaml`
- TreeView with TreeDataTemplate: Grid with TextBlock (Name), TextBox (EditName, IsEditing binding), TextBlock (Count)
- Search TextBox at top
- Flat ListBox for search results

### 2.4 Broker Handlers (existing)

Files found in `src/Adam.BrokerService/Handlers/`:
- `Collections/GetCollectionsHandler.cs`, `CreateCollectionHandler.cs`, `UpdateCollectionHandler.cs`, `DeleteCollectionHandler.cs`
- `Keywords/GetKeywordsHandler.cs`, `CreateKeywordHandler.cs`, `UpdateKeywordHandler.cs`, `DeleteKeywordHandler.cs`
- `Categories/GetCategoriesHandler.cs`, `CreateCategoryHandler.cs`, `UpdateCategoryHandler.cs`, `DeleteCategoryHandler.cs`

Protobuf contracts likely in `src/Adam.Shared/Contracts/`:
- `CollectionMessages.cs` (contains `CollectionNode`)
- Similar Keyword/Category message files

### 2.5 Existing Test Files

- `tests/Adam.CatalogBrowser.Tests/Controls/SearchableTreeViewItemsSourceTests.cs`
- `tests/Adam.CatalogBrowser.Tests/Controls/SearchableTreeViewHeadlessIntegrationTests.cs`
- `tests/Adam.CatalogBrowser.Tests/Controls/SearchableTreeViewFilterTests.cs`
- `tests/Adam.CatalogBrowser.Tests/Controls/SearchableTreeViewDropTests.cs`
- `tests/Adam.CatalogBrowser.Tests/ViewModels/DropCommandHandlersTests.cs` (uses KeywordNode, CategoryNode)
- `tests/Adam.CatalogBrowser.Tests/ViewModels/MainWindowViewModelPermissionTests.cs` (uses SidebarViewModel)
- `tests/Adam.CatalogBrowser.Tests/ViewModels/MainWindowViewModelTests.cs`

---

## 3. Ultra-Detailed Implementation Steps

### Wave 1: Client-Side CRUD (Standalone Mode)

#### T10.1 — XAML ContextFlyout Context Menus

**Files changed:**
- `src/Adam.CatalogBrowser/Controls/SearchableTreeView.axaml` — add ContextFlyout definitions
- `src/Adam.CatalogBrowser/ViewModels/SidebarViewModel.cs` — update Show*MenuCommand handlers
- `src/Adam.CatalogBrowser/Controls/SearchableTreeView.axaml.cs` — remove code-behind context menu if replaced

**Detailed changes:**

**A. `SearchableTreeView.axaml` — Add ContextFlyout to TreeView template:**

In the TreeView's `TreeDataTemplate`, wrap the inner Grid in a `Panel` with a `ContextFlyout`:

```xml
<TreeView.ItemTemplate>
  <TreeDataTemplate ItemsSource="{Binding Children}">
    <Panel>
      <Panel.ContextFlyout>
        <MenuFlyout x:DataType="vm:INotifyPropertyChanged"
                    x:CompileBindings="True">
          <!-- Keywords tree: Create, Rename, Delete, Filter, Separator -->
          <!-- The flyout items use x:DataType to be determined at runtime;
               but since we can't use multiple x:DataType on one flyout,
               we use a single code-behind approach for populating the flyout. -->
        </MenuFlyout>
      </Panel.ContextFlyout>
      <Grid ColumnDefinitions="*,Auto">
        <!-- ... existing Name/EditName/Count content ... -->
      </Grid>
    </Panel>
  </TreeDataTemplate>
</TreeView>
```

**Approach decision (D10.1):** Since we can't use discriminated `x:DataType` on a single MenuFlyout for four different node types, we use the **`ContextFlyout` property with dynamic population**:

1. Define a handler on `SearchableTreeView` that populates the ContextFlyout when requested
2. The `ContextRequested` event fires → determine node type → build appropriate MenuFlyout with Create/Rename/Delete commands bound from `NodeContextMenuCommand`'s parameter or from DataContext

**B. `SearchableTreeView.axaml.cs` — Dynamic ContextFlyout population:**

Add method:
```csharp
/// <summary>
/// Populates a ContextFlyout on a tree node based on its type.
/// Called from ContextRequested handler when NodeContextMenuCommand doesn't suffice
/// for XAML-defined flyouts with polymorphic content.
/// </summary>
private void PopulateNodeContextFlyout(object sender, ContextRequestedEventArgs e)
{
    if (sender is not Control control || control.DataContext == null) return;
    var node = control.DataContext;
    var vm = DataContext as SidebarViewModel;
    if (vm == null) return;

    var flyout = new MenuFlyout();

    switch (node)
    {
        case KeywordNode kw:
            AddCrudMenuItems(flyout, vm.CreateKeywordCommand, vm.RenameKeywordCommand, vm.DeleteKeywordCommand, kw, "New keyword", "Rename", "Delete");
            AddFilterMenuItems(flyout, vm, "Filter by keyword", "Clear keyword filter");
            break;
        case CategoryNode cat:
            AddCrudMenuItems(flyout, vm.CreateCategoryCommand, vm.RenameCategoryCommand, vm.DeleteCategoryCommand, cat, "New category", "Rename", "Delete");
            AddFilterMenuItems(flyout, vm, "Filter by category", "Clear category filter");
            break;
        case CollectionNode col:
            AddCrudMenuItems(flyout, vm.CreateCollectionCommand, vm.RenameCollectionCommand, vm.DeleteCollectionCommand, col, "New collection", "Rename", "Delete");
            AddFilterMenuItems(flyout, vm, "Filter by collection", "Clear collection filter");
            break;
        case FolderNode folder:
            AddFolderMenuItems(flyout, vm.RevealFolderCommand, vm.RescanFolderCommand, folder);
            AddFilterMenuItems(flyout, vm, "Filter by folder", "Clear folder filter");
            break;
    }

    flyout.Items.Add(new Separator());
    flyout.Items.Add(new MenuItem
    {
        Header = "Filter by this",
        Command = vm.FilterByThisCommand,
        CommandParameter = node
    });
    flyout.Items.Add(new MenuItem
    {
        Header = "Clear filter",
        Command = vm.ClearFilterCommand,
        CommandParameter = node
    });

    control.ContextFlyout = flyout;
    // Don't set e.Handled — let the default ContextRequested behavior show the flyout
}
```

Where the helper methods are:
```csharp
private static void AddCrudMenuItems(MenuFlyout flyout, ICommand createCmd, ICommand renameCmd,
    ICommand deleteCmd, object node, string createHdr, string renameHdr, string deleteHdr)
{
    flyout.Items.Add(new MenuItem { Header = createHdr, Command = createCmd, CommandParameter = node });
    flyout.Items.Add(new MenuItem { Header = renameHdr, Command = renameCmd, CommandParameter = node });
    flyout.Items.Add(new Separator());
    flyout.Items.Add(new MenuItem { Header = deleteHdr, Command = deleteCmd, CommandParameter = node, InputGesture = new KeyGesture(Key.Delete) });
}

private static void AddFolderMenuItems(MenuFlyout flyout, ICommand revealCmd, ICommand rescanCmd, object node)
{
    flyout.Items.Add(new MenuItem { Header = "Reveal in Explorer", Command = revealCmd, CommandParameter = node });
    flyout.Items.Add(new MenuItem { Header = "Re-scan Folder", Command = rescanCmd, CommandParameter = node });
    flyout.Items.Add(new Separator());
}
```

**C. `SidebarViewModel.cs` — Add filter commands:**

Add new commands:
```csharp
public ICommand FilterByThisCommand { get; }
public ICommand ClearFilterCommand { get; }
```

Initialize in constructor:
```csharp
FilterByThisCommand = new RelayCommand(OnFilterByThis);
ClearFilterCommand = new RelayCommand(OnClearFilter);
```

Implement:
```csharp
private void OnFilterByThis(object? parameter)
{
    // Set the appropriate Selected* property based on node type
    switch (parameter)
    {
        case KeywordNode kw:
            SelectedKeyword = kw;
            break;
        case CategoryNode cat:
            SelectedMetadataCategory = cat;
            break;
        case CollectionNode col:
            SelectedCollection = col;
            break;
        case FolderNode folder:
            SelectedFolder = folder;
            break;
    }
    // FilterChanged is fired by the setter
}

private void OnClearFilter(object? parameter)
{
    switch (parameter)
    {
        case KeywordNode:
            SelectedKeyword = null;
            break;
        case CategoryNode:
            SelectedMetadataCategory = null;
            break;
        case CollectionNode:
            SelectedCollection = null;
            break;
        case FolderNode:
            SelectedFolder = null;
            break;
    }
}
```

**D. Remove old Show*ContextMenu methods** (if replacing code-behind approach):
- Remove `ShowKeywordMenuCommand`, `ShowCategoryMenuCommand`, `ShowFolderMenuCommand` wired to NodeContextMenuCommand
- Replace with the inline ContextFlyout approach in the AXAML template

#### T10.2 — Inline Rename (Partially Complete)

**Already implemented** (verified in working tree):
1. `SearchableTreeView.axaml` — TextBox with `IsVisible="{Binding IsEditing}"` in TreeView template ✅
2. `SearchableTreeView.axaml.cs` — `RenameCompletedCommandProperty` (DependencyProperty) ✅
3. `SearchableTreeView.axaml.cs` — `OnTreeViewPointerPressed` (double-click → BeginRename + focus TextBox) ✅
4. `SearchableTreeView.axaml.cs` — `OnTreeViewKeyDown` (Enter → RenameCompletedCommand, Escape → CancelRename) ✅
5. `SidebarViewModel.cs` — `CommitRenameCommand` → `CommitNodeRename()` ✅
6. `SidebarViewModel.cs` — `PersistKeywordRenameAsync`, `PersistCategoryRenameAsync`, `PersistCollectionRenameAsync` ✅
7. `CollectionNode.BeginRename()` / `CommitRename()` / `CancelRename()` ✅
8. `KeywordNode.BeginRename()` / `CommitRename()` / `CancelRename()` ✅
9. `CategoryNode.BeginRename()` / `CommitRename()` / `CancelRename()` ✅

**Remaining work:**

**A. Wire RenameCompletedCommand in MainWindow.axaml.cs:**

In `MainWindow.axaml.cs`, find the Keyword/Category/Collection SearchableTreeView instances and wire:
```csharp
// Already partially done per git diff:
// Line 61: // T10.2: Wire rename completed command for SearchableTreeView instances
```

Wire in code-behind:
```csharp
var kwTree = this.FindControl<SearchableTreeView>("KeywordsTree");
if (kwTree != null)
    kwTree.RenameCompletedCommand = vm.Sidebar.CommitRenameCommand;

var catTree = this.FindControl<SearchableTreeView>("CategoriesTree");
if (catTree != null)
    catTree.RenameCompletedCommand = vm.Sidebar.CommitRenameCommand;

var colTree = this.FindControl<SearchableTreeView>("CollectionsTree");
if (colTree != null)
    colTree.RenameCompletedCommand = vm.Sidebar.CommitRenameCommand;
```

**B. Add F2 keyboard shortcut for inline rename:**

In `MainWindow.axaml`, add KeyBinding:
```xml
<KeyBinding Gesture="F2" Command="{Binding Sidebar.F2RenameCommand}" />
```

In `SidebarViewModel.cs`, add:
```csharp
public ICommand F2RenameCommand { get; }
```

Initialize:
```csharp
F2RenameCommand = new RelayCommand(_ => BeginRenameSelectedNode(), _ => SelectedKeyword != null || SelectedCollection != null || SelectedMetadataCategory != null);
```

Implement:
```csharp
private void BeginRenameSelectedNode()
{
    var node = (object?)SelectedKeyword ?? SelectedCollection ?? SelectedMetadataCategory;
    if (node == null) return;

    var beginMethod = node.GetType().GetMethod("BeginRename",
        BindingFlags.Instance | BindingFlags.Public);
    beginMethod?.Invoke(node, null);
    OnPropertyChanged(nameof(Keywords));
    OnPropertyChanged(nameof(Collections));
    OnPropertyChanged(nameof(MetadataCategories));
}
```

#### T10.3 — Permission Gating (Already Complete)

**Already implemented:**
1. `SidebarViewModel.CanEditMetadata` ✅ — checks `_modeManager.IsStandalone || EvaluatePermission("asset:update")`
2. `SidebarViewModel.CanCreateMetadata` ✅ — checks `_modeManager.IsStandalone || EvaluatePermission("collection:create") || EvaluatePermission("asset:create")`
3. `SidebarViewModel.RefreshPermissions()` ✅ — fires PropertyChanged + raises all command CanExecuteChanged
4. Called from `MainWindowViewModel.RefreshPermissionsAsync()` ✅

**Remaining work:**
- Ensure ContextFlyout menu items respect `CanEditMetadata`/`CanCreateMetadata` in their `IsEnabled` binding
- Add tooltips to disabled menu items explaining why

#### T10.4 — Cascade Delete with Confirmation

**Files changed:**
- `src/Adam.CatalogBrowser/ViewModels/SidebarViewModel.cs` — update `PromptDeleteKeywordAsync`, `PromptDeleteCollectionAsync`, `PromptDeleteCategoryAsync`

**Detailed implementation:**

**A. `PromptDeleteKeywordAsync` — add cascade delete:**

```csharp
private async Task PromptDeleteKeywordAsync()
{
    if (SelectedKeyword == null) return;

    var owner = GetOwnerWindow();
    if (owner == null) return;

    // Count descendants for confirmation message
    var descendantCount = CountDescendantKeywords(SelectedKeyword);
    var message = descendantCount > 0
        ? $"Are you sure you want to delete '{SelectedKeyword.Name}' and all {descendantCount} sub-keywords?\n\n" +
          $"The keyword(s) will be removed from all assets. This action cannot be undone."
        : $"Are you sure you want to delete '{SelectedKeyword.Name}'?\n\n" +
          $"The keyword will be removed from all assets. This action cannot be undone.";

    var confirmed = await Views.ConfirmationDialog.ShowAsync(owner, "Delete Keyword",
        message, "Delete", "Cancel", isDestructive: true);
    if (!confirmed) return;

    try
    {
        // Collect all descendant IDs recursively
        var allIds = new List<Guid> { SelectedKeyword.KeywordId };
        CollectDescendantKeywordIds(SelectedKeyword, allIds);

        if (_modeManager.IsMultiUser)
        {
            var resp = await SendBrokerRequestAsync(
                new DeleteKeywordRequest { Id = SelectedKeyword.KeywordId.ToString(), CascadeChildren = true },
                MessageTypeCode.DeleteKeywordRequest);

            // Broker handler will recursively delete all descendants
            if (resp == null || resp.StatusCode != 0) return;
        }
        else
        {
            await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);
            var keywords = await db.Keywords
                .Where(k => allIds.Contains(k.Id))
                .ToListAsync().ConfigureAwait(false);
            db.Keywords.RemoveRange(keywords);
            await db.SaveChangesAsync().ConfigureAwait(false);
        }

        SelectedKeyword = null;
        await LoadAsync();
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to delete keyword (cascade)");
    }
}
```

Helper methods:
```csharp
private static int CountDescendantKeywords(KeywordNode node)
{
    var count = 0;
    foreach (var child in node.Children)
    {
        count += 1 + CountDescendantKeywords(child);
    }
    return count;
}

private static void CollectDescendantKeywordIds(KeywordNode node, List<Guid> ids)
{
    foreach (var child in node.Children)
    {
        ids.Add(child.KeywordId);
        CollectDescendantKeywordIds(child, ids);
    }
}
```

**B. Same pattern for `PromptDeleteCategoryAsync` and `PromptDeleteCollectionAsync`** — collections cascade-delete child collections (not the assets, just the collection membership).

**C. Broker handler update:** Update `DeleteKeywordHandler` (and Category/Collection equivalents) to handle `CascadeChildren` flag. When true, recursively resolve all descendant IDs and delete them all in a single transaction.

#### T10.5 — Folder Context Menu

**Already implemented:**
1. `SidebarViewModel.RevealFolderCommand` → `RevealFolder()` ✅ — opens folder in Explorer/File manager
2. `SidebarViewModel.RescanFolderCommand` → `RescanFolderAsync()` ✅ — currently just reloads sidebar (placeholder)

**Remaining work:**

**A. Wire the folder context menu** — already handled by T10.1's dynamic ContextFlyout population (AddFolderMenuItems).

**B. Improve `RescanFolderAsync`:**

```csharp
private async Task RescanFolderAsync()
{
    if (SelectedFolder == null || string.IsNullOrEmpty(SelectedFolder.Path)) return;

    try
    {
        _logger.LogInformation("Re-scan requested for folder: {Path}", SelectedFolder.Path);
        
        // For standalone: trigger re-ingest of the folder path
        if (_modeManager.IsStandalone)
        {
            // Use the ingestion service to re-scan
            var vm = App.ServiceProvider?.GetService<IngestionViewModel>();
            if (vm != null)
            {
                // Add the folder to watched paths and trigger scan
                // Implementation TBD: wire to existing FolderWatcherHostedService
                ToastService?.Show($"Re-scanning: {SelectedFolder.Path}", ToastLevel.Info);
            }
        }
        
        await LoadAsync();
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to rescan folder: {Path}", SelectedFolder.Path);
    }
}
```

Note: Requires `ToastService` dependency — inject via constructor or resolve from service provider.

#### T10.13 — Visual Filter State

**New task (not in original plan):**

**Files changed:**
- `src/Adam.CatalogBrowser/ViewModels/SidebarViewModel.cs`
- `src/Adam.CatalogBrowser/Controls/SearchableTreeView.axaml`

**Implementation:**

A. Add `IsActiveFilter` property to tree node models:
```csharp
// In KeywordNode class:
private bool _isActiveFilter;
public bool IsActiveFilter
{
    get => _isActiveFilter;
    set { _isActiveFilter = value; OnPropertyChanged(); }
}

// In CategoryNode, CollectionNode, FolderNode — same pattern
```

B. In `SidebarViewModel.OnFilterChanged()`:
```csharp
private void OnFilterChanged()
{
    // Clear previous filter states
    ClearFilterStates();
    
    // Set active filter on selected node
    if (SelectedKeyword != null) SelectedKeyword.IsActiveFilter = true;
    else if (SelectedMetadataCategory != null) SelectedMetadataCategory.IsActiveFilter = true;
    else if (SelectedCollection != null) SelectedCollection.IsActiveFilter = true;
    else if (SelectedFolder != null) SelectedFolder.IsActiveFilter = true;
    
    FilterChanged?.Invoke();
}

private void ClearFilterStates()
{
    void ClearRecursive(IEnumerable children)
    {
        foreach (var child in children)
        {
            if (child is KeywordNode kw)
            {
                kw.IsActiveFilter = false;
                ClearRecursive(kw.Children);
            }
            else if (child is CategoryNode cat)
            {
                cat.IsActiveFilter = false;
                ClearRecursive(cat.Children);
            }
        }
    }
    
    ClearRecursive(Keywords.FirstOrDefault()?.Children ?? []);
    ClearRecursive(MetadataCategories.FirstOrDefault()?.Children ?? []);
}
```

C. In `SearchableTreeView.axaml`, style the active filter indicator:
```xml
<!-- In the TreeView template, modify the Name TextBlock -->
<TextBlock Text="{Binding Name}" FontSize="11" Foreground="#333">
  <TextBlock.Styles>
    <Style Selector="TextBlock[IsActiveFilter=True]">
      <Setter Property="FontWeight" Value="Bold" />
      <Setter Property="Foreground" Value="#1976D2" />
    </Style>
  </TextBlock.Styles>
</TextBlock>
```

---

### Wave 2: Broker-Side Handlers (Multi-User Mode)

#### T10.6 — Collection CRUD Handlers

**Files changed:**
- `src/Adam.BrokerService/Handlers/Collections/CreateCollectionHandler.cs` — verify/update
- `src/Adam.BrokerService/Handlers/Collections/UpdateCollectionHandler.cs` — verify/update
- `src/Adam.BrokerService/Handlers/Collections/DeleteCollectionHandler.cs` — verify/update

**Already verified existing:** These handlers exist. Verify each handles:
- Permission check (`collection:create` / `collection:update` / `collection:*`)
- Input validation (non-empty name, valid parent ID if specified)
- Cascade delete for DeleteCollectionHandler with `CascadeChildren` flag
- ChangeNotification broadcast after operation

**Add ChangeNotification broadcast to each handler:**

After successful operation, in each handler:
```csharp
// Get ConnectionRegistry from DI
var connectionRegistry = context.GetRequiredService<ConnectionRegistry>();
await connectionRegistry.BroadcastAsync(new Envelope
{
    MessageType = MessageTypeCode.ChangeNotification,
    Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new ChangeNotification
    {
        EntityType = "Collection",
        ChangeType = operationType // "Created" / "Updated" / "Deleted",
        EntityId = collectionId.ToString()
    }))
});
```

#### T10.7 — Keyword CRUD Handlers

**Same pattern as T10.6** — verify existing handlers, add cascade delete and ChangeNotification broadcast.

#### T10.8 — Category CRUD Handlers

**Same pattern as T10.6** — verify existing handlers, add cascade delete and ChangeNotification broadcast.

#### T10.9 — Protobuf Contracts

**Files changed:**
- `src/Adam.Shared/Contracts/` — verify/create protobuf messages

**Existing contracts to verify:**
- `CreateKeywordRequest` / `CreateKeywordResponse`
- `UpdateKeywordRequest` / `UpdateKeywordResponse`
- `DeleteKeywordRequest` / `DeleteKeywordResponse`
- `CreateCategoryRequest` / `CreateCategoryResponse`
- `UpdateCategoryRequest` / `UpdateCategoryResponse`
- `DeleteCategoryRequest` / `DeleteCategoryResponse`
- `CreateCollectionRequest` / `CreateCollectionResponse`
- `UpdateCollectionRequest` / `UpdateCollectionResponse`
- `DeleteCollectionRequest` / `DeleteCollectionResponse`

**Verify each has:**
- `IProtoSerializable` implementation
- Correct `MessageTypeCode` enum entry
- `CascadeChildren` bool field on delete messages (new, may need adding)
- Serialization/deserialization round-trip works

**If `CascadeChildren` field needs adding to `DeleteKeywordRequest`:**

```csharp
public sealed class DeleteKeywordRequest : IProtoSerializable
{
    public string Id { get; set; } = string.Empty;
    public bool CascadeChildren { get; set; } = true; // default: cascade

    public byte[] Serialize()
    {
        using var ms = new MemoryStream();
        var proto = ProtoWriter.Create(ms);
        ProtoWriter.WriteField(1, Id, proto);
        ProtoWriter.WriteField(2, CascadeChildren, proto);
        ProtoWriter.Flush(proto);
        return ms.ToArray();
    }

    public void Deserialize(byte[] data)
    {
        using var ms = new MemoryStream(data);
        var proto = ProtoReader.Create(ms);
        while (proto.ReadNextField())
        {
            switch (proto.FieldNumber)
            {
                case 1: Id = proto.ReadString(); break;
                case 2: CascadeChildren = proto.ReadBool(); break;
            }
        }
    }
}
```

#### T10.10 — Change Notification Broadcast

**Files changed:**
- CRUD handlers (T10.6-T10.8) — add broadcast after each operation

**Pattern:** Same for all handlers — after successful DB commit, broadcast ChangeNotification via ConnectionRegistry.

**Verify `MessageTypeCode.ChangeNotification` is routed correctly** in broker's message dispatch.

---

### Wave 3: Filter UX Integration

#### T10.11 — "Filter by this" Context Menu Entry

**Already covered by T10.1** — the `AddFilterMenuItems` helper adds "Filter by this" to every node's ContextFlyout.

**Implementation (from T10.1):**
```csharp
private void AddFilterMenuItems(MenuFlyout flyout, SidebarViewModel vm, string filterText, string clearText)
{
    flyout.Items.Add(new Separator());
    flyout.Items.Add(new MenuItem { Header = filterText, Command = vm.FilterByThisCommand, CommandParameter = /* current node */ });
    flyout.Items.Add(new MenuItem { Header = clearText, Command = vm.ClearFilterCommand, CommandParameter = /* current node */ });
}
```

#### T10.12 — "Clear filter" Context Menu Entry

**Same as T10.11** — already covered by the `AddFilterMenuItems` helper.

#### T10.13 — Visual Filter State

**Covered above** in the Wave 1 section.

---

## 4. Execution Waves

| Wave | Tasks | Depends On | Rationale |
|------|-------|------------|-----------|
| **Wave 1 — Client CRUD** | T10.1, T10.2 (remaining), T10.3, T10.4, T10.5, T10.13 | — | Standalone-mode CRUD with context menus, inline rename, cascade delete, filter state. All client-side, no broker dependency. |
| **Wave 2 — Broker Handlers** | T10.6, T10.7, T10.8, T10.9, T10.10 | Wave 1 | Server-side handlers for multi-user mode. Contracts exist; need verification and cascade delete + ChangeNotification. |
| **Wave 3 — Filter UX** | T10.11, T10.12 | Wave 1 | Filter integration — already covered by Wave 1's ContextFlyout implementation. |

---

## 5. File Change Matrix

| # | File | Change Type | Details |
|---|------|-------------|---------|
| 1 | `src/Adam.CatalogBrowser/Controls/SearchableTreeView.axaml` | Modify | Add ContextFlyout to TreeView template items; add `IsActiveFilter` trigger style |
| 2 | `src/Adam.CatalogBrowser/Controls/SearchableTreeView.axaml.cs` | Modify | Add `PopulateNodeContextFlyout`, `AddCrudMenuItems`, `AddFolderMenuItems`, `AddFilterMenuItems` methods |
| 3 | `src/Adam.CatalogBrowser/ViewModels/SidebarViewModel.cs` | Modify | Add `FilterByThisCommand`, `ClearFilterCommand`, `F2RenameCommand`; update delete methods for cascade; add `CollectDescendant*Ids` helpers; add `IsActiveFilter` clearing |
| 4 | `src/Adam.CatalogBrowser/Views/MainWindow.axaml.cs` | Modify | Wire `RenameCompletedCommand` to SearchableTreeView instances |
| 5 | `src/Adam.CatalogBrowser/Views/MainWindow.axaml` | Modify | Add F2 KeyBinding for inline rename |
| 6 | `src/Adam.BrokerService/Handlers/Collections/CreateCollectionHandler.cs` | Verify | Confirm permissions, add ChangeNotification |
| 7 | `src/Adam.BrokerService/Handlers/Collections/UpdateCollectionHandler.cs` | Verify | Confirm permissions, add ChangeNotification |
| 8 | `src/Adam.BrokerService/Handlers/Collections/DeleteCollectionHandler.cs` | Verify | Add cascade flag support + ChangeNotification |
| 9 | `src/Adam.BrokerService/Handlers/Keywords/CreateKeywordHandler.cs` | Verify | Same pattern |
| 10 | `src/Adam.BrokerService/Handlers/Keywords/UpdateKeywordHandler.cs` | Verify | Same pattern |
| 11 | `src/Adam.BrokerService/Handlers/Keywords/DeleteKeywordHandler.cs` | Verify | Add cascade flag support |
| 12 | `src/Adam.BrokerService/Handlers/Categories/CreateCategoryHandler.cs` | Verify | Same pattern |
| 13 | `src/Adam.BrokerService/Handlers/Categories/UpdateCategoryHandler.cs` | Verify | Same pattern |
| 14 | `src/Adam.BrokerService/Handlers/Categories/DeleteCategoryHandler.cs` | Verify | Add cascade flag support |
| 15 | `src/Adam.Shared/Contracts/` (multiple files) | Verify | Ensure CascadeChildren field on delete messages; verify IProtoSerializable |
| 16 | `tests/Adam.CatalogBrowser.Tests/ViewModels/` | Add | New test files for CRUD operations and context menu behavior |
| 17 | `tests/Adam.BrokerService.Tests/` | Add | New test files for cascade delete and ChangeNotification |

---

## 6. Testing Strategy

### 6.1 Unit Tests — Automated

| Test ID | Test Name | What It Verifies | File |
|---------|-----------|------------------|------|
| T10-T1 | `CreateKeyword_Standalone_PersistsToDb` | Creating a keyword via PromptCreateKeywordAsync adds it to the database | `SidebarCrudTests.cs` |
| T10-T2 | `CreateKeyword_UnderParent_SetsParentId` | Creating a keyword under a parent sets ParentId correctly | `SidebarCrudTests.cs` |
| T10-T3 | `RenameKeyword_Standalone_UpdatesDb` | Renaming updates the entity in the database | `SidebarCrudTests.cs` |
| T10-T4 | `RenameKeyword_NoChange_SkipsDbWrite` | Renaming to the same name doesn't call SaveChanges | `SidebarCrudTests.cs` |
| T10-T5 | `DeleteKeyword_Cascade_RemovesAllDescendants` | Deleting a parent keyword removes all descendant keywords from DB | `SidebarCrudTests.cs` |
| T10-T6 | `DeleteKeyword_Cascade_WithChildrenAsksConfirmation` | Confirmation dialog shows child count when deleting parent with children | `SidebarCrudTests.cs` |
| T10-T7 | `DeleteKeyword_NoChildren_StandardConfirmation` | Confirmation dialog doesn't mention children when deleting leaf node | `SidebarCrudTests.cs` |
| T10-T8 | `FilterByThis_SetsSelectedKeyword` | FilterByThisCommand sets SelectedKeyword and fires FilterChanged | `SidebarCrudTests.cs` |
| T10-T9 | `ClearFilter_ClearsSelectedKeyword` | ClearFilterCommand sets SelectedKeyword to null | `SidebarCrudTests.cs` |
| T10-T10 | `CanCreateMetadata_False_DisablesCreateCommand` | When CanCreateMetadata is false, Create* commands return false from CanExecute | `SidebarCrudTests.cs` |
| T10-T11 | `CanEditMetadata_False_DisablesRenameDeleteCommands` | When CanEditMetadata is false, Rename/Delete commands return false from CanExecute | `SidebarCrudTests.cs` |
| T10-T12 | `CollectDescendantKeywordIds_CollectsRecursively` | CollectDescendantKeywordIds correctly collects all descendant IDs | `SidebarCrudTests.cs` |
| T10-T13 | `FolderReveal_OpensExplorer` | RevealFolder starts the correct OS process (verify via Process.Start mock) | `SidebarCrudTests.cs` |
| T10-T14 | `CreateCollection_Standalone_PersistsToDb` | Creating a collection adds it to the database | `SidebarCrudTests.cs` |
| T10-T15 | `CreateCategory_Standalone_PersistsToDb` | Creating a category adds it to the database | `SidebarCrudTests.cs` |
| T10-T16 | `IsActiveFilter_TrueWhenSelected` | IsActiveFilter is true on the currently selected filter node | `SidebarCrudTests.cs` |
| T10-T17 | `IsActiveFilter_ClearsWhenFilterChanges` | IsActiveFilter is cleared on old node when a different filter is selected | `SidebarCrudTests.cs` |

### 6.2 Manual Test Cases

| Test | Steps | Expected |
|------|-------|----------|
| Right-click keyword | 1. Open app with assets. 2. Right-click any keyword node. | Context menu appears with Create/Rename/Delete + Filter by this/Clear filter |
| Inline rename via F2 | 1. Select keyword. 2. Press F2. | TextBox replaces TextBlock, EditName is pre-filled. |
| Inline rename via double-click | 1. Double-click keyword name. | Same as F2. |
| Commit rename via Enter | 1. Edit name. 2. Press Enter. | TextBlock returns with new name; DB updated; sidebar refreshes. |
| Cancel rename via Escape | 1. Double-click. 2. Edit. 3. Press Escape. | Original name restored; no DB change. |
| Cascade delete keyword | 1. Create "Animals > Mammals > Dog" hierarchy. 2. Right-click "Animals". 3. Select Delete. | Confirmation says "delete 'Animals' and all 2 sub-keywords". Confirm → all three removed. |
| Cascade delete collection | 1. Create nested collections. 2. Delete parent. | Children also deleted (not the assets). |
| Filter by this | 1. Right-click keyword. 2. "Filter by this". | Gallery filters to matching assets; keyword shows bold blue indicator. |
| Clear filter | 1. Right-click active filter. 2. "Clear filter". | Gallery returns to unfiltered view; bold indicator removed. |
| Folder: Reveal in Explorer | 1. Right-click folder. 2. "Reveal in Explorer". | OS file manager opens to that folder. |
| Folder: Re-scan | 1. Right-click folder. 2. "Re-scan Folder". | Sidebar refreshes with updated asset counts. |
| Multi-user: Create collection | 1. Two clients connected. 2. Client A creates collection. | Client B's sidebar updates in real-time with new collection. |
| Multi-user: Delete keyword | 1. Two clients connected. 2. Client A deletes keyword cascade. | Client B's sidebar removes the keyword and all children. |

---

## 7. Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| ContextFlyout in TreeView template doesn't support polymorphic `x:DataType` | Can't use compiled bindings for different node types | Fallback to code-behind flyout population via `ContextRequested` event handler (see T10.1 approach B) |
| Cascade delete of large keyword hierarchies is slow | UI freeze during DB operation | Run inside a transaction; use `RemoveRange` for bulk delete; show loading indicator |
| Inline rename TextBox focus steals from main search | Confusing UX | Ensure `Dispatcher.UIThread.Post` with `DispatcherPriority.Input` sequences correctly; add focus guard |
| Existing broker handlers don't have `CascadeChildren` field | Protocol mismatch | Add field with backward-compatible default (false); old clients default to non-cascade |

---

## 8. Dependencies

- **DEP-001**: Works across all tree node types (KeywordNode, CategoryNode, CollectionNode, FolderNode)
- **DEP-002**: Reuses Phase 7 permission infrastructure (`CanEditMetadata`, `CanCreateMetadata`, `EvaluatePermission`)
- **DEP-003**: Reuses Phase 3 ChangeNotification broadcast pattern
- **DEP-004**: Reuses existing `InputDialog` and `ConfirmationDialog` controls

---

## 9. Rollout Order

1. **Wave 1 (Client CRUD)**: Complete standalone-mode CRUD → test all operations
2. **Wave 2 (Broker Handlers)**: Add cascade delete + ChangeNotification to existing handlers → test multi-user sync
3. **Wave 3 (Filter UX)**: Filter integration + visual state → already covered by Wave 1
4. **Integration sweep**: Test all combinations (standalone vs multi-user, Viewer vs Editor vs Admin, empty vs populated trees)
