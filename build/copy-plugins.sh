#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DEST="$HOME/.config/pane/plugins"
CONFIG="${1:-Debug}"
for p in Scripts VSCode Apps Calculator Files; do
  SRC="$ROOT/src/Pane.Plugins.$p/bin/$CONFIG/net10.0"
  OUT="$DEST/Pane.Plugins.$p"
  mkdir -p "$OUT"
  cp "$SRC/Pane.Plugins.$p.dll" "$OUT/"
done
echo "Copied plugins to $DEST"
