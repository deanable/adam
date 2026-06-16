cd "C:/Users/Dean/source/repos/adam"

# Change 1: Line 487 - Add c.IsSmart to Select
sed -i '487s/\.Select(c => new { c.Id, c.Name, c.ParentId, AssetCount = c.Assets.Count })/.Select(c => new { c.Id, c.IsSmart, c.Name, c.ParentId, AssetCount = c.Assets.Count })/' src/Adam.CatalogBrowser/ViewModels/SidebarViewModel.cs
echo "1. Select updated"

# Change 2: Line 491 - Add IsSmart to standalone mapper
sed -i '491s/Id = c.Id, Name = c.Name, ParentId = c.ParentId, AssetCount = c.AssetCount/Id = c.Id, Name = c.Name, ParentId = c.ParentId, AssetCount = c.AssetCount, IsSmart = c.IsSmart/' src/Adam.CatalogBrowser/ViewModels/SidebarViewModel.cs
echo "2. Standalone mapper updated"

# Change 3: Line 513 - Add IsSmart to broker mapper
sed -i '513s/AssetCount = c.AssetCount/AssetCount = c.AssetCount,/' src/Adam.CatalogBrowser/ViewModels/SidebarViewModel.cs
sed -i '514i\                    IsSmart = c.IsSmart' src/Adam.CatalogBrowser/ViewModels/SidebarViewModel.cs
echo "3. Broker mapper updated"

# Change 4: Add _isSmart field after line 1898 (CollectionNode _isActiveFilter)
sed -i '1899a\    private bool _isSmart;' src/Adam.CatalogBrowser/ViewModels/SidebarViewModel.cs
echo "4. _isSmart field added"

# After inserts, lines shift by 2 (one for broker InsertAfter, one for _isSmart)
# Change 5: Insert IsSmart/SmartIcon properties after EditName (now at line 1908 due to 2-line shift)
sed -i '1908a\\n    /// <summary>\n    /// True when this collection is a smart collection (auto-refreshed from a saved query).\n    /// </summary>\n    public bool IsSmart\n    {\n        get => _isSmart;\n        set { _isSmart = value; OnPropertyChanged(); OnPropertyChanged(nameof(SmartIcon)); }\n    }\n\n    /// <summary>\n    /// Display icon for smart collection status: \xe2\x9c\xa6 when smart, empty when manual.\n    /// </summary>\n    public string SmartIcon => _isSmart ? "\xe2\x9c\xa6" : string.Empty;' src/Adam.CatalogBrowser/ViewModels/SidebarViewModel.cs
echo "5. IsSmart/SmartIcon properties added"

echo ""
echo "All changes applied!"
