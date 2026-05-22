#!/usr/bin/env bash
set -euo pipefail

if [ $# -ne 3 ]; then
    echo "Usage: $0 <publish-dir> <output-dmg> <version>"
    exit 1
fi

PUBLISH_DIR="${1}"
OUTPUT_DMG="${2}"
VERSION="${3}"

if [ ! -d "${PUBLISH_DIR}" ]; then
    echo "Error: Publish directory not found: ${PUBLISH_DIR}"
    exit 1
fi
if [ ! -f "${PUBLISH_DIR}/Adam.CatalogBrowser" ]; then
    echo "Error: Executable not found: ${PUBLISH_DIR}/Adam.CatalogBrowser"
    exit 1
fi

APP_BUNDLE="Adam.CatalogBrowser.app"
CONTENTS="${APP_BUNDLE}/Contents"
MACOS="${CONTENTS}/MacOS"
RESOURCES="${CONTENTS}/Resources"

echo "Creating macOS app bundle for Adam.CatalogBrowser v${VERSION}..."

# 1. Create .app bundle structure
trap 'rm -rf "${APP_BUNDLE}"' EXIT
rm -rf "${APP_BUNDLE}"
mkdir -p "${MACOS}"
mkdir -p "${RESOURCES}"

# 2. Copy the executable into Contents/MacOS/
cp "${PUBLISH_DIR}/Adam.CatalogBrowser" "${MACOS}/"

# 3. Copy all other published files into Contents/MacOS/
for file in "${PUBLISH_DIR}"/*; do
    basename_file=$(basename "${file}")
    if [ "${basename_file}" != "Adam.CatalogBrowser" ]; then
        cp -R "${file}" "${MACOS}/"
    fi
done

# 4. Generate Info.plist dynamically with the provided version
cat > "${CONTENTS}/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleExecutable</key>
    <string>Adam.CatalogBrowser</string>
    <key>CFBundleIdentifier</key>
    <string>com.adam.catalogbrowser</string>
    <key>CFBundleName</key>
    <string>Adam CatalogBrowser</string>
    <key>CFBundleDisplayName</key>
    <string>Adam CatalogBrowser</string>
    <key>CFBundleVersion</key>
    <string>${VERSION}</string>
    <key>CFBundleShortVersionString</key>
    <string>${VERSION}</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>LSMinimumSystemVersion</key>
    <string>10.15</string>
</dict>
</plist>
EOF

# 5. Make the executable executable
chmod +x "${MACOS}/Adam.CatalogBrowser"

# 6. Create DMG using hdiutil
echo "Creating DMG: ${OUTPUT_DMG}..."
hdiutil create -volname "Adam CatalogBrowser" -srcfolder "${APP_BUNDLE}" -ov -format UDZO "${OUTPUT_DMG}"

echo "Successfully created: ${OUTPUT_DMG}"
