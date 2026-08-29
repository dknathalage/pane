#!/usr/bin/env bash
# Rewrite marketplace.json so every plugin's `version` and its release-asset
# `source.url` point at the given version's tag (plugins-v<version>).
#
# release-please bumps the `version` fields inside the release PR natively, but
# it cannot template the version substring inside the download URLs — so the
# release workflow runs this after the release is cut to keep URLs in sync.
#
# Usage: build/sync-marketplace.sh <version>   e.g. build/sync-marketplace.sh 1.1.0
set -euo pipefail

VERSION="${1:?usage: sync-marketplace.sh <version>}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
FILE="$ROOT/marketplace.json"

tmp="$(mktemp)"
jq --arg v "$VERSION" '
  .plugins |= map(
    .version = $v
    | .source.url |= sub("plugins-v[0-9]+\\.[0-9]+\\.[0-9]+"; "plugins-v\($v)")
  )
' "$FILE" > "$tmp"
mv "$tmp" "$FILE"

echo "Synced $FILE to plugins-v$VERSION"
