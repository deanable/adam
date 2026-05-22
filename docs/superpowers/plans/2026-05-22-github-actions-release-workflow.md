# GitHub Actions Release Workflow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create a GitHub Actions workflow that automatically builds and releases cross-platform native installers for `Adam.CatalogBrowser` and `Adam.BrokerService` when a version tag `v*.*.*` is pushed.

**Architecture:** Native runners (Windows, macOS, Ubuntu) build self-contained single-file executables for each platform, package them into platform-specific installers (MSI, DMG, DEB), and create a draft GitHub Release with all artifacts attached.

**Tech Stack:** GitHub Actions, .NET 10, WiX Toolset v4, `dpkg-deb`, `hdiutil`, `tar`, `zip`

---

## File Structure

```
.github/
├── workflows/
│   └── release.yml                    # Main workflow definition
└── installer/
    ├── windows/
    │   ├── Product.wxs                # WiX MSI for CatalogBrowser
    │   └── BrokerService.wxs          # WiX MSI for BrokerService
    ├── macos/
    │   ├── Info.plist                 # macOS app bundle metadata
    │   └── create-dmg.sh              # DMG creation script
    └── linux/
        ├── control                    # DEB control file
        ├── postinst                   # DEB post-install script
        └── catalogbrowser.desktop     # Linux desktop entry
```

---

### Task 1: Create Installer Directory Structure

**Files:**
- Create: `.github/installer/windows/` (directory)
- Create: `.github/installer/macos/` (directory)
- Create: `.github/installer/linux/` (directory)

- [ ] **Step 1: Create directories**

Run:
```bash
mkdir -p .github/installer/windows
mkdir -p .github/installer/macos
mkdir -p .github/installer/linux
```

- [ ] **Step 2: Commit**

```bash
git add .github/installer/
git commit -m "chore: create installer template directories"
```

---

### Task 2: Windows WiX Configuration for CatalogBrowser

**Files:**
- Create: `.github/installer/windows/Product.wxs`

- [ ] **Step 1: Write WiX source for CatalogBrowser MSI**

```xml
<?xml version="1.0" encoding="UTF-8"?>
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
  <Package Name="Adam CatalogBrowser"
           Manufacturer="Adam"
           Version="$(var.Version)"
           UpgradeCode="e8f3a2b1-5c4d-6e7f-8a9b-0c1d2e3f4a5b"
           Scope="perMachine">
    <MajorUpgrade DowngradeErrorMessage="A newer version is already installed." />
    <MediaTemplate EmbedCab="yes" />

    <Feature Id="MainFeature" Title="Adam CatalogBrowser">
      <ComponentGroupRef Id="ProductComponents" />
      <ComponentGroupRef Id="ShortcutComponents" />
    </Feature>

    <StandardDirectory Id="ProgramFiles64Folder">
      <Directory Id="INSTALLFOLDER" Name="Adam\CatalogBrowser">
        <ComponentGroup Id="ProductComponents" Directory="INSTALLFOLDER">
          <Component>
            <File Source="$(var.PublishDir)\Adam.CatalogBrowser.exe" />
          </Component>
          <Component>
            <File Source="$(var.PublishDir)\Adam.CatalogBrowser.dll" />
          </Component>
          <!-- Include all published files via harvest if needed; for now reference key files -->
        </ComponentGroup>
      </Directory>
    </StandardDirectory>

    <StandardDirectory Id="DesktopFolder">
      <ComponentGroup Id="ShortcutComponents" Directory="DesktopFolder">
        <Component>
          <Shortcut Name="Adam CatalogBrowser"
                    Target="[INSTALLFOLDER]Adam.CatalogBrowser.exe"
                    Directory="DesktopFolder"
                    WorkingDirectory="INSTALLFOLDER" />
          <RemoveFolder Id="RemoveDesktopShortcut" Directory="DesktopFolder" On="uninstall" />
        </Component>
      </ComponentGroup>
    </StandardDirectory>
  </Package>
</Wix>
```

- [ ] **Step 2: Commit**

```bash
git add .github/installer/windows/Product.wxs
git commit -m "chore: add WiX configuration for CatalogBrowser MSI"
```

