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

// Each file creates services like:
//   var mediaFormatService = new MediaFormatService(_modeManager, new NullLogger<MediaFormatService>());
//   var dateTakenTreeService = new DateTakenTreeService(...);
//   var savedSearchService = new Adam.CatalogBrowser.Services.SavedSearchService(...);
// Then calls:
//   new SidebarViewModel(_modeManager, _logger, mediaFormatService, dateTakenTreeService, savedSearchService)
// or similar with different variable names.

// We need to:
// 1. After the savedSearchService creation line, add 4 new service creations
// 2. Replace the constructor call to include the 4 new params

const nl = "\r\n";

function fixFile(filePath) {
  let content = fs.readFileSync(filePath, "utf8");
  let modified = false;

  // --- Step 1: Insert service creations after the last savedSearchService line ---
  // Find the last line that creates a SavedSearchService
  const savedSearchPattern = /Adam\.CatalogBrowser\.Services\.SavedSearchService/;
  const lines = content.split(nl);
  let insertAfterLine = -1;
  for (let i = 0; i < lines.length; i++) {
    if (savedSearchPattern.test(lines[i])) {
      insertAfterLine = i;
    }
  }

  if (insertAfterLine >= 0) {
    const serviceInsert = [
      "        var folderTreeService = new FolderTreeService(_modeManager, new NullLogger<FolderTreeService>());",
      "        var collectionTreeService = new CollectionTreeService(_modeManager, new NullLogger<CollectionTreeService>());",
      "        var keywordTreeService = new KeywordTreeService(_modeManager, new NullLogger<KeywordTreeService>());",
      "        var categoryTreeService = new CategoryTreeService(_modeManager, new NullLogger<CategoryTreeService>());"
    ];
    lines.splice(insertAfterLine + 1, 0, ...serviceInsert);
    modified = true;
  }

  // --- Step 2: Fix all SidebarViewModel constructor calls ---
  // Find all lines containing 'new SidebarViewModel('
  const newContent = lines.join(nl);
  
  // Pattern: the constructor now expects 9 required params + 1 optional
  // new SidebarViewModel(mm, logger, mfs, dtts, sss, fts, cts, kts, cats, folderScan?)

  const newLines = newContent.split(nl);
  for (let i = 0; i < newLines.length; i++) {
    const line = newLines[i];
    if (line.includes("new SidebarViewModel(")) {
      // Find the closing paren
      let fullConstr = "";
      let j = i;
      let depth = 0;
      let started = false;
      while (j < newLines.length) {
        fullConstr += (j > i ? nl : "") + newLines[j];
        for (const ch of newLines[j]) {
          if (ch === '(') { depth++; started = true; }
          if (ch === ')') { depth--; }
        }
        if (started && depth === 0) break;
        j++;
      }

      // Count the existing params (between first ( and last ))
      const parenOpen = fullConstr.indexOf('(');
      const parenClose = fullConstr.lastIndexOf(')');
      if (parenOpen < 0 || parenClose < 0) continue;
      
      const paramsStr = fullConstr.substring(parenOpen + 1, parenClose);
      const allParams = paramsStr.split(',').map(p => p.trim());
      
      // If it already has 9+ params, skip (already fixed)
      if (allParams.length >= 9) {
        console.log(`  Already has ${allParams.length} params, skipping: ${path.basename(filePath)} line ~${i+1}`);
        continue;
      }

      // The old pattern has 5 params: modeManager, logger, mfs, dtts, sss
      // We need to add: folderTreeService, collectionTreeService, keywordTreeService, categoryTreeService
      // Find the variable names for the new services
      let ft = "folderTreeService", ct = "collectionTreeService", kt = "keywordTreeService", cat = "categoryTreeService";
      
      // Check if the new service variables exist in the file
      // (they might have slightly different names)
      const fileText = newLines.join(nl);
      // Use the standard names unless we detect different ones
      
      // Replace the constructor call
      const oldCall = allParams[allParams.length - 1]; // Last param (savedSearchService)
      const newParams = paramsStr.trimEnd() + `, ${ft}, ${ct}, ${kt}, ${cat}`;
      const oldCallStr = fullConstr;
      const newCallStr = fullConstr.replace(paramsStr, newParams);

      // Find and replace in the full file text
      // We need to be careful with the exact whitespace
      const fullText = newLines.join(nl);
      const idx = fullText.indexOf(oldCallStr);
      if (idx >= 0) {
        const before = fullText.slice(0, idx);
        const after = fullText.slice(idx + oldCallStr.length);
        const updated = before + newCallStr + after;
        // Split back into lines
        const updatedLines = updated.split(nl);
        // Update newLines reference
        newLines.length = 0;
        newLines.push(...updatedLines);
        console.log(`  Fixed constructor: ${path.basename(filePath)} line ~${i+1}`);
      }
    }
  }

  const finalContent = newLines.join(nl);
  if (finalContent !== content) {
    fs.writeFileSync(filePath, finalContent, "utf8");
    console.log(`  Saved: ${path.basename(filePath)}`);
  } else {
    console.log(`  No changes: ${path.basename(filePath)}`);
  }
}

console.log("Fixing test files (v2)...");
for (const f of files) {
  const filePath = path.join(testDir, f);
  fixFile(filePath);
}
console.log("Done!");
