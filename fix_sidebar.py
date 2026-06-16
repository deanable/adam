import os

path = 'src/Adam.CatalogBrowser/ViewModels/SidebarViewModel.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

# Fix 1: Add IsSmart/SmartIcon properties to CollectionNode class
# The marker is the IsActiveFilter property ending, then Children, BeginRename...
old_collection = '''    public bool IsActiveFilter
    {
        get => _isActiveFilter;
        set { _isActiveFilter = value; OnPropertyChanged(); }
    }

    public ObservableCollection<CollectionNode> Children { get; } = [];

    public void BeginRename() { EditName = Name; IsEditing = true; }
    public void CommitRename() { if (!string.IsNullOrWhiteSpace(EditName)) Name = EditName; IsEditing = false; }
    public void CancelRename() { IsEditing = false; EditName = Name; }'''

new_collection = '''    public bool IsSmart
    {
        get => _isSmart;
        set { _isSmart = value; OnPropertyChanged(); OnPropertyChanged(nameof(SmartIcon)); }
    }

    /// <summary>
    /// Display icon for smart collection status: \u2726 when smart, empty when manual.
    /// </summary>
    public string SmartIcon => _isSmart ? "\u2726" : string.Empty;

    /// <summary>
    /// True when this collection is the currently active gallery filter (T10.13).
    /// </summary>
    public bool IsActiveFilter
    {
        get => _isActiveFilter;
        set { _isActiveFilter = value; OnPropertyChanged(); }
    }

    public ObservableCollection<CollectionNode> Children { get; } = [];

    public void BeginRename() { EditName = Name; IsEditing = true; }
    public void CommitRename() { if (!string.IsNullOrWhiteSpace(EditName)) Name = EditName; IsEditing = false; }
    public void CancelRename() { IsEditing = false; EditName = Name; }'''

count = 0
if old_collection in content:
    content = content.replace(old_collection, new_collection, 1)
    count += 1
    print("Fix 1: Added IsSmart/SmartIcon to CollectionNode")
else:
    print("Fix 1 FAILED: CollectionNode pattern not found")

# Fix 2: Add _isSmart field after _isActiveFilter in CollectionNode
# Find _isActiveFilter field that's followed by a blank line then public Guid Id
old_field = '''    private bool _isActiveFilter;

    public Guid Id { get; set; }
    public Guid? ParentId { get; set; }
    public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
    public int AssetCount { get => _assetCount; set { _assetCount = value; OnPropertyChanged(); } }
    public bool IsEditing { get => _isEditing; set { _isEditing = value; OnPropertyChanged(); } }
    public string EditName { get => _editName; set { _editName = value; OnPropertyChanged(); } }

    /// <summary>
    /// True when this collection is the currently active gallery filter (T10.13).
    /// </summary>
    public bool IsActiveFilter'''

new_field = '''    private bool _isActiveFilter;
    private bool _isSmart;

    public Guid Id { get; set; }
    public Guid? ParentId { get; set; }
    public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
    public int AssetCount { get => _assetCount; set { _assetCount = value; OnPropertyChanged(); } }
    public bool IsEditing { get => _isEditing; set { _isEditing = value; OnPropertyChanged(); } }
    public string EditName { get => _editName; set { _editName = value; OnPropertyChanged(); } }

    /// <summary>
    /// True when this collection is a smart collection (auto-refreshed from a saved query).
    /// </summary>
    public bool IsSmart'''

if old_field in content:
    content = content.replace(old_field, new_field, 1)
    count += 1
    print("Fix 2: Added _isSmart field + IsSmart property header")
else:
    print("Fix 2 FAILED: field pattern not found")
    # Show context around CollectionNode to debug
    idx = content.find("private bool _isActiveFilter;")
    if idx >= 0:
        print(f"  Found _isActiveFilter at position {idx}")
        print(f"  Context: {repr(content[idx:idx+200])}")

# Fix 3: Update broker mapper to include IsSmart
old_broker = '''                    AssetCount = c.AssetCount
                }).ToList();'''

new_broker = '''                    AssetCount = c.AssetCount,
                    IsSmart = c.IsSmart
                }).ToList();'''

if old_broker in content:
    content = content.replace(old_broker, new_broker, 1)
    count += 1
    print("Fix 3: Added IsSmart to broker mapper")
else:
    print("Fix 3 FAILED: broker pattern not found")

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)

print(f"\n{count} fixes applied")
