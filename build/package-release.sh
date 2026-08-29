#!/usr/bin/env bash
# Build the release zips that `install.sh` (non-local) downloads.
# Produces dist/release/Pane-osx-arm64.zip and Pane-osx-x64.zip, each holding
# Pane.app + plugins/ at the archive root.
#
# Usage: build/package-release.sh [RID...]   (defaults to both mac arches)
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/dist/release"
RIDS=("$@")
[ ${#RIDS[@]} -eq 0 ] && RIDS=(osx-arm64 osx-x64)

mkdir -p "$OUT"
for RID in "${RIDS[@]}"; do
  STAGE="$ROOT/dist/$RID"
  "$ROOT/build/make-app.sh" "$RID" "$STAGE"
  ZIP="$OUT/Pane-$RID.zip"
  rm -f "$ZIP"
  ( cd "$STAGE" && zip -qry "$ZIP" Pane.app plugins )
  echo "==> $ZIP"
done

echo ""
echo "Attach to a GitHub release, e.g.:"
echo "  gh release create v1.0.0 $OUT/Pane-*.zip -t 'Pane v1.0.0' -n 'Release notes'"
