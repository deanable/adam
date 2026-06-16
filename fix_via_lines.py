with open('src/Adam.CatalogBrowser/ViewModels/SidebarViewModel.cs', 'r') as f:
    lines = f.readlines()

# Change 1: Line 487 (0-idx 486) - Add c.IsSmart to Select
assert '.Select(c => new { c.Id, c.Name' in lines[486], f"Line 487 mismatch"
lines[486] = "                .Select(c => new { c.Id, c.IsSmart, c.Name, c.ParentId, AssetCount = c.Assets.Count })\n"
print("1. Select updated")

# Change 2: Line 491 (0-idx 490) - Add IsSmart to standalone mapper
assert 'Id = c.Id, Name = c.Name' in lines[490], f"Line 491 mismatch"
lines[490] = "                Id = c.Id, Name = c.Name, ParentId = c.ParentId, AssetCount = c.AssetCount, IsSmart = c.IsSmart\n"
print("2. Standalone mapper updated")

# Change 3: Line 513-514 - Add IsSmart to broker mapper
assert 'AssetCount = c.AssetCount' in lines[512], f"Line 513 mismatch"
assert '}).ToList();' in lines[513], f"Line 514 mismatch"
lines[512] = "                    AssetCount = c.AssetCount,\n"
lines.insert(513, "                    IsSmart = c.IsSmart\n")
print("3. Broker mapper updated")

# Change 4: Add _isSmart field to CollectionNode
# After insert, find CollectionNode
for i, line in enumerate(lines):
    if 'class CollectionNode' in line:
        cn = i
        break

assert cn is not None
# _isActiveFilter at cn + 6 (0-indexed)
assert 'private bool _isActiveFilter;' in lines[cn + 6]
# blank at cn + 7
# Guid Id at cn + 8
assert 'public Guid Id' in lines[cn + 8]

lines.insert(cn + 7, "    private bool _isSmart;\n")
print("4. _isSmart field added")

# Change 5: Add IsSmart/SmartIcon after EditName
# EditName at cn + 13 (0-indexed)
assert 'EditName' in lines[cn + 13]  # still correct since insert was before this

insert_after = [
    "\n",
    "    /// <summary>\n",
    "    /// True when this collection is a smart collection (auto-refreshed from a saved query).\n",
    "    /// </summary>\n",
    "    public bool IsSmart\n",
    "    {\n",
    "        get => _isSmart;\n",
    "        set { _isSmart = value; OnPropertyChanged(); OnPropertyChanged(nameof(SmartIcon)); }\n",
    "    }\n",
    "\n",
    "    /// <summary>\n",
    "    /// Display icon for smart collection status: \u2726 when smart, empty when manual.\n",
    "    /// </summary>\n",
    '    public string SmartIcon => _isSmart ? "\u2726" : string.Empty;\n',
    "\n",
]

for j, cline in enumerate(reversed(insert_after)):
    lines.insert(cn + 14, cline)
print("5. IsSmart/SmartIcon properties added")

# Write back
with open('src/Adam.CatalogBrowser/ViewModels/SidebarViewModel.cs', 'w') as f:
    f.writelines(lines)
print("\nDone!")

# Verify
content = ''.join(lines)
for prop in ['ActiveSearchQueryText', 'SelectedSavedSearch', 'IsSmart', 'SmartIcon', '_isSmart']:
    assert prop in content, f"{prop} MISSING!"
    print(f"  OK: {prop}")
print("All verified!")
