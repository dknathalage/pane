#!/usr/bin/env bash
# Assemble a self-contained Pane.app bundle (+ plugins/) for a target RID.
# Self-contained means the .NET runtime is bundled inside the app, so the
# installed app needs no dotnet / Homebrew on PATH — which is exactly what a
# downloadable release requires.
#
# Usage: build/make-app.sh [RID] [OUTDIR]
#   RID     defaults to this machine's arch (osx-arm64 / osx-x64)
#   OUTDIR  defaults to <repo>/dist ; produces $OUTDIR/Pane.app and $OUTDIR/plugins
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
RID="${1:-$(uname -m | sed 's/^arm64$/osx-arm64/; s/^x86_64$/osx-x64/')}"
OUTDIR="${2:-$ROOT/dist}"
APP="$OUTDIR/Pane.app"
PUBLISH="$OUTDIR/.publish-$RID"
PLUGINS_OUT="$OUTDIR/plugins"

echo "==> Publishing self-contained Pane.App ($RID)"
rm -rf "$APP" "$PUBLISH"
dotnet publish "$ROOT/src/Pane.App" -c Release -r "$RID" --self-contained true \
  -p:PublishSingleFile=false -o "$PUBLISH" --nologo -v q

echo "==> Assembling $APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$PUBLISH/." "$APP/Contents/MacOS/"

ICON_LINE=""
if [ -f "$ROOT/build/Pane.icns" ]; then
  cp "$ROOT/build/Pane.icns" "$APP/Contents/Resources/"
  ICON_LINE="  <key>CFBundleIconFile</key><string>Pane.icns</string>"
fi

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Pane</string>
  <key>CFBundleDisplayName</key><string>Pane</string>
  <key>CFBundleIdentifier</key><string>com.pane.launcher</string>
  <key>CFBundleVersion</key><string>1.0</string>
  <key>CFBundleShortVersionString</key><string>1.0</string>
  <key>CFBundleExecutable</key><string>Pane.App</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>LSMinimumSystemVersion</key><string>11.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <!-- Menu-bar/background agent: no Dock icon, driven by the global hotkey. -->
  <key>LSUIElement</key><true/>
$ICON_LINE
</dict>
</plist>
PLIST

echo "==> Building + staging plugins → $PLUGINS_OUT"
rm -rf "$PLUGINS_OUT"
for p in Scripts VSCode Apps Calculator; do
  dotnet build "$ROOT/src/Pane.Plugins.$p" -c Release --nologo -v q >/dev/null
  mkdir -p "$PLUGINS_OUT/Pane.Plugins.$p"
  cp "$ROOT/src/Pane.Plugins.$p/bin/Release/net10.0/Pane.Plugins.$p.dll" \
     "$PLUGINS_OUT/Pane.Plugins.$p/"
done

rm -rf "$PUBLISH"
echo "==> Done: $APP"
echo "         $PLUGINS_OUT"