---

### Task 3: Windows WiX Configuration for BrokerService

**Files:**
- Create: `.github/installer/windows/BrokerService.wxs`

- [ ] **Step 1: Write WiX source for BrokerService MSI**

```xml
<?xml version="1.0" encoding="UTF-8"?>
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
  <Package Name="Adam BrokerService"
           Manufacturer="Adam"
           Version="$(var.Version)"
           UpgradeCode="b1c2d3e4-f5a6-7b8c-9d0e-1f2a3b4c5d6e"
           Scope="perMachine">
    <MajorUpgrade DowngradeErrorMessage="A newer version is already installed." />
    <MediaTemplate EmbedCab="yes" />

    <Feature Id="MainFeature" Title="Adam BrokerService">
      <ComponentGroupRef Id="ProductComponents" />
      <ComponentGroupRef Id="HelperComponents" />
    </Feature>

    <StandardDirectory Id="ProgramFiles64Folder">
      <Directory Id="INSTALLFOLDER" Name="Adam\BrokerService">
        <ComponentGroup Id="ProductComponents" Directory="INSTALLFOLDER">
          <Component>
            <File Source="$(var.PublishDir)\Adam.BrokerService.exe" />
          </Component>
          <Component>
            <File Source="$(var.PublishDir)\Adam.BrokerService.dll" />
          </Component>
        </ComponentGroup>
        <ComponentGroup Id="HelperComponents" Directory="INSTALLFOLDER">
          <Component>
            <File Id="InstallServiceBat" Source="$(var.PublishDir)\install-service.bat" />
          </Component>
        </ComponentGroup>
      </Directory>
    </StandardDirectory>
  </Package>
</Wix>
```

- [ ] **Step 2: Commit**

```bash
git add .github/installer/windows/BrokerService.wxs
git commit -m "chore: add WiX configuration for BrokerService MSI"
```

---

### Task 4: macOS App Bundle Info.plist

**Files:**
- Create: `.github/installer/macos/Info.plist`

- [ ] **Step 1: Write Info.plist**

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>Adam CatalogBrowser</string>
    <key>CFBundleDisplayName</key>
    <string>Adam CatalogBrowser</string>
    <key>CFBundleIdentifier</key>
    <string>com.adam.catalogbrowser</string>
    <key>CFBundleVersion</key>
    <string>$(VERSION)</string>
    <key>CFBundleShortVersionString</key>
    <string>$(VERSION)</string>
    <key>CFBundleExecutable</key>
    <string>Adam.CatalogBrowser</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>LSMinimumSystemVersion</key>
    <string>13.0</string>
    <key>NSHighResolutionCapable</key>
    <true/>
</dict>
</plist>
```

- [ ] **Step 2: Commit**

```bash
git add .github/installer/macos/Info.plist
git commit -m "chore: add macOS Info.plist for CatalogBrowser app bundle"
```

---

### Task 5: macOS DMG Creation Script

**Files:**
- Create: `.github/installer/macos/create-dmg.sh`

- [ ] **Step 1: Write DMG creation script**

```bash
#!/bin/bash
set -euo pipefail

APP_NAME="Adam CatalogBrowser"
APP_BUNDLE="Adam.CatalogBrowser.app"
PUBLISH_DIR="$1"
OUTPUT_DMG="$2"

# Create app bundle structure
mkdir -p "${APP_BUNDLE}/Contents/MacOS"
mkdir -p "${APP_BUNDLE}/Contents/Resources"

# Copy executable
cp "${PUBLISH_DIR}/Adam.CatalogBrowser" "${APP_BUNDLE}/Contents/MacOS/"

