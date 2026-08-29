#!/usr/bin/env bash
# Build each bundled plugin (Release) and zip it as an individual release asset,
# matching the URLs in the repo-root marketplace.json.
#   dist/plugins-release/Pane.Plugins.<name>.zip  (each zip holds the plugin dll)
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/dist/plugins-release"
mkdir -p "$OUT"

for p in Apps Calculator Scripts VSCode; do
  echo "==> Building Pane.Plugins.$p (Release)"
  dotnet build "$ROOT/src/Pane.Plugins.$p" -c Release --nologo -v q >/dev/null
  STAGE="$(mktemp -d)"
  cp "$ROOT/src/Pane.Plugins.$p/bin/Release/net10.0/Pane.Plugins.$p.dll" "$STAGE/"
  ZIP="$OUT/Pane.Plugins.$p.zip"
  rm -f "$ZIP"
  ( cd "$STAGE" && zip -qry "$ZIP" . )
  rm -rf "$STAGE"
  echo "==> $ZIP"
done

echo ""
echo "Publish with:"
echo "  gh release create plugins-v1.0.0 $OUT/*.zip -t 'Plugins v1.0.0' -n 'Bundled plugins'"
