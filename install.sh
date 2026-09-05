#!/usr/bin/env bash
# Install Pane as a proper macOS app + background login agent.
#
#   ./install.sh            Download the latest prebuilt release from the repo
#   ./install.sh --local    Build from this checkout instead
#
# Either way it installs Pane.app to ~/Applications, registers a LaunchAgent
# so the global hotkey is live at login, and launches the app.
#
# Env overrides:
#   PANE_REPO   GitHub repo to download releases from (default dknathalage/pane)
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
REPO="${PANE_REPO:-dknathalage/pane}"
APPHOME="$HOME/Applications"
APP="$APPHOME/Pane.app"
EXEC="$APP/Contents/MacOS/Pane.App"          # CFBundleExecutable (self-contained apphost)
LABEL="com.pane.launcher"
PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"
RID="$(uname -m | sed 's/^arm64$/osx-arm64/; s/^x86_64$/osx-x64/')"

LOCAL=0
[ "${1:-}" = "--local" ] && LOCAL=1

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

if [ "$LOCAL" = 1 ]; then
  echo "==> Building Pane from local source ($RID)"
  "$ROOT/build/make-app.sh" "$RID" "$TMP/dist"
else
  ASSET="Pane-$RID.zip"
  echo "==> Downloading latest release asset $ASSET from $REPO"
  if command -v gh >/dev/null 2>&1; then
    gh release download -R "$REPO" -p "$ASSET" -D "$TMP" --clobber
  else
    URL="$(curl -fsSL "https://api.github.com/repos/$REPO/releases/latest" \
      | grep -o "https://[^\"]*/$ASSET" | head -1)"
    if [ -z "$URL" ]; then
      echo "error: no asset '$ASSET' on $REPO/releases/latest." >&2
      echo "       Publish a release first (build/package-release.sh), or run: ./install.sh --local" >&2
      exit 1
    fi
    curl -fsSL "$URL" -o "$TMP/$ASSET"
  fi
  mkdir -p "$TMP/dist"
  unzip -q "$TMP/$ASSET" -d "$TMP/dist"
fi

SRC_APP="$TMP/dist/Pane.app"
[ -d "$SRC_APP" ] || { echo "error: no Pane.app produced/downloaded" >&2; exit 1; }

echo "==> Stopping any running Pane"
pkill -f "Pane.App" 2>/dev/null || true
launchctl bootout "gui/$(id -u)/$LABEL" 2>/dev/null || true
sleep 1

echo "==> Installing app → $APP"
mkdir -p "$APPHOME"
rm -rf "$APP"
cp -R "$SRC_APP" "$APP"
# Downloaded bundles are quarantined; clear it so Gatekeeper doesn't block launch.
xattr -dr com.apple.quarantine "$APP" 2>/dev/null || true

echo "==> Registering LaunchAgent → $PLIST"
mkdir -p "$(dirname "$PLIST")"
cat > "$PLIST" <<PLISTEOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key><string>$LABEL</string>
  <key>ProgramArguments</key>
  <array>
    <string>$EXEC</string>
    <string>--startup</string>
  </array>
  <key>RunAtLoad</key><true/>
  <key>KeepAlive</key><false/>
  <key>ProcessType</key><string>Interactive</string>
  <!-- Without this, launchd kills Pane's whole process group (including the
       updater's detached swap helper) the moment this job exits. -->
  <key>AbandonProcessGroup</key><true/>
</dict>
</plist>
PLISTEOF
launchctl bootout "gui/$(id -u)/$LABEL" 2>/dev/null || true
launchctl bootstrap "gui/$(id -u)" "$PLIST"

echo "==> Launching Pane"
open "$APP"

echo ""
echo "Pane installed to $APP"
echo "It runs in the menu bar; press the global hotkey (default Alt+Space) to open it."
