$file = "src/Adam.CatalogBrowser/ViewModels/SidebarViewModel.cs"
$content = [System.IO.File]::ReadAllText($file)
$count = 0

# Fix 1: Add IsSmart property + SmartIcon after EditName, before IsActiveFilter in CollectionNode
$marker = "    public string EditName { get => _editName; set { _editName = value; OnPropertyChanged(); } }"
$insertion = @"

    /// <summary>
    /// True when this collection is a smart collection (auto-refreshed from a saved query).
    /// </summary>
    public bool IsSmart
    {
        get => _isSmart;
        set { _isSmart = value; OnPropertyChanged(); OnPropertyChanged(nameof(SmartIcon)); }
    }

    /// <summary>
    /// Display icon for smart collection status: 2726 when smart, empty when manual.
    /// </summary>
    public string SmartIcon => _isSmart ? ([char]0x2726).ToString() : string.Empty;
"@
$result = $content.Replace("$marker`r`n`r`n    /// <summary>`r`n    /// True when this collection is the currently active gallery filter (T10.13).", "$marker$insertion`r`n`r`n    /// <summary>`r`n    /// True when this collection is the currently active gallery filter (T10.13).")
if ($result -ne $content) { $content = $result; $count++; Write-Host "Fix 1 applied" } else { Write-Host "Fix 1 FAILED" }

# Fix 2: Add IsSmart field after _isActiveFilter in CollectionNode
$content = $content.Replace("    private bool _isActiveFilter;`r`n`r`n    public Guid Id { get; set; }", "    private bool _isActiveFilter;`r`n    private bool _isSmart;`r`n`r`n    public Guid Id { get; set; }")
if ($content.Contains("_isSmart")) { Write-Host "Fix 2 applied" } else { Write-Host "Fix 2 FAILED" }

# Fix 3: Update broker mapper to include IsSmart
$old3 = "                    AssetCount = c.AssetCount`r`n                }).ToList();"
$new3 = "                    AssetCount = c.AssetCount,`r`n                    IsSmart = c.IsSmart`r`n                }).ToList();"
$result3 = $content.Replace($old3, $new3)
if ($result3 -ne $content) { $content = $result3; Write-Host "Fix 3 applied" } else { Write-Host "Fix 3 FAILED" }

[System.IO.File]::WriteAllText($file, $content)
Write-Host "Done!"
