#!/bin/bash
set -euo pipefail
BASE="$(cd "$(dirname "$0")/.." && pwd)"
APP="$BASE/app/P2P Clipboard.app"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources" "$BASE/.build/module-cache"
xcrun swiftc "$BASE/src/main.swift" -O -framework AppKit -module-cache-path "$BASE/.build/module-cache" -o "$APP/Contents/MacOS/P2PClipboard"
cp "$BASE/bin/p2p-clipboard" "$APP/Contents/Resources/p2p-clipboard"
cat > "$APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleExecutable</key><string>P2PClipboard</string>
<key>CFBundleIdentifier</key><string>local.p2pclipboard.launcher</string>
<key>CFBundleName</key><string>P2P Clipboard</string>
<key>CFBundleDisplayName</key><string>P2P Clipboard</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleShortVersionString</key><string>1.0.0</string>
<key>CFBundleVersion</key><string>1</string>
<key>LSMinimumSystemVersion</key><string>11.0</string>
<key>LSUIElement</key><true/>
<key>NSHighResolutionCapable</key><true/>
</dict></plist>
PLIST
/usr/bin/codesign --force --sign - "$APP/Contents/Resources/p2p-clipboard"
/usr/bin/codesign --force --sign - "$APP"
/usr/bin/plutil -lint "$APP/Contents/Info.plist"
/usr/bin/codesign --verify --deep --strict "$APP"
printf 'Built: %s\n' "$APP"
