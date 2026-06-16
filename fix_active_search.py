import re

with open('src/Adam.CatalogBrowser/ViewModels/SidebarViewModel.cs', 'r', encoding='utf-8') as f:
    content = f.read()

# 1. Add _activeSearchQueryText field after the last field declaration (after _dateTakenTree line)
old_field_section = '    private ObservableCollection<DateTakenNode> _dateTakenTree = [];\n\n    public SidebarViewModel'
new_field_section = '    private ObservableCollection<DateTakenNode> _dateTakenTree = [];\n    private string? _activeSearchQueryText;\n\n    public SidebarViewModel'

if old_field_section in content:
    content = content.replace(old_field_section, new_field_section)
    print("1. Added _activeSearchQueryText field")
else:
    print("1. FAILED: Could not find field insertion point")
    # Debug: show nearby text
    idx = content.find('DateTakenNode> _dateTakenTree')
    if idx >= 0:
        print(f"   Found _dateTakenTree at index {idx}")
        print(f"   Context: {repr(content[idx-20:idx+80])}")

# 2. Add ActiveSearchQueryText property before the constructor's closing brace or near other properties
# Let's find where public SidebarProperties start and add it after SelectedDateTaken
old_prop_pattern = '    public DateTakenNode? SelectedDateTaken\n    {\n        get => _selectedDateTaken;\n        set\n        {\n            _selectedDateTaken = value;\n            OnPropertyChanged();\n            OnFilterChanged();\n        }\n    }'
new_prop_pattern = '    public DateTakenNode? SelectedDateTaken\n    {\n        get => _selectedDateTaken;\n        set\n        {\n            _selectedDateTaken = value;\n            OnPropertyChanged();\n            OnFilterChanged();\n        }\n    }\n\n    /// <summary>\n    /// The currently active search query text, set when a saved search or\n    /// quick search is selected from the sidebar. Used by MainWindowViewModel\n    /// to pass the query to ApplyFilter (Phase 19 Wave 7).\n    /// </summary>\n    public string? ActiveSearchQueryText\n    {\n        get => _activeSearchQueryText;\n        set\n        {\n            _activeSearchQueryText = value;\n            OnPropertyChanged();\n        }\n    }'

if old_prop_pattern in content:
    content = content.replace(old_prop_pattern, new_prop_pattern, 1)
    print("2. Added ActiveSearchQueryText property")
else:
    print("2. FAILED: Could not find SelectedDateTaken property")
    # Debug: find it
    idx = content.find('SelectedDateTaken')
    if idx >= 0:
        print(f"   Found SelectedDateTaken at index {idx}")
        print(f"   Context: {repr(content[idx:idx+200])}")

with open('src/Adam.CatalogBrowser/ViewModels/SidebarViewModel.cs', 'w', encoding='utf-8') as f:
    f.write(content)

print("Done!")
