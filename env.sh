# Shared environment for the Soulstone Archipelago toolchain.
#
# Nothing here is specific to one machine: point SOULSTONE_DIR at the game and
# TSM_PROFILE at a Thunderstore Mod Manager profile if the guesses below miss.
# dotnet/NuGet state is redirected into .tools/ so a checkout stays self-contained.
AP_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
export AP_ROOT

# Captured before the redirects at the bottom, so the Mod Manager profile can
# still be found on the real Windows profile.
REAL_APPDATA="${APPDATA:-$HOME/AppData/Roaming}"

if [ -z "${SOULSTONE_DIR:-}" ]; then
  for candidate in \
    "C:/Program Files (x86)/Steam/steamapps/common/Soulstone Survivors" \
    "C:/Program Files/Steam/steamapps/common/Soulstone Survivors" \
    "D:/Steam/steamapps/common/Soulstone Survivors" \
    "D:/SteamLibrary/steamapps/common/Soulstone Survivors"; do
    if [ -d "$candidate" ]; then SOULSTONE_DIR="$candidate"; break; fi
  done
fi
export GAME_DIR="${SOULSTONE_DIR:-${GAME_DIR:-}}"
export INTEROP_DIR="${GAME_DIR:+$GAME_DIR/BepInEx/interop}"

export TSM_PROFILE="${TSM_PROFILE:-$REAL_APPDATA/Thunderstore Mod Manager/DataFolder/SoulstoneSurvivors/profiles/Default}"

# Prefer the SDK this project downloaded into .tools/ (the system `dotnet` may
# be a runtime-only install), then anything on PATH.
if [ -z "${DOTNET:-}" ]; then
  if [ -x "$AP_ROOT/.tools/dotnet/dotnet.exe" ]; then
    DOTNET="$AP_ROOT/.tools/dotnet/dotnet.exe"
  elif [ -x "$AP_ROOT/.tools/dotnet/dotnet" ]; then
    DOTNET="$AP_ROOT/.tools/dotnet/dotnet"
  else
    DOTNET="$(command -v dotnet || echo dotnet)"
  fi
fi
export DOTNET

export TEMP="$AP_ROOT/.tools/tmp"
export TMP="$AP_ROOT/.tools/tmp"
export APPDATA="$AP_ROOT/.tools/appdata/roaming"
export LOCALAPPDATA="$AP_ROOT/.tools/appdata/local"
export DOTNET_CLI_HOME="$AP_ROOT/.tools/home"
export DOTNET_NOLOGO=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export NUGET_PACKAGES="$AP_ROOT/.tools/nuget"
mkdir -p "$TEMP" "$APPDATA" "$LOCALAPPDATA" "$DOTNET_CLI_HOME" "$NUGET_PACKAGES"
