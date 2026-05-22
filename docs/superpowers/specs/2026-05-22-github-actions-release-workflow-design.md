# Design: GitHub Actions Release Workflow

**Date**: 2026-05-22
**Topic**: GitHub Actions Release Workflow for Adam
**Branch Context**: Main repository (`adam`)

## Summary

Create a GitHub Actions workflow that automatically builds and releases cross-platform native installers for both `Adam.CatalogBrowser` (Avalonia desktop app) and `Adam.BrokerService` (console service) when a version tag matching `v*.*.*` is pushed.

## Trigger

The workflow runs on:
- `push` events where the ref matches tag pattern `v*.*.*` (e.g., `v1.2.0`)
- `workflow_dispatch` (manual trigger) with an optional input version string for testing

## Workflow Structure

The workflow consists of five jobs:

| Job | Runner | Purpose |
|-----|--------|---------|
| `validate` | `ubuntu-latest` | Parse tag, extract version, validate format |
| `build-windows` | `windows-latest` | Build both apps for `win-x64`, create `.msi` and `.zip` |
| `build-macos` | `macos-latest` | Build both apps for `osx-x64` and `osx-arm64`, create `.dmg` and `.tar.gz` |
| `build-linux` | `ubuntu-latest` | Build both apps for `linux-x64`, create `.deb` and `.tar.gz` |
| `release` | `ubuntu-latest` | Collect all artifacts, create draft GitHub Release, attach everything |

All three `build-*` jobs run in parallel. The `release` job depends on all three succeeding.

## Build Steps (Per Platform)

Each build job performs the following:

1. Checkout source code
2. Setup .NET 10 SDK
3. Restore NuGet packages (`dotnet restore`)
4. Publish `Adam.CatalogBrowser` in Release mode with self-contained single-file executable
5. Publish `Adam.BrokerService` in Release mode with self-contained single-file executable
6. Package each app into the platform-specific installer format
7. Create a ZIP/TAR.GZ archive containing both published apps
8. Upload all artifacts using `actions/upload-artifact`

### Publishing Parameters

All publishes use:
```
dotnet publish -c Release -r <RID> --self-contained true -p:PublishSingleFile=true
```

## Platform Details

### Windows (`windows-latest`)

- **RID**: `win-x64`
- **Apps**: `Adam.CatalogBrowser.exe` (Avalonia desktop), `Adam.BrokerService.exe` (console)
- **Installer**: WiX Toolset v4
  - `Adam.CatalogBrowser-{version}-win-x64.msi`
    - Installs to `Program Files\Adam\CatalogBrowser`
    - Creates desktop shortcut
  - `Adam.BrokerService-{version}-win-x64.msi`
    - Installs to `Program Files\Adam\BrokerService`
    - Includes a service registration helper batch script
- **ZIP**: `Adam-{version}-win-x64.zip`
  - Contains `CatalogBrowser/` and `BrokerService/` subdirectories with published binaries

### macOS (`macos-latest`)

- **RIDs**: `osx-x64` and `osx-arm64` (separate builds)
- **Apps**: `Adam.CatalogBrowser` (Avalonia desktop), `Adam.BrokerService` (console)
- **Installers**: `.dmg`
  - `Adam.CatalogBrowser-{version}-osx-x64.dmg` and `-osx-arm64.dmg`
    - Contains `.app` bundle with `Info.plist`, app icon, and executable
    - Drag-and-drop to `/Applications`
  - `Adam.BrokerService-{version}-osx.dmg`
    - Contains the executable + `install.sh` script that creates a `launchd` plist
- **Archives**: `Adam-{version}-osx-x64.tar.gz` and `Adam-{version}-osx-arm64.tar.gz`
  - Contains published binaries in `CatalogBrowser/` and `BrokerService/` directories

### Linux (`ubuntu-latest`)

