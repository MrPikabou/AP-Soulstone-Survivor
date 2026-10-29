#!/usr/bin/env bash
# Copies the world package (and its client) into the local Archipelago install.
set -euo pipefail
source "$(dirname "$0")/env.sh"

AP_WORLDS="$AP_ROOT/.tools/archipelago/worlds"
TARGET="$AP_WORLDS/soulstone_survivors"

mkdir -p "$TARGET"
cp -f "$AP_ROOT"/world/soulstone_survivors/*.py "$TARGET/"
if [ -f "$AP_ROOT/world/soulstone_survivors/catalog.json" ]; then
  cp -f "$AP_ROOT/world/soulstone_survivors/catalog.json" "$TARGET/"
fi

echo "world deployed -> $TARGET"