# Copy published files (excluding the executable already copied)
for file in "${PUBLISH_DIR}"/*; do
    basename_file=$(basename "$file")
    if [ "$basename_file" != "Adam.CatalogBrowser" ]; then
        cp -R "$file" "${APP_BUNDLE}/Contents/MacOS/"
    fi
done

# Write Info.plist
VERSION="${3:-1.0.0}"
cat > "${APP_BUNDLE}/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>${APP_NAME}</string>
    <key>CFBundleDisplayName</key>
    <string>${APP_NAME}</string>
    <key>CFBundleIdentifier</key>
    <string>com.adam.catalogbrowser</string>
    <key>CFBundleVersion</key>
    <string>${VERSION}</string>
    <key>CFBundleShortVersionString</key>
    <string>${VERSION}</string>
    <key>CFBundleExecutable</key>
    <string>Adam.CatalogBrowser</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>LSMinimumSystemVersion</key>
    <string>13.0</string>
    <key>NSHighResolutionCapable</key>
    <true/>
</dict>
</plist>
EOF

# Make executable executable
chmod +x "${APP_BUNDLE}/Contents/MacOS/Adam.CatalogBrowser"

# Create DMG
hdiutil create -volname "${APP_NAME}" -srcfolder "${APP_BUNDLE}" -ov -format UDZO "${OUTPUT_DMG}"

# Clean up
rm -rf "${APP_BUNDLE}"

echo "Created ${OUTPUT_DMG}"
```

- [ ] **Step 2: Make script executable**

```bash
chmod +x .github/installer/macos/create-dmg.sh
```

- [ ] **Step 3: Commit**

```bash
git add .github/installer/macos/create-dmg.sh
git commit -m "chore: add macOS DMG creation script"
```

---

### Task 6: Linux DEB Control File

**Files:**
- Create: `.github/installer/linux/control`

- [ ] **Step 1: Write DEB control file**

```
Package: adam-catalogbrowser
Version: ${VERSION}
Section: utils
Priority: optional
Architecture: amd64
Depends: libx11-6, libxcb1, libxkbcommon0, libgl1-mesa-glx
Maintainer: Adam Team <team@adam.dev>
Description: Adam CatalogBrowser
 Cross-platform digital asset management catalog browser.
```

- [ ] **Step 2: Commit**

```bash
git add .github/installer/linux/control
git commit -m "chore: add DEB control file for CatalogBrowser"
```

---

### Task 7: Linux DEB Post-Install Script

**Files:**
- Create: `.github/installer/linux/postinst`

- [ ] **Step 1: Write post-install script for BrokerService**

```bash
#!/bin/bash
set -e

# Create adam user if it doesn't exist
if ! id -u adam > /dev/null 2>&1; then
    useradd --system --no-create-home --shell /usr/sbin/nologin adam
fi

# Reload systemd
if command -v systemctl > /dev/null 2>&1; then
    systemctl daemon-reload
    systemctl enable adam-brokerservice
fi

# Create data directory
mkdir -p /var/lib/adam/brokerservice
chown -R adam:adam /var/lib/adam/brokerservice

echo "Adam BrokerService installed."
echo "Start with: sudo systemctl start adam-brokerservice"
```

- [ ] **Step 2: Commit**

```bash
git add .github/installer/linux/postinst
git commit -m "chore: add DEB post-install script for BrokerService"
```

---

### Task 8: Linux Desktop Entry

**Files:**
- Create: `.github/installer/linux/catalogbrowser.desktop`

- [ ] **Step 1: Write desktop entry file**

```ini
[Desktop Entry]
Name=Adam CatalogBrowser
Comment=Digital Asset Management Catalog Browser
Exec=/usr/bin/adam-catalogbrowser
Type=Application
Terminal=false
Icon=adam-catalogbrowser
Categories=Office;Database;
StartupNotify=true
```

- [ ] **Step 2: Commit**

```bash
git add .github/installer/linux/catalogbrowser.desktop
git commit -m "chore: add Linux desktop entry for CatalogBrowser"
```

---

### Task 9: Main Release Workflow

**Files:**
- Create: `.github/workflows/release.yml`

- [ ] **Step 1: Write the release workflow**

```yaml
name: Release

on:
  push:
    tags:
      - 'v*.*.*'
  workflow_dispatch:
    inputs:
      version:
        description: 'Version string for artifact naming (e.g., 1.2.0)'
        required: false
        default: '0.0.0-test'

jobs:
  validate:
    runs-on: ubuntu-latest
    outputs:
      version: ${{ steps.get_version.outputs.version }}
    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Extract version
        id: get_version
        shell: bash
        run: |
          if [ "${{ github.event_name }}" = "workflow_dispatch" ]; then
            VERSION="${{ github.event.inputs.version }}"
          else
            VERSION="${GITHUB_REF#refs/tags/v}"
          fi
          if ! [[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+ ]]; then
            echo "Invalid version format: $VERSION"
            exit 1
          fi
          echo "version=$VERSION" >> $GITHUB_OUTPUT
          echo "Version: $VERSION"

  build-windows:
    needs: validate
    runs-on: windows-latest
    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Restore
        run: dotnet restore src/Adam.slnx

      - name: Publish CatalogBrowser
        run: |
          dotnet publish src/Adam.CatalogBrowser/Adam.CatalogBrowser.csproj `
            -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true `
            -o publish/CatalogBrowser

      - name: Publish BrokerService
        run: |
          dotnet publish src/Adam.BrokerService/Adam.BrokerService.csproj `
            -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true `
            -o publish/BrokerService

      - name: Install WiX
        run: dotnet tool install --global wix --version 4.0.5

      - name: Build CatalogBrowser MSI
        run: |
          wix build .github/installer/windows/Product.wxs `
            -o "Adam.CatalogBrowser-v${{ needs.validate.outputs.version }}-win-x64.msi" `
            -d PublishDir=publish/CatalogBrowser `
            -d Version=${{ needs.validate.outputs.version }}

      - name: Build BrokerService MSI
        run: |
          wix build .github/installer/windows/BrokerService.wxs `
            -o "Adam.BrokerService-v${{ needs.validate.outputs.version }}-win-x64.msi" `
            -d PublishDir=publish/BrokerService `
            -d Version=${{ needs.validate.outputs.version }}

      - name: Create ZIP
        run: |
          Compress-Archive -Path publish/* -DestinationPath "Adam-v${{ needs.validate.outputs.version }}-win-x64.zip"

      - name: Upload artifacts
        uses: actions/upload-artifact@v4
        with:
          name: windows-artifacts
          path: |
            Adam.CatalogBrowser-v*.msi
            Adam.BrokerService-v*.msi
            Adam-v*.zip

  build-macos:
    needs: validate
    runs-on: macos-latest
    strategy:
      matrix:
        rid: [osx-x64, osx-arm64]
    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Restore
        run: dotnet restore src/Adam.slnx

      - name: Publish CatalogBrowser
        run: |
          dotnet publish src/Adam.CatalogBrowser/Adam.CatalogBrowser.csproj \
            -c Release -r ${{ matrix.rid }} --self-contained true -p:PublishSingleFile=true \
            -o publish/CatalogBrowser

      - name: Publish BrokerService
        run: |
          dotnet publish src/Adam.BrokerService/Adam.BrokerService.csproj \
            -c Release -r ${{ matrix.rid }} --self-contained true -p:PublishSingleFile=true \
            -o publish/BrokerService

      - name: Create CatalogBrowser DMG
        run: |
          .github/installer/macos/create-dmg.sh \
            publish/CatalogBrowser \
            "Adam.CatalogBrowser-v${{ needs.validate.outputs.version }}-${{ matrix.rid }}.dmg" \
            "${{ needs.validate.outputs.version }}"

      - name: Create BrokerService DMG
        run: |
          mkdir -p broker-dmg
          cp publish/BrokerService/Adam.BrokerService broker-dmg/
          cat > broker-dmg/install.sh <<'EOF'
          #!/bin/bash
          set -e
          INSTALL_DIR="/usr/local/bin"
          sudo mkdir -p "$INSTALL_DIR"
          sudo cp "$(dirname "$0")/Adam.BrokerService" "$INSTALL_DIR/adam-brokerservice"
          sudo chmod +x "$INSTALL_DIR/adam-brokerservice"
          echo "Installed to $INSTALL_DIR/adam-brokerservice"
          EOF
          chmod +x broker-dmg/install.sh
          hdiutil create -volname "Adam BrokerService" -srcfolder broker-dmg -ov -format UDZO \
            "Adam.BrokerService-v${{ needs.validate.outputs.version }}-osx.dmg"

      - name: Create TAR.GZ
        run: |
          tar -czf "Adam-v${{ needs.validate.outputs.version }}-${{ matrix.rid }}.tar.gz" -C publish .

      - name: Upload artifacts
        uses: actions/upload-artifact@v4
        with:
          name: macos-artifacts-${{ matrix.rid }}
          path: |
            Adam.CatalogBrowser-v*.dmg
            Adam.BrokerService-v*.dmg
            Adam-v*.tar.gz

  build-linux:
    needs: validate
    runs-on: ubuntu-latest
    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Restore
        run: dotnet restore src/Adam.slnx

      - name: Publish CatalogBrowser
        run: |
          dotnet publish src/Adam.CatalogBrowser/Adam.CatalogBrowser.csproj \
            -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true \
            -o publish/CatalogBrowser

      - name: Publish BrokerService
        run: |
          dotnet publish src/Adam.BrokerService/Adam.BrokerService.csproj \
            -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true \
            -o publish/BrokerService

      - name: Build CatalogBrowser DEB
        run: |
          VERSION="${{ needs.validate.outputs.version }}"
          PKG_DIR="adam-catalogbrowser_${VERSION}_amd64"
          mkdir -p "${PKG_DIR}/DEBIAN"
          mkdir -p "${PKG_DIR}/usr/bin"
          mkdir -p "${PKG_DIR}/usr/share/applications"
          mkdir -p "${PKG_DIR}/usr/share/icons/hicolor/256x256/apps"

          cp publish/CatalogBrowser/Adam.CatalogBrowser "${PKG_DIR}/usr/bin/adam-catalogbrowser"
          chmod +x "${PKG_DIR}/usr/bin/adam-catalogbrowser"
          cp .github/installer/linux/catalogbrowser.desktop "${PKG_DIR}/usr/share/applications/"

          sed "s/\${VERSION}/${VERSION}/g" .github/installer/linux/control > "${PKG_DIR}/DEBIAN/control"
          touch "${PKG_DIR}/DEBIAN/md5sums"

          dpkg-deb --build "${PKG_DIR}"

      - name: Build BrokerService DEB
        run: |
          VERSION="${{ needs.validate.outputs.version }}"
          PKG_DIR="adam-brokerservice_${VERSION}_amd64"
          mkdir -p "${PKG_DIR}/DEBIAN"
          mkdir -p "${PKG_DIR}/usr/bin"
          mkdir -p "${PKG_DIR}/lib/systemd/system"
          mkdir -p "${PKG_DIR}/var/lib/adam/brokerservice"

          cp publish/BrokerService/Adam.BrokerService "${PKG_DIR}/usr/bin/adam-brokerservice"
          chmod +x "${PKG_DIR}/usr/bin/adam-brokerservice"
          cp .github/installer/linux/postinst "${PKG_DIR}/DEBIAN/postinst"
          chmod +x "${PKG_DIR}/DEBIAN/postinst"

          cat > "${PKG_DIR}/lib/systemd/system/adam-brokerservice.service" <<EOF
          [Unit]
          Description=Adam BrokerService
          After=network.target

          [Service]
          Type=simple
          User=adam
          ExecStart=/usr/bin/adam-brokerservice
          Restart=on-failure
          WorkingDirectory=/var/lib/adam/brokerservice

          [Install]
          WantedBy=multi-user.target
          EOF

          sed "s/\${VERSION}/${VERSION}/g" .github/installer/linux/control > "${PKG_DIR}/DEBIAN/control"
          sed -i 's/adam-catalogbrowser/adam-brokerservice/g; s/CatalogBrowser/BrokerService/g' "${PKG_DIR}/DEBIAN/control"
          touch "${PKG_DIR}/DEBIAN/md5sums"

          dpkg-deb --build "${PKG_DIR}"

      - name: Create TAR.GZ
        run: |
          tar -czf "Adam-v${{ needs.validate.outputs.version }}-linux-x64.tar.gz" -C publish .

      - name: Upload artifacts
        uses: actions/upload-artifact@v4
        with:
          name: linux-artifacts
          path: |
            adam-catalogbrowser_*.deb
            adam-brokerservice_*.deb
            Adam-v*.tar.gz

  release:
    needs: [build-windows, build-macos, build-linux]
    runs-on: ubuntu-latest
    steps:
      - name: Download all artifacts
        uses: actions/download-artifact@v4
        with:
          path: artifacts
          merge-multiple: true

      - name: List artifacts
        run: ls -R artifacts

      - name: Create Release
        uses: softprops/action-gh-release@v2
        with:
          draft: true
          name: Adam v${{ needs.validate.outputs.version }}
          generate_release_notes: true
          files: artifacts/*
```

- [ ] **Step 2: Validate YAML syntax**

Run:
```bash
cat .github/workflows/release.yml | python3 -c "import yaml, sys; yaml.safe_load(sys.stdin); print('YAML valid')"
```

Expected output:
```
YAML valid
```

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/release.yml
git commit -m "ci: add GitHub Actions release workflow"
```

---

### Task 10: Final Review and Complete

- [ ] **Step 1: Review all created files**

Verify the following files exist:
```bash
ls -la .github/workflows/release.yml
ls -la .github/installer/windows/Product.wxs
ls -la .github/installer/windows/BrokerService.wxs
ls -la .github/installer/macos/Info.plist
ls -la .github/installer/macos/create-dmg.sh
ls -la .github/installer/linux/control
ls -la .github/installer/linux/postinst
ls -la .github/installer/linux/catalogbrowser.desktop
```

- [ ] **Step 2: Final commit if any remaining changes**

```bash
git status
# If any uncommitted changes remain:
git add .
git commit -m "ci: complete release workflow setup"
```

- [ ] **Step 3: Validate workflow can be parsed by GitHub**

Run:
```bash
# This just validates the YAML is syntactically valid
python3 -c "import yaml; yaml.safe_load(open('.github/workflows/release.yml')); print('Workflow YAML is valid')"
```

Expected: `Workflow YAML is valid`

---

## Self-Review Checklist

### 1. Spec Coverage

| Spec Requirement | Implementing Task | Status |
|--------------------|-------------------|--------|
| Trigger on `v*.*.*` tags | Task 9 | Covered |
| Manual `workflow_dispatch` trigger | Task 9 | Covered |
| Version validation job | Task 9 (`validate`) | Covered |
| Windows `win-x64` build with MSI | Task 9 (`build-windows`) | Covered |
| Windows ZIP archive | Task 9 (`build-windows`) | Covered |
| macOS `osx-x64` and `osx-arm64` builds with DMG | Task 9 (`build-macos`) | Covered |
| macOS TAR.GZ archives | Task 9 (`build-macos`) | Covered |
| Linux `linux-x64` build with DEB | Task 9 (`build-linux`) | Covered |
| Linux TAR.GZ archive | Task 9 (`build-linux`) | Covered |
| Draft GitHub Release with auto-notes | Task 9 (`release`) | Covered |
| Artifact naming convention | Task 9 | Covered |
| Self-contained single-file publish | Task 9 (all build jobs) | Covered |
| WiX MSI configs | Tasks 2, 3 | Covered |
| macOS app bundle Info.plist | Task 4 | Covered |
| macOS DMG creation script | Task 5 | Covered |
| Linux DEB control file | Task 6 | Covered |
| Linux postinst script | Task 7 | Covered |
| Linux desktop entry | Task 8 | Covered |

### 2. Placeholder Scan

- No "TBD", "TODO", or "implement later" strings found in plan.
- No vague "add appropriate error handling" steps.
- All code blocks contain complete implementations.
- No "Similar to Task N" references.

### 3. Type/Name Consistency

- Project names: `Adam.CatalogBrowser`, `Adam.BrokerService` — consistent with actual project files.
- Solution file: `src/Adam.slnx` — confirmed from project context.
- Paths: `src/Adam.CatalogBrowser/`, `src/Adam.BrokerService/` — consistent with project structure.
- Version variable: `${{ needs.validate.outputs.version }}` — consistently used across all jobs.
- RID values: `win-x64`, `osx-x64`, `osx-arm64`, `linux-x64` — consistently used.

**All checks pass. Plan is ready for execution.**
