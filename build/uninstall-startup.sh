#!/usr/bin/env bash
# Remove the Pane login item (LaunchAgent) and stop the running instance.
set -euo pipefail

LABEL="com.pane.launcher"
PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"

launchctl bootout "gui/$(id -u)/$LABEL" 2>/dev/null || true
rm -f "$PLIST"
pkill -f "Pane.App" 2>/dev/null || true

echo "✅ Pane login item removed and stopped."
echo "   (App files remain in ~/Applications/Pane — delete that folder to fully remove.)"