- **RID**: `linux-x64`
- **Apps**: `Adam.CatalogBrowser` (Avalonia desktop), `Adam.BrokerService` (console)
- **Installers**: `.deb`
  - `adam-catalogbrowser_{version}_amd64.deb`
    - Binary installed to `/usr/bin/adam-catalogbrowser`
    - `.desktop` file for app launcher
    - Icon installed to `/usr/share/icons/hicolor/`
  - `adam-brokerservice_{version}_amd64.deb`
    - Binary installed to `/usr/bin/adam-brokerservice`
    - systemd unit file for service management
- **Archive**: `Adam-{version}-linux-x64.tar.gz`
  - Contains published binaries in `CatalogBrowser/` and `BrokerService/` directories

## Release Job

The `release` job:

1. Waits for `build-windows`, `build-macos`, and `build-linux` to succeed
2. Downloads all artifacts using `actions/download-artifact`
3. Creates a **draft** GitHub Release using `softprops/action-gh-release`
   - Tag: the pushed tag
   - Name: `Adam v{version}`
   - Body: auto-generated release notes from merged PRs since last tag
4. Attaches all artifacts to the release
5. Does **not** publish the release automatically (human review required)

### Artifact Naming Convention

```
Adam.CatalogBrowser-v1.2.0-win-x64.msi
Adam.BrokerService-v1.2.0-win-x64.msi
Adam-v1.2.0-win-x64.zip
Adam.CatalogBrowser-v1.2.0-osx-x64.dmg
Adam.CatalogBrowser-v1.2.0-osx-arm64.dmg
Adam.BrokerService-v1.2.0-osx.dmg
Adam-v1.2.0-osx-x64.tar.gz
Adam-v1.2.0-osx-arm64.tar.gz
adam-catalogbrowser_1.2.0_amd64.deb
adam-brokerservice_1.2.0_amd64.deb
Adam-v1.2.0-linux-x64.tar.gz
```

## Error Handling

- **Tag validation**: The `validate` job checks the tag against `^v\d+\.\d+\.\d+$`. Invalid tags fail immediately.
- **Build failures**: Any platform job failure blocks the `release` job (strict dependency chain).
- **Missing artifacts**: The `release` job fails with a clear error message if expected artifacts are missing.
- **Manual testing**: `workflow_dispatch` allows running the entire workflow against a branch without pushing a tag, using an input version string for artifact naming.

## Testing Strategy

1. Create a test tag `v0.0.0-test` on a non-main branch
2. Push the tag and verify the workflow triggers
3. Download artifacts from the draft release
4. Verify each artifact:
   - Windows MSI: installs and apps launch
   - macOS DMG: mounts, `.app` bundle runs, BrokerService installs via script
   - Linux DEB: installs with `dpkg`, apps launch, systemd unit works
   - ZIPs/TARs: extract and run binaries directly
5. Delete test tag and draft release after validation

## Files to Create

- `.github/workflows/release.yml` — The main workflow definition
- `.github/workflows/installer/windows/Product.wxs` — WiX MSI configuration for CatalogBrowser
- `.github/workflows/installer/windows/BrokerService.wxs` — WiX MSI configuration for BrokerService
- `.github/workflows/installer/macos/Info.plist` — macOS `.app` bundle metadata
- `.github/workflows/installer/macos/create-dmg.sh` — DMG creation script
- `.github/workflows/installer/linux/control` — DEB control files
- `.github/workflows/installer/linux/postinst` — DEB post-install script for BrokerService

## Dependencies

- GitHub-hosted runners: `windows-latest`, `macos-latest`, `ubuntu-latest`
- Actions: `actions/checkout@v4`, `actions/setup-dotnet@v4`, `actions/upload-artifact@v4`, `actions/download-artifact@v4`, `softprops/action-gh-release@v2`
- WiX Toolset v4 (installed via `dotnet tool install` on Windows runner)
- `dpkg-deb` (pre-installed on `ubuntu-latest`)
- `hdiutil` (pre-installed on `macos-latest`)
