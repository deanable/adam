import re

with open('src/Adam.CatalogBrowser/ViewModels/SidebarViewModel.cs', 'r', encoding='utf-8') as f:
    content = f.read()

# Find the closing of SelectedDateTaken property and add ActiveSearchQueryText after
# The pattern: closing brace of SelectedDateTaken followed by blank line + // T8.18: Sidebar CRUD commands
old = '        }\n    }\n\n    // ── T8.18: Sidebar CRUD commands ──'
new = '''        }
    }

    /// <summary>
    /// The currently active search query text, set when a saved search or
    /// quick search is selected from the sidebar. Used by MainWindowViewModel
    /// to pass the query to ApplyFilter (Phase 19 Wave 7).
    /// </summary>
    public string? ActiveSearchQueryText
    {
        get => _activeSearchQueryText;
        set
        {
            _activeSearchQueryText = value;
            OnPropertyChanged();
        }
    }

    // ── T8.18: Sidebar CRUD commands ──'''

if old in content:
    content = content.replace(old, new, 1)
    print("SUCCESS: Added ActiveSearchQueryText property")
else:
    print("FAILED: Could not find insertion point")
    # Debug
    idx = content.find('Sidebar CRUD commands')
    if idx >= 0:
        ctx = content[idx-100:idx+10]
        print(f"Context around 'Sidebar CRUD commands': {repr(ctx)}")

with open('src/Adam.CatalogBrowser/ViewModels/SidebarViewModel.cs', 'w', encoding='utf-8') as f:
    f.write(content)
