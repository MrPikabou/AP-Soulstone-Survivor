#!/usr/bin/env bash
# Builds the plugin and copies it into the game's BepInEx plugins folder and,
# when that profile exists, the Thunderstore Mod Manager profile.
# Override the locations with SOULSTONE_DIR and TSM_PROFILE.
set -euo pipefail
source "$(dirname "$0")/env.sh"

if [ -z "${GAME_DIR:-}" ] || [ ! -d "$GAME_DIR" ]; then
  echo "Soulstone Survivors not found. Set SOULSTONE_DIR to the game folder." >&2
  exit 1
fi
if [ ! -x "$DOTNET" ] && ! command -v "$DOTNET" >/dev/null 2>&1; then
  echo "dotnet not found. Install the .NET SDK or keep the one in .tools/." >&2
  exit 1
fi

"$DOTNET" build "$AP_ROOT/mod/SoulstoneAP.csproj" -c Release -v q --nologo -p:GameDir="$GAME_DIR"

BUILT="$AP_ROOT/mod/bin/Release/net6.0/SoulstoneAP.dll"
GAME_PLUGINS="$GAME_DIR/BepInEx/plugins"

mkdir -p "$GAME_PLUGINS"
cp -f "$BUILT" "$GAME_PLUGINS/"
echo "deployed -> $GAME_PLUGINS"

if [ -d "$TSM_PROFILE" ]; then
  mkdir -p "$TSM_PROFILE/BepInEx/plugins"
  cp -f "$BUILT" "$TSM_PROFILE/BepInEx/plugins/"
  echo "deployed -> $TSM_PROFILE/BepInEx/plugins"
else
  echo "no Thunderstore profile at $TSM_PROFILE — skipped (set TSM_PROFILE to install there)"
fi
