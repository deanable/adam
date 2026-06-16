import os, sys

path = 'src/Adam.CatalogBrowser/ViewModels/SidebarViewModel.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

original = content
count = 0

# ====== Change 1: Standalone Select ======
old1 = "                .Select(c => new { c.Id, c.Name, c.ParentId, AssetCount = c.Assets.Count })"
new1 = "                .Select(c => new { c.Id, c.IsSmart, c.Name, c.ParentId, AssetCount = c.Assets.Count })"
if old1 in content:
    content = content.replace(old1, new1, 1)
    count += 1
    print("OK: Standalone Select updated")
else:
    print("FAIL: Standalone Select not found")

# ====== Change 2: Standalone mapper ======
old2 = "                Id = c.Id, Name = c.Name, ParentId = c.ParentId, AssetCount = c.AssetCount\r\n            }).ToList()"
new2 = "                Id = c.Id, Name = c.Name, ParentId = c.ParentId, AssetCount = c.AssetCount, IsSmart = c.IsSmart\r\n            }).ToList()"
if old2 in content:
    content = content.replace(old2, new2, 1)
    count += 1
    print("OK: Standalone mapper updated")
else:
    # Try without \r
    old2b = "                Id = c.Id, Name = c.Name, ParentId = c.ParentId, AssetCount = c.AssetCount\n            }).ToList()"
    if old2b in content:
        content = content.replace(old2b, new2b, 1)
        count += 1
        print("OK: Standalone mapper updated (LF)")
    else:
        print("FAIL: Standalone mapper not found")

# ====== Change 3: Broker mapper ======
old3 = "                    AssetCount = c.AssetCount\r\n                }).ToList()"
new3 = "                    AssetCount = c.AssetCount,\r\n                    IsSmart = c.IsSmart\r\n                }).ToList()"
if old3 in content:
    content = content.replace(old3, new3, 1)
    count += 1
    print("OK: Broker mapper updated")
else:
    old3b = "                    AssetCount = c.AssetCount\n                }).ToList()"
    if old3b in content:
        new3b = "                    AssetCount = c.AssetCount,\n                    IsSmart = c.IsSmart\n                }).ToList()"
        content = content.replace(old3b, new3b, 1)
        count += 1
        print("OK: Broker mapper updated (LF)")
    else:
        print("FAIL: Broker mapper not found")

# ====== Change 4: Add _isSmart field to CollectionNode ======
# Find the CollectionNode field declarations - after _isActiveFilter, before Guid Id
old4 = "    private bool _isActiveFilter;\r\n\r\n    public Guid Id { get; set; }"
new4 = "    private bool _isActiveFilter;\r\n    private bool _isSmart;\r\n\r\n    public Guid Id { get; set; }"
if old4 in content:
    content = content.replace(old4, new4, 1)
    count += 1
    print("OK: _isSmart field added")
else:
    # Try with LF
    old4b = "    private bool _isActiveFilter;\n\n    public Guid Id { get; set; }"
    new4b = "    private bool _isActiveFilter;\n    private bool _isSmart;\n\n    public Guid Id { get; set; }"
    if old4b in content:
        content = content.replace(old4b, new4b, 1)
        count += 1
        print("OK: _isSmart field added (LF)")
    else:
        print("FAIL: _isSmart field not found")

# ====== Change 5: Add IsSmart property + SmartIcon to CollectionNode ======
# After EditName property, before IsActiveFilter summary
old5 = "    public string EditName { get => _editName; set { _editName = value; OnPropertyChanged(); } }\r\n\r\n    /// <summary>\r\n    /// True when this collection is the currently active gallery filter (T10.13)."
new5 = "    public string EditName { get => _editName; set { _editName = value; OnPropertyChanged(); } }\r\n\r\n    /// <summary>\r\n    /// True when this collection is a smart collection (auto-refreshed from a saved query).\r\n    /// </summary>\r\n    public bool IsSmart\r\n    {\r\n        get => _isSmart;\r\n        set { _isSmart = value; OnPropertyChanged(); OnPropertyChanged(nameof(SmartIcon)); }\r\n    }\r\n\r\n    /// <summary>\r\n    /// Display icon for smart collection status: \\u2726 when smart, empty when manual.\r\n    /// </summary>\r\n    public string SmartIcon => _isSmart ? \"✦\" : string.Empty;\r\n\r\n    /// <summary>\r\n    /// True when this collection is the currently active gallery filter (T10.13)."
if old5 in content:
    content = content.replace(old5, new5, 1)
    count += 1
    print("OK: IsSmart/SmartIcon properties added")
else:
    # Try with LF
    old5b = "    public string EditName { get => _editName; set { _editName = value; OnPropertyChanged(); } }\n\n    /// <summary>\n    /// True when this collection is the currently active gallery filter (T10.13)."
    new5b = "    public string EditName { get => _editName; set { _editName = value; OnPropertyChanged(); } }\n\n    /// <summary>\n    /// True when this collection is a smart collection (auto-refreshed from a saved query).\n    /// </summary>\n    public bool IsSmart\n    {\n        get => _isSmart;\n        set { _isSmart = value; OnPropertyChanged(); OnPropertyChanged(nameof(SmartIcon)); }\n    }\n\n    /// <summary>\n    /// Display icon for smart collection status: \\u2726 when smart, empty when manual.\n    /// </summary>\n    public string SmartIcon => _isSmart ? \"✦\" : string.Empty;\n\n    /// <summary>\n    /// True when this collection is the currently active gallery filter (T10.13)."
    if old5b in content:
        content = content.replace(old5b, new5b, 1)
        count += 1
        print("OK: IsSmart/SmartIcon properties added (LF)")
    else:
        print("FAIL: IsSmart/SmartIcon properties not found")

# Verify key properties still exist
for prop in ['ActiveSearchQueryText', 'SelectedSavedSearch', 'IsSmart', 'SmartIcon', '_isSmart']:
    if prop in content:
        print(f"  Verified: {prop} exists in file")
    else:
        print(f"  WARNING: {prop} missing from file!")

if content != original:
    with open(path, 'w', encoding='utf-8') as f:
        f.write(content)
    print(f"\n{count} changes applied successfully")
else:
    print("\nNo changes were made!")
