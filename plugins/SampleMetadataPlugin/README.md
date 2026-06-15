# Sample Metadata Plugin

This sample project demonstrates how to create a custom metadata extractor plugin for ADAM.

## How it works

The plugin implements `IMetadataExtractor` from `Adam.Shared.Extractors`:

- **`Priority`** — Controls the order in which extractors are tried. Lower = tried first. Built-in extractors use 100-200. Plugins should use 1000+.
- **`CanExtract(filePath, mimeType)`** — Returns true when this extractor can handle the given file.
- **`ExtractTextAsync(filePath, ct)`** — Extracts text metadata (Title, Description, Keywords).
- **`ExtractAsync(filePath, ct)`** — Extracts rich metadata profile (EXIF, XMP, GPS). Return null if not applicable.

## Building

```bash
cd plugins/SampleMetadataPlugin
dotnet build -c Release
```

## Installing

1. Build the project (see above)
2. Copy the output DLL to the plugin directory:
   - **Windows**: `%LOCALAPPDATA%/Adam/plugins/`
   - **Linux/macOS**: `~/.local/share/Adam/plugins/`
3. Restart ADAM (or click "Refresh" in Plugin Manager)

The system will automatically discover and load all `IMetadataExtractor` implementations from all DLLs in the plugin directory.

## Creating your own plugin

1. Create a new Class Library project targeting `net10.0`
2. Add a reference to `Adam.Shared.csproj`
3. Implement `IMetadataExtractor` on a public class with a parameterless constructor
4. Build and copy the DLL to the plugin directory

## Priority conventions

| Priority | Type | Description |
|----------|------|-------------|
| 100 | Built-in | Image EXIF/XMP Extractor |
| 200 | Built-in | Office Document Extractor |
| 1000+ | Plugin | Third-party extractors |

The priority chain ensures that:
- Built-in extractors always handle their file types first
- Plugins can handle file types that built-in extractors don't support
- If an extractor returns null (no relevant metadata found), the next-highest-priority extractor gets a chance
