#!/usr/bin/env bash
# Install Pane as a login item (LaunchAgent) so the global hotkey is always
# listening. Publishes the app to a stable path, deploys the plugins, and
# loads a per-user LaunchAgent that starts Pane hidden at login.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
APPDIR="$HOME/Applications/Pane"
BIN="$APPDIR/Pane.App"
PLUGINS_DEST="$HOME/.config/pane/plugins"
LABEL="com.pane.launcher"
PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"

echo "==> Publishing Pane.App (Release, self-contained) → $APPDIR"
# Self-contained bundles the .NET runtime so the LaunchAgent (which has a
# minimal PATH and can't find Homebrew's dotnet) runs standalone.
RID="osx-$(uname -m | sed 's/x86_64/x64/; s/arm64/arm64/')"
rm -rf "$APPDIR"
dotnet publish "$ROOT/src/Pane.App" -c Release -r "$RID" --self-contained true \
  -o "$APPDIR" --nologo -v q
chmod +x "$BIN"

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

echo "==> Writing LaunchAgent → $PLIST"
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
        <string>$BIN</string>
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

cat <<DONE

✅ Pane installed and started (hidden). It will now launch at every login.

ONE manual step for the global hotkey (Opt+Space):
  System Settings → Privacy & Security → Accessibility → add / enable:
    $BIN
  then press Opt+Space to summon Pane.

Manage it:
  Restart:   launchctl kickstart -k gui/$(id -u)/$LABEL
  Stop/undo: $ROOT/build/uninstall-startup.sh
DONE
