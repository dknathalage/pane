#!/usr/bin/env bash
# Install Pane as a login item (LaunchAgent) so the global hotkey is always
# listening. Publishes the app, deploys the plugins, and loads a per-user
# LaunchAgent that starts Pane hidden at login.
#
# The app is launched via the `dotnet` CLI (not the apphost) — the apphost
# can't locate Homebrew's .NET from the LaunchAgent's minimal PATH, while the
# CLI at a known path always can.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
APPDIR="$HOME/Applications/Pane"
DLL="$APPDIR/Pane.App.dll"
PLUGINS_DEST="$HOME/.config/pane/plugins"
LABEL="com.pane.launcher"
PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"
DOTNET="$(command -v dotnet)"

echo "==> Publishing Pane.App (Release) → $APPDIR"
rm -rf "$APPDIR"
dotnet publish "$ROOT/src/Pane.App" -c Release -o "$APPDIR" --nologo -v q

echo "==> Building + deploying plugins (Release) → $PLUGINS_DEST"
for p in Scripts VSCode Apps Calculator; do
  dotnet build "$ROOT/src/Pane.Plugins.$p" -c Release --nologo -v q >/dev/null
  OUT="$PLUGINS_DEST/Pane.Plugins.$p"
  mkdir -p "$OUT"
  cp "$ROOT/src/Pane.Plugins.$p/bin/Release/net10.0/Pane.Plugins.$p.dll" "$OUT/"
done

echo "==> Stopping any running Pane instances"
pkill -f "Pane.App" 2>/dev/null || true
sleep 1

echo "==> Writing LaunchAgent → $PLIST (via $DOTNET)"
mkdir -p "$(dirname "$PLIST")"
cat > "$PLIST" <<PLISTEOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>$LABEL</string>
    <key>ProgramArguments</key>
    <array>
        <string>$DOTNET</string>
        <string>$DLL</string>
        <string>--startup</string>
    </array>
    <key>RunAtLoad</key>
    <true/>
    <key>KeepAlive</key>
    <false/>
    <key>ProcessType</key>
    <string>Interactive</string>
</dict>
</plist>
PLISTEOF

echo "==> Loading LaunchAgent"
launchctl bootout "gui/$(id -u)/$LABEL" 2>/dev/null || true
launchctl bootstrap "gui/$(id -u)" "$PLIST"
sleep 3

if pgrep -f "Pane.App.dll" >/dev/null; then
  echo "==> Running ✅"
else
  echo "==> WARNING: not running — check: launchctl print gui/$(id -u)/$LABEL"
fi

cat <<DONE

✅ Pane installed and started (hidden). It will launch at every login.

ONE manual step for the global hotkey (Opt+Space):
  System Settings → Privacy & Security → Accessibility → add / enable
  your terminal (and $DOTNET if listed), then press Opt+Space.

Manage it:
  Restart:   launchctl kickstart -k gui/$(id -u)/$LABEL
  Stop/undo: $ROOT/build/uninstall-startup.sh
DONE
