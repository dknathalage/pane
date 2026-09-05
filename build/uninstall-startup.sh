#!/usr/bin/env bash
# SUPERSEDED — this only undoes install-startup.sh's non-bundle install into
# ~/Applications/Pane, which the in-app updater and Start-at-login toggle
# cannot manage (see install-startup.sh's header). If you installed with
# ../install.sh, remove Pane.app and the com.pane.launcher LaunchAgent
# yourself, or use the Start-at-login checkbox in Settings.
#
# Remove the Pane login item (LaunchAgent) and stop the running instance.
set -euo pipefail

LABEL="com.pane.launcher"
PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"

launchctl bootout "gui/$(id -u)/$LABEL" 2>/dev/null || true
rm -f "$PLIST"
pkill -f "Pane.App" 2>/dev/null || true

echo "✅ Pane login item removed and stopped."
echo "   (App files remain in ~/Applications/Pane — delete that folder to fully remove.)"
