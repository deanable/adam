$file = "src/Adam.CatalogBrowser/ViewModels/SidebarViewModel.cs"
$content = [System.IO.File]::ReadAllText($file)

# Step 1: Add IsSmart field + SmartIcon property to CollectionNode
$old = @'
    private bool _isActiveFilter;

    public Guid Id { get; set; }
    public Guid? ParentId { get; set; }
    public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
    public int AssetCount { get => _assetCount; set { _assetCount = value; OnPropertyChanged(); } }
    public bool IsEditing { get => _isEditing; set { _isEditing = value; OnPropertyChanged(); } }
    public string EditName { get => _editName; set { _editName = value; OnPropertyChanged(); } }

    /// <summary>
    /// True when this collection is the currently active gallery filter (T10.13).
    /// </summary>
    public bool IsActiveFilter
    {
        get => _isActiveFilter;
        set { _isActiveFilter = value; OnPropertyChanged(); }
    }
'@

$new = @'
    private bool _isActiveFilter;
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
    public bool IsSmart
    {
        get => _isSmart;
        set { _isSmart = value; OnPropertyChanged(); OnPropertyChanged(nameof(SmartIcon)); }
    }

    /// <summary>
    /// Display icon for smart collection status: ✦ when smart, empty when manual.
    /// </summary>
    public string SmartIcon => _isSmart ? "✦" : string.Empty;

    /// <summary>
    /// True when this collection is the currently active gallery filter (T10.13).
    /// </summary>
    public bool IsActiveFilter
    {
        get => _isActiveFilter;
        set { _isActiveFilter = value; OnPropertyChanged(); }
    }
'@

Write-Host "Step 1: Adding IsSmart/SmartIcon to CollectionNode..."
if ($content.Contains($old)) {
    $content = $content.Replace($old, $new)
    Write-Host "  Applied"
} else {
    Write-Host "  ERROR: Pattern 1 not found!"
}

# Step 2: Update standalone Select to include IsSmart
$old2 = "                .Select(c => new { c.Id, c.Name, c.ParentId, AssetCount = c.Assets.Count })"
$new2 = "                .Select(c => new { c.Id, c.IsSmart, c.Name, c.ParentId, AssetCount = c.Assets.Count })"

Write-Host "Step 2: Adding IsSmart to standalone Select..."
if ($content.Contains($old2)) {
    $content = $content.Replace($old2, $new2)
    Write-Host "  Applied"
} else {
    Write-Host "  ERROR: Pattern 2 not found!"
}

# Step 3: Update standalone mapper to include IsSmart
$old3 = "                Id = c.Id, Name = c.Name, ParentId = c.ParentId, AssetCount = c.AssetCount
            }).ToList();"
$new3 = "                Id = c.Id, Name = c.Name, ParentId = c.ParentId, AssetCount = c.AssetCount, IsSmart = c.IsSmart
            }).ToList();"

Write-Host "Step 3: Adding IsSmart to standalone mapper..."
if ($content.Contains($old3)) {
    $content = $content.Replace($old3, $new3)
    Write-Host "  Applied"
} else {
    Write-Host "  ERROR: Pattern 3 not found!"
}

# Step 4: Update broker mapper to include IsSmart
$old4 = @'
                    AssetCount = c.AssetCount
                }).ToList();
'@
$new4 = @'
                    AssetCount = c.AssetCount,
                    IsSmart = c.IsSmart
                }).ToList();
'@

Write-Host "Step 4: Adding IsSmart to broker mapper..."
if ($content.Contains($old4)) {
    $content = $content.Replace($old4, $new4)
    Write-Host "  Applied"
} else {
    Write-Host "  ERROR: Pattern 4 not found!"
}

[System.IO.File]::WriteAllText($file, $content)
Write-Host "Done! File written."
