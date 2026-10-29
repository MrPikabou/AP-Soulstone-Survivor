#!/usr/bin/env bash
# Builds the plugin and copies it into the game's BepInEx plugins folder and
# the Thunderstore Mod Manager profile, so both launch paths pick it up.
set -euo pipefail
source "$(dirname "$0")/env.sh"

"$DOTNET" build "$AP_ROOT/mod/SoulstoneAP.csproj" -c Release -v q --nologo

BUILT="$AP_ROOT/mod/bin/Release/net6.0/SoulstoneAP.dll"
GAME_PLUGINS="$GAME_DIR/BepInEx/plugins"
TSM_PLUGINS="C:/Users/faggot/AppData/Roaming/Thunderstore Mod Manager/DataFolder/SoulstoneSurvivors/profiles/Default/BepInEx/plugins"

mkdir -p "$GAME_PLUGINS" "$TSM_PLUGINS"
cp -f "$BUILT" "$GAME_PLUGINS/"
cp -f "$BUILT" "$TSM_PLUGINS/"

echo "deployed -> $GAME_PLUGINS"
echo "deployed -> $TSM_PLUGINS"
