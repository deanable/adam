const fs = require("fs");
const path = require("path");

const testDir = path.join(__dirname, "..", "tests", "Adam.CatalogBrowser.Tests", "ViewModels");
const files = [
  "SidebarCrudTests.cs",
  "SidebarSavedSearchTests.cs",
  "DropCommandHandlersTests.cs",
  "MainWindowViewModelTests.cs",
  "MainWindowViewModelPermissionTests.cs",
  "MainWindowPanelPersistenceTests.cs"
];

function fixFile(filePath) {
  let content = fs.readFileSync(filePath, "utf8");
  const origContent = content;

  // Each test file has patterns like:
  //   var mediaFormatService = new MediaFormatService(_modeManager, new NullLogger<MediaFormatService>());
  //   var dateTakenTreeService = new DateTakenTreeService(...);
  //   var savedSearchService = new Adam.CatalogBrowser.Services.SavedSearchService(...);
  //   _sidebar = new SidebarViewModel(_modeManager, _logger, mediaFormatService, dateTakenTreeService, savedSearchService);

  // Strategy: after the savedSearchService line, add 4 new service creation lines,
  // then add 4 new params to the SidebarViewModel constructor call.

  const nl = "\r\n";

  // 1. Add service creation lines after the savedSearchService line
  // Pattern: each file has `new Adam.CatalogBrowser.Services.SavedSearchService(...);`
  const savedSearchPattern = `new Adam.CatalogBrowser.Services.SavedSearchService(_modeManager, new NullLogger<Adam.CatalogBrowser.Services.SavedSearchService>());`;
  const savedSearchPattern2 = `new Adam.CatalogBrowser.Services.SavedSearchService(_modeManager, new NullLogger<Adam.CatalogBrowser.Services.SavedSearchService>())`;

  const serviceLines = [
    `        var folderTreeService = new FolderTreeService(_modeManager, new NullLogger<FolderTreeService>());`,
    `        var collectionTreeService = new CollectionTreeService(_modeManager, new NullLogger<CollectionTreeService>());`,
    `        var keywordTreeService = new KeywordTreeService(_modeManager, new NullLogger<KeywordTreeService>());`,
    `        var categoryTreeService = new CategoryTreeService(_modeManager, new NullLogger<CategoryTreeService>());`
  ];
  const serviceInsert = nl + serviceLines.join(nl) + nl;

  // Try to find the pattern and insert after it
  let idx = content.indexOf(savedSearchPattern);
  if (idx >= 0) {
    const endOfLine = content.indexOf(nl, idx);
    if (endOfLine > 0) {
      content = content.slice(0, endOfLine + nl.length) + serviceInsert + content.slice(endOfLine + nl.length);
    }
  }

  // 2. Add 4 new params to each SidebarViewModel constructor call
  // Pattern: new SidebarViewModel(mm, logger, mediaFormatService, dateTakenTreeService, savedSearchService)
  // We need to add: , folderTreeService, collectionTreeService, keywordTreeService, categoryTreeService

  // Each file may have variable names different from standard:
  // Standard: mediaFormatService, dateTakenTreeService, savedSearchService (most files)
  // MainWindowViewModelTests.cs line 1070: mediaFormatSvc, dateTakenTreeSvc, savedSearchSvc

  // Replace with regex for robustness
  function addServiceParams(str) {
    // Match: new SidebarViewModel(...MODE_MANAGER..., ...LOGGER..., mediaFormatSVC, dateTakenSVC, savedSearchSVC);
    // The key part is the last 3 params before the closing paren:
    // (..., mediaFormatXXX, dateTakenXXX, savedSearchXXX)
    const re = /(new SidebarViewModel\([^)]+,\s*(mediaFormat\w+),\s*(dateTakenTree\w+),\s*(savedSearch\w+)\s*\))/g;
    return str.replace(re, (match, full, mf, dtt, ss) => {
      return `new SidebarViewModel(${mf}, ${dtt}, ${ss}, folderTreeService, collectionTreeService, keywordTreeService, categoryTreeService)`;
    });
  }
  content = addServiceParams(content);

  // 3. For files where we inserted the service creation AFTER the savedSearchService line,
  //    but the original line had more code after it (like `var sidebar = new SidebarViewModel...`),
  //    we need to also handle the case where the mediaFormatService/dateTakenTreeService were declared earlier
  //    and the service insert happened. The regex should handle the constructor fix.

  // Write back if changes were made
  if (content !== origContent) {
    fs.writeFileSync(filePath, content, "utf8");
    console.log(`  Fixed: ${path.basename(filePath)}`);
  } else {
    console.log(`  No changes: ${path.basename(filePath)}`);
  }
}

// Fix each test file
console.log("Fixing test files...");
for (const f of files) {
  const filePath = path.join(testDir, f);
  fixFile(filePath);
}
console.log("Done!");
